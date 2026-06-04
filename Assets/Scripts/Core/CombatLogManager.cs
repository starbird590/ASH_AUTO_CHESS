using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 富文本滚动战斗日志管理器。
/// 场景中可放一个 CombatLogManager 并绑定 Text / ScrollRect；没有 UI 实例时仍会输出到 Console。
/// </summary>
public class CombatLogManager : MonoBehaviour
{
    public static CombatLogManager Instance { get; private set; }

    [Header("日志 UI")]
    [SerializeField, Tooltip("用于展示富文本战报的 Text 组件，请勾选 Rich Text。")]
    private Text logText;

    [SerializeField, Tooltip("可选：绑定 ScrollRect 后，新日志会自动滚动到底部。")]
    private ScrollRect scrollRect;

    [SerializeField, Tooltip("最多保留多少条战报，超过后滚动移除最早记录。")]
    private int maxLogLines = 80;

    private readonly List<string> logLines = new List<string>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    public static void LogDamage(UnitLogic attacker, UnitLogic target, int damage)
    {
        if (attacker == null || target == null)
        {
            return;
        }

        LogRaw(attacker.GetColoredDisplayName() + " 对 " + target.GetColoredDisplayName() + " 造成了 " + damage + " 点伤害！");
    }

    public static void LogDeath(UnitLogic unit)
    {
        if (unit == null)
        {
            return;
        }

        string resultText = unit.faction == UnitFaction.Player ? " 被重伤，退出了战场！" : " 被彻底击败，退出了战场！";
        LogRaw("<color=red>【战损】</color>" + unit.GetColoredDisplayName() + resultText);
    }

    public static void LogRaw(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        if (Instance != null)
        {
            Instance.AppendLine(message);
        }

        Debug.Log(message);
    }

    private void AppendLine(string message)
    {
        logLines.Add(message);
        while (logLines.Count > maxLogLines)
        {
            logLines.RemoveAt(0);
        }

        if (logText != null)
        {
            logText.supportRichText = true;
            logText.text = string.Join("\n", logLines.ToArray());
        }

        if (scrollRect != null)
        {
            Canvas.ForceUpdateCanvases();
            scrollRect.verticalNormalizedPosition = 0f;
        }
    }
}
