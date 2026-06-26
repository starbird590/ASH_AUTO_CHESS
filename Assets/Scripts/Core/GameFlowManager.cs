using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 全局游戏循环状态。
/// Result: 结算阶段
/// Intermission: 整备阶段
/// Deployment: 部署阶段
/// Battle: 战斗阶段
/// </summary>
public enum GameState
{
    Result,
    Intermission,
    Deployment,
    Battle
}

/// <summary>
/// 5x5 自走棋核心流程控制器。
/// 负责跨场景保存资金、单位状态快照，以及统一调度 Result -> Intermission -> Deployment -> Battle 的状态生命周期。
/// </summary>
public class GameFlowManager : MonoBehaviour
{
    public static GameFlowManager Instance { get; private set; }

    private const string FundsSaveKey = "GameFlow.Funds";
    private const string PopulationLimitSaveKey = "GameFlow.PopulationLimit";
    private const string CurrentStageIndexSaveKey = "GameFlow.CurrentStageIndex";

    public const int BoardWidth = BoardLayout.BattlefieldWidth;
    public const int BoardHeight = BoardLayout.BattlefieldHeight;
    public const int PlayerDeployMinY = BoardLayout.PlayerDeployMinY;
    public const int PlayerDeployMaxY = BoardLayout.PlayerDeployMaxY;
    public const int StrategicLineY = BoardLayout.StrategicLineY;
    public const int EnemyNestY = BoardLayout.EnemyNestY;

    [Header("Global Runtime Data")]
    [SerializeField] private GameState currentState = GameState.Result;
    [SerializeField] private int funds;
    [SerializeField] private int populationLimit = 5;
    [SerializeField] private int currentStageIndex = 1;
    [SerializeField] private bool loadPersistentDataOnAwake = false;

    [Header("Battle Result")]
    [SerializeField] private int defaultStageReward = 10;
    [SerializeField] private int defaultDefeatReward = 5;
    [SerializeField] private int pendingStageReward;
    [SerializeField] private float battleTimeLimitSeconds = 90f;

    [Header("Flow UI Panels")]
    [SerializeField] private StageMapUIView stageMapView;
    [SerializeField] private BattleResultUIManager battleResultUI;

    [Header("Strategic Line")]
    [SerializeField] private float strategicLineCaptureRequired = 100f;
    [SerializeField] private float strategicLineCaptureProgress;

    private readonly List<UnitLogic> playerUnits = new List<UnitLogic>();
    private readonly List<UnitLogic> newlyPurchasedUnits = new List<UnitLogic>();
    private readonly Dictionary<UnitLogic, Vector2Int> battleStartDeployPositions = new Dictionary<UnitLogic, Vector2Int>();
    private readonly Dictionary<UnitLogic, int> survivorHpSnapshot = new Dictionary<UnitLogic, int>();

    private float battleElapsedSeconds;
    private bool battleFinished;
    private bool hasPendingBattleResult;
    private bool lastBattleWasVictory;

    public GameState CurrentState => currentState;
    public int Funds => funds;
    public int PopulationLimit => populationLimit;
    public int CurrentStageIndex => currentStageIndex;
    public int DefaultStageReward => defaultStageReward;
    public int DefaultDefeatReward => defaultDefeatReward;
    public int PendingStageReward => pendingStageReward;
    public bool LastBattleWasVictory => lastBattleWasVictory;
    public float BattleElapsedSeconds => battleElapsedSeconds;
    public float BattleTimeLimitSeconds => battleTimeLimitSeconds;
    public float StrategicLineCaptureProgress => strategicLineCaptureProgress;
    public float StrategicLineCaptureRequired => strategicLineCaptureRequired;
    public bool IsStrategicLineCaptured => strategicLineCaptureProgress >= strategicLineCaptureRequired;
    public IReadOnlyList<UnitLogic> PlayerUnits => playerUnits;
    public IReadOnlyList<UnitLogic> NewlyPurchasedUnits => newlyPurchasedUnits;

