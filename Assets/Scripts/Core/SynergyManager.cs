using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 云顶式羁绊精算大管家：负责同名去重计数、档位计算、临时 Buff 灌注与 UI 通知。
/// </summary>
public class SynergyManager : MonoBehaviour
{
    private static SynergyManager instance;

    public static SynergyManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindObjectOfType<SynergyManager>();
                if (instance == null)
                {
                    GameObject managerObject = new GameObject("SynergyManager");
                    instance = managerObject.AddComponent<SynergyManager>();
                }
            }

            return instance;
        }
        private set
        {
            instance = value;
        }
    }

    public static bool HasInstance
    {
        get { return instance != null; }
    }

    private readonly Dictionary<TraitSO, int> currentTraitCounts = new Dictionary<TraitSO, int>();
    private readonly Dictionary<TraitSO, TraitEffect> activeTraitEffects = new Dictionary<TraitSO, TraitEffect>();
    private readonly List<UnitLogic> scratchUnits = new List<UnitLogic>();

    public event Action OnSynergyUpdated;

    public Dictionary<TraitSO, int> CurrentTraitCounts
    {
        get { return currentTraitCounts; }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        transform.SetParent(null);
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// 部署期或 UI 预览用刷新：只重算账本并通知 UI，不灌注临时战斗属性。
    /// </summary>
    public void RecalculateSynergies()
    {
        BuildTraitLedger();
        PublishSynergyUpdated();
    }

    /// <summary>
    /// 开战前黄金帧调用：先剥离旧 Buff，再重算当前阵容，最后按激活档位灌注本局临时属性。
    /// </summary>
    public void RefreshAndApplySynergies()
    {
        ClearRuntimeEffects(false);
        BuildTraitLedger();
        BuildActiveTraitEffects();
        ApplyActiveTraitEffects();
        PublishSynergyUpdated();
    }

    /// <summary>
    /// 战斗结束或返回整备时调用，确保所有临时属性完全卸载。
    /// </summary>
    public void ClearAllSynergyEffects()
    {
        ClearRuntimeEffects(true);
    }

    private void BuildTraitLedger()
    {
        currentTraitCounts.Clear();
        CollectPlayerBattlefieldUnits(scratchUnits);

        HashSet<string> countedUnitKeys = new HashSet<string>();
        for (int i = 0; i < scratchUnits.Count; i++)
        {
            UnitLogic unit = scratchUnits[i];
            if (unit == null)
            {
                continue;
            }

            string unitKey = unit.GetSynergyIdentityKey();
            if (countedUnitKeys.Contains(unitKey))
            {
                continue;
            }

            countedUnitKeys.Add(unitKey);
            IReadOnlyList<TraitSO> traits = unit.RuntimeTraits;
            for (int traitIndex = 0; traitIndex < traits.Count; traitIndex++)
            {
                TraitSO trait = traits[traitIndex];
                if (trait == null)
                {
                    continue;
                }

                int count;
                currentTraitCounts.TryGetValue(trait, out count);
                currentTraitCounts[trait] = count + 1;
            }
        }
    }

    private void BuildActiveTraitEffects()
    {
        activeTraitEffects.Clear();
        foreach (KeyValuePair<TraitSO, int> pair in currentTraitCounts)
        {
            if (pair.Key == null || pair.Value <= 0)
            {
                continue;
            }

            TraitEffect effect = pair.Key.GetEffectForCount(pair.Value);
            if (effect != null)
            {
                activeTraitEffects[pair.Key] = effect;
            }
        }
    }

    private void ApplyActiveTraitEffects()
    {
        CollectPlayerBattlefieldUnits(scratchUnits);
        for (int i = 0; i < scratchUnits.Count; i++)
        {
            UnitLogic unit = scratchUnits[i];
            if (unit == null)
            {
                continue;
            }

            IReadOnlyList<TraitSO> traits = unit.RuntimeTraits;
            for (int traitIndex = 0; traitIndex < traits.Count; traitIndex++)
            {
                TraitSO trait = traits[traitIndex];
                TraitEffect effect;
                if (trait != null && activeTraitEffects.TryGetValue(trait, out effect))
                {
                    unit.ApplyTraitEffect(effect);
                }
            }
        }
    }

    private void ClearRuntimeEffects(bool clearLedger)
    {
        IReadOnlyList<UnitLogic> activeUnits = UnitLogic.ActiveUnits;
        for (int i = 0; i < activeUnits.Count; i++)
        {
            UnitLogic unit = activeUnits[i];
            if (unit != null)
            {
                unit.ResetTemporarySynergyModifiers();
            }
        }

        activeTraitEffects.Clear();
        if (clearLedger)
        {
            currentTraitCounts.Clear();
            PublishSynergyUpdated();
        }
    }

    private void CollectPlayerBattlefieldUnits(List<UnitLogic> result)
    {
        result.Clear();

        GridManager gridManager = GridManager.Instance;
        Transform battlefieldContainer = gridManager != null ? gridManager.battlefieldContainer : null;
        if (battlefieldContainer == null)
        {
            return;
        }

        for (int i = 0; i < battlefieldContainer.childCount; i++)
        {
            UnitLogic unit = battlefieldContainer.GetChild(i).GetComponent<UnitLogic>();
            if (unit == null
                || unit.faction != UnitFaction.Player
                || !unit.IsAlive
                || !unit.gameObject.activeInHierarchy)
            {
                continue;
            }

            result.Add(unit);
        }
    }

    private void PublishSynergyUpdated()
    {
        if (OnSynergyUpdated != null)
        {
            OnSynergyUpdated.Invoke();
        }
    }
}
