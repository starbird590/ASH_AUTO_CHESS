using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Initial enemy spawn data for one battlefield slot.
/// Deployment shows warning projections, Battle spawns the real enemy at the same grid position.
/// </summary>
[Serializable]
public class InitialEnemyConfig
{
    [Tooltip("Inspector note name for this enemy.")]
    public string enemyName = "Enemy";

    [Tooltip("Enemy prefab spawned at battle start. Must have UnitLogic.")]
    public GameObject enemyPrefab;

    [Tooltip("Battlefield grid position. X=0~4, Y=0~4.")]
    public Vector2Int gridPosition = new Vector2Int(2, GameFlowManager.EnemyNestY);
}

[Serializable]
public class EnemyWaveConfig
{
    [Tooltip("Battle wave ID rolled by MapNodeSO.")]
    public int waveId;

    [Tooltip("Initial enemies for this wave. Deployment shows warnings, Battle spawns real enemies.")]
    public List<InitialEnemyConfig> initialEnemyConfigs = new List<InitialEnemyConfig>();

    [Header("Hive Boss")]
    [Tooltip("Hive Boss prefab for this wave. Leave empty if this wave has no boss.")]
    public GameObject hiveBossPrefab;

    [Tooltip("Hive Boss battlefield grid position.")]
    public Vector2Int hiveGridPosition = new Vector2Int(2, GameFlowManager.EnemyNestY);

    [Tooltip("Hive Boss max HP override for this wave.")]
    public int hiveMaxHp = 800;

    [Tooltip("Hive Boss armor override for this wave.")]
    public int hiveArmor = 20;

    [Tooltip("Hive Boss threat value override for this wave.")]
    public int hiveThreatValue = 50;

    [Header("Soft Enrage Spawning")]
    [Tooltip("Enemy prefab pool periodically spawned by the Hive Boss in this wave. Prefabs must have UnitLogic.")]
    public List<GameObject> hiveEnemyPool = new List<GameObject>();

    [Tooltip("Initial spawn interval for this wave, in seconds.")]
    public float baseSpawnInterval = 6f;

    [Tooltip("Linear interval reduction per elapsed battle second. Use 0 for constant spawn speed.")]
    public float enrageAcceleration = 0.05f;

    [Tooltip("Minimum spawn interval for this wave.")]
    public float minSpawnInterval = 1.2f;
}

/// <summary>
/// Enemy spawn lifecycle manager.
/// Handles deployment warnings, battle start enemies, wave-specific Hive Boss spawning, and Hive-bound soft enrage spawns.
/// </summary>
public class EnemySpawnManager : MonoBehaviour
{
    public static EnemySpawnManager Instance { get; private set; }

    [Header("Deployment Warning")]
    [Tooltip("Warning projection prefab shown during Deployment.")]
    [SerializeField] private GameObject warningProjectionPrefab;

    [Tooltip("Fallback initial enemy configs used when no map wave is selected or matched.")]
    [SerializeField] private List<InitialEnemyConfig> initialEnemyConfigs = new List<InitialEnemyConfig>();

    [Header("Map Wave Configs")]
    [Tooltip("Wave configs keyed by MapNodeSO battle wave ID.")]
    [SerializeField] private List<EnemyWaveConfig> enemyWaveConfigs = new List<EnemyWaveConfig>();

    private readonly List<GameObject> warningObjects = new List<GameObject>();
    private readonly List<UnitLogic> spawnedEnemies = new List<UnitLogic>();

    private UnitLogic hiveBoss;
    private bool spawningActive;
    private bool boundToFlowManager;
    private bool warnedEmptyHivePool;
    private bool warnedMissingWaveConfig;
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

    private void OnValidate()
    {
        if (initialEnemyConfigs == null)
        {
            initialEnemyConfigs = new List<InitialEnemyConfig>();
        }

        if (enemyWaveConfigs == null)
        {
            enemyWaveConfigs = new List<EnemyWaveConfig>();
            return;
        }

        for (int i = 0; i < enemyWaveConfigs.Count; i++)
        {
            EnemyWaveConfig waveConfig = enemyWaveConfigs[i];
            if (waveConfig == null)
            {
                continue;
            }

            waveConfig.hiveMaxHp = Mathf.Max(1, waveConfig.hiveMaxHp);
            waveConfig.hiveArmor = Mathf.Max(0, waveConfig.hiveArmor);
            waveConfig.hiveThreatValue = Mathf.Max(0, waveConfig.hiveThreatValue);
            waveConfig.baseSpawnInterval = Mathf.Max(0.1f, waveConfig.baseSpawnInterval);
            waveConfig.enrageAcceleration = Mathf.Max(0f, waveConfig.enrageAcceleration);
            waveConfig.minSpawnInterval = Mathf.Max(0.1f, waveConfig.minSpawnInterval);

            if (waveConfig.initialEnemyConfigs == null)
            {
                waveConfig.initialEnemyConfigs = new List<InitialEnemyConfig>();
            }

            if (waveConfig.hiveEnemyPool == null)
            {
                waveConfig.hiveEnemyPool = new List<GameObject>();
            }
        }
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

        EnemyWaveConfig currentWaveConfig = GetCurrentWaveConfig();
        if (currentWaveConfig == null)
        {
            return;
        }

        if (hiveBoss == null || !hiveBoss.IsAlive)
        {
            return;
        }

        spawnCountdown -= Time.deltaTime;
        if (spawnCountdown <= 0f)
        {
            SpawnHiveEnemy(currentWaveConfig);
            spawnCountdown = GetCurrentSpawnInterval(currentWaveConfig);
        }
    }

