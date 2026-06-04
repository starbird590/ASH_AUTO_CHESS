using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 商店等级概率矩阵。
/// 每个数组元素对应一个商店等级，策划可以在 Inspector 中直接调整各阶卡出现权重。
/// </summary>
[Serializable]
public class ShopLevelWeight
{
    public string levelName;

    [Tooltip("抽取1阶卡的权重百分比")]
    public float tier1Weight;

    [Tooltip("抽取2阶卡的权重百分比")]
    public float tier2Weight;

    [Tooltip("抽取3阶卡的权重百分比")]
    public float tier3Weight;
}

/// <summary>
/// 商店与经济平衡控制器。
/// 负责资金、刷新、升级、概率抽卡、备战席落座、退役变现、补编修理，以及当前上阵 Cost 统计。
/// </summary>
public class ShopManager : MonoBehaviour
{
    public static ShopManager Instance { get; private set; }

    private const int ShopSlotCount = 5;
    private const int MaxShopLevel = 5;

    [Header("⚙️ Economy Settings")]
    [SerializeField] private int initialFunds = 30;
    [SerializeField] private int refreshCost = 2;

    [Header("📊 Level & Cost Settings")]
    [SerializeField] private int[] upgradeLevelCosts = { 10, 15, 20, 25 };
    [SerializeField] private int[] maxCostLimits = { 5, 7, 9, 11, 13 };

    [Header("🎰 Probability Matrix (5个等级的抽卡概率配置表)")]
    [SerializeField] private ShopLevelWeight[] levelWeights = new ShopLevelWeight[5];

    [Header("📦 Card Pool Settings")]
    public List<GameObject> unitSupplyPool = new List<GameObject>();

    [Header("Runtime Shop State")]
    [SerializeField] private int funds;
    [SerializeField] private int level = 1;
    [SerializeField] private int currentTotalCost;
    [SerializeField] private GameObject[] shopSlots = new GameObject[ShopSlotCount];
    [SerializeField] private bool[] soldOutSlots = new bool[ShopSlotCount];

    public int Funds => funds;
    public int Level => level;
    public int CurrentTotalCost => currentTotalCost;
    public int CurrentMaxCostLimit => GetCurrentMaxCostLimit();
    public IReadOnlyList<GameObject> ShopSlots => shopSlots;
    public IReadOnlyList<bool> SoldOutSlots => soldOutSlots;

