using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class TraitEffect
{
    public float damageBonusPercent;
    public int armorBonus;
    public float attackIntervalReduction;
}

[Serializable]
public class TraitTierConfig
{
    public string unionChildId;
    public int unionCounts = 1;

    [TextArea(2, 6)]
    public string levelDescription;

    public string skillIds;

    public List<string> GetSkillIds()
    {
        return TraitSO.ParseSkillIdList(skillIds);
    }
}

[CreateAssetMenu(fileName = "Trait_", menuName = "ASH Auto Chess/Trait")]
public class TraitSO : ScriptableObject
{
    [Header("Table Identity")]
    public string unionId;

    [Header("Display")]
    public string traitName;
    public Sprite icon;

    [TextArea(3, 8)]
    public string description;

    [Header("Table-Driven Tiers")]
    public TraitTierConfig[] tiers = new TraitTierConfig[0];

    [Header("Legacy Tier Thresholds")]
    public int[] thresholds = new int[] { 2, 4, 6 };

    [Header("Legacy Direct Effects")]
    public TraitEffect[] tierEffects = new TraitEffect[3];

    public string traitId
    {
        get { return unionId; }
    }

    public string id
    {
        get { return unionId; }
    }

    public string displayName
    {
        get { return traitName; }
    }

    public int GetActiveTierIndex(int unitCount)
    {
        if (tiers != null && tiers.Length > 0)
        {
            int activeTier = -1;
            for (int i = 0; i < tiers.Length; i++)
            {
                TraitTierConfig tier = tiers[i];
                if (tier != null && unitCount >= Mathf.Max(1, tier.unionCounts))
                {
                    activeTier = i;
                }
            }

            return activeTier;
        }

        if (thresholds == null || thresholds.Length == 0)
        {
            return -1;
        }

        int legacyActiveTier = -1;
        for (int i = 0; i < thresholds.Length; i++)
        {
            if (unitCount >= thresholds[i])
            {
                legacyActiveTier = i;
            }
        }

        return legacyActiveTier;
    }

    public TraitTierConfig GetTierForCount(int unitCount)
    {
        int tierIndex = GetActiveTierIndex(unitCount);
        if (tierIndex < 0 || tiers == null || tierIndex >= tiers.Length)
        {
            return null;
        }

        return tiers[tierIndex];
    }

    public TraitEffect GetEffectForCount(int unitCount)
    {
        int tierIndex = GetActiveTierIndex(unitCount);
        if (tierIndex < 0 || tierEffects == null || tierIndex >= tierEffects.Length)
        {
            return null;
        }

        return tierEffects[tierIndex];
    }

    public int GetNextDisplayThreshold(int unitCount)
    {
        int[] displayThresholds = GetDisplayThresholds();
        if (displayThresholds == null || displayThresholds.Length == 0)
        {
            return Mathf.Max(1, unitCount);
        }

        for (int i = 0; i < displayThresholds.Length; i++)
        {
            if (unitCount < displayThresholds[i])
            {
                return displayThresholds[i];
            }
        }

        return displayThresholds[displayThresholds.Length - 1];
    }

    public int[] GetDisplayThresholds()
    {
        if (tiers != null && tiers.Length > 0)
        {
            int[] tierThresholds = new int[tiers.Length];
            for (int i = 0; i < tiers.Length; i++)
            {
                tierThresholds[i] = tiers[i] != null ? Mathf.Max(1, tiers[i].unionCounts) : 1;
            }

            return tierThresholds;
        }

        return thresholds;
    }

    public List<string> GetActiveTierSkillIds(int unitCount)
    {
        TraitTierConfig tier = GetTierForCount(unitCount);
        return tier != null ? tier.GetSkillIds() : new List<string>();
    }

    public static List<string> ParseSkillIdList(string value)
    {
        List<string> result = new List<string>();
        string inner = StripOuterBraces(CleanCell(value));
        if (string.IsNullOrWhiteSpace(inner))
        {
            return result;
        }

        string[] tokens = inner.Split(new[]
        {
            ',',
            ';',
            '|',
            '\uFF0C',
            '\uFF1B',
            '\u3001'
        }, StringSplitOptions.RemoveEmptyEntries);

        for (int i = 0; i < tokens.Length; i++)
        {
            string skillId = StripOuterBraces(CleanCell(tokens[i]));
            if (string.IsNullOrWhiteSpace(skillId) || ContainsIgnoreCase(result, skillId))
            {
                continue;
            }

            result.Add(skillId);
        }

        return result;
    }

    private void OnValidate()
    {
        unionId = CleanCell(unionId);
        traitName = CleanCell(traitName);
        SanitizeTiers();
        SanitizeLegacyThresholds();
        SanitizeLegacyEffects();
    }

    private void SanitizeTiers()
    {
        if (tiers == null || tiers.Length == 0)
        {
            return;
        }

        for (int i = 0; i < tiers.Length; i++)
        {
            if (tiers[i] == null)
            {
                tiers[i] = new TraitTierConfig();
            }

            tiers[i].unionChildId = CleanCell(tiers[i].unionChildId);
            tiers[i].levelDescription = CleanCell(tiers[i].levelDescription);
            tiers[i].skillIds = JoinSkillIds(ParseSkillIdList(tiers[i].skillIds));
            tiers[i].unionCounts = Mathf.Max(1, tiers[i].unionCounts);
        }

        Array.Sort(tiers, (left, right) =>
        {
            int leftCount = left != null ? left.unionCounts : 0;
            int rightCount = right != null ? right.unionCounts : 0;
            return leftCount.CompareTo(rightCount);
        });

        thresholds = GetDisplayThresholds();
    }

    private void SanitizeLegacyThresholds()
    {
        if (thresholds == null)
        {
            thresholds = new int[0];
            return;
        }

        for (int i = 0; i < thresholds.Length; i++)
        {
            thresholds[i] = Mathf.Max(1, thresholds[i]);
        }
    }

    private void SanitizeLegacyEffects()
    {
        if (tierEffects == null)
        {
            return;
        }

        for (int i = 0; i < tierEffects.Length; i++)
        {
            if (tierEffects[i] == null)
            {
                tierEffects[i] = new TraitEffect();
            }

            tierEffects[i].armorBonus = Mathf.Max(0, tierEffects[i].armorBonus);
            tierEffects[i].attackIntervalReduction = Mathf.Max(0f, tierEffects[i].attackIntervalReduction);
        }
    }

    private static string JoinSkillIds(List<string> skillIds)
    {
        return skillIds == null || skillIds.Count == 0 ? string.Empty : string.Join(";", skillIds.ToArray());
    }

    private static bool ContainsIgnoreCase(List<string> values, string value)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (string.Equals(values[i], value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string StripOuterBraces(string value)
    {
        string cleaned = CleanCell(value);
        while (cleaned.Length >= 2
            && ((cleaned[0] == '{' && cleaned[cleaned.Length - 1] == '}')
                || (cleaned[0] == '[' && cleaned[cleaned.Length - 1] == ']')))
        {
            cleaned = cleaned.Substring(1, cleaned.Length - 2).Trim();
        }

        return cleaned;
    }

    private static string CleanCell(string value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        return value
            .Replace("\uFEFF", string.Empty)
            .Replace("\u00A0", string.Empty)
            .Replace("\\xa0", string.Empty)
            .Trim();
    }
}
