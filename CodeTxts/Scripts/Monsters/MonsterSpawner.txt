using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 刷怪器（游戏侧）：按**场景配置表**（<see cref="SceneConfigTable"/>）的规则，
/// 在怪物巢穴格（`HexCell.MonsterLairLevel &gt; 0`）按**数量 / 频率 / 波次 / 存活上限**诞生怪物。
///
/// 需求对应：
///   「怪物会从怪物洞穴中诞生」      → 刷怪点默认取 MonsterLairLevel &gt; 0 的格（无巢穴回退地图西边缘）
///   「诞生数量和频率根据场景配置表配置」 → 每行 count（数量）/ interval（频率）/ maxAlive / waves
///   「目前没有场景配置表」          → 本组件配套新增 SceneConfigTable（CSV 驱动，见 docs/monsters.md）
///
/// 分层：本类是**游戏侧**，只读 HexGrid / HexCell；怪物单位本身是 <see cref="MonsterUnit"/>
/// （AI 与移动由它负责），本类只管「何时、在哪、生几只」。
///
/// EditMode（非运行态）下 Update 不跑：断言脚本用公开的 `StartSpawning()` + `Tick(now)` 手动驱动。
///
/// 装配：菜单 `Tools/怪物系统/一键装配 (AI 怪物)`。
/// </summary>
[DisallowMultipleComponent]
public class MonsterSpawner : MonoBehaviour
{
    public static MonsterSpawner Instance { get; private set; }

    [Header("引用")]
    [Tooltip("地图（留空自动查找场景里的 HexGrid）")]
    public HexGrid hexGrid;
    [Tooltip("场景配置表（留空自动从 Resources 加载 Configs/Tables/SceneConfigTable）")]
    public SceneConfigTable sceneConfigTable;
    [Tooltip("怪物表（留空自动从 Resources 加载；仅用于按 id 解析未填的 monster 引用）")]
    public MonsterTable monsterTable;
    [Tooltip("怪物实例的父物体（留空自动创建子物体 \"Monsters Container\"）")]
    public Transform spawnContainer;

    [Header("规则")]
    [Tooltip("生效场景名（空 = 用当前活动场景名）。见 SceneManager.GetActiveScene().name")]
    public string sceneNameOverride = "";
    [Tooltip("进入 Play 自动开始刷怪")]
    public bool autoStart = true;
    [Tooltip("等待本波清完时，最多多等多久就强制进入下一波（防止怪物杀不掉导致卡死；<=0 = 不超时）")]
    public float waveStallTimeout = 90f;
    [Tooltip("地图未就绪（HexGrid 未建图 / cells 为空）时等待而不刷怪——地图可能是运行时读档才有的")]
    public bool waitForMapReady = true;
    [Tooltip("等待地图就绪的超时（秒；<=0 = 无限等）。超时只告警一次，不刷怪")]
    public float mapWaitTimeout = 30f;
    [Tooltip("用巢穴作出生点（LairRoundRobin / LairRandom）时，等巢穴数据就绪再刷——避免静默落到西边缘")]
    public bool requireLairs = true;
    [Tooltip("等待巢穴就绪的超时（秒；<=0 = 无限等）。超时后允许回退西边缘并告警")]
    public float lairWaitTimeout = 15f;
    [Tooltip("出生点必须**能走到至少一个玩家**（A* 实际可达，不是直线距离）。\n" +
             "开启时优先挑「能到达玩家」的巢穴；巢穴全都不可达 → 告警一次并照常刷（怪物会原地待机，多半是地图被水/悬崖隔断）。\n" +
             "关掉 = 完全按配置刷，不做可达性判断")]
    public bool requireReachableSpawn = true;

    [Header("表现 / 调试")]
    [Tooltip("打印刷怪日志")]
    public bool logSpawns = true;
    [Tooltip("每帧清理失效（被外部销毁）的怪物登记")]
    public bool pruneStaleEachFrame = true;

    [Header("状态（只读）")]
    [Tooltip("是否正在按表刷怪（全部行刷完后仍为 true，直到 StopSpawning）")]
    public bool isRunning;
    [Tooltip("已刷出的怪物总数")]
    public int spawnedCount;
    [Tooltip("当前波次（所有行里的最大波次）")]
    public int currentWave;
    [Tooltip("波次总数（所有行里的最大波数）")]
    public int waveCount;

