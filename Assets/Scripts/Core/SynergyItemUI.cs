using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 左侧羁绊列表中的单条 UI。只负责接收数据并刷新视觉状态。
/// </summary>
public class SynergyItemUI : MonoBehaviour
{
    [Header("UI References")]
    public Image iconImage;
    public Text nameText;
    public Text countText;
    public Image backgroundImage;

    [Header("Tier Backgrounds")]
    public Sprite bronzeBackground;
    public Sprite silverBackground;
    public Sprite goldBackground;
    public Sprite rainbowBackground;

    [Header("Colors")]
    public Color inactiveIconColor = new Color(0.55f, 0.55f, 0.55f, 0.45f);
    public Color inactiveTextColor = new Color(0.65f, 0.65f, 0.65f, 0.55f);
    public Color activeIconColor = Color.white;
    public Color activeNameColor = Color.white;
    public Color activeCountColor = new Color(0.35f, 1f, 0.35f, 1f);
    public Color inactiveBackgroundColor = new Color(0.18f, 0.18f, 0.18f, 0.45f);
    public Color activeBackgroundColor = Color.white;

    public void Refresh(TraitSO trait, int unitCount)
    {
        if (trait == null)
        {
            gameObject.SetActive(false);
            return;
        }

        int safeCount = Mathf.Max(0, unitCount);
        int activeTier = trait.GetActiveTierIndex(safeCount);
        bool isActive = activeTier >= 0;

        if (iconImage != null)
        {
            iconImage.sprite = trait.icon;
            iconImage.color = isActive ? activeIconColor : inactiveIconColor;
            iconImage.enabled = trait.icon != null;
        }

        if (nameText != null)
        {
            nameText.text = string.IsNullOrEmpty(trait.traitName) ? trait.name : trait.traitName;
            nameText.color = isActive ? activeNameColor : inactiveTextColor;
        }

        if (countText != null)
        {
            // 1. 动态将 thresholds 整型数组（例如 [2, 4, 6]）转换为 "2 / 4 / 6" 的字符串链条
             string milestoneChain = "";
             if (trait.thresholds != null && trait.thresholds.Length > 0)
                {
                string[] tempArray = new string[trait.thresholds.Length];
                for (int i = 0; i < trait.thresholds.Length; i++)
                    {
                         tempArray[i] = trait.thresholds[i].ToString();
                    }
                milestoneChain = string.Join(" / ", tempArray);
                }

            // 2. 【核心优化】：完美对齐金铲铲排版，显示为： "当前人数   2 / 4 / 6"
            countText.text = safeCount + "   " + milestoneChain;
            countText.color = isActive ? activeCountColor : inactiveTextColor;
        }

        if (backgroundImage != null)
        {
            backgroundImage.sprite = isActive ? GetBackgroundForTier(activeTier) : null;
            backgroundImage.color = isActive ? activeBackgroundColor : inactiveBackgroundColor;
        }
    }

    private Sprite GetBackgroundForTier(int activeTier)
    {
        if (activeTier <= 0)
        {
            return bronzeBackground;
        }

        if (activeTier == 1)
        {
            return silverBackground != null ? silverBackground : bronzeBackground;
        }

        if (activeTier == 2)
        {
            return goldBackground != null ? goldBackground : silverBackground;
        }

        return rainbowBackground != null ? rainbowBackground : goldBackground;
    }
}
