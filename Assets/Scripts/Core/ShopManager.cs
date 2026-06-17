using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
/// <summary>
/// 商店与经济平衡控制器。
/// 负责资金、刷新、升级、概率抽卡、备战席落座、退役变现、补编修理，以及当前上阵 Cost 统计。
/// </summary>
public class ShopManager : MonoBehaviour
{
    public static ShopManager Instance { get; private set; }

    private const int ShopSlotCount = 5;
    private const int MaxShopLevel = 5;
    private const int UpgradeExpPurchaseCost = 4;
    private const int UpgradeExpPerPurchase = 4;

    [Header("⚙️ Economy Settings")]
    [SerializeField] private int initialFunds = 30;
    [SerializeField] private int refreshCost = 2;

    [Header("📊 Level & Cost Settings")]
    [SerializeField] private int[] upgradeLevelCosts = { 10, 15, 20, 25 };
    [SerializeField] private int[] maxCostLimits = { 5, 7, 9, 11, 13 };

    [Header("📦 Card Pool Settings")]
    public List<string> unitSupplyPool = new List<string>();

    [SerializeField] private bool buildSupplyPoolOnAwake = true;

    private readonly Dictionary<string, int> unitRemainingCounts = new Dictionary<string, int>();
    private readonly Dictionary<string, UnitLogicDataSO> unitDataByChessId = new Dictionary<string, UnitLogicDataSO>();
    private ShopManagerDataSO shopManagerData;

    [Header("Runtime Shop State")]
    [SerializeField] private int funds;
    [SerializeField] private int level = 1;
    [SerializeField] private int currentExp;
    [SerializeField] private int currentTotalCost;
    [SerializeField] private GameObject[] shopSlots = new GameObject[ShopSlotCount];
    [SerializeField] private string[] shopSlotChessIds = new string[ShopSlotCount];
    [SerializeField] private bool[] soldOutSlots = new bool[ShopSlotCount];
    [SerializeField] private bool isShopLocked;

    public int Funds => funds;
    public int Level => level;
    public int CurrentExp => currentExp;
    public int CurrentUpgradeExpRequirement => GetCurrentUpgradeExpRequirement();
    public int CurrentTotalCost => currentTotalCost;
    public int CurrentMaxCostLimit => GetCurrentMaxCostLimit();
    public bool IsShopLocked => isShopLocked;
    public IReadOnlyList<GameObject> ShopSlots => shopSlots;
    public IReadOnlyList<string> ShopSlotChessIds => shopSlotChessIds;
    public IReadOnlyList<bool> SoldOutSlots => soldOutSlots;
    public IReadOnlyDictionary<string, int> UnitRemainingCounts => unitRemainingCounts;

    public event Action<int> FundsChanged;
    public event Action<int> LevelChanged;
    public event Action<int, int> ExpChanged;
    public event Action<int, int> CostChanged;
    public event Action<bool> ShopLockChanged;
    public event Action ShopRefreshed;

    public void BuildSupplyPool()
    {
        unitSupplyPool.Clear();
        unitRemainingCounts.Clear();
        unitDataByChessId.Clear();

        EnsureShopManagerDataLoaded();
        if (shopManagerData == null)
        {
            Debug.LogWarning("[ShopManager] Missing Resources/Units/DataAssets/ShopManagerData.asset. Card counts will default to 0.");
        }

        List<UnitLogicDataSO> allUnitData = CollectAllUnitData();
        HashSet<string> seenChessIds = new HashSet<string>();
        for (int i = 0; i < allUnitData.Count; i++)
        {
            UnitLogicDataSO unitData = allUnitData[i];
            if (unitData == null)
            {
                continue;
            }

            string chessId = ReadStringValue(unitData, "chessId", "ChessId", "chessID", "ChessID");
            if (string.IsNullOrEmpty(chessId))
            {
                chessId = unitData.name;
            }

            int faction = ReadIntValue(unitData, -1, "faction", "Faction");
            int unitTier = ReadIntValue(unitData, 0, "unitTier", "UnitTier");
            int unitPrice = ReadIntValue(unitData, 0, "unitPrice", "UnitPrice");
            int unitRare = ReadIntValue(unitData, 0, "unitRare", "UnitRare");

            if (faction != 0 || unitTier != 1 || unitPrice <= 0 || !seenChessIds.Add(chessId))
            {
                continue;
            }

            int initialCount = GetInitialCardCount(unitRare);
            unitSupplyPool.Add(chessId);
            unitRemainingCounts[chessId] = initialCount;
            unitDataByChessId[chessId] = unitData;
        }

        Debug.Log("[ShopManager] Supply pool built. Unit types: " + unitSupplyPool.Count + ".");
    }

