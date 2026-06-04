using UnityEngine;

/// <summary>
/// 单个羁绊档位的属性效果。数值由策划在 TraitSO 资产中按门槛逐档配置。
/// </summary>
[System.Serializable]
public class TraitEffect
{
    [Tooltip("攻击伤害增幅百分比。填 20 表示基于单位基础伤害提高 20%。")]
    public float damageBonusPercent;

    [Tooltip("额外护甲值，直接叠加到单位本局战斗的临时护甲上。")]
    public int armorBonus;

    [Tooltip("远程射击间隔缩短秒数。系统会按攻速换算为更快的 attackSpeed。")]
    public float attackIntervalReduction;
}

/// <summary>
/// 数据驱动羁绊资产。每个资产代表一个职业、种族或战术标签。
/// </summary>
[CreateAssetMenu(fileName = "Trait_", menuName = "ASH Auto Chess/Trait")]
public class TraitSO : ScriptableObject
{
    [Header("基础标签信息")]
    [Tooltip("羁绊唯一名称，例如：装甲兵、突击队、机械师。")]
    public string traitName;

    [Tooltip("左侧羁绊面板上展示的图标。")]
    public Sprite icon;

    [TextArea(3, 8)]
    [Tooltip("羁绊战术描述，会显示在 UI 或后续详情面板中。")]
    public string description;

    [Header("多阶梯门槛线")]
    [Tooltip("触发该羁绊所需的去重人头档位，例如 2/4/6。")]
    public int[] thresholds = new int[] { 2, 4, 6 };

    [Header("阶梯效果配置")]
    [Tooltip("每个门槛档位对应一个效果。数组下标应与 thresholds 对齐。")]
    public TraitEffect[] tierEffects = new TraitEffect[3];

    public int GetActiveTierIndex(int unitCount)
    {
        if (thresholds == null || thresholds.Length == 0)
        {
            return -1;
        }

        int activeTier = -1;
        for (int i = 0; i < thresholds.Length; i++)
        {
            if (unitCount >= thresholds[i])
            {
                activeTier = i;
            }
        }

        return activeTier;
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
        if (thresholds == null || thresholds.Length == 0)
        {
            return Mathf.Max(1, unitCount);
        }

        for (int i = 0; i < thresholds.Length; i++)
        {
            if (unitCount < thresholds[i])
            {
                return thresholds[i];
            }
        }

        return thresholds[thresholds.Length - 1];
    }

    private void OnValidate()
    {
        if (thresholds != null)
        {
            for (int i = 0; i < thresholds.Length; i++)
            {
                thresholds[i] = Mathf.Max(1, thresholds[i]);
            }
        }

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
}
