using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Refreshes the logistics HUD with funds, deployed Cost, stage level, and level progress.
/// Text slots accept either legacy Unity Text or TextMeshProUGUI components.
/// </summary>
public class LogisticsPanelUI : MonoBehaviour
{
    [Header("Text Slots")]
    public Graphic fundsText;
    public Graphic costText;
    public Graphic levelText;
    public Graphic expText;

    [Header("Progress")]
    public Slider expSlider;

    private GameFlowManager subscribedFlowManager;

    private void OnEnable()
    {
        SubscribeToFlowManagerIfAvailable();
        RefreshAll();
    }

    private void Start()
    {
        SubscribeToFlowManagerIfAvailable();
        RefreshAll();
    }

    private void Update()
    {
        SubscribeToFlowManagerIfAvailable();
        RefreshDynamicValues();
    }

    private void OnDisable()
    {
        UnsubscribeFromFlowManager();
    }

    private void OnFundsChanged(int newFunds)
    {
        SetText(fundsText, "资金: $" + Mathf.Max(0, newFunds));
    }

    private void RefreshAll()
    {
        GameFlowManager flowManager = GameFlowManager.Instance;
        if (flowManager == null)
        {
            return;
        }

        RefreshFunds(flowManager);
        RefreshDynamicValues();
    }

    private void RefreshFunds(GameFlowManager flowManager)
    {
        if (flowManager == null)
        {
            return;
        }

        OnFundsChanged(flowManager.Funds);
    }

    private void RefreshDynamicValues()
    {
        GameFlowManager flowManager = GameFlowManager.Instance;
        if (flowManager == null)
        {
            return;
        }

        RefreshCost(flowManager);
        RefreshLevel(flowManager);
        RefreshExp(flowManager);
    }

    private void RefreshCost(GameFlowManager flowManager)
    {
        int currentCost = CalculateDeployedPlayerCost(flowManager);
        int costLimit = Mathf.Max(0, flowManager.PopulationLimit);
        SetText(costText, "Cost: " + currentCost + " / " + costLimit);
    }

    private void RefreshLevel(GameFlowManager flowManager)
    {
        int visibleLevel = ShopManager.Instance != null
            ? ShopManager.Instance.Level
            : flowManager.CurrentStageIndex;

        SetText(levelText, "后勤等级: " + Mathf.Max(1, visibleLevel));
    }

    private void RefreshExp(GameFlowManager flowManager)
    {
        ShopManager shopManager = ShopManager.Instance;
        if (shopManager == null)
        {
            return;
        }

        int currentExp = Mathf.Max(0, shopManager.CurrentExp);
        int targetExp = Mathf.Max(0, shopManager.CurrentUpgradeExpRequirement);

        SetText(expText, currentExp + " / " + targetExp);

        if (expSlider != null)
        {
            expSlider.minValue = 0f;
            expSlider.maxValue = 1f;
            expSlider.value = targetExp > 0 ? Mathf.Clamp01((float)currentExp / targetExp) : 0f;
        }
    }

    private int CalculateDeployedPlayerCost(GameFlowManager flowManager)
    {
        int totalCost = 0;
        if (flowManager == null || flowManager.PlayerUnits == null)
        {
            return totalCost;
        }

        for (int i = 0; i < flowManager.PlayerUnits.Count; i++)
        {
            UnitLogic unit = flowManager.PlayerUnits[i];
            if (unit == null || !unit.IsAlive || !IsDeployedOnBattlefield(unit))
            {
                continue;
            }

            totalCost += Mathf.Max(0, unit.unitCost);
        }

        return totalCost;
    }

    private bool IsDeployedOnBattlefield(UnitLogic unit)
    {
        if (unit == null)
        {
            return false;
        }

        GridManager gridManager = GridManager.Instance;
        return gridManager != null
            && gridManager.battlefieldContainer != null
            && unit.transform.parent == gridManager.battlefieldContainer;
    }

    private void SubscribeToFlowManagerIfAvailable()
    {
        GameFlowManager flowManager = GameFlowManager.Instance;
        if (flowManager == null || subscribedFlowManager == flowManager)
        {
            return;
        }

        UnsubscribeFromFlowManager();
        subscribedFlowManager = flowManager;
        subscribedFlowManager.FundsChanged += OnFundsChanged;
    }

    private void UnsubscribeFromFlowManager()
    {
        if (subscribedFlowManager == null)
        {
            return;
        }

        subscribedFlowManager.FundsChanged -= OnFundsChanged;
        subscribedFlowManager = null;
    }

    private void SetText(Graphic textSlot, string value)
    {
        if (textSlot == null)
        {
            return;
        }

        Text legacyText = textSlot as Text;
        if (legacyText != null)
        {
            legacyText.text = value;
            return;
        }

        TMP_Text tmpText = textSlot as TMP_Text;
        if (tmpText != null)
        {
            tmpText.text = value;
        }
    }
}
