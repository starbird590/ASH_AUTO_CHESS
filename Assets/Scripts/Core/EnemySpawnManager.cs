using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 关卡初始敌人配置。
/// Deployment 阶段在 gridPosition 生成预警投影，Battle 阶段在同一坐标生成真实敌人。
/// </summary>
[Serializable]
public class InitialEnemyConfig
{
    [Tooltip("方便在 Inspector 中识别该敌人的备注名。")]
    public string enemyName = "Enemy";

    [Tooltip("开战时真正生成的敌军预制体，必须挂载 UnitLogic。")]
    public GameObject enemyPrefab;

    [Tooltip("敌军出现的战场网格坐标。X=0~4，Y=0~4。")]
    public Vector2Int gridPosition = new Vector2Int(2, GameFlowManager.EnemyNestY);
}

/// <summary>
/// 敌军生成生命周期管理器。
/// 负责部署预警、开战实体化、母巢 Boss 生成，以及随战斗时间缩短间隔的软狂暴出兵。
/// </summary>
public class EnemySpawnManager : MonoBehaviour
{
    public static EnemySpawnManager Instance { get; private set; }

    [Header("部署阶段预警")]
    [Tooltip("静态战争迷雾预警投影预制体，例如红色问号、红点、虚影。")]
    [SerializeField] private GameObject warningProjectionPrefab;

    [Tooltip("本关开局敌军配置。Deployment 阶段显示预警，Battle 阶段生成真实敌人。")]
    [SerializeField] private List<InitialEnemyConfig> initialEnemyConfigs = new List<InitialEnemyConfig>();

    [Header("母巢 Boss 实体")]
    [Tooltip("母巢 Boss 预制体，必须挂载 UnitLogic。")]
    [SerializeField] private GameObject hiveBossPrefab;

    [Tooltip("母巢所在坐标。通常位于顶部排 Y=4。")]
    [SerializeField] private Vector2Int hiveGridPosition = new Vector2Int(2, GameFlowManager.EnemyNestY);

    [Tooltip("是否由本管理器强制覆盖母巢核心属性，确保它符合不可移动建筑定位。")]
    [SerializeField] private bool overrideHiveStats = true;

    [Tooltip("强制覆盖时母巢最大生命值。")]
    [SerializeField] private int hiveMaxHp = 800;

    [Tooltip("强制覆盖时母巢护甲。")]
    [SerializeField] private int hiveArmor = 20;

    [Tooltip("强制覆盖时母巢威胁值。")]
    [SerializeField] private int hiveThreatValue = 50;

    [Header("软狂暴出兵")]
    [Tooltip("母巢周期孵化的敌军卡池，预制体必须挂载 UnitLogic。")]
    [SerializeField] private List<GameObject> hiveEnemyPool = new List<GameObject>();

    [Tooltip("初始出兵间隔，单位：秒。")]
    [SerializeField] private float baseSpawnInterval = 6f;

    [Tooltip("随战斗已流逝时间线性缩短出兵间隔的速度。数值越高，软狂暴越快。")]
    [SerializeField] private float enrageAcceleration = 0.05f;

    [Tooltip("出兵间隔最小值，软狂暴不会突破该下限。")]
    [SerializeField] private float minSpawnInterval = 1.2f;

    private readonly List<GameObject> warningObjects = new List<GameObject>();
    private readonly List<UnitLogic> spawnedEnemies = new List<UnitLogic>();

    private UnitLogic hiveBoss;
    private bool spawningActive;
    private bool boundToFlowManager;
    private bool warnedEmptyHivePool;
    private float spawnCountdown;
    private int lastSpawnX = -1;

