using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

public class SynergyDetailPanelUI : MonoBehaviour
{
    [Header("Text")]
    public Text titleText;
    public Text bodyText;

    [Header("Unit Icons")]
    public SynergyDetailUnitIconUI unitIconPrefab;
    public Transform unitIconRoot;

    private RectTransform panelRect;
    private Canvas rootCanvas;
    private readonly List<SynergyDetailUnitIconUI> unitIconPool = new List<SynergyDetailUnitIconUI>();
    private readonly List<UnitLogicDataSO> unitRepresentatives = new List<UnitLogicDataSO>();
    private readonly Dictionary<string, UnitLogicDataSO> representativeByIdentity =
        new Dictionary<string, UnitLogicDataSO>(StringComparer.OrdinalIgnoreCase);

    private void Awake()
    {
        ResolveReferences();
        Hide();
    }

    private void Reset()
    {
        ResolveReferences();
    }

    public void Show(TraitSynergyDisplayModel displayModel, Vector2 screenPosition)
    {
        if (displayModel == null || displayModel.Trait == null)
        {
            Hide();
            return;
        }

        ResolveReferences();
        RefreshText(displayModel);
        RefreshUnitIcons(displayModel);
        AlignTopLeftToScreenPosition(screenPosition);
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private void ResolveReferences()
    {
        if (panelRect == null)
        {
            panelRect = transform as RectTransform;
        }

        if (rootCanvas == null)
        {
            rootCanvas = GetComponentInParent<Canvas>();
        }
    }

    private void RefreshText(TraitSynergyDisplayModel displayModel)
    {
        if (titleText != null)
        {
            titleText.text = displayModel.DisplayName;
        }

        if (bodyText != null)
        {
            bodyText.text = BuildDetailText(displayModel);
        }
    }

    private void RefreshUnitIcons(TraitSynergyDisplayModel displayModel)
    {
        for (int i = 0; i < unitIconPool.Count; i++)
        {
            if (unitIconPool[i] != null)
            {
                unitIconPool[i].gameObject.SetActive(false);
            }
        }

        if (unitIconPrefab == null || unitIconRoot == null)
        {
            return;
        }

        BuildUnitRepresentatives(displayModel.Trait);
        for (int i = 0; i < unitRepresentatives.Count; i++)
        {
            UnitLogicDataSO unitData = unitRepresentatives[i];
            SynergyDetailUnitIconUI item = GetOrCreateUnitIcon(i);
            if (item == null || unitData == null)
            {
                continue;
            }

            string identityKey = UnitLogic.GetSynergyIdentityKey(unitData);
            bool isActive = SynergyManager.HasInstance
                && SynergyManager.Instance.IsTraitUnitIdentityCounted(displayModel.Trait, identityKey);
            item.transform.SetParent(unitIconRoot, false);
            item.Refresh(unitData, isActive);
        }
    }

    private void BuildUnitRepresentatives(TraitSO trait)
    {
        unitRepresentatives.Clear();
        representativeByIdentity.Clear();

        UnitDataManager unitDataManager = UnitDataManager.Instance;
        if (unitDataManager == null)
        {
            return;
        }

        foreach (KeyValuePair<string, UnitLogicDataSO> pair in unitDataManager.UnitDataDict)
        {
            UnitLogicDataSO unitData = pair.Value;
            if (unitData == null
                || unitData.faction != (int)UnitFaction.Player
                || !unitDataManager.UnitDataHasTrait(unitData, trait))
            {
                continue;
            }

            string identityKey = UnitLogic.GetSynergyIdentityKey(unitData);
            if (string.IsNullOrWhiteSpace(identityKey))
            {
                continue;
            }

            if (!representativeByIdentity.TryGetValue(identityKey, out UnitLogicDataSO current)
                || IsBetterRepresentative(unitData, current))
            {
                representativeByIdentity[identityKey] = unitData;
            }
        }

        foreach (KeyValuePair<string, UnitLogicDataSO> pair in representativeByIdentity)
        {
            unitRepresentatives.Add(pair.Value);
        }

        unitRepresentatives.Sort(CompareUnitRepresentatives);
    }

    private SynergyDetailUnitIconUI GetOrCreateUnitIcon(int index)
    {
        while (unitIconPool.Count <= index)
        {
            SynergyDetailUnitIconUI newItem = Instantiate(unitIconPrefab, unitIconRoot);
            newItem.gameObject.SetActive(false);
            unitIconPool.Add(newItem);
        }

        return unitIconPool[index];
    }

    private void AlignTopLeftToScreenPosition(Vector2 screenPosition)
    {
        if (panelRect == null || rootCanvas == null)
        {
            return;
        }

        RectTransform parentRect = panelRect.parent as RectTransform;
        if (parentRect == null)
        {
            return;
        }

        Camera canvasCamera = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
        Vector3 worldPoint;
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(parentRect, screenPosition, canvasCamera, out worldPoint))
        {
            panelRect.position = worldPoint;
        }
    }

    private string BuildDetailText(TraitSynergyDisplayModel displayModel)
    {
        StringBuilder builder = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(displayModel.Description))
        {
            builder.AppendLine(displayModel.Description.Trim());
        }

        TraitTierConfig[] tiers = displayModel.AllTiers;
        if (tiers != null && tiers.Length > 0)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            for (int i = 0; i < tiers.Length; i++)
            {
                TraitTierConfig tier = tiers[i];
                if (tier == null)
                {
                    continue;
                }

                builder.Append(Mathf.Max(1, tier.unionCounts));
                builder.Append(": ");
                builder.Append(string.IsNullOrWhiteSpace(tier.levelDescription)
                    ? string.Empty
                    : tier.levelDescription.Trim());

                if (i < tiers.Length - 1)
                {
                    builder.AppendLine();
                }
            }
        }

        return builder.ToString();
    }

    private static bool IsBetterRepresentative(UnitLogicDataSO candidate, UnitLogicDataSO current)
    {
        if (current == null)
        {
            return true;
        }

        if (candidate == null)
        {
            return false;
        }

        int candidateTier = Mathf.Max(1, candidate.unitTier);
        int currentTier = Mathf.Max(1, current.unitTier);
        if (candidateTier != currentTier)
        {
            return candidateTier < currentTier;
        }

        return string.Compare(candidate.chessId, current.chessId, StringComparison.OrdinalIgnoreCase) < 0;
    }

    private static int CompareUnitRepresentatives(UnitLogicDataSO left, UnitLogicDataSO right)
    {
        if (left == null && right == null)
        {
            return 0;
        }

        if (left == null)
        {
            return 1;
        }

        if (right == null)
        {
            return -1;
        }

        int costCompare = left.unitCost.CompareTo(right.unitCost);
        if (costCompare != 0)
        {
            return costCompare;
        }

        return string.Compare(left.chessId, right.chessId, StringComparison.OrdinalIgnoreCase);
    }
}
