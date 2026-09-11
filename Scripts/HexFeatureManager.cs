using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HexFeatureManager : MonoBehaviour
{

    // 每类特征按密度等级 1/2/3 索引的 prefab 数组（[0]=等级1 ... [2]=等级3）
    // 保留 urbanPrefabs 序列化字段名，用户已配置的引用不受影响
    public Transform[] urbanPrefabs;
    public Transform[] farmPrefabs;
    public Transform[] plantPrefabs;

    /// <summary>
    /// 怪物巢穴 prefab（僵尸巢穴等放置点）。等级 1..n 对应数组下标 0..n-1。
    /// 巢穴优先级最高：某格有巢穴时不再放 farm/urban/plant。仅放在格中心（dense 边缘槽不放）。
    /// </summary>
    public Transform[] monsterLairPrefabs;

    /// <summary>
    /// 巢穴目标 x/z 世界宽度（一格直径 = outerRadius*2 = 10）。空 prefab（无 Renderer）自动跳过缩放。
    /// </summary>
    [Tooltip("巢穴目标宽度（10 ≈ 一格直径；12 ≈ 1.2 格，稍大于一格）")]
    public float lairSize = 10f;

    /// <summary>
    /// 巢穴高度倍率：在 lairSize 归一化之后再把 y 乘上该值（默认 1 = 保持模型天然比例）。
    /// 洞穴类模型天然很扁（MonsterLair 高/宽 ≈ 0.33 → 宽 12 时仅 3.9 高），单纯放大宽度仍然矮，
    /// 会出现「怪物比巢穴还高」→ 需要单独抬高。1.5 → 宽 12 时约 5.9 高。
    /// 贴地按拉伸后的网格底边计算，所以抬高不会离地。
    /// </summary>
    [Tooltip("巢穴高度倍率（1 = 天然比例；在 lairSize 归一化后再乘 y）")]
    public float lairHeight = 1f;

    /// <summary>
    /// farm 田块的 x/z 目标 scale（世界宽度 ≈ farmSize，一格直径 = outerRadius*2 = 10）。
    /// 默认 10 → SoilCell 实例 localScale.x/z = 10，观感正好一格；改小更留边距、改大盖住格间坡缝。
    /// </summary>
    [Tooltip("farm 田块 x/z 目标 scale（10 ≈ 一格直径，SoilCell 原始即 10）")]
    public float farmSize = 10f;

    /// <summary>
    /// farm 田块厚度倍率：localScale.y = farmSize * farmThickness。
    /// 默认 2 → y=20（比 x 厚一倍，降低与坡面贴合时的穿帮/被埋）。
    /// </summary>
    [Tooltip("farm 厚度倍率（y = farmSize * 此值，2 → y=20）")]
    public float farmThickness = 2f;

    /// <summary>
    /// 是否为特征实例保留自身碰撞体。默认 false（关闭）。
    /// 原因：田块/巢穴/树木等 prefab 自带 Mesh/Box Collider，而地图编辑与点击移动都用
    /// Physics.Raycast 取**最近命中**——特征压在格面上、体积又大，射线先打到特征，
    /// hit.point 被换算成**邻近格**，于是"点 A 格却在 B 格生成特征/点了没反应"。
    /// 特征只是视觉表现，不需要碰撞；若将来要让巢穴成为物理障碍再勾选此项。
    /// </summary>
    [Tooltip("保留特征自带碰撞体（默认关闭；开启会让编辑/点击射线命中特征而非地形）")]
    public bool keepColliders = false;

    Transform container;

    public void Clear() {
        if (container)
        {
            Destroy(container.gameObject);
        }
        container = new GameObject("Features Container").transform;
        container.SetParent(transform, false);
    }

    public void Apply() { 
        
    }

    /// <summary>
    /// 按类型等级与哈希概率挑一个 prefab（立式装饰：树/建筑）。
    /// 等级 0 无；每升 1 级出现概率 +25%。样式 = 数组[等级-1]，固定不轮换。
    /// </summary>
    Transform PickPrefab(Transform[] prefabs, int level, float hash) {
        if (prefabs != null && level > 0 && level <= prefabs.Length && hash < level * 0.25f)
        {
            return prefabs[level - 1];
        }
        return null;
    }

    /// <summary>
    /// 只统计「网格渲染器」（MeshRenderer / SkinnedMeshRenderer）的合并包围盒，忽略粒子渲染器。
    /// 为什么忽略粒子：ParticleSystemRenderer.bounds 在实例化当帧可能为 0，或远大于/偏离模型
    /// （MonsterLair.prefab 里带了传送门粒子，实测 bounds 宽 ~56 → FitToWidth 把巢穴缩到 0.32 倍、
    /// SnapToGround 用粒子底边 −2.2 把巢穴抬起 2.6，模型因此又小又悬空）。
    /// </summary>
    static bool TryGetMeshBounds(Transform instance, out Bounds bounds) {
        bounds = new Bounds();
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        bool found = false;
        for (int i = 0; i < renderers.Length; i++) {
            Renderer r = renderers[i];
            if (r is ParticleSystemRenderer) continue;
            if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
            if (found) bounds.Encapsulate(r.bounds);
            else { bounds = r.bounds; found = true; }
        }
        return found;
    }

    /// <summary>
    /// 把实例的 x/z 外接尺寸缩放到 targetWidth（无 Renderer 的空 prefab 自动跳过）。
    /// 优先用「网格渲染器合并包围盒」；没有网格渲染器时退回第一个 Renderer（保持旧行为）。
    /// </summary>
    void FitToWidth(Transform instance, float targetWidth) {
        if (targetWidth <= 0f) return;
        float cur;
        Bounds meshBounds;
        if (TryGetMeshBounds(instance, out meshBounds)) {
            cur = Mathf.Max(meshBounds.size.x, meshBounds.size.z);
        }
        else {
            Renderer r = instance.GetComponentInChildren<Renderer>();
            if (r == null) return;
            cur = Mathf.Max(r.bounds.size.x, r.bounds.size.z);
        }
        if (cur > 0.0001f)
        {
            instance.localScale *= (targetWidth / cur);
        }
    }

    /// <summary>
    /// 贴地吸附：以 renderer 实际底边为锚，落到地表 y。
    /// 兼容 pivot 在底部/中心/任意偏移的 prefab；无 Renderer 的空 prefab 直接跳过。
    /// meshOnly = true 时只用网格渲染器的合并底边（巢穴用；避免被粒子 bounds 带偏）。
    /// 城市/农田保持默认 false = 第一个 Renderer（现有观感不变）。
    /// </summary>
    void SnapToGround(Transform instance, float groundY, bool meshOnly = false) {
        float bottom;
        Bounds meshBounds;
        if (meshOnly && TryGetMeshBounds(instance, out meshBounds)) {
            bottom = meshBounds.min.y;
        }
        else {
            Renderer rdr = instance.GetComponentInChildren<Renderer>();
            if (rdr == null) return;
            bottom = rdr.bounds.min.y;
        }
        Vector3 lp = instance.localPosition;
        lp.y -= (bottom - groundY);
        instance.localPosition = lp;
    }

    /// <summary>
    /// 在格内放置特征。优先级：怪物巢穴 &gt; farm 田块 &gt; urban/plant 装饰。
    ///  - monsterLair（允许中心槽时）：等级&gt;0 必放，等级即样式，固定朝向、尺寸按 lairSize。
    ///  - farm（六边形田块）：等级即样式（slider 1/2/3 直选 SoilCell1/2/3），必放、贴地、固定朝向。
    ///  - urban/plant（树等立式装饰）：概率出现 + 随机绕 Y。
    /// allowFarm=false：dense 模式的 6 个边缘候选槽，只放立式装饰（不放巢穴/田块）。
    /// 放置后按 keepColliders 决定是否剥离碰撞体（默认剥离）。
    /// </summary>
    public void AddFeature(HexCell cell, Vector3 position, bool allowFarm = true) {
        HexHash hash = HexMetrics.SampleHashGrid(position);

        Transform prefab = null;
        bool fromFarm = false;
        bool fromLair = false;

        if (allowFarm && cell.MonsterLairLevel > 0 &&
            monsterLairPrefabs != null && monsterLairPrefabs.Length > 0)
        {
            // 怪物巢穴：等级>0 必放，等级直接选样式
            int li = Mathf.Clamp(cell.MonsterLairLevel - 1, 0, monsterLairPrefabs.Length - 1);
            prefab = monsterLairPrefabs[li];
            fromLair = true;
        }
        else if (allowFarm && cell.FarmLevel > 0 &&
            farmPrefabs != null && farmPrefabs.Length > 0)
        {
            // farm 不做概率抽奖：等级>0 必放，且等级直接选样式（1→SoilCell1, 2→SoilCell2, 3→SoilCell3）
            int idx = Mathf.Clamp(cell.FarmLevel - 1, 0, farmPrefabs.Length - 1);
            prefab = farmPrefabs[idx];
            fromFarm = true;
        }
        else
        {
            // urban/plant 概率竞争同一个放置位（同现时取哈希更小者，刷新稳定）
            prefab = PickPrefab(urbanPrefabs, cell.UrbanLevel, hash.a);
            Transform otherPrefab = PickPrefab(plantPrefabs, cell.PlantLevel, hash.c);
            if (prefab)
            {
                if (otherPrefab && hash.c < hash.a)
                {
                    prefab = otherPrefab;
                }
            }
            else if (otherPrefab)
            {
                prefab = otherPrefab;
            }
            else
            {
                return;
            }
        }

        Transform instance = Instantiate(prefab);
        instance.SetParent(container, false);

        // 去掉特征自带碰撞体，保证射线只命中地形（否则编辑/点击会落到邻近格）
        if (!keepColliders)
        {
            Collider[] cols = instance.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
            {
                cols[i].enabled = false;
            }
        }

        if (fromLair)
        {
            // 巢穴：固定朝向（大型地标，不随机转）+ 按 lairSize 归一化宽度
            instance.localRotation = Quaternion.identity;
            FitToWidth(instance, lairSize);
            if (lairHeight > 0f && !Mathf.Approximately(lairHeight, 1f))
            {
                // 再单独抬高：洞穴模型天然扁，宽度归一化后仍不够高（免得怪物比巢穴还高）
                Vector3 ls = instance.localScale;
                ls.y *= lairHeight;
                instance.localScale = ls;
            }
        }
        else if (fromFarm)
        {
            // farm 固定朝向（prefab 内部装配已对齐 hex 边）
            instance.localRotation = Quaternion.identity;
            if (farmSize > 0f)
            {
                // 归一化到目标 x/z scale：以 prefab 自身根 scale 为基准，乘到 farmSize；
                // y 再按 farmThickness 加厚。换 prefab（根 scale 不同）也能得到一致观感。
                float k = farmSize / instance.localScale.x;
                Vector3 ls = instance.localScale * k;
                ls.y = ls.x * farmThickness;
                instance.localScale = ls;
            }
        }
        else
        {
            // 立式装饰随机绕 Y 增加变化
            instance.localRotation = Quaternion.Euler(0f, 360f * hash.e, 0f);
        }

        instance.localPosition = HexMetrics.Perturb(position);
        // 巢穴按网格底边贴地（忽略粒子）；城市/农田沿用旧的第一个 Renderer 语义
        SnapToGround(instance, position.y, fromLair);
    }

}