    public bool TryDrawUnit(string chessId)
    {
        if (string.IsNullOrEmpty(chessId))
        {
            return false;
        }

        int remainingCount;
        if (!unitRemainingCounts.TryGetValue(chessId, out remainingCount) || remainingCount <= 0)
        {
            return false;
        }

        unitRemainingCounts[chessId] = remainingCount - 1;
        return true;
    }

    public void ReturnUnitToPool(string chessId)
    {
        if (string.IsNullOrEmpty(chessId))
        {
            return;
        }

        int remainingCount;
        unitRemainingCounts.TryGetValue(chessId, out remainingCount);
        unitRemainingCounts[chessId] = Mathf.Max(0, remainingCount) + 1;
    }

    public int GetRemainingCount(string chessId)
    {
        int remainingCount;
        return unitRemainingCounts.TryGetValue(chessId, out remainingCount) ? remainingCount : 0;
    }

    private int GetInitialCardCount(int unitRare)
    {
        if (shopManagerData == null)
        {
            return 0;
        }

        IReadOnlyList<ShopPoolConfig> poolConfigs = shopManagerData.PoolConfigs;
        for (int i = 0; i < poolConfigs.Count; i++)
        {
            ShopPoolConfig config = poolConfigs[i];
            if (config != null && config.unitRare == unitRare)
            {
                return Mathf.Max(0, config.cardCount);
            }
        }

        return 0;
    }

    public UnitLogicDataSO GetShopSlotUnitData(int slotIndex)
    {
        if (!IsValidShopSlot(slotIndex))
        {
            return null;
        }

        string chessId = shopSlotChessIds[slotIndex];
        if (string.IsNullOrEmpty(chessId))
        {
            return null;
        }

        UnitLogicDataSO unitData;
        return unitDataByChessId.TryGetValue(chessId, out unitData) ? unitData : null;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        EnsureFixedConfigArrays();
        if (buildSupplyPoolOnAwake)
        {
            BuildSupplyPool();
        }

        funds = Mathf.Max(0, initialFunds);
        level = 1;
        currentExp = 0;
        RefreshCurrentTotalCost();
    }

    private void Start()
    {
        if (GameFlowManager.Instance != null)
        {
            EnsureFixedConfigArrays();
            int minimumCostLimit = maxCostLimits.Length > 0 ? maxCostLimits[0] : 5;
            if (GameFlowManager.Instance.PopulationLimit < minimumCostLimit)
            {
                GameFlowManager.Instance.UpdatePopulationLimit(minimumCostLimit);
            }

            funds = GameFlowManager.Instance.Funds; 

           // 👇【亲手加上这三行：如果是全新开局没有老存档，强制把大管家初始化为商店的1级数值】
            if (!PlayerPrefs.HasKey("GameFlow.PopulationLimit"))
            {
                GameFlowManager.Instance.UpdatePopulationLimit(maxCostLimits[0]);
            }
            // 🔗 【新增反推】根据全局存档读取到的 Cost 上限，自动对齐商店的初始等级
            int savedLimit = GameFlowManager.Instance.PopulationLimit;
            EnsureFixedConfigArrays(); // 确保数组已初始化
            for (int i = 0; i < maxCostLimits.Length; i++)
            {
                if (maxCostLimits[i] == savedLimit)
                {
                    level = i + 1;
                    break;
                }
            }
        }
        
        RefreshShopFree();
    }

    private void OnValidate()
    {
        initialFunds = Mathf.Max(0, initialFunds);
        refreshCost = Mathf.Max(0, refreshCost);
        EnsureFixedConfigArrays();
    }

