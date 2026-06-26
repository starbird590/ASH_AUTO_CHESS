using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class TraitSynergyDisplayModel
{
    public TraitSO Trait { get; private set; }
    public int UnitCount { get; private set; }
    public int ActiveTierIndex { get; private set; }
    public TraitTierConfig ActiveTier { get; private set; }
    public int[] Thresholds { get; private set; }
    public TraitTierConfig[] AllTiers { get; private set; }

    public bool IsActive
    {
        get { return ActiveTierIndex >= 0; }
    }

    public string DisplayName
    {
        get
        {
            if (Trait == null)
            {
                return string.Empty;
            }

            return string.IsNullOrWhiteSpace(Trait.traitName) ? Trait.name : Trait.traitName;
        }
    }

    public string Description
    {
        get { return Trait != null ? Trait.description : string.Empty; }
    }

    public TraitSynergyDisplayModel(TraitSO trait, int unitCount)
    {
        Trait = trait;
        UnitCount = Mathf.Max(0, unitCount);
        ActiveTierIndex = trait != null ? trait.GetActiveTierIndex(UnitCount) : -1;
        ActiveTier = trait != null ? trait.GetTierForCount(UnitCount) : null;
        Thresholds = trait != null ? trait.GetDisplayThresholds() : new int[0];
        AllTiers = trait != null && trait.tiers != null ? trait.tiers : new TraitTierConfig[0];
    }
}

public sealed class ActiveTraitTierModel
{
    public TraitSO Trait { get; private set; }
    public int UnitCount { get; private set; }
    public int ActiveTierIndex { get; private set; }
    public TraitTierConfig ActiveTier { get; private set; }
    public List<string> SkillIds { get; private set; }
    public List<UnitLogic> QualifyingUnits { get; private set; }