    /// <summary>
    /// Called by GameFlowManager when battle ends.
    /// Stops spawning and optionally clears all enemies.
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
        warnedMissingWaveConfig = false;
        ClearWarnings();
        ClearSpawnedEnemies();
        CreateWarningProjections();
    }

    private void EnterBattleSpawning()
    {
        warnedEmptyHivePool = false;
        warnedMissingWaveConfig = false;
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
            Debug.LogWarning("<color=orange>[Enemy Warning]</color> warningProjectionPrefab is not assigned. Deployment warnings will not be shown.");
            return;
        }

        GridManager gridManager = GridManager.Instance;
        if (gridManager == null || gridManager.battlefieldContainer == null)
        {
            Debug.LogWarning("<color=orange>[Enemy Warning]</color> Missing battlefieldContainer. Cannot create warning projections.");
            return;
        }

        List<InitialEnemyConfig> configs = GetCurrentInitialEnemyConfigs();
        for (int i = 0; i < configs.Count; i++)
        {
            InitialEnemyConfig config = configs[i];
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
        List<InitialEnemyConfig> configs = GetCurrentInitialEnemyConfigs();
        for (int i = 0; i < configs.Count; i++)
        {
            InitialEnemyConfig config = configs[i];
            if (config == null || config.enemyPrefab == null)
            {
                Debug.LogWarning("<color=orange>[Enemy Spawn]</color> Initial enemy config is missing enemyPrefab. Skipped.");
                continue;
            }

            SpawnEnemyAt(config.enemyPrefab, config.gridPosition, false);
        }
    }

    private List<InitialEnemyConfig> GetCurrentInitialEnemyConfigs()
    {
        EnemyWaveConfig waveConfig = GetCurrentWaveConfig();
        if (waveConfig != null && waveConfig.initialEnemyConfigs != null && waveConfig.initialEnemyConfigs.Count > 0)
        {
            return waveConfig.initialEnemyConfigs;
        }

        return initialEnemyConfigs;
    }

    private EnemyWaveConfig GetCurrentWaveConfig()
    {
        int currentWaveId = StageMapManager.Instance != null ? StageMapManager.Instance.CurrentBattleWaveId : -1;
        if (currentWaveId < 0 || enemyWaveConfigs == null)
        {
            return null;
        }

        for (int i = 0; i < enemyWaveConfigs.Count; i++)
        {
            EnemyWaveConfig waveConfig = enemyWaveConfigs[i];
            if (waveConfig != null && waveConfig.waveId == currentWaveId)
            {
                return waveConfig;
            }
        }

        if (!warnedMissingWaveConfig)
        {
            warnedMissingWaveConfig = true;
            Debug.LogWarning("<color=orange>[Map Wave]</color> Cannot find enemy wave config for waveId=" + currentWaveId + ". Fallback initialEnemyConfigs will be used where possible.");
        }

        return null;
    }

    private void SpawnHiveBoss()
    {
        hiveBoss = null;
        EnemyWaveConfig waveConfig = GetCurrentWaveConfig();
        if (waveConfig == null || waveConfig.hiveBossPrefab == null)
        {
            return;
        }

        hiveBoss = SpawnEnemyAt(waveConfig.hiveBossPrefab, waveConfig.hiveGridPosition, true);
        if (hiveBoss == null)
        {
            return;
        }

        hiveBoss.maxHp = Mathf.Max(1, waveConfig.hiveMaxHp);
        hiveBoss.currentHp = hiveBoss.maxHp;
        hiveBoss.armor = Mathf.Max(0, waveConfig.hiveArmor);
        hiveBoss.moveSpeed = 0f;
        hiveBoss.threatValue = Mathf.Max(0, waveConfig.hiveThreatValue);
    }

    private void SpawnHiveEnemy(EnemyWaveConfig waveConfig)
    {
        if (waveConfig == null)
        {
            return;
        }

        if (waveConfig.hiveEnemyPool == null || waveConfig.hiveEnemyPool.Count == 0)
        {
            if (!warnedEmptyHivePool)
            {
                warnedEmptyHivePool = true;
                Debug.LogWarning("<color=orange>[Soft Enrage Spawn]</color> Current wave hiveEnemyPool is empty. Hive periodic spawning is skipped.");
            }

            return;
        }

        GameObject prefab = waveConfig.hiveEnemyPool[UnityEngine.Random.Range(0, waveConfig.hiveEnemyPool.Count)];
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
            Debug.LogWarning("<color=orange>[Enemy Spawn]</color> Missing battlefieldContainer. Cannot spawn enemy.");
            return null;
        }

        if (!gridManager.IsInsideBattlefield(gridPosition))
        {
            Debug.LogWarning("<color=orange>[Enemy Spawn]</color> Grid position " + gridPosition + " is outside the 5x5 battlefield. Skipped.");
            return null;
        }

        GameObject enemyObject = Instantiate(prefab, gridManager.battlefieldContainer);
        enemyObject.transform.localPosition = new Vector3(gridPosition.x, gridPosition.y, 0f);

        UnitLogic unit = enemyObject.GetComponent<UnitLogic>();
        if (unit == null)
        {
            Debug.LogError("<color=red>[Enemy Spawn Failed]</color> Enemy prefab is missing UnitLogic. Instance destroyed.");
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

    private float GetCurrentSpawnInterval(EnemyWaveConfig waveConfig = null)
    {
        EnemyWaveConfig currentWaveConfig = waveConfig != null ? waveConfig : GetCurrentWaveConfig();
        if (currentWaveConfig == null)
        {
            return 0.1f;
        }

        float elapsed = GameFlowManager.Instance != null ? GameFlowManager.Instance.BattleElapsedSeconds : 0f;
        float interval = currentWaveConfig.baseSpawnInterval - elapsed * currentWaveConfig.enrageAcceleration;
        return Mathf.Max(currentWaveConfig.minSpawnInterval, interval);
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