    /// <summary>
    /// 招募单位：检查货架、资金、备战席空位，然后实例化并落座到 1x9 备战席。
    /// </summary>
    public bool BuyUnit(int slotIndex)
    {
        if (!IsIntermissionState())
        {
            Debug.LogWarning("<color=orange>[商店拦截]</color> 只有整备阶段 Intermission 可以购买单位。");
            return false;
        }

        if (!IsValidShopSlot(slotIndex))
        {
            Debug.LogWarning("<color=red>[商店拦截]</color> 购买槽位越界，合法范围是 0~4。");
            return false;
        }

        if (soldOutSlots[slotIndex])
        {
            Debug.LogWarning("<color=orange>[商店拦截]</color> 该货架已经售出，请刷新商店。");
            return false;
        }

        string chessId = shopSlotChessIds[slotIndex];
        if (string.IsNullOrEmpty(chessId))
        {
            Debug.LogWarning("<color=orange>[商店拦截]</color> 该货架为空，无法购买。");
            return false;
        }

        UnitLogicDataSO unitData = GetShopSlotUnitData(slotIndex);
        if (unitData == null)
        {
            Debug.LogError("<color=red>[商店配置错误]</color> 找不到货架单位数据: " + chessId + "。");
            return false;
        }

        GridManager gridManager = GridManager.Instance;
        if (gridManager == null || gridManager.reserveContainer == null)
        {
            Debug.LogError("<color=red>[商店配置错误]</color> 找不到 GridManager 或 reserveContainer，无法把新单位放入备战席。");
            return false;
        }

        Vector2Int emptyReservePos;
        if (!gridManager.GetEmptyReserveSlot(out emptyReservePos))
        {
            Debug.LogWarning("<color=orange>[商店拦截]</color> 9 个备战席已满，购买被拦截。");
            return false;
        }

        if (funds < unitData.unitPrice)
        {
            Debug.LogWarning("<color=orange>[商店拦截]</color> 资金不足，无法购买该单位。");
            return false;
        }

        if (!TryDrawUnit(chessId))
        {
            Debug.LogWarning("<color=orange>[商店拦截]</color> 公共牌库中该单位已经被抽空: " + chessId + "。");
            return false;
        }

        SpendFunds(unitData.unitPrice);

        UnitDataManager dataManager = GetUnitDataManagerInstance();
        UnitLogic unit = dataManager != null
            ? dataManager.SpawnUnitOnBoard(chessId, Vector3.zero, UnitFaction.Player)
            : null;
        if (unit == null)
        {
            ReturnUnitToPool(chessId);
            AddFunds(unitData.unitPrice);
            Debug.LogError("<color=red>[商店配置错误]</color> UnitDataManager 生成单位失败，已自动退款: " + chessId + "。");
            return false;
        }

        gridManager.PlaceUnitOnReserve(unit, emptyReservePos);

        GameFlowManager flowManager = GameFlowManager.Instance;
        if (flowManager != null && !flowManager.RegisterPurchasedUnitFromShop(unit))
        {
            ReturnUnitToPool(chessId);
            AddFunds(unitData.unitPrice);
            Destroy(unit.gameObject);
            gridManager.RefreshOccupancy();
            Debug.LogWarning("<color=orange>[商店回滚]</color> 状态机拒绝登记新单位，已销毁单位并退款。");
            return false;
        }

        soldOutSlots[slotIndex] = true;
        RefreshCurrentTotalCost();
        Debug.Log("<color=green>[购买成功]</color> 单位已落座备战席 X=" + emptyReservePos.x + "，花费 " + unitData.unitPrice + "。");
        return true;
    }

    /// <summary>
    /// 支付刷新：扣除刷新费用，重新生成 5 个货架。
    /// </summary>
    public bool RefreshShop()
    {
        if (!IsIntermissionState())
        {
            Debug.LogWarning("<color=orange>[刷新拦截]</color> 只有整备阶段 Intermission 可以刷新商店。");
            return false;
        }

        if (funds < refreshCost)
        {
            Debug.LogWarning("<color=orange>[刷新拦截]</color> 资金不足，无法刷新商店。");
            return false;
        }

        SpendFunds(refreshCost);
        GenerateShopSlots();
        Debug.Log("<color=cyan>[刷新成功]</color> 已花费 " + refreshCost + " 刷新 5 个货架。");
        return true;
    }

    public void ToggleShopLock()
    {
        isShopLocked = !isShopLocked;
        ShopLockChanged?.Invoke(isShopLocked);
        Debug.Log("<color=cyan>[商店锁]</color> 当前状态: " + (isShopLocked ? "锁定" : "未锁定") + "。");
    }

    public void HandlePostBattleRefresh()
    {
        if (isShopLocked)
        {
            isShopLocked = false;
            ShopLockChanged?.Invoke(isShopLocked);
            Debug.Log("<color=cyan>[商店锁]</color> 已保留上一轮货架，并自动解除锁定。");
            return;
        }

        GenerateShopSlots();
        Debug.Log("<color=cyan>[战后刷新]</color> 新整备阶段已自动刷新商店货架。");
    }

