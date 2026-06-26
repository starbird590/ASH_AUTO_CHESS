using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// 左侧羁绊列表中的单条 UI。只负责接收数据并刷新视觉状态。
/// </summary>
public class SynergyItemUI : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
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

    private SynergyListUI owner;
    private TraitSynergyDisplayModel currentDisplayModel;
    private Coroutine longPressCoroutine;
    private Vector2 longPressScreenPosition;

    public void SetOwner(SynergyListUI listOwner)
    {
        owner = listOwner;
    }

    public void Refresh(TraitSynergyDisplayModel displayModel)
    {
        if (displayModel == null)
        {
            currentDisplayModel = null;
            gameObject.SetActive(false);
            return;
        }

        currentDisplayModel = displayModel;
        Refresh(displayModel.Trait, displayModel.UnitCount, displayModel.ActiveTierIndex, displayModel.Thresholds);
    }

    public void Refresh(TraitSO trait, int unitCount)
    {
        int safeCount = Mathf.Max(0, unitCount);
        int activeTier = trait != null ? trait.GetActiveTierIndex(safeCount) : -1;
        int[] displayThresholds = trait != null ? trait.GetDisplayThresholds() : new int[0];
        Refresh(trait, unitCount, activeTier, displayThresholds);
    }

    private void Refresh(TraitSO trait, int unitCount, int activeTier, int[] displayThresholds)
    {
        if (trait == null)
        {
            currentDisplayModel = null;
            gameObject.SetActive(false);
            return;
        }

        int safeCount = Mathf.Max(0, unitCount);
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
             if (displayThresholds != null && displayThresholds.Length > 0)
                {
                string[] tempArray = new string[displayThresholds.Length];
                for (int i = 0; i < displayThresholds.Length; i++)
                    {
                         tempArray[i] = displayThresholds[i].ToString();
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

    public void OnPointerDown(PointerEventData eventData)
    {
        StopLongPress();
        if (owner == null || currentDisplayModel == null)
        {
            return;
        }

        longPressScreenPosition = eventData.position;
        longPressCoroutine = StartCoroutine(ShowDetailsAfterDelay());
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        StopLongPress();
        HideDetails();
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        StopLongPress();
        HideDetails();
    }

    private void OnDisable()
    {
        StopLongPress();
        HideDetails();
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

    private IEnumerator ShowDetailsAfterDelay()
    {
        yield return new WaitForSeconds(owner != null ? owner.LongPressSeconds : 0.45f);
        longPressCoroutine = null;
        if (owner != null && currentDisplayModel != null)
        {
            owner.ShowTraitDetails(currentDisplayModel, longPressScreenPosition);
        }
    }

    private void StopLongPress()
    {
        if (longPressCoroutine != null)
        {
            StopCoroutine(longPressCoroutine);
            longPressCoroutine = null;
        }
    }

    private void HideDetails()
    {
        if (owner != null)
        {
            owner.HideTraitDetails();
        }
    }
}
