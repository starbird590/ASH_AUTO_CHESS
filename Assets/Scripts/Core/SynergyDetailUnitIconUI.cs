using UnityEngine;
using UnityEngine.UI;

public class SynergyDetailUnitIconUI : MonoBehaviour
{
    public Image iconImage;
    public Text nameText;
    public GameObject activeHighlight;
    public GameObject inactiveOverlay;

    public void Refresh(UnitLogicDataSO unitData, bool isActive)
    {
        if (unitData == null)
        {
            gameObject.SetActive(false);
            return;
        }

        if (iconImage != null)
        {
            iconImage.sprite = unitData.unitSprite;
            iconImage.enabled = unitData.unitSprite != null;
        }

        if (nameText != null)
        {
            nameText.text = unitData.chessName;
        }

        if (activeHighlight != null)
        {
            activeHighlight.SetActive(isActive);
        }

        if (inactiveOverlay != null)
        {
            inactiveOverlay.SetActive(!isActive);
        }

        gameObject.SetActive(true);
    }
}