    /// <summary>
    /// 支付升级：最高 5 级，升级到 2/3/4/5 级分别读取 upgradeLevelCosts[0..3]。
    /// </summary>
    public bool UpgradeLevel()
    {
        if (!IsIntermissionState())
        {
            Debug.LogWarning("<color=orange>[Shop Upgrade Blocked]</color> Only Intermission can buy logistics exp.");
            return false;
        }

        if (level >= MaxShopLevel)
        {
            currentExp = 0;
            ExpChanged?.Invoke(currentExp, GetCurrentUpgradeExpRequirement());
            Debug.LogWarning("<color=orange>[Shop Upgrade Blocked]</color> Shop is already at max level.");
            return false;
        }

        if (funds < UpgradeExpPurchaseCost)
        {
            Debug.LogWarning("<color=orange>[Shop Upgrade Blocked]</color> Buying exp requires " + UpgradeExpPurchaseCost + " funds.");
            return false;
        }

        SpendFunds(UpgradeExpPurchaseCost);
        currentExp += UpgradeExpPerPurchase;

        bool leveledUp = false;
        while (level < MaxShopLevel)
        {
            int targetExp = GetCurrentUpgradeExpRequirement();
            if (targetExp <= 0 || currentExp < targetExp)
            {
                break;
            }

            currentExp -= targetExp;
            level++;
            leveledUp = true;

            SyncPopulationLimitToCurrentLevel();
            LevelChanged?.Invoke(level);
            Debug.Log("<color=green>[Shop Upgrade]</color> Shop reached Level " + level + ". Current Cost limit: " + CurrentMaxCostLimit + ".");
        }

        if (level >= MaxShopLevel)
        {
            currentExp = 0;
        }

        ExpChanged?.Invoke(currentExp, GetCurrentUpgradeExpRequirement());
        RefreshCurrentTotalCost();

        if (!leveledUp)
        {
            Debug.Log("<color=cyan>[Buy Exp]</color> Spent " + UpgradeExpPurchaseCost + " funds for " + UpgradeExpPerPurchase + " exp.");
        }

        return true;
    }

    private bool UpgradeLevelLegacy()
    {
        if (!IsIntermissionState())
        {
            Debug.LogWarning("<color=orange>[升级拦截]</color> 只有整备阶段 Intermission 可以升级商店等级。");
            return false;
        }

        if (level >= MaxShopLevel)
        {
            Debug.LogWarning("<color=orange>[升级拦截]</color> 商店已经达到 5 级，无法继续升级。");
            return false;
        }

        int costIndex = level - 1;
        int upgradeCost = upgradeLevelCosts[costIndex];
        if (funds < upgradeCost)
        {
            Debug.LogWarning("<color=orange>[升级拦截]</color> 资金不足，升级需要 " + upgradeCost + "。");
            return false;
        }

        SpendFunds(upgradeCost);
        level++;
        // 🔗 【新增同步】升级成功后，立刻把下一档的 Cost 上限同步给大管家归档
        if (GameFlowManager.Instance != null)
        {
            int newLimit = maxCostLimits[Mathf.Clamp(level - 1, 0, maxCostLimits.Length - 1)];
            GameFlowManager.Instance.UpdatePopulationLimit(newLimit);
        }
        LevelChanged?.Invoke(level);
        RefreshCurrentTotalCost();
        Debug.Log("<color=green>[升级成功]</color> 商店等级提升到 Level " + level + "，当前 Cost 上限为 " + CurrentMaxCostLimit + "。");
        return true;
    }

    /// <summary>
    /// 1:1 无损退役变现。
    /// 退款金额 = 单位原价 * 当前血量 / 最大血量。
    /// </summary>
    public bool RetireUnit(UnitLogic unit)
    {
        if (!IsIntermissionState())
        {
            Debug.LogWarning("<color=orange>[退役拦截]</color> 只有整备阶段 Intermission 可以退役单位。");
            return false;
        }

        if (unit == null || unit.maxHp <= 0)
        {
            Debug.LogWarning("<color=orange>[退役拦截]</color> 单位为空或最大生命值配置异常。");
            return false;
        }

        int refundAmount = Mathf.FloorToInt(unit.unitPrice * ((float)unit.currentHp / unit.maxHp));
        bool wasOnBattlefield = IsUnitOnBattlefield(unit);

        GameFlowManager flowManager = GameFlowManager.Instance;
        if (flowManager != null)
        {
            flowManager.UnregisterUnit(unit);
        }

        AddFunds(refundAmount);
        ReturnUnitToPool(GetPoolChessIdFromUnit(unit));
        unit.ClearGridPosition();
        unit.transform.SetParent(null);
        Destroy(unit.gameObject);

        GridManager gridManager = GridManager.Instance;
        if (gridManager != null)
        {
            gridManager.RefreshOccupancy();
        }

        RefreshCurrentTotalCost();
        if (SynergyManager.Instance != null)
        {
            SynergyManager.Instance.RecalculateSynergies();
        }

        Debug.Log("<color=green>[退役成功]</color> 返还资金 " + refundAmount + (wasOnBattlefield ? "，已同步释放上阵 Cost。" : "。"));
        return true;
    }

