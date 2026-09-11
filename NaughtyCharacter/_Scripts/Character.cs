using UnityEngine;

namespace NaughtyCharacter
{
    public enum ERotationBehavior
    {
        OrientRotationToMovement,
        UseControlRotation
    }

    [System.Serializable]
    public class RotationSettings
    {
        [Header("Control Rotation")]
        public float MinPitchAngle = -45.0f;
        public float MaxPitchAngle = 75.0f;

        [Header("Character Orientation")]
        public ERotationBehavior RotationBehavior = ERotationBehavior.OrientRotationToMovement;
        public float MinRotationSpeed = 600.0f; // The turn speed when the player is at max speed (in degrees/second)
        public float MaxRotationSpeed = 1200.0f; // The turn speed when the player is stationary (in degrees/second)
    }

    [System.Serializable]
    public class MovementSettings
    {
        public float Acceleration = 25.0f; // In meters/second
        public float Decceleration = 25.0f; // In meters/second
        public float MaxHorizontalSpeed = 8.0f; // In meters/second
        public float JumpSpeed = 10.0f; // In meters/second
        public float JumpAbortSpeed = 10.0f; // In meters/second
    }

    [System.Serializable]
    public class GravitySettings
    {
        public float Gravity = 20.0f; // Gravity applied when the player is airborne
        public float GroundedGravity = 5.0f; // A constant gravity that is applied when the player is grounded
        public float MaxFallSpeed = 40.0f; // The max speed at which the player can fall
    }

    [System.Serializable]
    public class GroundSettings
    {
        public LayerMask GroundLayers; // Which layers are considered as ground
        public float SphereCastRadius = 0.35f; // The radius of the sphere cast for the grounded check
        public float SphereCastDistance = 0.15f; // The distance below the character's capsule used for the sphere cast grounded check
    }

    public class Character : MonoBehaviour
    {
        public Controller Controller; // The controller that controls the character
        public MovementSettings MovementSettings;
        public GravitySettings GravitySettings;
        public RotationSettings RotationSettings;
        public GroundSettings GroundSettings;

        private CharacterController _characterController; // The Unity's CharacterController
        private CharacterAnimator _characterAnimator;

        private float _targetHorizontalSpeed; // In meters/second
        private float _horizontalSpeed; // In meters/second
        private float _verticalSpeed; // In meters/second
        private bool _justWalkedOffALedge;

        private Vector2 _controlRotation; // X (Pitch), Y (Yaw)
        private Vector3 _movementInput;
        private Vector3 _lastMovementInput;
        private bool _hasMovementInput;
        private bool _jumpInput;

        public Vector3 Velocity => _characterController.velocity;
        public Vector3 HorizontalVelocity => _characterController.velocity.SetY(0.0f);
        public Vector3 VerticalVelocity => _characterController.velocity.Multiply(0.0f, 1.0f, 0.0f);
        public bool IsGrounded { get; private set; }

        private void Awake()
        {
            // 不在 Awake 绑定 Controller：绑定推迟到首个 Update/FixedUpdate（TryAutoBindController）。
            // 原因：NaughtyCharacter 假设"每场景单活角色"，Awake 直接 Controller.Character = this 会让
            // Mirror 联机下的远端化身（同样会 Awake）劫持共享 Controller，化身销毁后留下悬空引用 -> MRE。
            // 组件被 disable 的远端化身不会执行 Update/FixedUpdate，天然不参与绑定。
            _characterController = GetComponent<CharacterController>();
            _characterAnimator = GetComponent<CharacterAnimator>();
        }

        /// <summary>网络远端化身抑制标记：由联机层（EllenNetController）在 OnStartClient(!isLocalPlayer) 置 true。
        /// 置 true 后本角色绝不认领共享 Controller（双保险：组件禁用已阻止 Update 执行）。</summary>
        [System.NonSerialized]
        public bool RemoteSuppressed;

        private void Update()
        {
            TryAutoBindController();
            if (Controller != null && Controller.Character == this)
            {
                Controller.OnCharacterUpdate();
            }
        }

        private void FixedUpdate()
        {
            TryAutoBindController();
            Tick(Time.deltaTime);
            if (Controller != null && Controller.Character == this)
            {
                Controller.OnCharacterFixedUpdate();
            }
        }

        /// <summary>共享 Controller 归属仲裁（每帧轻量调用，不做缓存一次性判断）：
        ///  - RemoteSuppressed 的远端化身：若自己被误绑为 owner 则让位（置 null）；
        ///  - 本地活角色：owner 为空、或 owner 已销毁/禁用/失活/被抑制时，认领为 Controller.Character。
        /// 联机时序坑（实测）：远端化身可能在 OnStartClient 置 RemoteSuppressed 之前抢先绑定共享 Controller，
        /// 若用"每激活周期只查一次"的缓存会在本地角色被覆盖后永久失去驱动权 → 必须每帧仲裁。
        /// 单机（唯一活角色）语义与原版等价：首帧认领后每帧 cur==this 直接返回，无额外开销。</summary>
        void TryAutoBindController()
        {
            if (Controller == null)
            {
                return;
            }
            if (RemoteSuppressed)
            {
                if (Controller.Character == this)
                {
                    Controller.Character = null;
                }
                return;
            }
            Character cur = Controller.Character;
            if (cur == this)
            {
                return; // 已是 owner
            }
            if (cur != null && cur.enabled && cur.gameObject.activeInHierarchy)
            {
                return; // owner 是另一个活跃角色（不应发生；NaughtyCharacter 假设单活角色），不抢
            }
            // 无主 / owner 已销毁或禁用或失活 → 认领
            Controller.Init();
            Controller.Character = this;
        }

