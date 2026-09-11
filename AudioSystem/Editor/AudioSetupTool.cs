using System.Collections.Generic;
using NaughtyCharacter;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 音频系统一键装配工具（菜单：Tools/音频系统/...）。
///
/// 一键装配做的事（可重复执行，幂等）：
///  1. 建素材目录树 Assets/Resources/Audio/（BGM、SFX 分类子目录——素材丢进对应目录即自动生效）；
///  2. 创建 / 补齐 Assets/Resources/Audio/AudioBank.asset（SoundBank，默认 key 表 + autoLoadFolder 指向素材目录）；
///  3. 场景建根对象 [AudioSystem]（AudioService，拖好 bank 引用）；
///  4. HexGrid 挂 HexGroundAudioBridge（地面材质桥）；
///  5. 场景活角色（enabled 的 NaughtyCharacter Character）挂 CharacterFootstepAudio；
///     并同步给 Assets/Resources/Prefabs/EllenNet.prefab（联机 spawn 用）；
///  6. 生成 AmbientEmitter.prefab（场景音源预制体）并在 HexGrid 中心上空放一个示例；
///  7. 校验场景 active AudioListener 唯一；保存场景。
/// </summary>
public static class AudioSetupTool
{
    const string AudioRootFolder = "Assets/Resources/Audio";
    const string BankPath = AudioRootFolder + "/AudioBank.asset";
    const string AmbientPrefabPath = "Assets/Resources/Prefabs/AmbientEmitter.prefab";
    const string EllenNetPrefabPath = "Assets/Resources/Prefabs/EllenNet.prefab";
    const string ServiceRootName = "[AudioSystem]";
    const string ExampleEmitterName = "AmbientEmitter (示例-河流)";

    // 素材子目录（相对 AudioRootFolder）——与 SoundBank autoLoadFolder 一一对应
    static readonly string[] AudioSubFolders =
    {
        "BGM",
        "SFX/Footsteps/Grass",
        "SFX/Footsteps/Stone",
        "SFX/Footsteps/Sand",
        "SFX/Footsteps/Water",
        "SFX/Splash/In",
        "SFX/Splash/Out",
        "SFX/Land",
        "SFX/Ambient/Waterfall",
        "SFX/Ambient/River",
        "SFX/Combat",
    };

    // 默认 SoundBank 条目：(key, spatial, volume, pitchMin, pitchMax, minDistance, maxDistance, autoLoad子目录)
    struct DefaultEntry
    {
        public string key;
        public bool spatial;
        public float volume;
        public float pitchMin;
        public float pitchMax;
        public float minDist;
        public float maxDist;
        public string subFolder;

        public DefaultEntry(string key, bool spatial, float volume, float pitchMin, float pitchMax,
            float minDist, float maxDist, string subFolder)
        {
            this.key = key;
            this.spatial = spatial;
            this.volume = volume;
            this.pitchMin = pitchMin;
            this.pitchMax = pitchMax;
            this.minDist = minDist;
            this.maxDist = maxDist;
            this.subFolder = subFolder;
        }
    }

    static readonly DefaultEntry[] DefaultEntries =
    {
        new DefaultEntry("bgm/main",            false, 0.60f, 1f,    1f,    1f,  40f, "BGM"),
        new DefaultEntry("footstep/grass",      true,  1.00f, 0.92f, 1.08f, 0.5f, 25f, "SFX/Footsteps/Grass"),
        new DefaultEntry("footstep/stone",      true,  1.00f, 0.92f, 1.08f, 0.5f, 25f, "SFX/Footsteps/Stone"),
        new DefaultEntry("footstep/sand",       true,  0.90f, 0.92f, 1.08f, 0.5f, 25f, "SFX/Footsteps/Sand"),
        new DefaultEntry("footstep/water",      true,  1.10f, 0.90f, 1.10f, 1f,   30f, "SFX/Footsteps/Water"),
        new DefaultEntry("splash/in",           true,  1.20f, 0.95f, 1.05f, 2f,   40f, "SFX/Splash/In"),
        new DefaultEntry("splash/out",          true,  1.10f, 0.95f, 1.05f, 2f,   40f, "SFX/Splash/Out"),
        new DefaultEntry("land",                true,  1.00f, 0.95f, 1.05f, 1f,   30f, "SFX/Land"),
        new DefaultEntry("ambient/waterfall",   true,  0.90f, 1f,    1f,    5f,   60f, "SFX/Ambient/Waterfall"),
        new DefaultEntry("ambient/river",       true,  0.80f, 1f,    1f,    3f,   40f, "SFX/Ambient/River"),
        new DefaultEntry("combat/hit",          true,  1.00f, 0.95f, 1.05f, 2f,   40f, "SFX/Combat"),
    };