    public UnitLogic HiveBoss => hiveBoss;
    public IReadOnlyList<UnitLogic> SpawnedEnemies => spawnedEnemies;
    public bool IsSpawningActive => spawningActive;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        TryBindFlowManager();
        SyncToCurrentState();
    }

    private void OnDestroy()
    {
        if (GameFlowManager.Instance != null && boundToFlowManager)
        {
            GameFlowManager.Instance.StateEntered -= HandleStateEntered;
            GameFlowManager.Instance.StateExited -= HandleStateExited;
        }
    }

    private void Update()
    {
        if (!boundToFlowManager)
        {
            TryBindFlowManager();
        }

        if (!spawningActive || GameFlowManager.Instance == null || GameFlowManager.Instance.CurrentState != GameState.Battle)
        {
            return;
        }

        spawnCountdown -= Time.deltaTime;
        if (spawnCountdown <= 0f)
        {
            SpawnHiveEnemy();
            spawnCountdown = GetCurrentSpawnInterval();
        }
    }

    /// <summary>
    /// 战斗结束时由 GameFlowManager 调用。
    /// 胜利时清理残余敌军；失败时也停止计时器，避免 Result 阶段继续出兵。
    /// </summary>
    public void StopBattleLifecycle(bool clearEnemies)
    {
        spawningActive = false;
        spawnCountdown = 0f;
        ClearWarnings();

        if (clearEnemies)
        {
            ClearSpawnedEnemies();
        }
    }

    private void TryBindFlowManager()
    {
        if (boundToFlowManager || GameFlowManager.Instance == null)
        {
            return;
        }

        GameFlowManager.Instance.StateEntered += HandleStateEntered;
        GameFlowManager.Instance.StateExited += HandleStateExited;
        boundToFlowManager = true;
    }

    private void SyncToCurrentState()
    {
        if (GameFlowManager.Instance == null)
        {
            return;
        }

        if (GameFlowManager.Instance.CurrentState == GameState.Deployment)
        {
            EnterDeploymentPreview();
        }
        else if (GameFlowManager.Instance.CurrentState == GameState.Battle)
        {
            EnterBattleSpawning();
        }
    }

    private void HandleStateEntered(GameState state)
    {
        if (state == GameState.Deployment)
        {
            EnterDeploymentPreview();
        }
        else if (state == GameState.Battle)
        {
            EnterBattleSpawning();
        }
        else if (state == GameState.Result || state == GameState.Intermission)
        {
            StopBattleLifecycle(false);
        }
    }

    private void HandleStateExited(GameState state)
    {
        if (state == GameState.Battle)
        {
            spawningActive = false;
        }
    }

    private void EnterDeploymentPreview()
    {
        spawningActive = false;
        warnedEmptyHivePool = false;
        ClearWarnings();
        ClearSpawnedEnemies();
        CreateWarningProjections();
    }

    private void EnterBattleSpawning()
    {
        ClearWarnings();
        ClearSpawnedEnemies();
        SpawnInitialEnemies();
        SpawnHiveBoss();
        spawningActive = true;
        lastSpawnX = -1;
        spawnCountdown = GetCurrentSpawnInterval();
    }

    private void CreateWarningProjections()
    {
        if (warningProjectionPrefab == null)
        {
            Debug.LogWarning("<color=orange>[敌军预警]</color> 未配置 warningProjectionPrefab，Deployment 阶段不会显示敌军预警投影。");
            return;
        }

        GridManager gridManager = GridManager.Instance;
        if (gridManager == null || gridManager.battlefieldContainer == null)
        {
            Debug.LogWarning("<color=orange>[敌军预警]</color> 找不到 battlefieldContainer，无法生成预警投影。");
            return;
        }

        for (int i = 0; i < initialEnemyConfigs.Count; i++)
        {
            InitialEnemyConfig config = initialEnemyConfigs[i];
            if (config == null || !gridManager.IsInsideBattlefield(config.gridPosition))
            {
                continue;
            }

            GameObject warning = Instantiate(warningProjectionPrefab, gridManager.battlefieldContainer);
            warning.transform.localPosition = new Vector3(config.gridPosition.x, config.gridPosition.y, 0f);
            warningObjects.Add(warning);
        }
    }

    private void SpawnInitialEnemies()
    {
        for (int i = 0; i < initialEnemyConfigs.Count; i++)
        {
            InitialEnemyConfig config = initialEnemyConfigs[i];
            if (config == null || config.enemyPrefab == null)
            {
                Debug.LogWarning("<color=orange>[敌军生成]</color> 初始敌人配置缺少 enemyPrefab，已跳过。");
                continue;
            }

            SpawnEnemyAt(config.enemyPrefab, config.gridPosition, false);
        }
    }

    private void SpawnHiveBoss()
    {
        hiveBoss = null;
        if (hiveBossPrefab == null)
        {
            Debug.LogError("<color=red>[母巢生成失败]</color> 未配置 hiveBossPrefab，战斗胜利的斩首判定将无法触发。");
            return;
        }

        hiveBoss = SpawnEnemyAt(hiveBossPrefab, hiveGridPosition, true);
        if (hiveBoss == null)
        {
            return;
        }

        if (overrideHiveStats)
        {
            hiveBoss.maxHp = Mathf.Max(1, hiveMaxHp);
            hiveBoss.currentHp = hiveBoss.maxHp;
            hiveBoss.armor = Mathf.Max(0, hiveArmor);
            hiveBoss.moveSpeed = 0f;
            hiveBoss.threatValue = Mathf.Max(0, hiveThreatValue);
        }
    }

    private void SpawnHiveEnemy()
    {
        if (hiveEnemyPool.Count == 0)
        {
            if (!warnedEmptyHivePool)
            {
                warnedEmptyHivePool = true;
                Debug.LogWarning("<color=orange>[软狂暴出兵]</color> hiveEnemyPool 为空，母巢无法周期出兵。");
            }

            return;
        }

        GameObject prefab = hiveEnemyPool[UnityEngine.Random.Range(0, hiveEnemyPool.Count)];
        if (prefab == null)
        {
            return;
        }

        int spawnX = RollSpawnXWithoutRepeat();
        SpawnEnemyAt(prefab, new Vector2Int(spawnX, GameFlowManager.EnemyNestY), false);
    }

    private UnitLogic SpawnEnemyAt(GameObject prefab, Vector2Int gridPosition, bool isHive)
    {
        GridManager gridManager = GridManager.Instance;
        if (gridManager == null || gridManager.battlefieldContainer == null)
        {
            Debug.LogWarning("<color=orange>[敌军生成]</color> 找不到 battlefieldContainer，无法生成敌人。");
            return null;
        }

        if (!gridManager.IsInsideBattlefield(gridPosition))
        {
            Debug.LogWarning("<color=orange>[敌军生成]</color> 坐标 " + gridPosition + " 不在 5x5 战场内，已跳过。");
            return null;
        }

        GameObject enemyObject = Instantiate(prefab, gridManager.battlefieldContainer);
        enemyObject.transform.localPosition = new Vector3(gridPosition.x, gridPosition.y, 0f);

        UnitLogic unit = enemyObject.GetComponent<UnitLogic>();
        if (unit == null)
        {
            Debug.LogError("<color=red>[敌军生成失败]</color> 敌军预制体缺少 UnitLogic，已销毁实例。");
            Destroy(enemyObject);
            return null;
        }

        unit.SetFaction(UnitFaction.Enemy);
        unit.SetGridPosition(gridPosition);
        unit.SetVeteran(false);
        unit.SetPositionLocked(true);
        unit.ResetForBattle();
        unit.SetCombatEnabled(true);
        spawnedEnemies.Add(unit);

        if (isHive)
        {
            hiveBoss = unit;
        }

        return unit;
    }

    private int RollSpawnXWithoutRepeat()
    {
        int width = GameFlowManager.BoardWidth;
        if (width <= 1)
        {
            lastSpawnX = 0;
            return 0;
        }

        int spawnX = UnityEngine.Random.Range(0, width);
        if (spawnX == lastSpawnX)
        {
            spawnX = (spawnX + UnityEngine.Random.Range(1, width)) % width;
        }

        lastSpawnX = spawnX;
        return spawnX;
    }

    private float GetCurrentSpawnInterval()
    {
        float elapsed = GameFlowManager.Instance != null ? GameFlowManager.Instance.BattleElapsedSeconds : 0f;
        float interval = baseSpawnInterval - elapsed * enrageAcceleration;
        return Mathf.Max(minSpawnInterval, interval);
    }

    private void ClearWarnings()
    {
        for (int i = warningObjects.Count - 1; i >= 0; i--)
        {
            if (warningObjects[i] != null)
            {
                Destroy(warningObjects[i]);
            }
        }

        warningObjects.Clear();
    }

    private void ClearSpawnedEnemies()
    {
        for (int i = spawnedEnemies.Count - 1; i >= 0; i--)
        {
            UnitLogic unit = spawnedEnemies[i];
            if (unit != null)
            {
                Destroy(unit.gameObject);
            }
        }

        spawnedEnemies.Clear();
        hiveBoss = null;
    }
}