    /// <summary>
    /// 1:1 等额补编修理。
    /// 维修费用 = 单位原价 * 损失血量 / 最大血量。
    /// </summary>
    public bool RepairUnit(UnitLogic unit)
    {
        if (!IsIntermissionState())
        {
            Debug.LogWarning("<color=orange>[修理拦截]</color> 只有整备阶段 Intermission 可以修理单位。");
            return false;
        }

        if (unit == null || unit.maxHp <= 0)
        {
            Debug.LogWarning("<color=orange>[修理拦截]</color> 单位为空或最大生命值配置异常。");
            return false;
        }

        if (unit.currentHp >= unit.maxHp)
        {
            Debug.Log("<color=cyan>[修理跳过]</color> 单位已经满血，无需修理。");
            return false;
        }

        int repairCost;
        if (GameFlowManager.Instance != null)
        {
            repairCost = GameFlowManager.Instance.CalculateRepairCost(unit);
        }
        else if (unit.currentHp == 0)
        {
            repairCost = unit.unitPrice;
        }
        else
        {
            repairCost = Mathf.CeilToInt(unit.unitPrice * ((float)(unit.maxHp - unit.currentHp) / unit.maxHp));
        }

        if (funds < repairCost)
        {
            Debug.LogWarning("<color=orange>[修理拦截]</color> 资金不足，修理需要 " + repairCost + "。");
            return false;
        }

        SpendFunds(repairCost);
        if (!unit.gameObject.activeSelf)
        {
            unit.gameObject.SetActive(true);
        }

        unit.SetCurrentHp(unit.maxHp);
        Debug.Log("<color=green>[修理成功]</color> 花费 " + repairCost + "，单位生命值已恢复至满。");
        return true;
    }

    /// <summary>
    /// 上阵前门禁：检测新增 Cost 后是否超过当前等级上限。
    /// </summary>
    public bool CanDeployAdditionalCost(int additionalCost)
    {
        RefreshCurrentTotalCost();
        return currentTotalCost + Mathf.Max(0, additionalCost) <= CurrentMaxCostLimit;
    }

    /// <summary>
    /// 从战场容器实时统计已上阵单位 Cost。
    /// 拖拽上阵、退回备战席、退役后都应调用一次。
    /// </summary>
    public void RefreshCurrentTotalCost()
    {
        int totalCost = 0;
        GridManager gridManager = GridManager.Instance;
        if (gridManager != null && gridManager.battlefieldContainer != null)
        {
            Transform battlefield = gridManager.battlefieldContainer;
            for (int i = 0; i < battlefield.childCount; i++)
            {
                UnitLogic unit = battlefield.GetChild(i).GetComponent<UnitLogic>();
                if (unit != null && unit.faction == UnitFaction.Player)
                {
                    totalCost += Mathf.Max(0, unit.unitCost);
                }
            }
        }

        currentTotalCost = totalCost;
        CostChanged?.Invoke(currentTotalCost, CurrentMaxCostLimit);
    }

    private List<UnitLogicDataSO> CollectAllUnitData()
    {
        List<UnitLogicDataSO> result = new List<UnitLogicDataSO>();
        UnitDataManager dataManager = GetUnitDataManagerInstance();
        if (dataManager != null)
        {
            CollectUnitDataFromObject(dataManager, result);
        }

        UnitLogicDataSO[] resourceData = Resources.LoadAll<UnitLogicDataSO>("Units/DataAssets");
        for (int i = 0; i < resourceData.Length; i++)
        {
            AddUnitDataIfMissing(result, resourceData[i]);
        }

        return result;
    }