    [MenuItem("Tools/音频系统/一键装配（SampleScene）")]
    public static void Setup()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != "SampleScene")
        {
            Debug.LogError("[音频] 请先打开 SampleScene 再执行装配（当前场景: " + scene.name + "）");
            return;
        }

        // 1. 素材目录树
        EnsureAudioFolders();

        // 2. AudioBank（存在则补齐缺失 key，保留用户配置）
        SoundBank bank = EnsureBank();

        // 3. [AudioSystem] 根（先清旧实例，保证可重复执行）
        foreach (AudioService old in Object.FindObjectsOfType<AudioService>(true))
        {
            if (old != null && old.gameObject != null)
            {
                Object.DestroyImmediate(old.gameObject);
            }
        }
        GameObject serviceRoot = new GameObject(ServiceRootName);
        AudioService svc = serviceRoot.AddComponent<AudioService>();
        svc.bank = bank;

        // 4. HexGrid → 地面材质桥
        HexGrid hexGrid = Object.FindObjectOfType<HexGrid>();
        HexGroundAudioBridge bridge = null;
        if (hexGrid != null)
        {
            bridge = hexGrid.GetComponent<HexGroundAudioBridge>();
            if (bridge == null)
            {
                bridge = hexGrid.gameObject.AddComponent<HexGroundAudioBridge>();
            }
            bridge.hexGrid = hexGrid;
        }
        else
        {
            Debug.LogWarning("[音频] 场景中未找到 HexGrid——脚步声需手动在 HexGrid 上挂 HexGroundAudioBridge");
        }

        // 5a. 场景活角色挂脚步组件
        int footstepCount = AttachToSceneCharacters(bridge, bank);

        // 5b. EllenNet prefab（联机 spawn 用）同步挂
        bool ellenNetUpdated = AttachToEllenNetPrefab();

        // 6. AmbientEmitter prefab + 场景示例
        bool ambientCreated = EnsureAmbientEmitterPrefab(bank);
        GameObject example = PlaceExampleEmitter(bank, hexGrid);

        // 7. AudioListener 唯一性校验
        ValidateAudioListener();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[音频] 装配完成 ✅\n" +
                  "  - 素材目录: " + AudioRootFolder + "/（BGM、SFX 分类；把音频文件丢进对应目录即自动生效）\n" +
                  "  - AudioBank: " + BankPath + "（默认 key 表 " + bank.entries.Length + " 条）\n" +
                  "  - " + ServiceRootName + ": AudioService（BGM/SFX/音量）\n" +
                  (bridge != null ? "  - HexGrid: HexGroundAudioBridge ✅\n" : "  - ⚠️ HexGrid 未找到\n") +
                  "  - 场景活角色挂 CharacterFootstepAudio x" + footstepCount + "\n" +
                  (ellenNetUpdated ? "  - EllenNet.prefab 已加 CharacterFootstepAudio\n" : "  - EllenNet.prefab 未找到（跳过；联机前先跑多人装配）\n") +
                  (ambientCreated ? "  - AmbientEmitter.prefab 已生成；示例摆件: " + ExampleEmitterName + "\n" : "") +
                  "场景已保存。素材到位后 Play 即可听到 BGM/脚步/环境音。");
    }

    // ---------------- 素材目录 ----------------

    static void EnsureAudioFolders()
    {
        EnsureFolder("Assets/Resources");
        EnsureFolder("Assets/Resources/Audio");
        foreach (string sub in AudioSubFolders)
        {
            EnsureFolder(AudioRootFolder + "/" + sub);
        }
    }

    static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath)) return;
        string parent = folderPath.Substring(0, folderPath.LastIndexOf('/'));
        string name = folderPath.Substring(folderPath.LastIndexOf('/') + 1);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    // ---------------- AudioBank ----------------

    static SoundBank EnsureBank()
    {
        SoundBank bank = AssetDatabase.LoadAssetAtPath<SoundBank>(BankPath);
        if (bank == null)
        {
            bank = ScriptableObject.CreateInstance<SoundBank>();
            AssetDatabase.CreateAsset(bank, BankPath);
            Debug.Log("[音频] 已创建 AudioBank.asset");
        }

        // 补齐缺失的默认 key（保留已有的——用户可能改过参数）
        List<SoundBank.SoundEntry> list = new List<SoundBank.SoundEntry>(bank.entries ?? new SoundBank.SoundEntry[0]);
        foreach (DefaultEntry d in DefaultEntries)
        {
            bool exists = false;
            foreach (SoundBank.SoundEntry e in list)
            {
                if (e != null && e.key == d.key) { exists = true; break; }
            }
            if (exists) continue;

            SoundBank.SoundEntry entry = new SoundBank.SoundEntry
            {
                key = d.key,
                clips = new AudioClip[0],
                autoLoadFolder = AudioRootFolder.Replace("Assets/Resources/", "") + "/" + d.subFolder,
                spatial = d.spatial,
                volume = d.volume,
                pitchMin = d.pitchMin,
                pitchMax = d.pitchMax,
                minDistance = d.minDist,
                maxDistance = d.maxDist,
            };
            list.Add(entry);
        }
        bank.entries = list.ToArray();
        bank.InvalidateLookup();
        EditorUtility.SetDirty(bank);
        return bank;
    }

    // ---------------- 场景角色 / EllenNet ----------------

    static int AttachToSceneCharacters(HexGroundAudioBridge bridge, SoundBank bank)
    {
        int count = 0;
        Character[] all = Object.FindObjectsOfType<Character>(true);
        foreach (Character c in all)
        {
            if (c == null || !c.gameObject.activeInHierarchy || !c.enabled || c.RemoteSuppressed)
            {
                continue;
            }
            GameObject go = c.gameObject;
            CharacterFootstepAudio fa = go.GetComponent<CharacterFootstepAudio>();
            if (fa == null)
            {
                fa = go.AddComponent<CharacterFootstepAudio>();
            }
            fa.character = c;
            fa.groundBridge = bridge;
            count++;
        }
        if (count == 0)
        {
            Debug.LogWarning("[音频] 场景中未找到 enabled 的 Character（活角色）——脚步组件未挂载，请手工挂到角色");
        }
        return count;
    }

    static bool AttachToEllenNetPrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EllenNetPrefabPath);
        if (prefab == null)
        {
            return false;
        }
        bool changed = false;
        string contentsPath = EllenNetPrefabPath;
        GameObject contents = PrefabUtility.LoadPrefabContents(contentsPath);
        if (contents.GetComponent<CharacterFootstepAudio>() == null)
        {
            contents.AddComponent<CharacterFootstepAudio>();
            changed = true;
        }
        if (changed)
        {
            PrefabUtility.SaveAsPrefabAsset(contents, contentsPath);
        }
        PrefabUtility.UnloadPrefabContents(contents);
        return true;
    }

    // ---------------- AmbientEmitter ----------------

    static bool EnsureAmbientEmitterPrefab(SoundBank bank)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(AmbientPrefabPath) != null)
        {
            return false; // 已存在
        }
        GameObject go = new GameObject("AmbientEmitter");
        AudioSource src = go.AddComponent<AudioSource>();
        src.spatialBlend = 1f;
        src.loop = true;
        src.playOnAwake = false;
        AmbientAudioSource ambient = go.AddComponent<AmbientAudioSource>();
        ambient.bank = bank;
        ambient.ambientKey = "ambient/waterfall";

        PrefabUtility.SaveAsPrefabAsset(go, AmbientPrefabPath);
        Object.DestroyImmediate(go);
        return true;
    }

    static GameObject PlaceExampleEmitter(SoundBank bank, HexGrid hexGrid)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AmbientPrefabPath);
        if (prefab == null)
        {
            return null;
        }

        // 清理旧示例
        GameObject old = GameObject.Find(ExampleEmitterName);
        if (old != null)
        {
            Object.DestroyImmediate(old);
        }

        Vector3 pos = hexGrid != null
            ? hexGrid.transform.position + Vector3.up * 12f
            : new Vector3(0f, 12f, 0f);
        GameObject inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        inst.name = ExampleEmitterName;
        inst.transform.position = pos;

        AmbientAudioSource ambient = inst.GetComponent<AmbientAudioSource>();
        if (ambient != null)
        {
            ambient.bank = bank;
            ambient.ambientKey = "ambient/river";
        }
        return inst;
    }

    static void ValidateAudioListener()
    {
        AudioListener[] listeners = Object.FindObjectsOfType<AudioListener>(true);
        int activeCount = 0;
        foreach (AudioListener l in listeners)
        {
            if (l.gameObject.activeInHierarchy && l.enabled)
            {
                activeCount++;
            }
        }
        if (activeCount == 0)
        {
            Debug.LogWarning("[音频] 场景没有 active 的 AudioListener——3D 音效无声。请确保主相机（CameraRig）带 AudioListener");
        }
        else if (activeCount > 1)
        {
            Debug.LogWarning("[音频] 场景有 " + activeCount + " 个 active AudioListener（应唯一，否则音量叠加/告警）。请只保留主相机的");
        }
    }

    // ---------------- 清理 ----------------

    [MenuItem("Tools/音频系统/清理装配（场景对象与示例）")]
    public static void Cleanup()
    {
        foreach (AudioService s in Object.FindObjectsOfType<AudioService>(true))
        {
            if (s != null && s.gameObject != null) Object.DestroyImmediate(s.gameObject);
        }
        foreach (HexGroundAudioBridge b in Object.FindObjectsOfType<HexGroundAudioBridge>(true))
        {
            if (b != null) Object.DestroyImmediate(b);
        }
        foreach (CharacterFootstepAudio f in Object.FindObjectsOfType<CharacterFootstepAudio>(true))
        {
            if (f != null) Object.DestroyImmediate(f);
        }
        GameObject example = GameObject.Find(ExampleEmitterName);
        if (example != null) Object.DestroyImmediate(example);

        Scene scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[音频] 已清理场景装配（[AudioSystem] / bridge / 脚步组件 / 示例发射器）。\n" +
                  "注：AudioBank.asset、AmbientEmitter.prefab、素材目录保留（含你的配置，勿删）；如需删除请手动。");
    }
}
