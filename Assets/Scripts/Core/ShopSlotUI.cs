using UnityEngine;
using UnityEngine.UI;

public class ShopSlotUI : MonoBehaviour
{
    [Header("Shop Slot")]
    [SerializeField] private int slotIndex = 0;

    private Text buttonText;

    private void Awake()
    {
        buttonText = GetComponentInChildren<Text>();
    }

    private void Start()
    {
        if (ShopManager.Instance != null)
        {
            ShopManager.Instance.ShopRefreshed += UpdateSlotDisplay;
        }

        UpdateSlotDisplay();
    }

    private void OnDestroy()
    {
        if (ShopManager.Instance != null)
        {
            ShopManager.Instance.ShopRefreshed -= UpdateSlotDisplay;
        }
    }

    private void UpdateSlotDisplay()
    {
        if (ShopManager.Instance == null || buttonText == null)
        {
            return;
        }

        if (ShopManager.Instance.SoldOutSlots[slotIndex])
        {
            buttonText.text = "<color=red>[Sold]</color>";
            return;
        }

        UnitLogicDataSO unitData = ShopManager.Instance.GetShopSlotUnitData(slotIndex);
        if (unitData == null)
        {
            buttonText.text = "[Empty]";
            return;
        }

        string displayName = string.IsNullOrEmpty(unitData.chessName) ? unitData.chessId : unitData.chessName;
        buttonText.text = unitData.unitRare + " Cost " + displayName + "\nPrice: " + unitData.unitPrice + " (Pop: " + unitData.unitCost + ")";
    }

    private void Update()
    {
        if (ShopManager.Instance != null && ShopManager.Instance.SoldOutSlots[slotIndex])
        {
            if (buttonText != null && buttonText.text != "<color=red>[Sold]</color>")
            {
                buttonText.text = "<color=red>[Sold]</color>";
            }
        }
    }
}