    private UnitDataManager GetUnitDataManagerInstance()
    {
        Type managerType = typeof(UnitDataManager);
        BindingFlags staticFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
        PropertyInfo instanceProperty = managerType.GetProperty("Instance", staticFlags);
        if (instanceProperty != null && managerType.IsAssignableFrom(instanceProperty.PropertyType))
        {
            UnitDataManager instance = instanceProperty.GetValue(null, null) as UnitDataManager;
            if (instance != null)
            {
                return instance;
            }
        }

        FieldInfo instanceField = managerType.GetField("Instance", staticFlags);
        if (instanceField != null && managerType.IsAssignableFrom(instanceField.FieldType))
        {
            UnitDataManager instance = instanceField.GetValue(null) as UnitDataManager;
            if (instance != null)
            {
                return instance;
            }
        }

        return FindObjectOfType<UnitDataManager>();
    }

    private void CollectUnitDataFromObject(object source, List<UnitLogicDataSO> result)
    {
        if (source == null)
        {
            return;
        }

        Type sourceType = source.GetType();
        BindingFlags instanceFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        PropertyInfo[] properties = sourceType.GetProperties(instanceFlags);
        for (int i = 0; i < properties.Length; i++)
        {
            PropertyInfo property = properties[i];
            if (property.GetIndexParameters().Length == 0)
            {
                TryCollectUnitDataFromValue(SafeGetPropertyValue(property, source), result);
            }
        }

        FieldInfo[] fields = sourceType.GetFields(instanceFlags);
        for (int i = 0; i < fields.Length; i++)
        {
            TryCollectUnitDataFromValue(fields[i].GetValue(source), result);
        }

        MethodInfo[] methods = sourceType.GetMethods(instanceFlags);
        for (int i = 0; i < methods.Length; i++)
        {
            MethodInfo method = methods[i];
            if (method.IsSpecialName || method.GetParameters().Length != 0)
            {
                continue;
            }

            if (typeof(UnitLogicDataSO).IsAssignableFrom(method.ReturnType)
                || typeof(System.Collections.IEnumerable).IsAssignableFrom(method.ReturnType))
            {
                TryCollectUnitDataFromValue(SafeInvokeMethod(method, source), result);
            }
        }
    }

    private void TryCollectUnitDataFromValue(object value, List<UnitLogicDataSO> result)
    {
        if (value == null)
        {
            return;
        }

        UnitLogicDataSO directData = value as UnitLogicDataSO;
        if (directData != null)
        {
            AddUnitDataIfMissing(result, directData);
            return;
        }

        System.Collections.IDictionary dictionary = value as System.Collections.IDictionary;
        if (dictionary != null)
        {
            foreach (object item in dictionary.Values)
            {
                TryCollectUnitDataFromValue(item, result);
            }

            return;
        }

        string stringValue = value as string;
        System.Collections.IEnumerable enumerable = value as System.Collections.IEnumerable;
        if (enumerable == null || stringValue != null)
        {
            return;
        }

        foreach (object item in enumerable)
        {
            UnitLogicDataSO itemData = item as UnitLogicDataSO;
            if (itemData != null)
            {
                AddUnitDataIfMissing(result, itemData);
                continue;
            }

            object reflectedValue = ReadObjectValue(item, "Value", "value");
            if (reflectedValue != null && reflectedValue != item)
            {
                TryCollectUnitDataFromValue(reflectedValue, result);
            }
        }
    }

    private void AddUnitDataIfMissing(List<UnitLogicDataSO> result, UnitLogicDataSO unitData)
    {
        if (unitData == null || result.Contains(unitData))
        {
            return;
        }

        result.Add(unitData);
    }

    private object SafeGetPropertyValue(PropertyInfo property, object source)
    {
        try
        {
            return property.GetValue(source, null);
        }
        catch
        {
            return null;
        }
    }

    private object SafeInvokeMethod(MethodInfo method, object source)
    {
        try
        {
            return method.Invoke(source, null);
        }
        catch
        {
            return null;
        }
    }

    private string ReadStringValue(object source, params string[] names)
    {
        object value = ReadObjectValue(source, names);
        return value != null ? value.ToString() : string.Empty;
    }

    private int ReadIntValue(object source, int defaultValue, params string[] names)
    {
        object value = ReadObjectValue(source, names);
        if (value == null)
        {
            return defaultValue;
        }

        try
        {
            return Convert.ToInt32(value);
        }
        catch
        {
            return defaultValue;
        }
    }

    private object ReadObjectValue(object source, params string[] names)
    {
        if (source == null || names == null)
        {
            return null;
        }

        Type sourceType = source.GetType();
        BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        for (int i = 0; i < names.Length; i++)
        {
            FieldInfo field = sourceType.GetField(names[i], flags);
            if (field != null)
            {
                return field.GetValue(source);
            }

            PropertyInfo property = sourceType.GetProperty(names[i], flags);
            if (property != null && property.GetIndexParameters().Length == 0)
            {
                return SafeGetPropertyValue(property, source);
            }
        }

