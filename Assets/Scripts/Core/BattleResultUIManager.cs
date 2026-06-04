using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 结算面板 UI 控制器。
/// 挂载在 Result_Panel 根物体上，监听 GameFlowManager 的 Result 状态并展示胜负与奖励。
/// </summary>
public class BattleResultUIManager : MonoBehaviour
{
    [Header("结算 UI")]
    [SerializeField, Tooltip("结算标题文本。")]
    private Text titleText;

    [SerializeField, Tooltip("奖励金额文本。")]
    private Text rewardText;

    [SerializeField, Tooltip("确认结算并继续按钮。")]
    private Button confirmButton;

    private CanvasGroup canvasGroup;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        HidePanel();

        if (confirmButton != null)
        {
            confirmButton.onClick.AddListener(ConfirmAndContinue);
        }
    }

    private void OnEnable()
    {
        if (GameFlowManager.Instance != null)
        {
            GameFlowManager.Instance.StateEntered += HandleStateEntered;
        }
    }

    private void OnDisable()
    {
        if (GameFlowManager.Instance != null)
        {
            GameFlowManager.Instance.StateEntered -= HandleStateEntered;
        }
    }

    private void Start()
    {
        if (GameFlowManager.Instance != null)
        {
            GameFlowManager.Instance.StateEntered -= HandleStateEntered;
            GameFlowManager.Instance.StateEntered += HandleStateEntered;
        }
    }

    private void HandleStateEntered(GameState state)
    {
        if (state != GameState.Result)
        {
            return;
        }

        ShowResult();
    }

    private void ShowResult()
    {
        if (canvasGroup != null)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }

        GameFlowManager flowManager = GameFlowManager.Instance;
        if (flowManager == null)
        {
            return;
        }

        if (titleText != null)
        {
            titleText.supportRichText = true;
            titleText.text = flowManager.LastBattleWasVictory
                ? "<color=#00FFCC>【关卡大捷】</color>"
                : "<color=#FF3333>【战线失守】</color>";
        }

        if (rewardText != null)
        {
            rewardText.text = "+" + flowManager.PendingStageReward + "元";
        }
    }

    private void ConfirmAndContinue()
    {
        if (GameFlowManager.Instance != null)
        {
            GameFlowManager.Instance.ConfirmResultAndEnterIntermission();
        }

        HidePanel();
    }

    private void HidePanel()
    {
        if (canvasGroup == null)
        {
            return;
        }

        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }
}