    public event Action<int> FundsChanged;
    public event Action<int> LevelChanged;
    public event Action<int, int> CostChanged;
    public event Action ShopRefreshed;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        EnsureFixedConfigArrays();
        funds = Mathf.Max(0, initialFunds);
        level = 1;
        RefreshCurrentTotalCost();
    }

    private void Start()
    {
        if (GameFlowManager.Instance != null)
        {
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

        GameObject unitPrefab = shopSlots[slotIndex];
        if (unitPrefab == null)
        {
            Debug.LogWarning("<color=orange>[商店拦截]</color> 该货架为空，无法购买。");
            return false;
        }

        UnitLogic prefabLogic = unitPrefab.GetComponent<UnitLogic>();
        if (prefabLogic == null)
        {
            Debug.LogError("<color=red>[商店配置错误]</color> 预制体缺少 UnitLogic，无法读取价格/阶级/Cost。");
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

        if (funds < prefabLogic.unitPrice)
        {
            Debug.LogWarning("<color=orange>[商店拦截]</color> 资金不足，无法购买该单位。");
            return false;
        }

        SpendFunds(prefabLogic.unitPrice);

        GameObject unitObject = Instantiate(unitPrefab);
        UnitLogic unit = unitObject.GetComponent<UnitLogic>();
        if (unit == null)
        {
            AddFunds(prefabLogic.unitPrice);
            Destroy(unitObject);
            Debug.LogError("<color=red>[商店配置错误]</color> 实例化后的单位缺少 UnitLogic，已自动退款。");
            return false;
        }

        gridManager.PlaceUnitOnReserve(unit, emptyReservePos);

        GameFlowManager flowManager = GameFlowManager.Instance;
        if (flowManager != null && !flowManager.RegisterPurchasedUnitFromShop(unit))
        {
            AddFunds(prefabLogic.unitPrice);
            Destroy(unitObject);
            gridManager.RefreshOccupancy();
            Debug.LogWarning("<color=orange>[商店回滚]</color> 状态机拒绝登记新单位，已销毁单位并退款。");
            return false;
        }

        soldOutSlots[slotIndex] = true;
        RefreshCurrentTotalCost();
        Debug.Log("<color=green>[购买成功]</color> 单位已落座备战席 X=" + emptyReservePos.x + "，花费 " + prefabLogic.unitPrice + "。");
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

    /// <summary>
    /// 支付升级：最高 5 级，升级到 2/3/4/5 级分别读取 upgradeLevelCosts[0..3]。
    /// </summary>
    public bool UpgradeLevel()
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
                if (unit != null)
                {
                    totalCost += Mathf.Max(0, unit.unitCost);
                }
            }
        }

        currentTotalCost = totalCost;
        CostChanged?.Invoke(currentTotalCost, CurrentMaxCostLimit);
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
            shopSlots[i] = RollUnitPrefabForCurrentLevel();
            soldOutSlots[i] = false;
        }

        ShopRefreshed?.Invoke();
    }

    private GameObject RollUnitPrefabForCurrentLevel()
    {
        ShopLevelWeight weight = levelWeights[Mathf.Clamp(level - 1, 0, MaxShopLevel - 1)];
        int targetTier = RollTier(weight);
        List<GameObject> candidates = GetCandidatesByTier(targetTier);

        if (candidates.Count == 0)
        {
            Debug.LogWarning("<color=orange>[抽卡警告]</color> 卡池中没有 " + targetTier + " 阶卡，本货架将保持为空。");
            return null;
        }

        int randomIndex = UnityEngine.Random.Range(0, candidates.Count);
        return candidates[randomIndex];
    }

    private int RollTier(ShopLevelWeight weight)
    {
        float tier1 = Mathf.Max(0f, weight.tier1Weight);
        float tier2 = Mathf.Max(0f, weight.tier2Weight);
        float tier3 = Mathf.Max(0f, weight.tier3Weight);
        float total = tier1 + tier2 + tier3;

        if (total <= 0f)
        {
            Debug.LogWarning("<color=orange>[概率警告]</color> 当前等级权重总和为 0，默认抽取 1 阶卡。");
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

        return 3;
    }

    private List<GameObject> GetCandidatesByTier(int targetTier)
    {
        List<GameObject> candidates = new List<GameObject>();
        for (int i = 0; i < unitSupplyPool.Count; i++)
        {
            GameObject prefab = unitSupplyPool[i];
            if (prefab == null)
            {
                continue;
            }

            UnitLogic unit = prefab.GetComponent<UnitLogic>();
            if (unit != null && unit.unitTier == targetTier)
            {
                candidates.Add(prefab);
            }
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
        EnsureLevelWeights();
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

    private void EnsureLevelWeights()
    {
        if (levelWeights == null || levelWeights.Length != MaxShopLevel)
        {
            ShopLevelWeight[] oldValues = levelWeights;
            levelWeights = new ShopLevelWeight[MaxShopLevel];
            if (oldValues != null)
            {
                int copyCount = Mathf.Min(oldValues.Length, levelWeights.Length);
                for (int i = 0; i < copyCount; i++)
                {
                    levelWeights[i] = oldValues[i];
                }
            }
        }

        for (int i = 0; i < levelWeights.Length; i++)
        {
            if (levelWeights[i] == null)
            {
                levelWeights[i] = CreateDefaultWeight(i);
            }

            if (string.IsNullOrEmpty(levelWeights[i].levelName))
            {
                levelWeights[i].levelName = "Level " + (i + 1);
            }
        }
    }

    private void EnsureRuntimeSlots()
    {
        if (shopSlots == null || shopSlots.Length != ShopSlotCount)
        {
            shopSlots = new GameObject[ShopSlotCount];
        }

        if (soldOutSlots == null || soldOutSlots.Length != ShopSlotCount)
        {
            soldOutSlots = new bool[ShopSlotCount];
        }
    }

    private ShopLevelWeight CreateDefaultWeight(int index)
    {
        ShopLevelWeight weight = new ShopLevelWeight();
        weight.levelName = "Level " + (index + 1);

        switch (index)
        {
            case 0:
                weight.tier1Weight = 100f;
                weight.tier2Weight = 0f;
                weight.tier3Weight = 0f;
                break;
            case 1:
                weight.tier1Weight = 80f;
                weight.tier2Weight = 20f;
                weight.tier3Weight = 0f;
                break;
            case 2:
                weight.tier1Weight = 60f;
                weight.tier2Weight = 35f;
                weight.tier3Weight = 5f;
                break;
            case 3:
                weight.tier1Weight = 35f;
                weight.tier2Weight = 50f;
                weight.tier3Weight = 15f;
                break;
            default:
                weight.tier1Weight = 20f;
                weight.tier2Weight = 50f;
                weight.tier3Weight = 30f;
                break;
        }

        return weight;
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
