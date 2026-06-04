using UnityEngine;
using UnityEngine.UI;

public class ShopSlotUI : MonoBehaviour
{
    [Header("⚙️ 货架编号设置")]
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
        if (ShopManager.Instance == null || buttonText == null) return;

        if (ShopManager.Instance.SoldOutSlots[slotIndex])
        {
            buttonText.text = "<color=red>【已售罄】</color>";
            return;
        }

        GameObject unitPrefab = ShopManager.Instance.ShopSlots[slotIndex];
        if (unitPrefab == null)
        {
            buttonText.text = "【空置】";
            return;
        }

        UnitLogic logic = unitPrefab.GetComponent<UnitLogic>();
        if (logic != null)
        {
            // 🌟 动态写入：几阶卡、Unity文件名、原价、占用Cost数
            buttonText.text = $"{logic.unitTier}阶•{unitPrefab.name}\n价格: {logic.unitPrice}元 (Cost:{logic.unitCost})";
        }
    }

    private void Update()
    {
        if (ShopManager.Instance != null && ShopManager.Instance.SoldOutSlots[slotIndex])
        {
            if (buttonText != null && buttonText.text != "<color=red>【已售罄】</color>")
            {
                buttonText.text = "<color=red>【已售罄】</color>";
            }
        }
    }
}