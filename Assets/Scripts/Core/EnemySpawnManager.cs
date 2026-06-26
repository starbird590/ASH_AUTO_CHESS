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
    public Vector2Int gridPosition = BoardLayout.EnemyNestCenter;
}

[Serializable]
public class EnemyWaveConfig
{
    [Tooltip("Battle wave ID rolled by MapNodeSO or MapNode.csv.")]
    public string waveId;

    [Tooltip("Initial enemies for this wave. Deployment shows warnings, Battle spawns real enemies.")]
    public List<InitialEnemyConfig> initialEnemyConfigs = new List<InitialEnemyConfig>();

    [Header("Hive Boss")]
    [Tooltip("Hive Boss prefab for this wave. Leave empty if this wave has no boss.")]
    public GameObject hiveBossPrefab;

    [Tooltip("Hive Boss battlefield grid position.")]
    public Vector2Int hiveGridPosition = BoardLayout.EnemyNestCenter;

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

    [Header("Fallback Inspector Wave Configs")]
    [Tooltip("Fallback wave configs keyed by MapNodeSO or MapNode.csv battle wave ID.")]
    [SerializeField] private List<EnemyWaveConfig> enemyWaveConfigs = new List<EnemyWaveConfig>();

    private readonly List<GameObject> warningObjects = new List<GameObject>();
    private readonly List<UnitLogic> spawnedEnemies = new List<UnitLogic>();

    private UnitLogic hiveBoss;
    private bool spawningActive;
    private bool boundToFlowManager;
    private bool warnedEmptyHivePool;
    private bool warnedMissingWaveConfig;
    private bool warnedMissingTableWave;
    private bool warnedMissingUnitDataManager;
    private float spawnCountdown;
    private int lastSpawnX = -1;

    public UnitLogic HiveBoss => hiveBoss;
    public IReadOnlyList<UnitLogic> SpawnedEnemies => spawnedEnemies;
    public bool IsSpawningActive => spawningActive;

    private struct ActiveWaveData
    {
        public WaveNodeData TableWave;
        public EnemyWaveConfig FallbackWave;
    }

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