        return null;
    }

    private void RefreshShopFree()
    {
        GenerateShopSlots();
        Debug.Log("<color=cyan>[商店初始化]</color> 初始货架已生成，当前资金 " + funds + "。");
    }

    private void GenerateShopSlots()
    {
        EnsureFixedConfigArrays();

        for (int i = 0; i < ShopSlotCount; i++)
        {
            string chessId = RollUnitChessIdForCurrentLevel();
            shopSlotChessIds[i] = chessId;
            shopSlots[i] = null;
            soldOutSlots[i] = false;
        }

        ShopRefreshed?.Invoke();
    }

    private string RollUnitChessIdForCurrentLevel()
    {
        int targetTier = RollTier();
        List<string> candidates = GetCandidatesByRare(targetTier);

        if (candidates.Count == 0)
        {
            Debug.LogWarning("<color=orange>[抽卡警告]</color> 卡池中没有 " + targetTier + " 阶卡，本货架将保持为空。");
            return string.Empty;
        }

        int randomIndex = UnityEngine.Random.Range(0, candidates.Count);
        return candidates[randomIndex];
    }

    private int RollTier()
    {
        EnsureShopManagerDataLoaded();
        ShopProbabilityConfig config = GetProbabilityConfigForCurrentLevel();
        if (config == null)
        {
            Debug.LogWarning("<color=orange>[概率警告]</color> 找不到当前商店等级的 ShopProbability.csv 配置，默认抽 1 费卡。");
            return 1;
        }

        float tier1 = Mathf.Max(0f, config.weightT1);
        float tier2 = Mathf.Max(0f, config.weightT2);
        float tier3 = Mathf.Max(0f, config.weightT3);
        float tier4 = Mathf.Max(0f, config.weightT4);
        float tier5 = Mathf.Max(0f, config.weightT5);
        float total = tier1 + tier2 + tier3 + tier4 + tier5;

        if (total <= 0f)
        {
            Debug.LogWarning("<color=orange>[概率警告]</color> 当前等级权重总和为 0，默认抽取 1 费卡。");
            return 1;
        }

        float roll = UnityEngine.Random.Range(0f, total);
        if (roll < tier1)
        {
            return 1;
        }

        if (roll < tier1 + tier2)
        {
            return 2;
        }

        if (roll < tier1 + tier2 + tier3)
        {
            return 3;
        }

        if (roll < tier1 + tier2 + tier3 + tier4)
        {
            return 4;
        }

        return 5;
    }

    private ShopProbabilityConfig GetProbabilityConfigForCurrentLevel()
    {
        if (shopManagerData == null)
        {
            return null;
        }

        int currentShopLevel = Mathf.Clamp(level, 1, MaxShopLevel);
        IReadOnlyList<ShopProbabilityConfig> probabilityConfigs = shopManagerData.ProbabilityConfigs;
        for (int i = 0; i < probabilityConfigs.Count; i++)
        {
            ShopProbabilityConfig config = probabilityConfigs[i];
            if (config != null && config.shopLevel == currentShopLevel)
            {
                return config;
            }
        }

        return null;
    }

    private void EnsureShopManagerDataLoaded()
    {
        if (shopManagerData != null)
        {
            return;
        }

        shopManagerData = Resources.Load<ShopManagerDataSO>("Units/DataAssets/ShopManagerData");
    }

    private List<string> GetCandidatesByRare(int targetRare)
    {
        List<string> candidates = new List<string>();
        for (int i = 0; i < unitSupplyPool.Count; i++)
        {
            string chessId = unitSupplyPool[i];
            UnitLogicDataSO unitData;
            if (string.IsNullOrEmpty(chessId)
                || !unitDataByChessId.TryGetValue(chessId, out unitData)
                || unitData == null
                || unitData.unitRare != targetRare
                || GetRemainingCount(chessId) <= 0)
            {
                continue;
            }

            candidates.Add(chessId);
        }

        return candidates;
    }

    private bool IsValidShopSlot(int slotIndex)
    {
        return slotIndex >= 0 && slotIndex < ShopSlotCount;
    }

    private bool IsIntermissionState()
    {
        GameFlowManager flowManager = GameFlowManager.Instance;
        return flowManager == null || flowManager.CurrentState == GameState.Intermission;
    }

    private bool IsUnitOnBattlefield(UnitLogic unit)
    {
        GridManager gridManager = GridManager.Instance;
        return unit != null
            && gridManager != null
            && gridManager.battlefieldContainer != null
            && unit.transform.parent == gridManager.battlefieldContainer;
    }