    /// <summary>刷出一只怪物：(怪物单位)。</summary>
    public event Action<MonsterUnit> Spawned;
    /// <summary>一只怪物离场（死亡 / 被清理）。</summary>
    public event Action<MonsterUnit> MonsterRemoved;
    /// <summary>某行进入新的一波：(波次, 总波数)。</summary>
    public event Action<int, int> WaveStarted;
    /// <summary>所有行都已刷完（在刷的怪可能还活着）。</summary>
    public event Action AllSpawned;
    /// <summary>场上怪物清空（配合 AllSpawned 可判定「通关」）。</summary>
    public event Action Cleared;

    /// <summary>一行场景配置的运行状态。</summary>
    class RowRuntime
    {
        public MonsterSceneConfigEntry entry;
        public int rowIndex;
        public bool started;
        public bool done;
        public bool inWaveGap;
        public bool fallbackWarned;
        public bool unreachableWarned;
        public int waveIndex;
        public int spawnedInWave;
        public int spawnedTotal;
        public float nextSpawnTime;
        public float nextWaveTime;
        public float gapStartTime;
        public int roundRobinIndex;
        public List<HexCell> spawnCells = new List<HexCell>();
    }

    readonly List<RowRuntime> rows = new List<RowRuntime>();
    readonly List<MonsterUnit> alive = new List<MonsterUnit>();
    readonly List<HexCell> lairCellCache = new List<HexCell>();
    readonly List<HexCell> edgeCellCache = new List<HexCell>();
    bool allSpawnedRaised;
    bool clearedRaised;
    bool mapReadyWarned;
    bool startTimeSet;
    float firstTickTime;

    // ================= 只读状态 =================

    /// <summary>当前配置生效的场景名。</summary>
    public string SceneName
    {
        get { return string.IsNullOrEmpty(sceneNameOverride) ? SceneConfigTable.ActiveSceneName() : sceneNameOverride; }
    }

    /// <summary>本场总共要刷多少只（按已加载的行）。</summary>
    public int TotalToSpawn
    {
        get
        {
            int total = 0;
            for (int i = 0; i < rows.Count; i++) total += Mathf.Max(0, rows[i].entry.count);
            return total;
        }
    }

    /// <summary>场上存活怪物数。</summary>
    public int AliveCount { get { Prune(); return alive.Count; } }
    /// <summary>还剩多少只没刷。</summary>
    public int RemainingToSpawn { get { return Mathf.Max(0, TotalToSpawn - spawnedCount); } }
    /// <summary>已加载的配置行数。</summary>
    public int RowCount { get { return rows.Count; } }
    /// <summary>已刷完的行数。</summary>
    public int FinishedRowCount
    {
        get
        {
            int n = 0;
            for (int i = 0; i < rows.Count; i++) if (rows[i].done) n++;
            return n;
        }
    }
    /// <summary>存活怪物只读列表（调用方不要修改）。</summary>
    public IList<MonsterUnit> AliveMonsters { get { return alive; } }

    /// <summary>怪物洞窟格（MonsterLairLevel &gt; 0）。缓存；地图重建时调 InvalidateSpawnCells()。</summary>
    public List<HexCell> GetLairCells()
    {
        EnsureRefs();
        if (lairCellCache.Count > 0 || hexGrid == null || hexGrid.Cells == null) return lairCellCache;
        HexCell[] cells = hexGrid.Cells;
        for (int i = 0; i < cells.Length; i++)
        {
            HexCell c = cells[i];
            if (c != null && c.MonsterLairLevel > 0) lairCellCache.Add(c);
        }
        return lairCellCache;
    }

    /// <summary>地图东/西边缘格（无巢穴时的回退出怪点）。</summary>
    public List<HexCell> GetEdgeCells(bool west)
    {
        EnsureRefs();
        edgeCellCache.Clear();
        if (hexGrid == null || hexGrid.Cells == null) return edgeCellCache;
        int x = west ? 0 : hexGrid.CellCountX - 1;
        for (int z = 0; z < hexGrid.CellCountZ; z++)
        {
            HexCell c = hexGrid.GetCellByOffset(x, z);
            if (c != null) edgeCellCache.Add(c);
        }
        return edgeCellCache;
    }

    /// <summary>清掉刷怪点缓存（新建地图 / 读档重建地图后调用）。</summary>
    public void InvalidateSpawnCells()
    {
        lairCellCache.Clear();
        edgeCellCache.Clear();
        for (int i = 0; i < rows.Count; i++) rows[i].spawnCells.Clear();
    }