        private void OnDestroy()
        {
            // 若自己仍是共享 Controller 的拥有者则释放，避免销毁后 Controller.Character 悬空（网络角色销毁/场景卸载）
            if (Controller != null && Controller.Character == this)
            {
                Controller.Character = null;
            }
        }

        private void Tick(float deltaTime)
        {
            UpdateHorizontalSpeed(deltaTime);
            UpdateVerticalSpeed(deltaTime);

            Vector3 movement = _horizontalSpeed * GetMovementInput() + _verticalSpeed * Vector3.up;
            _characterController.Move(movement * deltaTime);

            OrientToTargetRotation(movement.SetY(0.0f), deltaTime);

            UpdateGrounded();

            _characterAnimator.UpdateState();
        }

        public void SetMovementInput(Vector3 movementInput)
        {
            bool hasMovementInput = movementInput.sqrMagnitude > 0.0f;

            if (_hasMovementInput && !hasMovementInput)
            {
                _lastMovementInput = _movementInput;
            }

            _movementInput = movementInput;
            _hasMovementInput = hasMovementInput;
        }

        private Vector3 GetMovementInput()
        {
            Vector3 movementInput = _hasMovementInput ? _movementInput : _lastMovementInput;
            if (movementInput.sqrMagnitude > 1f)
            {
                movementInput.Normalize();
            }

            return movementInput;
        }

        public void SetJumpInput(bool jumpInput)
        {
            _jumpInput = jumpInput;
        }

        public Vector2 GetControlRotation()
        {
            return _controlRotation;
        }

        public void SetControlRotation(Vector2 controlRotation)
        {
            // Adjust the pitch angle (X Rotation)
            float pitchAngle = controlRotation.x;
            pitchAngle %= 360.0f;
            pitchAngle = Mathf.Clamp(pitchAngle, RotationSettings.MinPitchAngle, RotationSettings.MaxPitchAngle);

            // Adjust the yaw angle (Y Rotation)
            float yawAngle = controlRotation.y;
            yawAngle %= 360.0f;

            _controlRotation = new Vector2(pitchAngle, yawAngle);
        }

        private bool CheckGrounded()
        {
            Vector3 spherePosition = transform.position;
            spherePosition.y = transform.position.y + GroundSettings.SphereCastRadius - GroundSettings.SphereCastDistance;
            bool isGrounded = Physics.CheckSphere(spherePosition, GroundSettings.SphereCastRadius, GroundSettings.GroundLayers, QueryTriggerInteraction.Ignore);

            return isGrounded;
        }

        private void UpdateGrounded()
        {
            _justWalkedOffALedge = false;

            bool isGrounded = CheckGrounded();
            if (IsGrounded && !isGrounded && !_jumpInput)
            {
                _justWalkedOffALedge = true;
            }

            IsGrounded = isGrounded;
        }

        private void UpdateHorizontalSpeed(float deltaTime)
        {
            Vector3 movementInput = _movementInput;
            if (movementInput.sqrMagnitude > 1.0f)
            {
                movementInput.Normalize();
            }

            _targetHorizontalSpeed = movementInput.magnitude * MovementSettings.MaxHorizontalSpeed;
            float acceleration = _hasMovementInput ? MovementSettings.Acceleration : MovementSettings.Decceleration;

            _horizontalSpeed = Mathf.MoveTowards(_horizontalSpeed, _targetHorizontalSpeed, acceleration * deltaTime);
        }

        private void UpdateVerticalSpeed(float deltaTime)
        {
            if (IsGrounded)
            {
                _verticalSpeed = -GravitySettings.GroundedGravity;

                if (_jumpInput)
                {
                    _verticalSpeed = MovementSettings.JumpSpeed;
                }
            }
            else
            {
                if (!_jumpInput && _verticalSpeed > 0.0f)
                {
                    // This is what causes holding jump to jump higher than tapping jump.
                    _verticalSpeed = Mathf.MoveTowards(_verticalSpeed, -GravitySettings.MaxFallSpeed, MovementSettings.JumpAbortSpeed * deltaTime);
                }
                else if (_justWalkedOffALedge)
                {
                    _verticalSpeed = 0.0f;
                }

                _verticalSpeed = Mathf.MoveTowards(_verticalSpeed, -GravitySettings.MaxFallSpeed, GravitySettings.Gravity * deltaTime);
            }
        }

        private void OrientToTargetRotation(Vector3 horizontalMovement, float deltaTime)
        {
            if (RotationSettings.RotationBehavior == ERotationBehavior.OrientRotationToMovement && horizontalMovement.sqrMagnitude > 0.0f)
            {
                float rotationSpeed = Mathf.Lerp(
                    RotationSettings.MaxRotationSpeed, RotationSettings.MinRotationSpeed, _horizontalSpeed / _targetHorizontalSpeed);

                Quaternion targetRotation = Quaternion.LookRotation(horizontalMovement, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * deltaTime);
            }
            else if (RotationSettings.RotationBehavior == ERotationBehavior.UseControlRotation)
            {
                Quaternion targetRotation = Quaternion.Euler(0.0f, _controlRotation.y, 0.0f);
                transform.rotation = targetRotation;
            }
        }
    }
}
