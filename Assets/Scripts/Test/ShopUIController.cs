using UnityEngine;

public class ShopUIController : MonoBehaviour
{
    [Header("🔗 UI Elements Assignment")]
    [SerializeField] private GameObject shopUIPanel;     // 商店大总管面板（包含遮罩和货架）
    [SerializeField] private GameObject openShopButton;   // 常驻角落的呼出按钮

    private void Start()
    {
        if (GameFlowManager.Instance != null)
        {
            GameFlowManager.Instance.StateEntered += OnGameStateEntered;
            OnGameStateEntered(GameFlowManager.Instance.CurrentState); // 初始化安检
        }
    }

    private void OnDestroy()
    {
        if (GameFlowManager.Instance != null)
        {
            GameFlowManager.Instance.StateEntered -= OnGameStateEntered;
        }
    }

    private void OnGameStateEntered(GameState newState)
    {
        if (newState == GameState.Intermission)
        {
            // 进入整备期：全自动展示商店面板与呼出图标
            SetShopPanelActive(true);
            if (openShopButton != null) openShopButton.SetActive(true);
        }
        else
        {
            // 切换到其他期：无条件收回所有商店UI，免得挡住战局
            SetShopPanelActive(false);
            if (openShopButton != null) openShopButton.SetActive(false);
        }
    }

    public void ClickOpenShop()
    {
        if (GameFlowManager.Instance != null && GameFlowManager.Instance.CurrentState == GameState.Intermission)
        {
            SetShopPanelActive(true);
        }
    }

    public void ClickCloseShop()
    {
        SetShopPanelActive(false);
    }

    private void SetShopPanelActive(bool isActive)
    {
        if (shopUIPanel != null)
        {
            shopUIPanel.SetActive(isActive);
        }
    }
}