    // ================= 生命周期 =================

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[Monsters] 场景中存在多个 MonsterSpawner，保留先者，禁用后来者：" + name);
            enabled = false;
            return;
        }
        Instance = this;
    }

    void OnDisable()
    {
        if (Instance == this) Instance = null;
    }

    void Start()
    {
        EnsureRefs();
        if (autoStart) StartSpawning();
    }

    void Update()
    {
        if (!Application.isPlaying) return;
        EnsureRefs();
        if (pruneStaleEachFrame) Prune();
        if (isRunning) Tick(Time.time);
    }

    void EnsureRefs()
    {
        if (hexGrid == null) hexGrid = FindObjectOfType<HexGrid>();
        if (sceneConfigTable == null) sceneConfigTable = SceneConfigTable.Load();
        if (monsterTable == null) monsterTable = MonsterTable.Load();
        if (spawnContainer == null)
        {
            Transform existing = transform.Find("Monsters Container");
            if (existing != null) spawnContainer = existing;
            else
            {
                GameObject go = new GameObject("Monsters Container");
                go.transform.SetParent(transform, false);
                spawnContainer = go.transform;
            }
        }
    }

    // ================= 启动 / 停止 =================

    /// <summary>
    /// 按「当前场景」加载配置行并开始刷怪。返回 false = 没有可用行（表缺失 / 全被禁用 / 场景不匹配）。
    /// </summary>
    public bool StartSpawning()
    {
        BuildRows();
        if (rows.Count == 0)
        {
            if (logSpawns)
            {
                Debug.LogWarning("[Monsters] 场景 \"" + SceneName + "\" 没有可用的刷怪配置行" +
                    (sceneConfigTable == null
                        ? "（找不到场景配置表：Resources/" + SceneConfigTable.ResourcePath + "）"
                        : "（共 " + sceneConfigTable.Count + " 行，可能都是 disabled / 场景不匹配 / 缺 prefab）"));
            }
            return false;
        }

        isRunning = true;
        allSpawnedRaised = false;
        clearedRaised = false;
        mapReadyWarned = false;
        startTimeSet = false;            // 就绪等待窗口从本次 StartSpawning 后的第一个 Tick 起算
        for (int i = 0; i < rows.Count; i++)
        {
            rows[i].fallbackWarned = false;
            rows[i].unreachableWarned = false;
        }
        if (logSpawns)
        {
            Debug.Log("[Monsters] 开始刷怪：场景 \"" + SceneName + "\"，" + rows.Count + " 行，共 " +
                      TotalToSpawn + " 只。出生点（巢穴格）=" + GetLairCells().Count + " 个。");
        }
        return true;
    }

    /// <summary>停止刷怪（已在场的怪物默认保留）。</summary>
    public void StopSpawning(bool clearMonsters = false)
    {
        isRunning = false;
        if (clearMonsters) ClearMonsters();
    }

    /// <summary>重新按表加载配置行（不动已在场的怪物）。</summary>
    public void BuildRows()
    {
        EnsureRefs();
        rows.Clear();
        currentWave = 0;
        waveCount = 0;
        if (sceneConfigTable == null) return;

        List<MonsterSceneConfigEntry> entries = sceneConfigTable.GetForScene(SceneName);
        for (int i = 0; i < entries.Count; i++)
        {
            MonsterSceneConfigEntry e = entries[i];
            if (e == null) continue;

            // 未在 Inspector 里填 monster 时，按 monsterId 解析（CSV 只写 id 的常见情形）
            if (e.monster == null && !string.IsNullOrEmpty(e.monsterId))
            {
                MonsterConfig byTable = monsterTable != null ? monsterTable.FindConfig(e.monsterId) : null;
                e.monster = byTable != null ? byTable : MonsterTable.LoadConfigById(e.monsterId);
            }
            // 未填怪物表时用约定路径兜底
            if (monsterTable == null && !string.IsNullOrEmpty(e.monsterId))
            {
                MonsterConfig byPath = MonsterTable.LoadConfigById(e.monsterId);
                if (byPath != null) e.monster = byPath;
            }

            if (!e.IsUsable)
            {
                if (logSpawns && e.enabled)
                {
                    Debug.LogWarning("[Monsters] 行 \"" + e.id + "\" 不可用（怪物未接线 / prefab 缺失 / count<=0），已跳过。");
                }
                continue;
            }

            RowRuntime r = new RowRuntime { entry = e, rowIndex = rows.Count };
            rows.Add(r);
            waveCount = Mathf.Max(waveCount, e.waves <= 1 ? 1 : e.waves);
        }
    }

    /// <summary>地图是否已就绪（HexGrid 已建图、cells 非空）。未就绪时 waitForMapReady 会让刷怪等待。</summary>
    public bool IsMapReady()
    {
        EnsureRefs();
        return hexGrid != null && hexGrid.Cells != null && hexGrid.Cells.Length > 0;
    }

    /// <summary>该出生点模式是否依赖怪物巢穴。</summary>
    public static bool UsesLairSpawn(MonsterSpawnPointMode mode)
    {
        return mode == MonsterSpawnPointMode.LairRoundRobin || mode == MonsterSpawnPointMode.LairRandom;
    }

    /// <summary>
    /// 出生前的就绪闸门：地图与巢穴数据可能是**运行时读档/后建**才到位的，
    /// 未就绪时本帧不刷（返回 false），避免静默把怪刷到西边缘。
    /// 超时后：地图未就绪持续等待（只告警一次）；巢穴未就绪则放行 → 由 ResolveSpawnCell 回退并告警。
    /// </summary>
    bool ReadyToSpawn(RowRuntime r, float now)
    {
        if (!startTimeSet)
        {
            startTimeSet = true;
            firstTickTime = now;
        }
        float waited = now - firstTickTime;

        if (waitForMapReady && !IsMapReady())
        {
            if (mapWaitTimeout <= 0f || waited < mapWaitTimeout) return false;
            if (!mapReadyWarned)
            {
                mapReadyWarned = true;
                Debug.LogWarning("[Monsters] 等待地图就绪已超时（" + mapWaitTimeout +
                                 "s）：HexGrid 未建图或 cells 为空，暂不刷怪。建图 / 读档后会自动开始。");
            }
            return false;
        }

        if (requireLairs && UsesLairSpawn(r.entry.spawnPoint) && GetLairCells().Count == 0)
        {
            if (lairWaitTimeout <= 0f || waited < lairWaitTimeout) return false;
            // 超时：放行 → ResolveSpawnCell 回退西边缘（并告警一次）
        }
        return true;
    }

    /// <summary>单次刷怪推进（Update 在 Play 模式每帧调用；断言脚本可手动驱动）。</summary>
    public void Tick(float now)
    {
        if (rows.Count == 0) return;

        for (int i = 0; i < rows.Count; i++) ProcessRow(rows[i], now);

        Prune();
        if (!allSpawnedRaised && IsAllRowsDone())
        {
            allSpawnedRaised = true;
            if (AllSpawned != null) AllSpawned();
        }
        if (allSpawnedRaised && !clearedRaised && alive.Count == 0)
        {
            clearedRaised = true;
            if (Cleared != null) Cleared();
        }
    }

    bool IsAllRowsDone()
    {
        for (int i = 0; i < rows.Count; i++) if (!rows[i].done) return false;
        return true;
    }

    void ProcessRow(RowRuntime r, float now)
    {
        if (r.done) return;
        MonsterSceneConfigEntry e = r.entry;

        if (!r.started)
        {
            r.started = true;
            r.waveIndex = 1;
            r.spawnedInWave = 0;
            r.nextSpawnTime = now + Mathf.Max(0f, e.startDelay);
            currentWave = Mathf.Max(currentWave, 1);
            if (WaveStarted != null) WaveStarted(1, Mathf.Max(1, e.waves));
        }

        // 就绪闸门：地图 / 巢穴数据未到位时先不刷（避免静默落到西边缘）
        if (!ReadyToSpawn(r, now)) return;

        // 波间等待（waves > 1 时才可能进入）
        if (r.inWaveGap)
        {
            bool clearOk = !e.waitForClear || CountAliveInRow(r.rowIndex) == 0;
            bool timeOk = now >= r.nextWaveTime;
            bool stalled = e.waitForClear && waveStallTimeout > 0f &&
                           now - r.gapStartTime >= waveStallTimeout;
            if (stalled)
            {
                if (logSpawns)
                {
                    Debug.LogWarning("[Monsters] 行 \"" + e.id + "\" 等本波清场超过 " + waveStallTimeout +
                                     "s，强制进入下一波。");
                }
            }
            if (timeOk && (clearOk || stalled))
            {
                r.inWaveGap = false;
                r.waveIndex++;
                r.spawnedInWave = 0;
                r.nextSpawnTime = now;
                currentWave = Mathf.Max(currentWave, r.waveIndex);
                if (WaveStarted != null) WaveStarted(r.waveIndex, Mathf.Max(1, e.waves));
            }
            else return;
        }

        if (r.spawnedTotal >= e.count)
        {
            r.done = true;
            return;
        }
        if (now < r.nextSpawnTime) return;
        if (e.maxAlive > 0 && CountAliveInRow(r.rowIndex) >= e.maxAlive) return;

        int thisWaveLimit = e.waves <= 1
            ? e.count
            : Mathf.Min(e.PerWaveCount, e.count - (r.waveIndex - 1) * e.PerWaveCount);
        if (e.waves > 1 && r.spawnedInWave >= Mathf.Max(1, thisWaveLimit))
        {
            EnterWaveGap(r, now);
            return;
        }

        if (SpawnOne(r) == null) return;   // 出生点拿不到（地图没生成）：留待下一帧重试
        r.nextSpawnTime = now + Mathf.Max(0.05f, e.interval);

        if (r.spawnedTotal >= e.count)
        {
            r.done = true;
            return;
        }
        if (e.waves > 1 && r.spawnedInWave >= Mathf.Max(1, thisWaveLimit)) EnterWaveGap(r, now);
    }

    void EnterWaveGap(RowRuntime r, float now)
    {
        r.inWaveGap = true;
        r.gapStartTime = now;
        r.nextWaveTime = now + Mathf.Max(0.5f, r.entry.waveInterval);
    }

    /// <summary>该行当前存活数（按 sourceRowIndex 归组）。</summary>
    public int CountAliveInRow(int rowIndex)
    {
        int n = 0;
        for (int i = 0; i < alive.Count; i++)
        {
            MonsterUnit u = alive[i];
            if (u != null && u.sourceRowIndex == rowIndex) n++;
        }
        return n;
    }

    // ================= 生成 =================

    /// <summary>让下一条未刷完的行刷一只（手动 / 调试用；全部刷完返回 null）。</summary>
    public MonsterUnit SpawnOne()
    {
        for (int i = 0; i < rows.Count; i++)
        {
            RowRuntime r = rows[i];
            if (r.done) continue;
            if (r.entry.maxAlive > 0 && CountAliveInRow(r.rowIndex) >= r.entry.maxAlive) continue;
            MonsterUnit u = SpawnOneForRow(r.rowIndex);
            if (u != null) return u;
        }
        return null;
    }

    /// <summary>按行下标刷一只（调试 / 断言用）。越界、该行已完成、出生点不可用均返回 null。</summary>
    public MonsterUnit SpawnOneForRow(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= rows.Count) return null;
        RowRuntime r = rows[rowIndex];
        if (r.done) return null;

        MonsterUnit u = SpawnOne(r);
        if (u == null) return null;
        r.nextSpawnTime = Time.time + Mathf.Max(0.05f, r.entry.interval);
        if (r.spawnedTotal >= r.entry.count) r.done = true;
        return u;
    }

    /// <summary>取某行的出生格（断言 / 预览用；不改变轮流游标）。</summary>
    public HexCell ResolveSpawnCellForRow(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= rows.Count) return null;
        RowRuntime r = rows[rowIndex];
        int saved = r.roundRobinIndex;
        HexCell c = ResolveSpawnCell(r);
        r.roundRobinIndex = saved;
        return c;
    }

    /// <summary>某行的配置行（越界返回 null）。</summary>
    public MonsterSceneConfigEntry GetRowEntry(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= rows.Count) return null;
        return rows[rowIndex].entry;
    }

    /// <summary>某行已刷出的总数（越界返回 -1）。</summary>
    public int GetRowSpawnedTotal(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= rows.Count) return -1;
        return rows[rowIndex].spawnedTotal;
    }

    /// <summary>某行当前波次（越界返回 -1）。</summary>
    public int GetRowWaveIndex(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= rows.Count) return -1;
        return rows[rowIndex].waveIndex;
    }

    /// <summary>某行是否已刷完。</summary>
    public bool IsRowDone(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= rows.Count) return true;
        return rows[rowIndex].done;
    }

    MonsterUnit SpawnOne(RowRuntime r)
    {
        HexCell cell = ResolveSpawnCell(r);
        if (cell == null) return null;
        return SpawnAt(r, cell);
    }

    /// <summary>在指定格刷出一只指定行的怪物（公开：断言脚本 / 手工摆放用）。</summary>
    public MonsterUnit SpawnAtCell(int rowIndex, HexCell cell)
    {
        if (rowIndex < 0 || rowIndex >= rows.Count) return null;
        return SpawnAt(rows[rowIndex], cell);
    }

    MonsterUnit SpawnAt(RowRuntime r, HexCell cell)
    {
        if (r == null || r.entry == null || r.entry.monster == null) return null;
        MonsterConfig cfg = r.entry.monster;

        EnsureRefs();
        GameObject go = Instantiate(cfg.prefab, spawnContainer, false);
        spawnedCount++;
        r.spawnedTotal++;
        r.spawnedInWave++;
        go.name = cfg.DisplayName + " #" + spawnedCount;

        MonsterUnit unit = go.GetComponent<MonsterUnit>();
        if (unit == null) unit = go.AddComponent<MonsterUnit>();
        unit.sourceRowIndex = r.rowIndex;
        unit.Initialize(cfg, cell, this);

        alive.Add(unit);
        if (logSpawns)
        {
            Debug.Log("[Monsters] 刷出 " + go.name + " @ " + cell.coordinates.ToString() +
                      "（第 " + r.waveIndex + " 波，本行累计 " + r.spawnedTotal + "/" + r.entry.count + "）");
        }
        if (Spawned != null) Spawned(unit);
        return unit;
    }

    /// <summary>解析某行的出生格（巢穴轮流 / 巢穴随机 / 边缘 / 指定坐标）。</summary>
    HexCell ResolveSpawnCell(RowRuntime r)
    {
        EnsureRefs();
        if (hexGrid == null) return null;
        MonsterSceneConfigEntry e = r.entry;

        switch (e.spawnPoint)
        {
            case MonsterSpawnPointMode.LairRandom:
            {
                HexCell c = PickLairCell(r, true);
                if (c != null) return c;
                break;
            }
            case MonsterSpawnPointMode.WestEdge:
                return PickEdge(true);
            case MonsterSpawnPointMode.EastEdge:
                return PickEdge(false);
            case MonsterSpawnPointMode.OffsetCoordinates:
            {
                HexCell c = hexGrid.GetCellByOffset(e.offsetCoordinates.x, e.offsetCoordinates.y);
                if (c != null) return c;
                break;
            }
            default: // LairRoundRobin
            {
                HexCell c = PickLairCell(r, false);
                if (c != null) return c;
                break;
            }
        }
        // 回退：地图西边缘（告警一次，避免"怪从错误的地方出来"静默发生）
        WarnFallback(r);
        return PickEdge(true);
    }

    /// <summary>
    /// 挑一个巢穴格。requireReachableSpawn 开启时**优先挑能走到玩家的巢穴**；
    /// 若巢穴全都走不到玩家 → 告警一次并仍然用巢穴（不偷偷换地点，把问题暴露给关卡）。
    /// </summary>
    HexCell PickLairCell(RowRuntime r, bool random)
    {
        List<HexCell> all = GetLairCells();
        if (all.Count == 0) return null;

        List<HexCell> usable = GetReachableLairCells();
        if (usable.Count == 0)
        {
            WarnUnreachable(r, all[0]);
            usable = all;
        }

        if (random) return usable[UnityEngine.Random.Range(0, usable.Count)];
        HexCell c = usable[r.roundRobinIndex % usable.Count];
        r.roundRobinIndex++;
        return c;
    }

    /// <summary>巢穴存在但**走不到任何玩家**时告警一次（每个配置行一条）。</summary>
    void WarnUnreachable(RowRuntime r, HexCell lair)
    {
        if (r.unreachableWarned) return;
        r.unreachableWarned = true;
        Debug.LogWarning("[Monsters] 行 \"" + r.entry.id + "\"（怪物 " + r.entry.monsterId +
                         "）的出生点 " + (lair != null ? lair.coordinates.ToString() : "?") +
                         " **走不到任何玩家**（A* 无路径）→ 怪物刷出来会原地不动。\n" +
                         "  当前通行设置：blockUnderwater=" + (hexGrid != null && hexGrid.blockUnderwater) +
                         "  blockCliffs=" + (hexGrid != null && hexGrid.blockCliffs) + "。\n" +
                         "  处理办法（任选）：① 关掉悬崖阻断 blockCliffs（允许翻越 2 级落差，地图通常立刻连通）；" +
                         "② 把怪物巢穴画在玩家可达的陆地上；③ 把地形改缓（削掉 2 级落差 / 填水）；" +
                         "④ 或把 requireReachableSpawn 关掉，明确接受怪物不追人。\n" +
                         "  详情：菜单 Tools/怪物系统/诊断出生点可达性");
    }

    /// <summary>
    /// 从某格出发能否走到场上任一可索敌玩家所在的格（**A* 实际可达**，不是直线距离）。
    /// 场上没有可索敌玩家时返回 true —— 还没玩家时无法判断，不该拦着不放。
    /// </summary>
    public bool CanReachAnyPlayer(HexCell from)
    {
        EnsureRefs();
        if (hexGrid == null || from == null) return false;
        IList<PlayerTarget> targets = PlayerTargetRegistry.All;
        int considered = 0;
        for (int i = 0; i < targets.Count; i++)
        {
            PlayerTarget t = targets[i];
            if (t == null || !t.IsTargetable) continue;
            HexCell to = hexGrid.GetCellAtWorld(t.FootWorld);
            if (to == null) continue;
            if (!hexGrid.IsWalkable(to)) to = hexGrid.FindNearestWalkable(to);
            if (to == null) continue;
            considered++;
            if (hexGrid.FindPath(from, to)) return true;
            if (from == to) return true;
        }
        return considered == 0;      // 没有可索敌玩家 → 不阻拦
    }

    /// <summary>
    /// 巢穴格中「能走到玩家」的那些。requireReachableSpawn 关闭 / 场上无玩家 / 无巢穴时 = 原样返回。
    /// </summary>
    public List<HexCell> GetReachableLairCells()
    {
        List<HexCell> all = GetLairCells();
        if (!requireReachableSpawn || all.Count == 0) return all;
        if (!HasTargetablePlayer()) return all;          // 场上还没玩家：无法判断，不拦
        List<HexCell> ok = new List<HexCell>();
        for (int i = 0; i < all.Count; i++)
        {
            if (CanReachAnyPlayer(all[i])) ok.Add(all[i]);
        }
        return ok;
    }

    /// <summary>场上是否存在可索敌玩家。</summary>
    public static bool HasTargetablePlayer()
    {
        IList<PlayerTarget> targets = PlayerTargetRegistry.All;
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] != null && targets[i].IsTargetable) return true;
        }
        return false;
    }

    /// <summary>
    /// 出生点可达性诊断（Console / 菜单用）：列出玩家、巢穴、以及每个巢穴能否走到玩家，
    /// 并给出「地图被隔断」时的具体成因（水下 / 悬崖开关）。
    /// </summary>
    public string DiagnoseSpawnReachability()
    {
        EnsureRefs();
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        if (hexGrid == null || hexGrid.Cells == null || hexGrid.Cells.Length == 0)
        {
            sb.Append("HexGrid 未就绪 / 未建图。");
            return sb.ToString();
        }

        IList<PlayerTarget> targets = PlayerTargetRegistry.All;
        int validTargets = 0;
        sb.AppendLine("玩家目标：登记 " + targets.Count + " 个");
        for (int i = 0; i < targets.Count; i++)
        {
            PlayerTarget t = targets[i];
            if (t == null) continue;
            HexCell tc = hexGrid.GetCellAtWorld(t.FootWorld);
            if (t.IsTargetable) validTargets++;
            sb.AppendLine("   - " + t.name
                + "  可索敌=" + t.IsTargetable
                + "  所在格=" + (tc != null ? tc.coordinates.ToString() : "null")
                + "  世界=" + t.FootWorld.ToString("0.0"));
        }
        if (validTargets == 0) sb.AppendLine("   ⚠ 没有可索敌玩家 → 怪物会原地待机。");

        List<HexCell> lairs = GetLairCells();
        sb.AppendLine("怪物巢穴：" + lairs.Count + " 个（MonsterLairLevel > 0）");
        int reachable = 0;
        for (int i = 0; i < lairs.Count; i++)
        {
            bool ok = CanReachAnyPlayer(lairs[i]);
            if (ok) reachable++;
            sb.AppendLine("   - " + lairs[i].coordinates.ToString()
                + "  海拔=" + lairs[i].Elevation
                + "  水下=" + lairs[i].IsUnderwater
                + (ok ? "   ✔ 能走到玩家" : "   ✘ 走不到玩家（A* 无路径）"));
        }

        sb.AppendLine("HexGrid 通行设置：blockUnderwater=" + hexGrid.blockUnderwater
            + "  blockCliffs=" + hexGrid.blockCliffs
            + "  walkableTerrain(沙草泥石雪)=[" + string.Join(",", hexGrid.walkableTerrain) + "]");
        if (reachable == 0 && lairs.Count > 0 && validTargets > 0)
        {
            sb.AppendLine("⚠ 巢穴全都走不到玩家 → 怪物刷出来会原地不动。");
            sb.AppendLine("   处理办法（任选）：① 关掉悬崖阻断 blockCliffs（允许翻越 2 级落差，地图通常立刻连通）；" +
                          "② 把怪物巢穴画在玩家可达的那片陆地上；③ 把地形改缓（削掉 2 级落差 / 填水）；" +
                          "④ 或把 requireReachableSpawn 关掉，明确接受怪物不追人。");
        }
        return sb.ToString();
    }

    /// <summary>出生点回退西边缘时告警一次（每个配置行只打一条）。</summary>
    void WarnFallback(RowRuntime r)
    {
        if (r.fallbackWarned) return;
        r.fallbackWarned = true;
        Debug.LogWarning("[Monsters] 行 \"" + r.entry.id + "\"（怪物 " + r.entry.monsterId +
                         "）的出生点回退到**地图西边缘**：该模式=" + r.entry.spawnPoint +
                         "，但当前扫到的怪物巢穴格为 0。" +
                         " 检查：地图是否已建图、是否画了 Lair（MonsterLairLevel > 0，" +
                         "菜单 Tools/地形特征 或特征编辑器 Lair 滑条）、行配置的 spawnPoint 是否正确。");
    }

    HexCell PickEdge(bool west)
    {
        List<HexCell> cells = GetEdgeCells(west);
        if (cells.Count == 0) return null;
        return cells[UnityEngine.Random.Range(0, cells.Count)];
    }

    // ================= 存活统计 / 清理 =================

    /// <summary>怪物死亡回调（由 MonsterUnit.HandleDied 调用）。</summary>
    public void NotifyMonsterDied(MonsterUnit unit)
    {
        RemoveFromAlive(unit, false);
    }

    /// <summary>怪物实例被销毁时的兜底注销（幂等）。</summary>
    public void NotifyMonsterRemoved(MonsterUnit unit)
    {
        RemoveFromAlive(unit, false);
    }

    void RemoveFromAlive(MonsterUnit unit, bool destroy)
    {
        if (unit == null) return;
        bool removed = alive.Remove(unit);
        if (removed && MonsterRemoved != null) MonsterRemoved(unit);
        if (destroy && unit != null && unit.gameObject != null) DestroyObject(unit.gameObject);
    }

    /// <summary>清理失效登记（怪物被外部 Destroy 时留下的 Unity 伪 null）。返回清理条数。</summary>
    public int Prune()
    {
        if (alive.Count == 0) return 0;
        int removed = 0;
        for (int i = alive.Count - 1; i >= 0; i--)
        {
            if (alive[i] == null)
            {
                alive.RemoveAt(i);
                removed++;
            }
        }
        return removed;
    }

    /// <summary>清空场上所有怪物。</summary>
    public void ClearMonsters(bool destroy = true)
    {
        for (int i = alive.Count - 1; i >= 0; i--)
        {
            MonsterUnit u = alive[i];
            if (u == null) continue;
            if (destroy) DestroyObject(u.gameObject);
        }
        alive.Clear();
        if (spawnContainer != null && destroy)
        {
            for (int i = spawnContainer.childCount - 1; i >= 0; i--)
            {
                GameObject go = spawnContainer.GetChild(i).gameObject;
                if (go.GetComponent<MonsterUnit>() != null) DestroyObject(go);
            }
        }
    }

    static void DestroyObject(GameObject go)
    {
        if (go == null) return;
        if (Application.isPlaying) Destroy(go);
        else DestroyImmediate(go);
    }
}