    private string GetPoolChessIdFromUnit(UnitLogic unit)
    {
        if (unit == null || unit.unitDataConfig == null || string.IsNullOrWhiteSpace(unit.unitDataConfig.chessId))
        {
            return string.Empty;
        }

        string chessId = unit.unitDataConfig.chessId.Trim();
        return unitRemainingCounts.ContainsKey(chessId) ? chessId : string.Empty;
    }

    private int GetCurrentUpgradeExpRequirement()
    {
        EnsureFixedConfigArrays();
        if (level >= MaxShopLevel)
        {
            return 0;
        }

        int index = Mathf.Clamp(level - 1, 0, upgradeLevelCosts.Length - 1);
        return Mathf.Max(0, upgradeLevelCosts[index]);
    }

    private void SyncPopulationLimitToCurrentLevel()
    {
        if (GameFlowManager.Instance == null)
        {
            return;
        }

        EnsureFixedConfigArrays();
        int newLimit = maxCostLimits[Mathf.Clamp(level - 1, 0, maxCostLimits.Length - 1)];
        GameFlowManager.Instance.UpdatePopulationLimit(newLimit);
    }

    private int GetCurrentMaxCostLimit()
    {
        // 🔗 【真值绑定】不再读自己的小数组，直接去大管家那里拿真正的上限
        if (GameFlowManager.Instance != null)
        {
            return GameFlowManager.Instance.PopulationLimit;
        }
        
        // 兜底防御，万一没开大管家
        EnsureFixedConfigArrays();
        int index = Mathf.Clamp(level - 1, 0, maxCostLimits.Length - 1);
        return maxCostLimits[index];
    }

    public void AddFunds(int amount)
    {
       if (amount <= 0)
        {
            return;
        }

        funds += amount;

        // 🔗 【精准对接】同步让全局大管家增加入账
        if (GameFlowManager.Instance != null)
        {
            GameFlowManager.Instance.AddFunds(amount);
        }

        FundsChanged?.Invoke(funds);
    }

    private void SpendFunds(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        funds -= amount;

        // 🔗 【精准对接】同步让全局大管家扣除开销
        if (GameFlowManager.Instance != null)
        {
            GameFlowManager.Instance.TrySpendFunds(amount);
        }

        FundsChanged?.Invoke(funds);
    }

    private void EnsureFixedConfigArrays()
    {
        EnsureUpgradeCosts();
        EnsureMaxCostLimits();
        EnsureRuntimeSlots();
    }

    private void EnsureUpgradeCosts()
    {
        if (upgradeLevelCosts == null || upgradeLevelCosts.Length != MaxShopLevel - 1)
        {
            int[] oldValues = upgradeLevelCosts;
            upgradeLevelCosts = new int[MaxShopLevel - 1] { 10, 15, 20, 25 };
            CopyIntValues(oldValues, upgradeLevelCosts);
        }

        for (int i = 0; i < upgradeLevelCosts.Length; i++)
        {
            upgradeLevelCosts[i] = Mathf.Max(0, upgradeLevelCosts[i]);
        }
    }

    private void EnsureMaxCostLimits()
    {
        if (maxCostLimits == null || maxCostLimits.Length != MaxShopLevel)
        {
            int[] oldValues = maxCostLimits;
            maxCostLimits = new int[MaxShopLevel] { 5, 7, 9, 11, 13 };
            CopyIntValues(oldValues, maxCostLimits);
        }

        for (int i = 0; i < maxCostLimits.Length; i++)
        {
            maxCostLimits[i] = Mathf.Max(0, maxCostLimits[i]);
        }
    }

    private void EnsureRuntimeSlots()
    {
        if (shopSlots == null || shopSlots.Length != ShopSlotCount)
        {
            shopSlots = new GameObject[ShopSlotCount];
        }

        if (shopSlotChessIds == null || shopSlotChessIds.Length != ShopSlotCount)
        {
            shopSlotChessIds = new string[ShopSlotCount];
        }

        if (soldOutSlots == null || soldOutSlots.Length != ShopSlotCount)
        {
            soldOutSlots = new bool[ShopSlotCount];
        }
    }

    private void CopyIntValues(int[] source, int[] target)
    {
        if (source == null || target == null)
        {
            return;
        }

        int copyCount = Mathf.Min(source.Length, target.Length);
        for (int i = 0; i < copyCount; i++)
        {
            target[i] = source[i];
        }
    }
}