    public event Action<GameState> StateEntered;
    public event Action<GameState> StateExited;
    public event Action<int> FundsChanged;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        if (loadPersistentDataOnAwake)
        {
            LoadGlobalData();
        }
    }

    private void Start()
    {
        OnStateEnter(currentState);
        ShowStageMapOnStartIfNeeded();
    }

    private void Update()
    {
        if (currentState == GameState.Battle)
        {
            TickBattle(Time.deltaTime);
        }
    }

    /// <summary>
    /// 统一状态切换入口。所有外部 UI 按钮和系统事件都应该通过这里推进流程。
    /// </summary>
    public void ChangeState(GameState nextState)
    {
        if (currentState == nextState)
        {
            return;
        }

        OnStateExit(currentState);
        currentState = nextState;
        OnStateEnter(currentState);
    }

    /// <summary>
    /// UI：结算阶段按钮。点击后进入整备阶段。
    /// </summary>
    public void ConfirmResultAndEnterIntermission()
    {
        if (currentState != GameState.Result)
        {
            Debug.LogWarning("只有在 Result 阶段才能进入 Intermission。");
            return;
        }

        if (stageMapView != null)
        {
            HideBattleResultUI();
            ShowStageMapUI();
        }
        else
        {
            Debug.LogWarning("[Stage Map] Cannot show map because the Stage Map View reference is not assigned on GameFlowManager.");
        }
    }

    /// <summary>
    /// UI：整备结束按钮。点击后公开情报并进入部署阶段。
    /// </summary>
    public void FinishIntermissionAndEnterDeployment()
    {
        if (currentState != GameState.Intermission)
        {
            Debug.LogWarning("只有在 Intermission 阶段才能进入 Deployment。");
            return;
        }

        ChangeState(GameState.Deployment);
    }

    /// <summary>
    /// UI：开战按钮。点击后进入战斗阶段。
    /// </summary>
    public void StartBattle()
    {
        if (currentState != GameState.Deployment)
        {
            Debug.LogWarning("只有在 Deployment 阶段才能进入 Battle。");
            return;
        }

        ChangeState(GameState.Battle);
    }

    /// <summary>
    /// 战斗系统：战斗结束后调用，回到结算阶段。
    /// </summary>
    public void FinishBattle(bool victory, int reward)
    {
        if (currentState != GameState.Battle || battleFinished)
        {
            return;
        }

        battleFinished = true;
        lastBattleWasVictory = victory;
        pendingStageReward = Mathf.Max(0, reward);
        hasPendingBattleResult = true;
        ChangeState(GameState.Result);
    }

    /// <summary>
    /// 整备阶段：招募新单位。新单位会被标记为本轮新购买，部署阶段允许拖拽部署。
    /// </summary>
    public bool TryRegisterPurchasedUnit(UnitLogic unit)
    {
        if (currentState != GameState.Intermission)
        {
            Debug.LogWarning("只能在 Intermission 阶段招募单位。");
            return false;
        }

        if (unit == null || playerUnits.Contains(unit))
        {
            return false;
        }

        if (playerUnits.Count >= populationLimit)
        {
            Debug.LogWarning("人口已达上限，无法继续招募。");
            return false;
        }

        if (!TrySpendFunds(unit.UnitPrice))
        {
            Debug.LogWarning("资金不足，无法招募单位。");
            return false;
        }

        unit.SetVeteran(false);
        unit.SetFaction(UnitFaction.Player);
        unit.SetPositionLocked(true);
        unit.SetCombatEnabled(false);
        playerUnits.Add(unit);
        newlyPurchasedUnits.Add(unit);
        TriggerTriplicateCheck();
        return true;
    }

    /// <summary>
    /// 商店系统专用注册入口。
    /// ShopManager 已经负责扣款与备战席容量检查，这里只登记“本轮新购买单位”，避免经济被重复扣除。
    /// </summary>
    public bool RegisterPurchasedUnitFromShop(UnitLogic unit)
    {
        if (currentState != GameState.Intermission)
        {
            Debug.LogWarning("只能在 Intermission 阶段从商店登记新单位。");
            return false;
        }

        if (unit == null || playerUnits.Contains(unit))
        {
            return false;
        }

        unit.SetVeteran(false);
        unit.SetFaction(UnitFaction.Player);
        unit.SetPositionLocked(true);
        unit.SetCombatEnabled(false);
        playerUnits.Add(unit);
        newlyPurchasedUnits.Add(unit);
        TriggerTriplicateCheck();
        return true;
    }

    /// <summary>
    /// 商店退役/销毁单位前调用，清理状态机内部引用。
    /// </summary>
    public void UnregisterUnit(UnitLogic unit)
    {
        if (unit == null)
        {
            return;
        }

        playerUnits.Remove(unit);
        newlyPurchasedUnits.Remove(unit);
        battleStartDeployPositions.Remove(unit);
        survivorHpSnapshot.Remove(unit);
    }

    /// <summary>
    /// 整备阶段：升级后勤/人口上限。
    /// </summary>
    public bool TryUpgradePopulationLimit(int cost, int increaseAmount)
    {
        if (currentState != GameState.Intermission)
        {
            Debug.LogWarning("只能在 Intermission 阶段升级后勤上限。");
            return false;
        }

        if (cost < 0 || increaseAmount <= 0 || !TrySpendFunds(cost))
        {
            return false;
        }

        populationLimit += increaseAmount;
        SaveGlobalData();
        return true;
    }

    /// <summary>
    /// 整备阶段：退役单位，回收 50% 资金并释放人口与位置。
    /// </summary>
    public bool TryRetireUnit(UnitLogic unit)
    {
        if (currentState != GameState.Intermission)
        {
            Debug.LogWarning("只能在 Intermission 阶段退役单位。");
            return false;
        }

        if (unit == null || !playerUnits.Remove(unit))
        {
            return false;
        }

        newlyPurchasedUnits.Remove(unit);
        battleStartDeployPositions.Remove(unit);
        survivorHpSnapshot.Remove(unit);

        int refund = Mathf.CeilToInt(unit.UnitPrice * 0.5f);
        AddFunds(refund);

        unit.ClearGridPosition();
        Destroy(unit.gameObject);
        return true;
    }

    /// <summary>
    /// 整备阶段：修理/补充编制。
    /// 缺失血量比例 missingHpRatio = (maxHp - currentHp) / maxHp
    /// 实际修理花费 repairCost = Ceil(unitPrice * 0.6 * missingHpRatio)
    /// </summary>
    public bool TryRepairUnit(UnitLogic unit)
    {
        if (currentState != GameState.Intermission)
        {
            Debug.LogWarning("只能在 Intermission 阶段修理单位。");
            return false;
        }

        if (unit == null || unit.MaxHp <= 0 || unit.CurrentHp >= unit.MaxHp)
        {
            return false;
        }

        int repairCost = CalculateRepairCost(unit);
        if (!TrySpendFunds(repairCost))
        {
            Debug.LogWarning("资金不足，无法修理单位。");
            return false;
        }

        unit.SetCurrentHp(unit.MaxHp);
        survivorHpSnapshot[unit] = unit.CurrentHp;
        return true;
    }

    public int CalculateRepairCost(UnitLogic unit)
    {
        if (unit == null || unit.MaxHp <= 0)
        {
            return 0;
        }

        if (unit.CurrentHp == 0)
        {
            return unit.unitPrice;
        }

        float missingHpRatio = (float)(unit.MaxHp - unit.CurrentHp) / unit.MaxHp;
        return Mathf.CeilToInt(unit.UnitPrice * 0.6f * missingHpRatio);
    }

    public bool TryGetSurvivorHpSnapshot(UnitLogic unit, out int currentHp)
    {
        if (unit == null)
        {
            currentHp = 0;
            return false;
        }

        return survivorHpSnapshot.TryGetValue(unit, out currentHp);
    }

    /// <summary>
    /// 拖拽系统调用：当前单位是否允许被移动。
    /// Intermission 全员禁止移动；Deployment 只有本轮新购买单位允许移动；Battle/Result 禁止玩家拖拽。
    /// </summary>
    public bool CanDragUnit(UnitLogic unit)
    {
        if (unit == null)
        {
            return false;
        }

        return currentState == GameState.Deployment
            && (newlyPurchasedUnits.Contains(unit) || !IsUnitOnBattlefield(unit))
            && !unit.IsVeteran
            && !unit.IsPositionLocked;
    }

    /// <summary>
    /// 部署阶段：拖拽系统完成释放时调用。
    /// 只有本轮新购买单位可以被放入我方部署区，且目标格必须为空。
    /// </summary>
    public bool TryPlaceNewPurchasedUnit(UnitLogic unit, Vector2Int targetGridPosition)
    {
        if (!CanDragUnit(unit))
        {
            return false;
        }

        if (!IsPlayerDeploymentCell(targetGridPosition) || IsCellOccupied(targetGridPosition, unit))
        {
            return false;
        }

        unit.SetGridPosition(targetGridPosition);
        return true;
    }

    /// <summary>
    /// 棋盘系统调用：判断目标格是否为我方部署区。
    /// </summary>
    public bool IsPlayerDeploymentCell(Vector2Int gridPosition)
    {
        return BoardLayout.IsInsidePlayerDeploymentArea(gridPosition);
    }

    public bool IsInsideBoard(Vector2Int gridPosition)
    {
        return BoardLayout.IsInsideBattlefield(gridPosition);
    }

    public bool IsCellOccupied(Vector2Int gridPosition, UnitLogic ignoredUnit = null)
    {
        foreach (UnitLogic unit in playerUnits)
        {
            if (unit == null || unit == ignoredUnit || !unit.HasGridPosition)
            {
                continue;
            }

            if (unit.GridPosition == gridPosition)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// UI 或情报系统调用：Intermission 必须显示 NO SIGNAL，Deployment/Battle 才允许显示敌军情报。
    /// </summary>
    public bool CanRevealEnemyIntel()
    {
        return currentState == GameState.Deployment || currentState == GameState.Battle;
    }

    public void AddStrategicLineCapture(float amount)
    {
        if (currentState != GameState.Battle || amount <= 0f || IsStrategicLineCaptured)
        {
            return;
        }

        strategicLineCaptureProgress = Mathf.Clamp(
            strategicLineCaptureProgress + amount,
            0f,
            strategicLineCaptureRequired);

        if (IsStrategicLineCaptured)
        {
            SwitchPlayerCaptureUnitsToPushLine();
        }
    }

    private void SwitchPlayerCaptureUnitsToPushLine()
    {
        for (int i = 0; i < playerUnits.Count; i++)
        {
            UnitLogic unit = playerUnits[i];
            if (unit == null
                || unit.faction != UnitFaction.Player
                || !unit.IsAlive
                || unit.playerDirective != PlayerDirective.CapturePoint)
            {
                continue;
            }

            unit.SwitchToPushLineDirective();
        }
    }

    public void AddFunds(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        funds += amount;
        FundsChanged?.Invoke(funds);
        SaveGlobalData();
    }

    public bool TrySpendFunds(int amount)
    {
        if (amount <= 0)
        {
            return true;
        }

        if (funds < amount)
        {
            return false;
        }

        funds -= amount;
        FundsChanged?.Invoke(funds);
        SaveGlobalData();
        return true;
    }

    public void SaveGlobalData()
    {
        PlayerPrefs.SetInt(FundsSaveKey, funds);
        PlayerPrefs.SetInt(PopulationLimitSaveKey, populationLimit);
        PlayerPrefs.SetInt(CurrentStageIndexSaveKey, currentStageIndex);
        PlayerPrefs.Save();
    }

    public void LoadGlobalData()
    {
        funds = PlayerPrefs.GetInt(FundsSaveKey, funds);
        populationLimit = PlayerPrefs.GetInt(PopulationLimitSaveKey, populationLimit);
        currentStageIndex = PlayerPrefs.GetInt(CurrentStageIndexSaveKey, currentStageIndex);
    }

    protected virtual void OnStateEnter(GameState state)
    {
        switch (state)
        {
            case GameState.Result:
                EnterResult();
                break;
            case GameState.Intermission:
                EnterIntermission();
                break;
            case GameState.Deployment:
                EnterDeployment();
                break;
            case GameState.Battle:
                EnterBattle();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, null);
        }

        StateEntered?.Invoke(state);
    }

    protected virtual void OnStateExit(GameState state)
    {
        switch (state)
        {
            case GameState.Result:
                ExitResult();
                break;
            case GameState.Intermission:
                ExitIntermission();
                break;
            case GameState.Deployment:
                ExitDeployment();
                break;
            case GameState.Battle:
                ExitBattle();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(state), state, null);
        }

        StateExited?.Invoke(state);
    }

    protected virtual void EnterResult()
    {
        if (hasPendingBattleResult)
        {
            AddFunds(pendingStageReward);

            SnapshotSurvivorHp();
            ResetSurvivorsToBattleStartPositions();
        }

        DisableAllPlayerCombat();
        LockAllPlayerPositions();
        newlyPurchasedUnits.Clear();
        ShowBattleResultUI();
    }

    protected virtual void ExitResult()
    {
        pendingStageReward = 0;
        hasPendingBattleResult = false;
        HideBattleResultUI();
    }

    protected virtual void EnterIntermission()
    {
        // 整备阶段继承归位后的老兵，但必须屏蔽敌军情报并禁止拖拽换位。
        DisableAllPlayerCombat();
        LockAllPlayerPositions();
        MarkSurvivorsAsVeterans();
        if (ShopManager.Instance != null)
        {
            ShopManager.Instance.HandlePostBattleRefresh();
        }

        if (SynergyManager.Instance != null)
        {
            SynergyManager.Instance.RecalculateSynergies();
        }
    }

    protected virtual void ExitIntermission()
    {
        // 离开整备时不公开任何额外数据；敌军情报由 Deployment 的 EnterDeployment 统一初始化。
    }

    protected virtual void EnterDeployment()
    {
        HideStageMapUI();
        HideBattleResultUI();

        // 部署阶段公开本关情报。EnemySpawnManager 会响应状态事件并生成敌军预警投影。
        RevealStageIntel();

        foreach (UnitLogic unit in playerUnits)
        {
            if (unit == null)
            {
                continue;
            }

            bool isNewPurchase = newlyPurchasedUnits.Contains(unit);
            unit.SetPositionLocked(!isNewPurchase && IsUnitOnBattlefield(unit));
            unit.SetCombatEnabled(false);
        }

        if (SynergyManager.Instance != null)
        {
            SynergyManager.Instance.RecalculateSynergies();
        }
    }

    protected virtual void ExitDeployment()
    {
        CaptureBattleStartPositions();
        MarkAllDeployedUnitsAsVeterans();
        newlyPurchasedUnits.Clear();
    }

    protected virtual void EnterBattle()
    {
        HideStageMapUI();
        HideBattleResultUI();

        battleElapsedSeconds = 0f;
        battleFinished = false;
        strategicLineCaptureProgress = 0f;

        foreach (UnitLogic unit in playerUnits)
        {
            if (unit != null && unit.IsAlive && IsUnitOnBattlefield(unit))
            {
                unit.ResetForBattle();
                unit.SetPositionLocked(true);
                unit.SetCombatEnabled(false);
            }
        }

        if (SynergyManager.Instance != null)
        {
            SynergyManager.Instance.RefreshAndApplySynergies();
        }

        foreach (UnitLogic unit in playerUnits)
        {
            if (unit != null && unit.IsAlive && IsUnitOnBattlefield(unit))
            {
                unit.SetCombatEnabled(true);
            }
        }

        SkillRuntimeManager.Instance.QueueBattleStart();
    }

    protected virtual void ExitBattle()
    {
        if (EnemySpawnManager.Instance != null)
        {
            EnemySpawnManager.Instance.StopBattleLifecycle(true);
        }

        DisableAllPlayerCombat();
        if (SkillRuntimeManager.HasInstance)
        {
            SkillRuntimeManager.Instance.HandleBattleEnded();
        }

        if (SynergyManager.Instance != null)
        {
            SynergyManager.Instance.ClearAllSynergyEffects();
        }

        CleanupNullPlayerUnitReferences();

        if (StageMapManager.Instance != null)
        {
            StageMapManager.Instance.MarkCurrentNodeCompleted();
        }
    }

    private void TriggerTriplicateCheck()
    {
        bool mergedAny = false;
        bool mergedThisPass;
        do
        {
            CleanupNullPlayerUnitReferences();
            mergedThisPass = TryMergeOneTriplicate();
            mergedAny |= mergedThisPass;
        }
        while (mergedThisPass);

        if (!mergedAny)
        {
            return;
        }

        if (GridManager.Instance != null)
        {
            GridManager.Instance.RefreshOccupancy();
        }

        if (ShopManager.Instance != null)
        {
            ShopManager.Instance.RefreshCurrentTotalCost();
        }

        if (SynergyManager.Instance != null)
        {
            if (currentState == GameState.Battle)
            {
                SynergyManager.Instance.RefreshAndApplySynergies();
            }
            else
            {
                SynergyManager.Instance.RecalculateSynergies();
            }
        }
    }

    private bool TryMergeOneTriplicate()
    {
        Dictionary<string, List<UnitLogic>> mergeGroups = new Dictionary<string, List<UnitLogic>>();
        for (int i = 0; i < playerUnits.Count; i++)
        {
            UnitLogic unit = playerUnits[i];
            if (unit == null || !unit.IsAlive)
            {
                continue;
            }

            string key = unit.GetDisplayName() + "|" + unit.StarLevel;
            if (!mergeGroups.TryGetValue(key, out List<UnitLogic> group))
            {
                group = new List<UnitLogic>();
                mergeGroups[key] = group;
            }

            group.Add(unit);
            if (group.Count >= 3)
            {
                MergeTriplicateGroup(group);
                return true;
            }
        }

        return false;
    }

    private void MergeTriplicateGroup(List<UnitLogic> group)
    {
        if (group == null || group.Count < 3)
        {
            return;
        }

        UnitLogic mergeSubject = SelectTriplicateMergeSubject(group);
        if (mergeSubject == null)
        {
            return;
        }

        int sacrificesRemoved = 0;
        for (int i = 0; i < group.Count && sacrificesRemoved < 2; i++)
        {
            UnitLogic sacrifice = group[i];
            if (sacrifice == null || sacrifice == mergeSubject)
            {
                continue;
            }

            playerUnits.Remove(sacrifice);
            newlyPurchasedUnits.Remove(sacrifice);
            battleStartDeployPositions.Remove(sacrifice);
            survivorHpSnapshot.Remove(sacrifice);
            Destroy(sacrifice.gameObject);
            sacrificesRemoved++;
        }

        if (sacrificesRemoved < 2)
        {
            return;
        }

        mergeSubject.ApplyStarUpgrade();
        Debug.Log("<color=#FFD700>[三合一升星]</color> " + mergeSubject.GetDisplayName() + " 升至 " + mergeSubject.StarLevel + " 星。");
    }

    private UnitLogic SelectTriplicateMergeSubject(List<UnitLogic> group)
    {
        for (int i = 0; i < group.Count; i++)
        {
            UnitLogic unit = group[i];
            if (unit != null && IsUnitOnBattlefield(unit))
            {
                return unit;
            }
        }

        for (int i = 0; i < group.Count; i++)
        {
            if (group[i] != null)
            {
                return group[i];
            }
        }

        return null;
    }

    private void ShowStageMapUI()
    {
        if (stageMapView == null)
        {
            return;
        }

        stageMapView.gameObject.SetActive(true);
        stageMapView.ShowMap();
    }

    private void ShowStageMapOnStartIfNeeded()
    {
        if (currentState != GameState.Result && currentState != GameState.Intermission)
        {
            return;
        }

        StageMapManager stageMapManager = StageMapManager.Instance;
        if (stageMapManager == null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(stageMapManager.CurrentNodeId) && !stageMapManager.CurrentNodeCompleted)
        {
            return;
        }

        HideBattleResultUI();
        ShowStageMapUI();
    }

    private void HideStageMapUI()
    {
        if (stageMapView == null)
        {
            return;
        }

        stageMapView.gameObject.SetActive(false);
    }

    private void ShowBattleResultUI()
    {
        if (battleResultUI == null)
        {
            return;
        }

        battleResultUI.gameObject.SetActive(true);
    }

    private void HideBattleResultUI()
    {
        if (battleResultUI == null)
        {
            return;
        }

        battleResultUI.gameObject.SetActive(false);
    }

    private void TickBattle(float deltaTime)
    {
        if (battleFinished)
        {
            return;
        }

        battleElapsedSeconds += deltaTime;

        if (AreAllEnemyBattlefieldUnitsDead())
        {
            FinishBattle(true, CalculateBattleReward(true));
            return;
        }

        if (AreAllPlayerBattlefieldUnitsDead())
        {
            FinishBattle(false, CalculateBattleReward(false));
            return;
        }

        if (battleElapsedSeconds >= battleTimeLimitSeconds)
        {
            FinishBattle(true, CalculateBattleReward(true));
        }
    }

    private int CalculateBattleReward(bool victory)
    {
        if (StageMapManager.Instance != null)
        {
            return StageMapManager.Instance.CalculateCurrentNodeReward(victory, defaultStageReward, defaultDefeatReward);
        }

        return victory ? defaultStageReward : defaultDefeatReward;
    }

    private void SnapshotSurvivorHp()
    {
        survivorHpSnapshot.Clear();

        foreach (UnitLogic unit in playerUnits)
        {
            if (unit != null && unit.IsAlive)
            {
                survivorHpSnapshot[unit] = unit.CurrentHp;
            }
        }
    }

    private void ResetSurvivorsToBattleStartPositions()
    {
        foreach (UnitLogic unit in playerUnits)
        {
            if (unit == null)
            {
                continue;
            }

            if (!unit.gameObject.activeSelf)
            {
                unit.gameObject.SetActive(true);
            }

            if (battleStartDeployPositions.TryGetValue(unit, out Vector2Int deployPosition))
            {
                GridManager gridManager = GridManager.Instance;
                if (gridManager != null && gridManager.battlefieldContainer != null)
                {
                    unit.transform.SetParent(gridManager.battlefieldContainer, false);
                    unit.transform.localPosition = new Vector3(deployPosition.x, deployPosition.y, 0f);
                }

                unit.SetGridPosition(deployPosition);
            }
        }
    }

    private void CaptureBattleStartPositions()
    {
        battleStartDeployPositions.Clear();

        foreach (UnitLogic unit in playerUnits)
        {
            if (unit != null && unit.IsAlive && unit.HasGridPosition && IsUnitOnBattlefield(unit))
            {
                battleStartDeployPositions[unit] = unit.GridPosition;
            }
        }
    }

    private void MarkSurvivorsAsVeterans()
    {
        foreach (UnitLogic unit in playerUnits)
        {
            if (unit != null && IsUnitOnBattlefield(unit))
            {
                unit.SetVeteran(true);
            }
        }
    }

    private void MarkAllDeployedUnitsAsVeterans()
    {
        foreach (UnitLogic unit in playerUnits)
        {
            if (unit != null && unit.IsAlive && unit.HasGridPosition && IsUnitOnBattlefield(unit))
            {
                unit.SetVeteran(true);
            }
        }
    }

    private void LockAllPlayerPositions()
    {
        foreach (UnitLogic unit in playerUnits)
        {
            if (unit != null)
            {
                unit.SetPositionLocked(IsUnitOnBattlefield(unit));
            }
        }
    }

    private void DisableAllPlayerCombat()
    {
        foreach (UnitLogic unit in playerUnits)
        {
            if (unit != null)
            {
                unit.SetCombatEnabled(false);
            }
        }
    }

    private void CleanupNullPlayerUnitReferences()
    {
        for (int i = playerUnits.Count - 1; i >= 0; i--)
        {
            UnitLogic unit = playerUnits[i];
            if (unit == null)
            {
                playerUnits.RemoveAt(i);
            }
        }
    }

    private bool AreAllPlayerBattlefieldUnitsDead()
    {
        foreach (UnitLogic unit in playerUnits)
        {
            if (unit != null && unit.IsAlive && IsUnitOnBattlefield(unit))
            {
                return false;
            }
        }

        return true;
    }

    private bool AreAllEnemyBattlefieldUnitsDead()
    {
        IReadOnlyList<UnitLogic> activeUnits = UnitLogic.ActiveUnits;
        for (int i = 0; i < activeUnits.Count; i++)
        {
            UnitLogic unit = activeUnits[i];
            if (unit == null)
            {
                continue;
            }

            if (unit.faction == UnitFaction.Enemy && unit.IsAlive && IsUnitOnBattlefield(unit))
            {
                return false;
            }
        }

        return true;
    }

    private void RevealStageIntel()
    {
        // 这里保持为空实现，给 UI/关卡系统通过 StateEntered 事件或重写此处接入。
        Debug.Log("Deployment: 敌军情报公开，战略线 Buff 与环境 Debuff 生效。");
    }

    private bool IsUnitOnBattlefield(UnitLogic unit)
    {
        if (unit == null)
        {
            return false;
        }

        GridManager gridManager = GridManager.Instance;
        if (gridManager == null || gridManager.battlefieldContainer == null)
        {
            return true;
        }

        return unit.transform.parent == gridManager.battlefieldContainer;
    }
    // 🔗 【新增桥梁】允许商店在升级时，直接修改全局的 Cost 上限并自动触发存档
    public void UpdatePopulationLimit(int newCostLimit)
    {
        populationLimit = newCostLimit;
        SaveGlobalData(); // 自动存档，下一关无缝继承
    }
}