    public ActiveTraitTierModel(TraitSynergyDisplayModel displayModel, List<UnitLogic> qualifyingUnits)
    {
        Trait = displayModel != null ? displayModel.Trait : null;
        UnitCount = displayModel != null ? displayModel.UnitCount : 0;
        ActiveTierIndex = displayModel != null ? displayModel.ActiveTierIndex : -1;
        ActiveTier = displayModel != null ? displayModel.ActiveTier : null;
        SkillIds = ActiveTier != null ? ActiveTier.GetSkillIds() : new List<string>();
        QualifyingUnits = qualifyingUnits != null ? new List<UnitLogic>(qualifyingUnits) : new List<UnitLogic>();
    }
}

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
    private readonly Dictionary<TraitSO, List<UnitLogic>> currentTraitUnits = new Dictionary<TraitSO, List<UnitLogic>>();
    private readonly Dictionary<TraitSO, TraitEffect> activeTraitEffects = new Dictionary<TraitSO, TraitEffect>();
    private readonly List<TraitSynergyDisplayModel> traitDisplayModels = new List<TraitSynergyDisplayModel>();
    private readonly List<ActiveTraitTierModel> activeTraitTiers = new List<ActiveTraitTierModel>();
    private readonly List<UnitLogic> scratchUnits = new List<UnitLogic>();

    public event Action OnSynergyUpdated;

    public Dictionary<TraitSO, int> CurrentTraitCounts
    {
        get { return currentTraitCounts; }
    }

    public IReadOnlyList<TraitSynergyDisplayModel> TraitDisplayModels
    {
        get { return traitDisplayModels; }
    }

    public IReadOnlyList<ActiveTraitTierModel> ActiveTraitTiers
    {
        get { return activeTraitTiers; }
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

    public void RecalculateSynergies()
    {
        BuildTraitLedger();
        BuildSynergyModels();
        PublishSynergyUpdated();
    }

    public void RefreshAndApplySynergies()
    {
        ClearRuntimeEffects(false);
        BuildTraitLedger();
        BuildSynergyModels();
        BuildActiveTraitEffects();
        ApplyActiveTraitEffects();
        PublishSynergyUpdated();
    }

    public void ClearAllSynergyEffects()
    {
        ClearRuntimeEffects(true);
    }

    public bool IsTraitUnitIdentityCounted(TraitSO trait, string identityKey)
    {
        if (trait == null || string.IsNullOrWhiteSpace(identityKey))
        {
            return false;
        }

        if (!currentTraitUnits.TryGetValue(trait, out List<UnitLogic> units))
        {
            return false;
        }

        for (int i = 0; i < units.Count; i++)
        {
            UnitLogic unit = units[i];
            if (unit != null && string.Equals(unit.GetSynergyIdentityKey(), identityKey, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void BuildTraitLedger()
    {
        currentTraitCounts.Clear();
        currentTraitUnits.Clear();
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
                AddCurrentTraitUnit(trait, unit);
            }
        }
    }

    private void AddCurrentTraitUnit(TraitSO trait, UnitLogic unit)
    {
        if (trait == null || unit == null)
        {
            return;
        }

        if (!currentTraitUnits.TryGetValue(trait, out List<UnitLogic> units))
        {
            units = new List<UnitLogic>();
            currentTraitUnits[trait] = units;
        }

        if (!units.Contains(unit))
        {
            units.Add(unit);
        }
    }

    private void BuildSynergyModels()
    {
        traitDisplayModels.Clear();
        activeTraitTiers.Clear();

        foreach (KeyValuePair<TraitSO, int> pair in currentTraitCounts)
        {
            TraitSO trait = pair.Key;
            if (trait == null || pair.Value <= 0)
            {
                continue;
            }

            TraitSynergyDisplayModel displayModel = new TraitSynergyDisplayModel(trait, pair.Value);
            traitDisplayModels.Add(displayModel);

            if (displayModel.IsActive)
            {
                activeTraitTiers.Add(new ActiveTraitTierModel(displayModel, GetCurrentTraitUnits(trait)));
            }
        }

        traitDisplayModels.Sort(CompareTraitDisplayModels);
        activeTraitTiers.Sort(CompareActiveTraitTierModels);
    }

    private List<UnitLogic> GetCurrentTraitUnits(TraitSO trait)
    {
        if (trait != null && currentTraitUnits.TryGetValue(trait, out List<UnitLogic> units))
        {
            return units;
        }

        return new List<UnitLogic>();
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
            currentTraitUnits.Clear();
            traitDisplayModels.Clear();
            activeTraitTiers.Clear();
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
                || unit.IsSummoned
                || !unit.IsAlive
                || !unit.gameObject.activeInHierarchy)
            {
                continue;
            }

            result.Add(unit);
        }
    }

    private static int CompareTraitDisplayModels(TraitSynergyDisplayModel left, TraitSynergyDisplayModel right)
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

        if (left.IsActive != right.IsActive)
        {
            return left.IsActive ? -1 : 1;
        }

        int tierCompare = right.ActiveTierIndex.CompareTo(left.ActiveTierIndex);
        if (tierCompare != 0)
        {
            return tierCompare;
        }

        int countCompare = right.UnitCount.CompareTo(left.UnitCount);
        if (countCompare != 0)
        {
            return countCompare;
        }

        return string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
    }

    private static int CompareActiveTraitTierModels(ActiveTraitTierModel left, ActiveTraitTierModel right)
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

        int tierCompare = right.ActiveTierIndex.CompareTo(left.ActiveTierIndex);
        if (tierCompare != 0)
        {
            return tierCompare;
        }

        int countCompare = right.UnitCount.CompareTo(left.UnitCount);
        if (countCompare != 0)
        {
            return countCompare;
        }

        string leftName = left.Trait != null && !string.IsNullOrWhiteSpace(left.Trait.traitName)
            ? left.Trait.traitName
            : string.Empty;
        string rightName = right.Trait != null && !string.IsNullOrWhiteSpace(right.Trait.traitName)
            ? right.Trait.traitName
            : string.Empty;
        return string.Compare(leftName, rightName, StringComparison.OrdinalIgnoreCase);
    }

    private void PublishSynergyUpdated()
    {
        if (OnSynergyUpdated != null)
        {
            OnSynergyUpdated.Invoke();
        }
    }
}