            if (!string.IsNullOrWhiteSpace(waveConfig.waveId))
            {
                waveConfig.waveId = waveConfig.waveId.Trim();
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

        if (hiveBoss == null || !hiveBoss.IsAlive)
        {
            return;
        }

        spawnCountdown -= Time.deltaTime;
        if (spawnCountdown > 0f)
        {
            return;
        }

        ActiveWaveData waveData = GetActiveWaveData(true);
        if (waveData.TableWave != null)
        {
            SpawnHiveEnemy(waveData.TableWave);
            spawnCountdown = GetCurrentSpawnInterval(waveData.TableWave);
            return;
        }

        if (waveData.FallbackWave != null)
        {
            SpawnHiveEnemy(waveData.FallbackWave);
            spawnCountdown = GetCurrentSpawnInterval(waveData.FallbackWave);
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
        warnedMissingTableWave = false;
        warnedMissingUnitDataManager = false;
        ClearWarnings();
        ClearSpawnedEnemies();
        CreateWarningProjections();
    }

    private void EnterBattleSpawning()
    {
        warnedEmptyHivePool = false;
        warnedMissingWaveConfig = false;
        warnedMissingTableWave = false;
        warnedMissingUnitDataManager = false;
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

        ActiveWaveData waveData = GetActiveWaveData(true);
        if (waveData.TableWave != null)
        {
            for (int i = 0; i < waveData.TableWave.InitialEnemyConfigs.Count; i++)
            {
                CreateWarningAt(gridManager, waveData.TableWave.InitialEnemyConfigs[i].GridPosition);
            }

            return;
        }

        List<InitialEnemyConfig> configs = GetCurrentInitialEnemyConfigs(waveData.FallbackWave);
        for (int i = 0; i < configs.Count; i++)
        {
            InitialEnemyConfig config = configs[i];
            if (config != null)
            {
                CreateWarningAt(gridManager, config.gridPosition);
            }
        }
    }

    private void CreateWarningAt(GridManager gridManager, Vector2Int gridPosition)
    {
        if (gridManager == null || !gridManager.IsInsideBattlefield(gridPosition))
        {
            return;
        }

        GameObject warning = Instantiate(warningProjectionPrefab, gridManager.battlefieldContainer);
        warning.transform.localPosition = new Vector3(gridPosition.x, gridPosition.y, 0f);
        warningObjects.Add(warning);
    }

    private void SpawnInitialEnemies()
    {
        ActiveWaveData waveData = GetActiveWaveData(true);
        if (waveData.TableWave != null)
        {
            for (int i = 0; i < waveData.TableWave.InitialEnemyConfigs.Count; i++)
            {
                WaveEnemyConfigData config = waveData.TableWave.InitialEnemyConfigs[i];
                if (config != null)
                {
                    SpawnEnemyAtChessId(config.ChessId, config.GridPosition, false);
                }
            }

            return;
        }

        List<InitialEnemyConfig> configs = GetCurrentInitialEnemyConfigs(waveData.FallbackWave);
        for (int i = 0; i < configs.Count; i++)
        {
            InitialEnemyConfig config = configs[i];
            if (config == null || config.enemyPrefab == null)
            {
                Debug.LogWarning("<color=orange>[Enemy Spawn]</color> Initial enemy config is missing enemyPrefab. Skipped.");
                continue;
            }

            SpawnEnemyAtPrefab(config.enemyPrefab, config.gridPosition, false);
        }
    }

    private List<InitialEnemyConfig> GetCurrentInitialEnemyConfigs(EnemyWaveConfig waveConfig)
    {
        if (waveConfig != null && waveConfig.initialEnemyConfigs != null && waveConfig.initialEnemyConfigs.Count > 0)
        {
            return waveConfig.initialEnemyConfigs;
        }

        return initialEnemyConfigs;
    }

    private ActiveWaveData GetActiveWaveData(bool warnIfMissingTableWave)
    {
        WaveNodeData tableWave = GetCurrentTableWaveData(out bool hasWaveTableRows);
        if (tableWave != null)
        {
            return new ActiveWaveData { TableWave = tableWave };
        }

        if (hasWaveTableRows)
        {
            if (warnIfMissingTableWave)
            {
                WarnMissingTableWaveOnce();
            }

            return new ActiveWaveData();
        }

        return new ActiveWaveData { FallbackWave = GetCurrentFallbackWaveConfig() };
    }

    private WaveNodeData GetCurrentTableWaveData(out bool hasWaveTableRows)
    {
        hasWaveTableRows = false;
        if (StageMapManager.Instance == null)
        {
            return null;
        }

        return StageMapManager.Instance.TryGetCurrentWaveNode(out WaveNodeData waveData, out hasWaveTableRows) ? waveData : null;
    }

    private EnemyWaveConfig GetCurrentFallbackWaveConfig()
    {
        string currentWaveId = StageMapManager.Instance != null ? StageMapManager.Instance.CurrentBattleWaveId : string.Empty;
        if (string.IsNullOrWhiteSpace(currentWaveId) || enemyWaveConfigs == null)
        {
            return null;
        }

        for (int i = 0; i < enemyWaveConfigs.Count; i++)
        {
            EnemyWaveConfig waveConfig = enemyWaveConfigs[i];
            if (waveConfig != null && string.Equals(waveConfig.waveId, currentWaveId, StringComparison.OrdinalIgnoreCase))
            {
                return waveConfig;
            }
        }

        if (!warnedMissingWaveConfig)
        {
            warnedMissingWaveConfig = true;
            Debug.LogWarning("<color=orange>[Map Wave]</color> Cannot find fallback enemy wave config for waveId=" + currentWaveId + ". Fallback initialEnemyConfigs will be used where possible.");
        }

        return null;
    }

    private void SpawnHiveBoss()
    {
        hiveBoss = null;

        ActiveWaveData waveData = GetActiveWaveData(true);
        if (waveData.TableWave != null)
        {
            SpawnTableHiveBoss(waveData.TableWave);
            return;
        }

        EnemyWaveConfig waveConfig = waveData.FallbackWave;
        if (waveConfig == null || waveConfig.hiveBossPrefab == null)
        {
            return;
        }

        hiveBoss = SpawnEnemyAtPrefab(waveConfig.hiveBossPrefab, waveConfig.hiveGridPosition, true);
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

    private void SpawnTableHiveBoss(WaveNodeData tableWave)
    {
        if (tableWave == null || !tableWave.HasBoss || tableWave.BossInfo == null || !tableWave.BossInfo.HasBossChessId)
        {
            return;
        }

        hiveBoss = SpawnEnemyAtChessId(tableWave.BossInfo.BossChessId, tableWave.BossInfo.GridPosition, true);
        if (hiveBoss != null)
        {
            // 母巢本身是据点，不参与普通前进。
            hiveBoss.moveSpeed = 0f;
        }
    }

    private void SpawnHiveEnemy(WaveNodeData tableWave)
    {
        if (tableWave == null || tableWave.BossInfo == null || tableWave.BossInfo.SpawnPoolChessIds == null || tableWave.BossInfo.SpawnPoolChessIds.Count == 0)
        {
            WarnEmptyHivePoolOnce();
            return;
        }

        string chessId = tableWave.BossInfo.SpawnPoolChessIds[UnityEngine.Random.Range(0, tableWave.BossInfo.SpawnPoolChessIds.Count)];
        if (string.IsNullOrWhiteSpace(chessId))
        {
            return;
        }

        int spawnX = RollSpawnXWithoutRepeat();
        SpawnEnemyAtChessId(chessId, new Vector2Int(spawnX, BoardLayout.EnemyNestY), false);
    }

    private void SpawnHiveEnemy(EnemyWaveConfig waveConfig)
    {
        if (waveConfig == null)
        {
            return;
        }

        if (waveConfig.hiveEnemyPool == null || waveConfig.hiveEnemyPool.Count == 0)
        {
            WarnEmptyHivePoolOnce();
            return;
        }

        GameObject prefab = waveConfig.hiveEnemyPool[UnityEngine.Random.Range(0, waveConfig.hiveEnemyPool.Count)];
        if (prefab == null)
        {
            return;
        }

        int spawnX = RollSpawnXWithoutRepeat();
        SpawnEnemyAtPrefab(prefab, new Vector2Int(spawnX, BoardLayout.EnemyNestY), false);
    }

    private UnitLogic SpawnEnemyAtChessId(string chessId, Vector2Int gridPosition, bool isHive)
    {
        if (!ValidateSpawnRequest(chessId, gridPosition))
        {
            return null;
        }

        UnitDataManager unitDataManager = UnitDataManager.Instance;
        if (unitDataManager == null)
        {
            if (!warnedMissingUnitDataManager)
            {
                warnedMissingUnitDataManager = true;
                Debug.LogWarning("<color=orange>[Enemy Spawn]</color> UnitDataManager is missing. Cannot spawn ChessId=" + chessId + " from WaveNode.csv.");
            }

            return null;
        }

        UnitLogic unit = unitDataManager.SpawnUnitOnBoard(chessId, Vector3.zero, UnitFaction.Enemy);
        if (unit == null)
        {
            return null;
        }

        return PrepareSpawnedEnemy(unit, gridPosition, isHive);
    }

    private UnitLogic SpawnEnemyAtPrefab(GameObject prefab, Vector2Int gridPosition, bool isHive)
    {
        if (prefab == null || !ValidateSpawnRequest(prefab.name, gridPosition))
        {
            return null;
        }

        GridManager gridManager = GridManager.Instance;
        GameObject enemyObject = Instantiate(prefab, gridManager.battlefieldContainer);
        UnitLogic unit = enemyObject.GetComponent<UnitLogic>();
        if (unit == null)
        {
            Debug.LogError("<color=red>[Enemy Spawn Failed]</color> Enemy prefab is missing UnitLogic. Instance destroyed.");
            Destroy(enemyObject);
            return null;
        }

        return PrepareSpawnedEnemy(unit, gridPosition, isHive);
    }

    private bool ValidateSpawnRequest(string spawnLabel, Vector2Int gridPosition)
    {
        GridManager gridManager = GridManager.Instance;
        if (gridManager == null || gridManager.battlefieldContainer == null)
        {
            Debug.LogWarning("<color=orange>[Enemy Spawn]</color> Missing battlefieldContainer. Cannot spawn enemy.");
            return false;
        }

        if (!gridManager.IsInsideBattlefield(gridPosition))
        {
            Debug.LogWarning("<color=orange>[Enemy Spawn]</color> Grid position " + gridPosition + " is outside the 5x5 battlefield. Skipped: " + spawnLabel);
            return false;
        }

        return true;
    }

    private UnitLogic PrepareSpawnedEnemy(UnitLogic unit, Vector2Int gridPosition, bool isHive)
    {
        GridManager gridManager = GridManager.Instance;
        if (unit == null || gridManager == null || gridManager.battlefieldContainer == null)
        {
            return null;
        }

        unit.transform.SetParent(gridManager.battlefieldContainer, false);
        unit.transform.localPosition = new Vector3(gridPosition.x, gridPosition.y, 0f);
        unit.SetFaction(UnitFaction.Enemy);
        unit.SetGridPosition(gridPosition);
        unit.SetVeteran(false);
        unit.SetSummoned(false);
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
        int width = BoardLayout.BattlefieldWidth;
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
        ActiveWaveData waveData = GetActiveWaveData(false);
        if (waveData.TableWave != null)
        {
            return GetCurrentSpawnInterval(waveData.TableWave);
        }

        if (waveData.FallbackWave != null)
        {
            return GetCurrentSpawnInterval(waveData.FallbackWave);
        }

        return 0.1f;
    }

    private void WarnMissingTableWaveOnce()
    {
        if (warnedMissingTableWave)
        {
            return;
        }

        warnedMissingTableWave = true;
        string currentWaveId = StageMapManager.Instance != null ? StageMapManager.Instance.CurrentBattleWaveId : string.Empty;
        if (string.IsNullOrWhiteSpace(currentWaveId))
        {
            Debug.LogWarning("<color=orange>[WaveNode.csv]</color> WaveNode.csv has loaded wave rows, so Inspector enemy wave fallbacks are ignored. No current battle wave is selected.");
            return;
        }

        Debug.LogWarning("<color=orange>[WaveNode.csv]</color> WaveNode.csv has loaded wave rows, so it is the only enemy wave data source. Cannot find waveId=" + currentWaveId + "; Inspector enemy wave fallbacks are ignored.");
    }

    private float GetCurrentSpawnInterval(WaveNodeData tableWave)
    {
        if (tableWave == null || tableWave.BossSpawn == null)
        {
            return 0.1f;
        }

        float elapsed = GameFlowManager.Instance != null ? GameFlowManager.Instance.BattleElapsedSeconds : 0f;
        float interval = tableWave.BossSpawn.BaseSpawnInterval - elapsed * tableWave.BossSpawn.EnrageAcceleration;
        return Mathf.Max(tableWave.BossSpawn.MinSpawnInterval, interval);
    }

    private float GetCurrentSpawnInterval(EnemyWaveConfig waveConfig)
    {
        if (waveConfig == null)
        {
            return 0.1f;
        }

        float elapsed = GameFlowManager.Instance != null ? GameFlowManager.Instance.BattleElapsedSeconds : 0f;
        float interval = waveConfig.baseSpawnInterval - elapsed * waveConfig.enrageAcceleration;
        return Mathf.Max(waveConfig.minSpawnInterval, interval);
    }

    private void WarnEmptyHivePoolOnce()
    {
        if (warnedEmptyHivePool)
        {
            return;
        }

        warnedEmptyHivePool = true;
        Debug.LogWarning("<color=orange>[Soft Enrage Spawn]</color> Current wave hive spawn pool is empty. Hive periodic spawning is skipped.");
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
