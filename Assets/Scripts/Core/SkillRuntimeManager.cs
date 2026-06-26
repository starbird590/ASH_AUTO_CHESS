using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public sealed class SkillRuntimeManager : MonoBehaviour
{
    private const int TargetSelf = 101;
    private const int TargetAllDeployed = 102;
    private const int TargetSameTraitAllies = 103;
    private const int TargetAllSummons = 104;
    private const int TargetAllOwned = 105;
    private const int TargetNearbyAllies = 111;
    private const int TargetNearestAlly = 121;
    private const int TargetRandomAlly = 131;
    private const int TargetCurrentAttackTarget = 201;
    private const int TargetAllEnemies = 202;
    private const int TargetAllUnits = 301;

    private const int TriggerBattleStart = 1;
    private const int TriggerPeriodic = 2;
    private const int TriggerDeath = 3;
    private const int TriggerDamaged = 4;
    private const int TriggerEveryNFire = 5;
    private const int TriggerKill = 6;
    private const int TriggerEveryNBayonet = 7;

    private const int EffectModifyAttribute = 101;
    private const int EffectRecoverResource = 102;
    private const int EffectFireDamage = 201;
    private const int EffectBayonetDamage = 202;
    private const int EffectCustomMechanism = 300;

    private const int MaxSkillChainDepth = 16;

    private enum FormulaContextMode
    {
        Source,
        Target
    }

    private static SkillRuntimeManager instance;

    public static SkillRuntimeManager Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindObjectOfType<SkillRuntimeManager>();
                if (instance == null)
                {
                    GameObject managerObject = new GameObject("SkillRuntimeManager");
                    instance = managerObject.AddComponent<SkillRuntimeManager>();
                }
            }

            return instance;
        }
    }

    public static bool HasInstance => instance != null;

    [Header("Skill Table Sources")]
    [SerializeField] private TextAsset skillTableCsv;
    [SerializeField] private string skillTableCsvAssetPath = "Assets/Data/SkillTable.csv";
    [SerializeField] private string skillTableCsvResourcePath = "Data/SkillTable";
    [SerializeField] private string skillAssetResourcesPath = "Skills";
    [SerializeField] private List<SkillDataSO> globalSkillAssets = new List<SkillDataSO>();

    private readonly Dictionary<string, SkillDefinition> skillDefinitionsById = new Dictionary<string, SkillDefinition>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<UnitLogic, UnitSkillRuntime> runtimeByUnit = new Dictionary<UnitLogic, UnitSkillRuntime>();
    private readonly List<UnitLogic> scratchUnits = new List<UnitLogic>();
    private readonly List<UnitLogic> scratchTargets = new List<UnitLogic>();
    private readonly List<UnitSkillRuntime> scratchRuntimeBooks = new List<UnitSkillRuntime>();
    private readonly List<TraitSkillRuntime> traitSkillRuntimes = new List<TraitSkillRuntime>();
    private readonly List<UnitLogic> summonedUnits = new List<UnitLogic>();
    private readonly List<ActiveStatModifier> activeStatModifiers = new List<ActiveStatModifier>();
    private readonly List<ActiveShield> activeShields = new List<ActiveShield>();
    private readonly HashSet<string> warnedMissingSkillIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> warnedUnsupportedEffects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private bool definitionsLoaded;
    private bool battleActive;
    private bool battleStartQueued;
    private int battleStartDelayFrames;
    private int resolvingDepth;

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

    private void OnEnable()
    {
        UnitLogic.UnitDamaged += NotifyUnitDamaged;
        UnitLogic.UnitDied += NotifyUnitDied;
        UnitLogic.UnitKilled += NotifyUnitKilled;
        UnitLogic.FireAttackPerformed += NotifyFireAttack;
        UnitLogic.BayonetAttackPerformed += NotifyBayonetAttack;
    }

    private void OnDisable()
    {
        UnitLogic.UnitDamaged -= NotifyUnitDamaged;
        UnitLogic.UnitDied -= NotifyUnitDied;
        UnitLogic.UnitKilled -= NotifyUnitKilled;
        UnitLogic.FireAttackPerformed -= NotifyFireAttack;
        UnitLogic.BayonetAttackPerformed -= NotifyBayonetAttack;
    }

    private void Update()
    {
        if (battleStartQueued)
        {
            if (battleStartDelayFrames > 0)
            {
                battleStartDelayFrames--;
            }
            else
            {
                HandleBattleStarted();
            }
        }

        if (!battleActive)
        {
            return;
        }

        TickPeriodicSkills(Time.deltaTime);
        TickTimedRuntimeEffects(Time.deltaTime);
    }

    public void QueueBattleStart()
    {
        battleStartQueued = true;
        battleStartDelayFrames = 1;
    }

    public void HandleBattleStarted()
    {
        battleStartQueued = false;
        CleanupRuntimeEffects();
        runtimeByUnit.Clear();
        summonedUnits.Clear();
        warnedMissingSkillIds.Clear();
        warnedUnsupportedEffects.Clear();
        EnsureDefinitionsLoaded();
        battleActive = true;

        CollectActiveBattleUnits(scratchUnits);
        for (int i = 0; i < scratchUnits.Count; i++)
        {
            UnitLogic unit = scratchUnits[i];
            if (unit == null)
            {
                continue;
            }

            unit.SetSummoned(false);
            EnsureRuntimeBook(unit);
        }

        BuildTraitSkillRuntimes();
        InvokeTraitTrigger(TriggerBattleStart, null);

        for (int i = 0; i < scratchUnits.Count; i++)
        {
            InvokeUnitTrigger(scratchUnits[i], TriggerBattleStart, null);
        }
    }

    public void HandleBattleEnded()
    {
        battleStartQueued = false;
        battleActive = false;
        CleanupRuntimeEffects();
        ClearSummonedUnits();
        runtimeByUnit.Clear();
        traitSkillRuntimes.Clear();
    }

    public void RegisterSummonedUnit(UnitLogic unit)
    {
        if (unit == null)
        {
            return;
        }

        AddUniqueUnit(summonedUnits, unit);
        unit.SetSummoned(true);
        if (battleActive)
        {
            EnsureRuntimeBook(unit);
        }
    }

    private void NotifyUnitDamaged(UnitLogic damagedUnit, UnitLogic attacker, int hpDamage, AttackDamageTrack damageTrack)
    {
        if (!battleActive || damagedUnit == null)
        {
            return;
        }

        InvokeUnitTrigger(damagedUnit, TriggerDamaged, attacker);
    }

    private void NotifyUnitDied(UnitLogic deadUnit, UnitLogic attacker)
    {
        if (!battleActive || deadUnit == null)
        {
            return;
        }

        InvokeUnitTrigger(deadUnit, TriggerDeath, attacker);
    }

    private void NotifyUnitKilled(UnitLogic killer, UnitLogic deadUnit)
    {
        if (!battleActive || killer == null || deadUnit == null || killer == deadUnit)
        {
            return;
        }

        InvokeUnitTrigger(killer, TriggerKill, deadUnit);
    }

    private void NotifyFireAttack(UnitLogic source, UnitLogic attackTarget)
    {
        if (!battleActive || source == null)
        {
            return;
        }

        InvokeCountedTrigger(source, TriggerEveryNFire, attackTarget);
    }

    private void NotifyBayonetAttack(UnitLogic source, UnitLogic attackTarget)
    {
        if (!battleActive || source == null)
        {
            return;
        }

        InvokeCountedTrigger(source, TriggerEveryNBayonet, attackTarget);
    }

    private void EnsureDefinitionsLoaded()
    {
        if (definitionsLoaded)
        {
            return;
        }

        definitionsLoaded = true;
        skillDefinitionsById.Clear();

        if (globalSkillAssets != null)
        {
            for (int i = 0; i < globalSkillAssets.Count; i++)
            {
                RegisterSkillAsset(globalSkillAssets[i]);
            }
        }

        if (!string.IsNullOrWhiteSpace(skillAssetResourcesPath))
        {
            SkillDataSO[] resourcesSkills = Resources.LoadAll<SkillDataSO>(skillAssetResourcesPath.Trim());
            for (int i = 0; i < resourcesSkills.Length; i++)
            {
                RegisterSkillAsset(resourcesSkills[i]);
            }
        }

        TextAsset csvAsset = skillTableCsv != null ? skillTableCsv : LoadSkillTableCsv();
        if (csvAsset != null)
        {
            RegisterSkillCsv(StageTableParser.ReadTextAsset(csvAsset));
        }
    }

    private TextAsset LoadSkillTableCsv()
    {
#if UNITY_EDITOR
        if (!string.IsNullOrWhiteSpace(skillTableCsvAssetPath))
        {
            TextAsset editorAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(skillTableCsvAssetPath.Trim().Replace('\\', '/'));
            if (editorAsset != null)
            {
                return editorAsset;
            }
        }
#endif

        if (!string.IsNullOrWhiteSpace(skillTableCsvResourcePath))
        {
            return Resources.Load<TextAsset>(skillTableCsvResourcePath.Trim());
        }

        return null;
    }

    private void RegisterSkillAsset(SkillDataSO skillAsset)
    {
        if (skillAsset == null || string.IsNullOrWhiteSpace(skillAsset.SkillID))
        {
            return;
        }

        RegisterDefinition(new SkillDefinition
        {
            SkillID = skillAsset.SkillID.Trim(),
            TargetType = skillAsset.TargetType,
            TriggerType = skillAsset.TriggerType,
            EffectType = skillAsset.EffectType,
            EffectParams = skillAsset.EffectParams
        });
    }

    private void RegisterSkillCsv(string csvText)
    {
        List<List<string>> rows = StageTableParser.ParseCsvRows(csvText);
        if (rows.Count == 0)
        {
            return;
        }

        int headerRowIndex = FindHeaderRow(rows, "SkillID");
        if (headerRowIndex < 0)
        {
            return;
        }

        Dictionary<string, int> columns = BuildSkillColumnIndex(rows[headerRowIndex]);
        for (int rowIndex = headerRowIndex + 1; rowIndex < rows.Count; rowIndex++)
        {
            List<string> row = rows[rowIndex];
            if (IsRowEmpty(row))
            {
                continue;
            }

            string skillId = GetCell(row, columns, "SkillID");
            if (string.IsNullOrWhiteSpace(skillId))
            {
                continue;
            }

            RegisterDefinition(new SkillDefinition
            {
                SkillID = skillId.Trim(),
                TargetType = ParseInt(GetCell(row, columns, "TargetType"), TargetSelf),
                TriggerType = GetCell(row, columns, "TriggerType"),
                EffectType = ParseInt(GetCell(row, columns, "EffectType"), EffectModifyAttribute),
                EffectParams = GetCell(row, columns, "EffectParams")
            });
        }
    }

    private void RegisterDefinition(SkillDefinition definition)
    {
        if (definition == null || string.IsNullOrWhiteSpace(definition.SkillID))
        {
            return;
        }

        skillDefinitionsById[definition.SkillID.Trim()] = definition;
    }

    private UnitSkillRuntime EnsureRuntimeBook(UnitLogic unit)
    {
        if (unit == null)
        {
            return null;
        }

        if (runtimeByUnit.TryGetValue(unit, out UnitSkillRuntime runtime))
        {
            return runtime;
        }

        runtime = new UnitSkillRuntime(unit, GetSkillDefinitionsForUnit(unit));
        runtimeByUnit[unit] = runtime;
        return runtime;
    }

    private List<SkillDefinition> GetSkillDefinitionsForUnit(UnitLogic unit)
    {
        List<SkillDefinition> result = new List<SkillDefinition>();
        if (unit == null || unit.unitDataConfig == null)
        {
            return result;
        }

        UnitLogicDataSO unitData = unit.unitDataConfig;
        List<string> ids = SplitTopLevel(unitData.skillIds);
        for (int i = 0; i < ids.Count; i++)
        {
            string skillId = ids[i].Trim();
            if (string.IsNullOrWhiteSpace(skillId))
            {
                continue;
            }

            if (skillDefinitionsById.TryGetValue(skillId, out SkillDefinition definition))
            {
                AddUniqueDefinition(result, definition);
            }
            else
            {
                WarnMissingSkillIdOnce(skillId, unit);
            }
        }

        return result;
    }

    private List<SkillDefinition> GetSkillDefinitionsForIds(IReadOnlyList<string> skillIds, string warningContext)
    {
        List<SkillDefinition> result = new List<SkillDefinition>();
        if (skillIds == null)
        {
            return result;
        }

        for (int i = 0; i < skillIds.Count; i++)
        {
            string skillId = skillIds[i];
            if (string.IsNullOrWhiteSpace(skillId))
            {
                continue;
            }

            skillId = skillId.Trim();
            if (skillDefinitionsById.TryGetValue(skillId, out SkillDefinition definition))
            {
                AddUniqueDefinition(result, definition);
            }
            else
            {
                WarnMissingSkillIdOnce(skillId, warningContext);
            }
        }

        return result;
    }

    private void AddUniqueDefinition(List<SkillDefinition> definitions, SkillDefinition definition)
    {
        if (definitions == null || definition == null)
        {
            return;
        }

        for (int i = 0; i < definitions.Count; i++)
        {
            if (string.Equals(definitions[i].SkillID, definition.SkillID, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        definitions.Add(definition);
    }

    private void InvokeUnitTrigger(UnitLogic source, int triggerType, UnitLogic eventTarget)
    {
        UnitSkillRuntime runtime = EnsureRuntimeBook(source);
        if (runtime == null || runtime.SkillDefinitions.Count == 0)
        {
            return;
        }

        for (int i = 0; i < runtime.SkillDefinitions.Count; i++)
        {
            SkillDefinition skill = runtime.SkillDefinitions[i];
            if (ReadTriggerKind(skill) == triggerType)
            {
                InvokeSkill(skill, source, eventTarget);
            }
        }
    }

    private void InvokeCountedTrigger(UnitLogic source, int triggerType, UnitLogic eventTarget)
    {
        UnitSkillRuntime runtime = EnsureRuntimeBook(source);
        if (runtime == null || runtime.SkillDefinitions.Count == 0)
        {
            return;
        }

        for (int i = 0; i < runtime.SkillDefinitions.Count; i++)
        {
            SkillDefinition skill = runtime.SkillDefinitions[i];
            if (ReadTriggerKind(skill) != triggerType)
            {
                continue;
            }

            int interval = Mathf.Max(1, Mathf.RoundToInt(ReadTriggerNumber(skill, 1f)));
            string key = skill.SkillID + "_" + triggerType;
            int count = runtime.GetAndIncrementCount(key);
            if (count % interval == 0)
            {
                InvokeSkill(skill, source, eventTarget);
            }
        }
    }

    private void TickPeriodicSkills(float deltaTime)
    {
        CleanupNullRuntimeBooks();

        scratchRuntimeBooks.Clear();
        foreach (KeyValuePair<UnitLogic, UnitSkillRuntime> pair in runtimeByUnit)
        {
            scratchRuntimeBooks.Add(pair.Value);
        }

        for (int runtimeIndex = 0; runtimeIndex < scratchRuntimeBooks.Count; runtimeIndex++)
        {
            UnitSkillRuntime runtime = scratchRuntimeBooks[runtimeIndex];
            UnitLogic source = runtime != null ? runtime.Owner : null;
            if (!IsUsableSkillSource(source) || runtime == null || runtime.SkillDefinitions.Count == 0)
            {
                continue;
            }

            for (int i = 0; i < runtime.SkillDefinitions.Count; i++)
            {
                SkillDefinition skill = runtime.SkillDefinitions[i];
                if (ReadTriggerKind(skill) != TriggerPeriodic)
                {
                    continue;
                }

                float interval = Mathf.Max(0.1f, ReadTriggerNumber(skill, 1f));
                string key = skill.SkillID + "_" + TriggerPeriodic;
                float elapsed = runtime.AddPeriodicTime(key, deltaTime);
                if (elapsed < interval)
                {
                    continue;
                }

                runtime.SetPeriodicTime(key, elapsed - interval);
                InvokeSkill(skill, source, null);
            }
        }

        TickPeriodicTraitSkills(deltaTime);
    }

    private void BuildTraitSkillRuntimes()
    {
        traitSkillRuntimes.Clear();
        if (!SynergyManager.HasInstance)
        {
            return;
        }

        IReadOnlyList<ActiveTraitTierModel> activeTiers = SynergyManager.Instance.ActiveTraitTiers;
        for (int i = 0; i < activeTiers.Count; i++)
        {
            ActiveTraitTierModel activeTier = activeTiers[i];
            if (activeTier == null || activeTier.SkillIds == null || activeTier.SkillIds.Count == 0)
            {
                continue;
            }

            List<SkillDefinition> definitions = GetSkillDefinitionsForIds(activeTier.SkillIds, GetTraitTierWarningContext(activeTier));
            if (definitions.Count > 0)
            {
                traitSkillRuntimes.Add(new TraitSkillRuntime(activeTier, definitions));
            }
        }
    }

    private void InvokeTraitTrigger(int triggerType, UnitLogic eventTarget)
    {
        for (int runtimeIndex = 0; runtimeIndex < traitSkillRuntimes.Count; runtimeIndex++)
        {
            TraitSkillRuntime runtime = traitSkillRuntimes[runtimeIndex];
            if (runtime == null || runtime.SkillDefinitions.Count == 0)
            {
                continue;
            }

            for (int i = 0; i < runtime.SkillDefinitions.Count; i++)
            {
                SkillDefinition skill = runtime.SkillDefinitions[i];
                if (ReadTriggerKind(skill) == triggerType)
                {
                    InvokeTraitSkill(runtime, skill, eventTarget);
                }
            }
        }
    }

    private void TickPeriodicTraitSkills(float deltaTime)
    {
        for (int runtimeIndex = 0; runtimeIndex < traitSkillRuntimes.Count; runtimeIndex++)
        {
            TraitSkillRuntime runtime = traitSkillRuntimes[runtimeIndex];
            if (runtime == null || runtime.SkillDefinitions.Count == 0)
            {
                continue;
            }

            for (int i = 0; i < runtime.SkillDefinitions.Count; i++)
            {
                SkillDefinition skill = runtime.SkillDefinitions[i];
                if (ReadTriggerKind(skill) != TriggerPeriodic)
                {
                    continue;
                }

                float interval = Mathf.Max(0.1f, ReadTriggerNumber(skill, 1f));
                string key = skill.SkillID + "_" + TriggerPeriodic;
                float elapsed = runtime.AddPeriodicTime(key, deltaTime);
                if (elapsed < interval)
                {
                    continue;
                }

                runtime.SetPeriodicTime(key, elapsed - interval);
                InvokeTraitSkill(runtime, skill, null);
            }
        }
    }

    private void InvokeTraitSkill(TraitSkillRuntime runtime, SkillDefinition skill, UnitLogic eventTarget)
    {
        if (runtime == null || skill == null || resolvingDepth >= MaxSkillChainDepth)
        {
            return;
        }

        if (ShouldInvokeTraitSkillPerQualifyingUnit(skill.TargetType))
        {
            for (int i = 0; i < runtime.QualifyingUnits.Count; i++)
            {
                UnitLogic source = runtime.QualifyingUnits[i];
                if (IsUsableTraitQualifier(source))
                {
                    InvokeSkill(skill, source, eventTarget);
                }
            }

            return;
        }

        UnitLogic contextSource = FindTraitContextSource(runtime);
        if (contextSource == null)
        {
            return;
        }

        resolvingDepth++;
        try
        {
            ResolveTraitTargets(skill.TargetType, runtime, contextSource, eventTarget, scratchTargets);
            if (scratchTargets.Count == 0 && ShouldFallbackToSourceForCustomMechanism(skill))
            {
                scratchTargets.Add(contextSource);
            }

            List<UnitLogic> targets = new List<UnitLogic>(scratchTargets);
            ApplyEffect(skill, contextSource, targets, eventTarget, FormulaContextMode.Target);
        }
        finally
        {
            resolvingDepth--;
        }
    }

    private bool ShouldInvokeTraitSkillPerQualifyingUnit(int targetType)
    {
        switch (targetType)
        {
            case TargetSelf:
            case TargetNearbyAllies:
            case TargetNearestAlly:
            case TargetRandomAlly:
            case TargetCurrentAttackTarget:
                return true;
            default:
                return false;
        }
    }

    private void ResolveTraitTargets(int targetType, TraitSkillRuntime runtime, UnitLogic contextSource, UnitLogic eventTarget, List<UnitLogic> result)
    {
        result.Clear();
        if (runtime == null || contextSource == null)
        {
            return;
        }

        switch (targetType)
        {
            case TargetAllDeployed:
                CollectFriendlyUnits(contextSource, false, result);
                break;
            case TargetSameTraitAllies:
                AddQualifyingTraitUnits(runtime, result);
                break;
            case TargetAllSummons:
                CollectSummonedUnits(contextSource.faction, result);
                break;
            case TargetAllOwned:
                CollectFriendlyUnits(contextSource, true, result);
                break;
            case TargetAllEnemies:
                CollectEnemyUnits(contextSource, result);
                break;
            case TargetAllUnits:
                CollectAllUnits(result);
                break;
            default:
                ResolveTargets(targetType, contextSource, eventTarget, result);
                break;
        }
    }

    private void AddQualifyingTraitUnits(TraitSkillRuntime runtime, List<UnitLogic> result)
    {
        if (runtime == null || result == null)
        {
            return;
        }

        for (int i = 0; i < runtime.QualifyingUnits.Count; i++)
        {
            UnitLogic unit = runtime.QualifyingUnits[i];
            if (IsUsableTraitQualifier(unit))
            {
                AddUniqueUnit(result, unit);
            }
        }
    }

    private UnitLogic FindTraitContextSource(TraitSkillRuntime runtime)
    {
        if (runtime == null)
        {
            return null;
        }

        for (int i = 0; i < runtime.QualifyingUnits.Count; i++)
        {
            UnitLogic unit = runtime.QualifyingUnits[i];
            if (IsUsableTraitQualifier(unit))
            {
                return unit;
            }
        }

        return null;
    }

    private bool IsUsableTraitQualifier(UnitLogic unit)
    {
        return IsUsableSkillSource(unit) && IsBattlefieldUnit(unit) && !unit.IsSummoned && unit.faction == UnitFaction.Player;
    }

    private string GetTraitTierWarningContext(ActiveTraitTierModel activeTier)
    {
        if (activeTier == null || activeTier.Trait == null)
        {
            return "trait tier";
        }

        string traitName = !string.IsNullOrWhiteSpace(activeTier.Trait.traitName)
            ? activeTier.Trait.traitName
            : activeTier.Trait.name;
        return traitName + " tier " + activeTier.UnitCount;
    }

    private void InvokeSkill(SkillDefinition skill, UnitLogic source, UnitLogic eventTarget)
    {
        if (skill == null || source == null || resolvingDepth >= MaxSkillChainDepth)
        {
            return;
        }

        resolvingDepth++;
        try
        {
            ResolveTargets(skill.TargetType, source, eventTarget, scratchTargets);
            if (scratchTargets.Count == 0 && ShouldFallbackToSourceForCustomMechanism(skill))
            {
                scratchTargets.Add(source);
            }

            List<UnitLogic> targets = new List<UnitLogic>(scratchTargets);
            ApplyEffect(skill, source, targets, eventTarget, FormulaContextMode.Source);
        }
        finally
        {
            resolvingDepth--;
        }
    }

    private void ApplyEffect(SkillDefinition skill, UnitLogic source, List<UnitLogic> targets, UnitLogic eventTarget, FormulaContextMode formulaContextMode)
    {
        switch (skill.EffectType)
        {
            case EffectModifyAttribute:
                ApplyAttributeModifier(skill, source, targets, formulaContextMode);
                break;
            case EffectRecoverResource:
                ApplyResourceRecovery(skill, source, targets, formulaContextMode);
                break;
            case EffectFireDamage:
                ApplyDamageEffect(skill, source, targets, AttackDamageTrack.Fire, formulaContextMode);
                break;
            case EffectBayonetDamage:
                ApplyDamageEffect(skill, source, targets, AttackDamageTrack.Bayonet, formulaContextMode);
                break;
            case EffectCustomMechanism:
                ApplyCustomMechanismEffect(skill, source, targets, formulaContextMode);
                break;
            default:
                WarnUnsupportedEffectOnce(skill, "unsupported EffectType=" + skill.EffectType + ".");
                break;
        }
    }

    private bool ShouldFallbackToSourceForCustomMechanism(SkillDefinition skill)
    {
        if (skill == null || skill.EffectType != EffectCustomMechanism)
        {
            return false;
        }

        List<string> parts = SplitTopLevel(skill.EffectParams);
        if (parts.Count == 0)
        {
            return false;
        }

        switch (NormalizeKey(parts[0]))
        {
            case "shield":
            case "temporaryshield":
            case "301":
            case "summon":
            case "summonunit":
            case "302":
            case "revive":
            case "reviveunit":
            case "303":
                return true;
            default:
                return false;
        }
    }

    private void ApplyCustomMechanismEffect(SkillDefinition skill, UnitLogic source, List<UnitLogic> targets, FormulaContextMode formulaContextMode)
    {
        List<string> parts = SplitTopLevel(skill.EffectParams);
        if (parts.Count == 0)
        {
            Debug.LogWarning("[SkillRuntime] EffectType 300 needs {MechanismName,...params}: " + skill.SkillID);
            return;
        }

        SkillDefinition parameterView = new SkillDefinition
        {
            SkillID = skill.SkillID,
            TargetType = skill.TargetType,
            TriggerType = skill.TriggerType,
            EffectType = skill.EffectType,
            EffectParams = JoinParams(parts, 1)
        };

        switch (NormalizeKey(parts[0]))
        {
            case "shield":
            case "temporaryshield":
            case "301":
                ApplyTemporaryShield(parameterView, source, targets, formulaContextMode);
                break;
            case "damage":
            case "firedamage":
            case "fire":
            case "201":
                ApplyDamageEffect(parameterView, source, targets, AttackDamageTrack.Fire, formulaContextMode);
                break;
            case "bayonetdamage":
            case "bayonet":
            case "melee":
            case "202":
                ApplyDamageEffect(parameterView, source, targets, AttackDamageTrack.Bayonet, formulaContextMode);
                break;
            case "summon":
            case "summonunit":
            case "302":
                ApplySummon(parameterView, source, targets);
                break;
            case "revive":
            case "reviveunit":
            case "303":
                ApplyRevive(parameterView, source, targets, formulaContextMode);
                break;
            default:
                WarnUnsupportedEffectOnce(skill, "unknown EffectType=300 mechanism '" + parts[0] + "'.");
                break;
        }
    }

    private void ApplyAttributeModifier(SkillDefinition skill, UnitLogic source, List<UnitLogic> targets, FormulaContextMode formulaContextMode)
    {
        List<string> parts = SplitTopLevel(skill.EffectParams);
        if (parts.Count < 2)
        {
            Debug.LogWarning("[SkillRuntime] EffectType 101 needs {AttributeName,Value,Duration}: " + skill.SkillID);
            return;
        }

        string statName = parts[0];
        string amountFormula = parts[1];
        string durationFormula = parts.Count >= 3 ? parts[2] : null;
        float sourceAmount = formulaContextMode == FormulaContextMode.Source ? EvaluateFormula(amountFormula, source, 0f) : 0f;
        float sourceDuration = formulaContextMode == FormulaContextMode.Source && durationFormula != null
            ? EvaluateFormula(durationFormula, source, -1f)
            : -1f;

        for (int i = 0; i < targets.Count; i++)
        {
            UnitLogic target = targets[i];
            if (target == null)
            {
                continue;
            }

            UnitLogic formulaUnit = GetFormulaContextUnit(formulaContextMode, source, target);
            float amount = formulaContextMode == FormulaContextMode.Target
                ? EvaluateFormula(amountFormula, formulaUnit, 0f)
                : sourceAmount;
            float duration = formulaContextMode == FormulaContextMode.Target && durationFormula != null
                ? EvaluateFormula(durationFormula, formulaUnit, -1f)
                : sourceDuration;

            if (!TryApplyStatDelta(target, statName, amount))
            {
                Debug.LogWarning("[SkillRuntime] Unknown stat '" + statName + "' in skill " + skill.SkillID + ".");
                continue;
            }

            activeStatModifiers.Add(new ActiveStatModifier(target, statName, amount, duration));
        }
    }

    private void ApplyResourceRecovery(SkillDefinition skill, UnitLogic source, List<UnitLogic> targets, FormulaContextMode formulaContextMode)
    {
        List<string> parts = SplitTopLevel(skill.EffectParams);
        if (parts.Count < 2)
        {
            Debug.LogWarning("[SkillRuntime] EffectType 102 needs {BaseHp/Ammo,Value}: " + skill.SkillID);
            return;
        }

        string resourceName = NormalizeKey(parts[0]);
        string amountFormula = parts[1];
        int sourceAmount = formulaContextMode == FormulaContextMode.Source
            ? Mathf.RoundToInt(EvaluateFormula(amountFormula, source, 0f))
            : 0;

        for (int i = 0; i < targets.Count; i++)
        {
            UnitLogic target = targets[i];
            if (target == null)
            {
                continue;
            }

            UnitLogic formulaUnit = GetFormulaContextUnit(formulaContextMode, source, target);
            int amount = formulaContextMode == FormulaContextMode.Target
                ? Mathf.RoundToInt(EvaluateFormula(amountFormula, formulaUnit, 0f))
                : sourceAmount;
            if (amount <= 0)
            {
                continue;
            }

            if (resourceName == "basehp" || resourceName == "hp" || resourceName == "health")
            {
                target.currentHp = Mathf.Min(target.maxHp, target.currentHp + amount);
            }
            else if (resourceName == "ammo" || resourceName == "currentammo")
            {
                target.currentAmmo = Mathf.Min(target.maxAmmo, target.currentAmmo + amount);
            }
        }
    }

    private void ApplyDamageEffect(SkillDefinition skill, UnitLogic source, List<UnitLogic> targets, AttackDamageTrack damageTrack, FormulaContextMode formulaContextMode)
    {
        string amountFormula = SingleFormulaParam(skill.EffectParams);
        float sourceAmount = formulaContextMode == FormulaContextMode.Source ? EvaluateFormula(amountFormula, source, 0f) : 0f;

        DamageType damageType = damageTrack == AttackDamageTrack.Bayonet ? DamageType.Bayonet : DamageType.Fire;
        for (int i = 0; i < targets.Count; i++)
        {
            UnitLogic target = targets[i];
            if (target == null || !target.IsAlive)
            {
                continue;
            }

            UnitLogic formulaUnit = GetFormulaContextUnit(formulaContextMode, source, target);
            float amount = formulaContextMode == FormulaContextMode.Target
                ? EvaluateFormula(amountFormula, formulaUnit, 0f)
                : sourceAmount;
            if (amount > 0f)
            {
                target.ReceiveDamage(source, amount, damageType);
            }
        }
    }

    private void ApplyTemporaryShield(SkillDefinition skill, UnitLogic source, List<UnitLogic> targets, FormulaContextMode formulaContextMode)
    {
        List<string> parts = SplitTopLevel(skill.EffectParams);
        if (parts.Count == 0)
        {
            return;
        }

        string shieldFormula = parts[0];
        string durationFormula = parts.Count >= 2 ? parts[1] : null;
        int sourceShieldAmount = formulaContextMode == FormulaContextMode.Source
            ? Mathf.RoundToInt(EvaluateFormula(shieldFormula, source, 0f))
            : 0;
        float sourceDuration = formulaContextMode == FormulaContextMode.Source && durationFormula != null
            ? EvaluateFormula(durationFormula, source, -1f)
            : -1f;

        for (int i = 0; i < targets.Count; i++)
        {
            UnitLogic target = targets[i];
            if (target == null)
            {
                continue;
            }

            UnitLogic formulaUnit = GetFormulaContextUnit(formulaContextMode, source, target);
            int shieldAmount = formulaContextMode == FormulaContextMode.Target
                ? Mathf.RoundToInt(EvaluateFormula(shieldFormula, formulaUnit, 0f))
                : sourceShieldAmount;
            float duration = formulaContextMode == FormulaContextMode.Target && durationFormula != null
                ? EvaluateFormula(durationFormula, formulaUnit, -1f)
                : sourceDuration;
            if (shieldAmount <= 0)
            {
                continue;
            }

            target.AddTemporaryShield(shieldAmount);
            activeShields.Add(new ActiveShield(target, shieldAmount, duration));
        }
    }

    private void ApplySummon(SkillDefinition skill, UnitLogic source, List<UnitLogic> anchors)
    {
        List<string> parts = SplitTopLevel(skill.EffectParams);
        if (parts.Count == 0)
        {
            Debug.LogWarning("[SkillRuntime] EffectType 302 needs {UnitID,Count}: " + skill.SkillID);
            return;
        }

        string chessId = parts[0].Trim();
        int count = parts.Count >= 2 ? Mathf.Max(1, Mathf.RoundToInt(EvaluateFormula(parts[1], source, 1f))) : 1;
        if (string.IsNullOrWhiteSpace(chessId))
        {
            return;
        }

        for (int anchorIndex = 0; anchorIndex < anchors.Count; anchorIndex++)
        {
            UnitLogic anchor = anchors[anchorIndex] != null ? anchors[anchorIndex] : source;
            for (int i = 0; i < count; i++)
            {
                SpawnSummonedUnit(source, anchor, chessId);
            }
        }
    }

    private void ApplyRevive(SkillDefinition skill, UnitLogic source, List<UnitLogic> targets, FormulaContextMode formulaContextMode)
    {
        List<string> parts = SplitTopLevel(skill.EffectParams);
        if (parts.Count == 0)
        {
            return;
        }

        string hpFormula = parts[0];
        string delayFormula = parts.Count >= 2 ? parts[1] : null;
        int sourceHp = Mathf.Max(1, Mathf.RoundToInt(EvaluateFormula(hpFormula, source, source.maxHp)));
        float sourceDelay = delayFormula != null ? Mathf.Max(0f, EvaluateFormula(delayFormula, source, 0f)) : 0f;
        for (int i = 0; i < targets.Count; i++)
        {
            UnitLogic target = targets[i];
            if (target == null)
            {
                continue;
            }

            UnitLogic formulaUnit = GetFormulaContextUnit(formulaContextMode, source, target);
            int hp = formulaContextMode == FormulaContextMode.Target
                ? Mathf.Max(1, Mathf.RoundToInt(EvaluateFormula(hpFormula, formulaUnit, formulaUnit.maxHp)))
                : sourceHp;
            float delay = formulaContextMode == FormulaContextMode.Target && delayFormula != null
                ? Mathf.Max(0f, EvaluateFormula(delayFormula, formulaUnit, 0f))
                : sourceDelay;

            if (delay <= 0f)
            {
                target.ReviveWithHp(hp);
            }
            else
            {
                StartCoroutine(ReviveAfterDelay(target, hp, delay));
            }
        }
    }

    private IEnumerator ReviveAfterDelay(UnitLogic target, int hp, float delay)
    {
        yield return new WaitForSeconds(delay);
        if (target != null && battleActive)
        {
            target.ReviveWithHp(hp);
        }
    }

    private void SpawnSummonedUnit(UnitLogic source, UnitLogic anchor, string chessId)
    {
        if (source == null || UnitDataManager.Instance == null || GridManager.Instance == null)
        {
            return;
        }

        if (!TryFindSummonCell(anchor != null ? anchor : source, out Vector2Int cell))
        {
            Debug.LogWarning("[SkillRuntime] No empty battlefield cell for summoned unit: " + chessId);
            return;
        }

        UnitLogic summoned = UnitDataManager.Instance.SpawnUnitOnBoard(chessId, Vector3.zero, source.faction);
        if (summoned == null)
        {
            return;
        }

        GridManager.Instance.PlaceUnitOnBattlefield(summoned, cell);
        summoned.SetFaction(source.faction);
        summoned.SetVeteran(false);
        summoned.SetSummoned(true);
        summoned.SetPositionLocked(true);
        summoned.ResetForBattle();
        summoned.SetCombatEnabled(GameFlowManager.Instance == null || GameFlowManager.Instance.CurrentState == GameState.Battle);
        RegisterSummonedUnit(summoned);
    }

    private void ResolveTargets(int targetType, UnitLogic source, UnitLogic eventTarget, List<UnitLogic> result)
    {
        result.Clear();
        if (source == null)
        {
            return;
        }

        switch (targetType)
        {
            case TargetSelf:
                AddUniqueUnit(result, source);
                break;
            case TargetAllDeployed:
                CollectFriendlyUnits(source, false, result);
                break;
            case TargetSameTraitAllies:
                CollectFriendlyUnits(source, true, scratchUnits);
                for (int i = 0; i < scratchUnits.Count; i++)
                {
                    if (scratchUnits[i] != source && SharesAnyTrait(source, scratchUnits[i]))
                    {
                        AddUniqueUnit(result, scratchUnits[i]);
                    }
                }
                break;
            case TargetAllSummons:
                CollectSummonedUnits(source.faction, result);
                break;
            case TargetAllOwned:
                CollectFriendlyUnits(source, true, result);
                break;
            case TargetNearbyAllies:
                CollectFriendlyUnits(source, true, scratchUnits);
                for (int i = 0; i < scratchUnits.Count; i++)
                {
                    UnitLogic unit = scratchUnits[i];
                    if (unit != null && unit != source && Vector2.Distance(source.transform.position, unit.transform.position) <= 1.5f)
                    {
                        AddUniqueUnit(result, unit);
                    }
                }
                break;
            case TargetNearestAlly:
                UnitLogic nearestAlly = FindNearestFriendly(source);
                AddUniqueUnit(result, nearestAlly);
                break;
            case TargetRandomAlly:
                UnitLogic randomAlly = FindRandomFriendly(source);
                AddUniqueUnit(result, randomAlly);
                break;
            case TargetCurrentAttackTarget:
                if (eventTarget != null && eventTarget.IsAlive && IsHostile(source, eventTarget))
                {
                    AddUniqueUnit(result, eventTarget);
                }
                break;
            case TargetAllEnemies:
                CollectEnemyUnits(source, result);
                break;
            case TargetAllUnits:
                CollectAllUnits(result);
                break;
            default:
                AddUniqueUnit(result, source);
                break;
        }
    }

    private void CollectActiveBattleUnits(List<UnitLogic> result)
    {
        result.Clear();
        IReadOnlyList<UnitLogic> activeUnits = UnitLogic.ActiveUnits;
        for (int i = 0; i < activeUnits.Count; i++)
        {
            UnitLogic unit = activeUnits[i];
            if (IsUsableSkillSource(unit) && (IsBattlefieldUnit(unit) || unit.IsSummoned))
            {
                AddUniqueUnit(result, unit);
            }
        }
    }

    private void CollectFriendlyUnits(UnitLogic source, bool includeSummons, List<UnitLogic> result)
    {
        result.Clear();
        if (source == null)
        {
            return;
        }

        IReadOnlyList<UnitLogic> activeUnits = UnitLogic.ActiveUnits;
        for (int i = 0; i < activeUnits.Count; i++)
        {
            UnitLogic unit = activeUnits[i];
            if (unit == null || !unit.IsAlive || unit.faction != source.faction)
            {
                continue;
            }

            if (unit.IsSummoned)
            {
                if (includeSummons)
                {
                    AddUniqueUnit(result, unit);
                }

                continue;
            }

            if (IsBattlefieldUnit(unit))
            {
                AddUniqueUnit(result, unit);
            }
        }
    }

    private void CollectSummonedUnits(UnitFaction faction, List<UnitLogic> result)
    {
        result.Clear();
        for (int i = summonedUnits.Count - 1; i >= 0; i--)
        {
            UnitLogic unit = summonedUnits[i];
            if (unit == null)
            {
                summonedUnits.RemoveAt(i);
                continue;
            }

            if (unit.IsAlive && unit.faction == faction)
            {
                AddUniqueUnit(result, unit);
            }
        }
    }

    private void CollectEnemyUnits(UnitLogic source, List<UnitLogic> result)
    {
        result.Clear();
        IReadOnlyList<UnitLogic> activeUnits = UnitLogic.ActiveUnits;
        for (int i = 0; i < activeUnits.Count; i++)
        {
            UnitLogic unit = activeUnits[i];
            if (unit != null && unit.IsAlive && IsHostile(source, unit) && (IsBattlefieldUnit(unit) || unit.IsSummoned))
            {
                AddUniqueUnit(result, unit);
            }
        }
    }

    private void CollectAllUnits(List<UnitLogic> result)
    {
        result.Clear();
        IReadOnlyList<UnitLogic> activeUnits = UnitLogic.ActiveUnits;
        for (int i = 0; i < activeUnits.Count; i++)
        {
            UnitLogic unit = activeUnits[i];
            if (unit != null && unit.IsAlive && (IsBattlefieldUnit(unit) || unit.IsSummoned))
            {
                AddUniqueUnit(result, unit);
            }
        }
    }

    private UnitLogic FindNearestFriendly(UnitLogic source)
    {
        CollectFriendlyUnits(source, true, scratchUnits);
        UnitLogic nearest = null;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < scratchUnits.Count; i++)
        {
            UnitLogic unit = scratchUnits[i];
            if (unit == null || unit == source)
            {
                continue;
            }

            float distance = Vector2.Distance(source.transform.position, unit.transform.position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = unit;
            }
        }

        return nearest;
    }

    private UnitLogic FindRandomFriendly(UnitLogic source)
    {
        CollectFriendlyUnits(source, true, scratchUnits);
        for (int i = scratchUnits.Count - 1; i >= 0; i--)
        {
            if (scratchUnits[i] == null || scratchUnits[i] == source)
            {
                scratchUnits.RemoveAt(i);
            }
        }

        return scratchUnits.Count > 0 ? scratchUnits[UnityEngine.Random.Range(0, scratchUnits.Count)] : null;
    }

    private bool SharesAnyTrait(UnitLogic source, UnitLogic candidate)
    {
        if (source == null || candidate == null)
        {
            return false;
        }

        IReadOnlyList<TraitSO> sourceTraits = source.RuntimeTraits;
        IReadOnlyList<TraitSO> candidateTraits = candidate.RuntimeTraits;
        for (int i = 0; i < sourceTraits.Count; i++)
        {
            TraitSO sourceTrait = sourceTraits[i];
            if (sourceTrait == null)
            {
                continue;
            }

            for (int j = 0; j < candidateTraits.Count; j++)
            {
                if (candidateTraits[j] == sourceTrait)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool IsHostile(UnitLogic source, UnitLogic candidate)
    {
        if (source == null || candidate == null || source.faction == UnitFaction.Neutral || candidate.faction == UnitFaction.Neutral)
        {
            return false;
        }

        return source.faction != candidate.faction;
    }

    private bool IsUsableSkillSource(UnitLogic unit)
    {
        return unit != null && unit.IsAlive && unit.gameObject.activeInHierarchy;
    }

    private bool IsBattlefieldUnit(UnitLogic unit)
    {
        if (unit == null)
        {
            return false;
        }

        GridManager gridManager = GridManager.Instance;
        if (gridManager == null || gridManager.battlefieldContainer == null)
        {
            return unit.HasGridPosition;
        }

        return unit.transform.parent == gridManager.battlefieldContainer;
    }

    private bool TryFindSummonCell(UnitLogic anchor, out Vector2Int cell)
    {
        cell = Vector2Int.zero;
        GridManager gridManager = GridManager.Instance;
        if (gridManager == null)
        {
            return false;
        }

        Vector2Int origin = anchor != null && anchor.HasGridPosition
            ? anchor.GridPosition
            : BoardLayout.StrategicLineCenter;

        for (int radius = 0; radius <= 4; radius++)
        {
            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    if (Mathf.Abs(x) != radius && Mathf.Abs(y) != radius)
                    {
                        continue;
                    }

                    Vector2Int candidate = new Vector2Int(origin.x + x, origin.y + y);
                    if (gridManager.IsInsideBattlefield(candidate) && !IsAnyBattlefieldCellOccupied(candidate))
                    {
                        cell = candidate;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private bool IsAnyBattlefieldCellOccupied(Vector2Int cell)
    {
        IReadOnlyList<UnitLogic> activeUnits = UnitLogic.ActiveUnits;
        for (int i = 0; i < activeUnits.Count; i++)
        {
            UnitLogic unit = activeUnits[i];
            if (unit == null || !unit.IsAlive || !IsBattlefieldUnit(unit) || !unit.HasGridPosition)
            {
                continue;
            }

            if (unit.GridPosition == cell)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryApplyStatDelta(UnitLogic unit, string statName, float delta)
    {
        if (!TryGetStat(unit, statName, out float current))
        {
            return false;
        }

        return TrySetStat(unit, statName, current + delta);
    }

    private bool TryGetStat(UnitLogic unit, string statName, out float value)
    {
        value = 0f;
        if (unit == null)
        {
            return false;
        }

        switch (NormalizeKey(statName))
        {
            case "basehp":
            case "maxhp":
            case "hp":
                value = unit.maxHp;
                return true;
            case "basearmor":
            case "armor":
                value = unit.armor;
                return true;
            case "bayonetarmor":
                value = unit.bayonetArmor;
                return true;
            case "critrate":
                value = unit.critRate;
                return true;
            case "critdamage":
                value = unit.critDamage;
                return true;
            case "firedamage":
            case "damage":
                value = unit.fireDamage;
                return true;
            case "firerate":
                value = unit.fireRate;
                return true;
            case "firespeed":
            case "attackspeed":
                value = unit.fireSpeed;
                return true;
            case "firerange":
            case "attackrange":
                value = unit.fireRange;
                return true;
            case "ammo":
            case "maxammo":
                value = unit.maxAmmo;
                return true;
            case "currentammo":
                value = unit.currentAmmo;
                return true;
            case "ammospeed":
                value = unit.ammoSpeed;
                return true;
            case "firepenpct":
                value = unit.firePenPct;
                return true;
            case "firepenflat":
                value = unit.firePenFlat;
                return true;
            case "damageaoe":
                value = unit.damageAoe;
                return true;
            case "bayonetdamage":
                value = unit.bayonetDamage;
                return true;
            case "bayonetspeed":
                value = unit.bayonetSpeed;
                return true;
            case "bayonetrange":
                value = unit.bayonetRange;
                return true;
            case "bayonetpenpct":
                value = unit.bayonetPenPct;
                return true;
            case "bayonetpenflat":
                value = unit.bayonetPenFlat;
                return true;
            case "movespeed":
                value = unit.moveSpeed;
                return true;
            case "capturespeed":
                value = unit.captureSpeed;
                return true;
            case "threatvalue":
                value = unit.threatValue;
                return true;
            default:
                return false;
        }
    }

    private bool TrySetStat(UnitLogic unit, string statName, float value)
    {
        if (unit == null)
        {
            return false;
        }

        switch (NormalizeKey(statName))
        {
            case "basehp":
            case "maxhp":
            case "hp":
                unit.maxHp = Mathf.Max(1, Mathf.RoundToInt(value));
                if (unit.currentHp > unit.maxHp)
                {
                    unit.currentHp = unit.maxHp;
                }
                return true;
            case "basearmor":
            case "armor":
                unit.armor = Mathf.RoundToInt(value);
                return true;
            case "bayonetarmor":
                unit.bayonetArmor = Mathf.RoundToInt(value);
                return true;
            case "critrate":
                unit.critRate = value;
                return true;
            case "critdamage":
                unit.critDamage = value;
                return true;
            case "firedamage":
            case "damage":
                unit.fireDamage = Mathf.RoundToInt(value);
                return true;
            case "firerate":
                unit.fireRate = value;
                return true;
            case "firespeed":
            case "attackspeed":
                unit.fireSpeed = value;
                return true;
            case "firerange":
            case "attackrange":
                unit.fireRange = Mathf.RoundToInt(value);
                return true;
            case "ammo":
            case "maxammo":
                unit.maxAmmo = Mathf.RoundToInt(value);
                if (unit.currentAmmo > unit.maxAmmo)
                {
                    unit.currentAmmo = unit.maxAmmo;
                }
                return true;
            case "currentammo":
                unit.currentAmmo = Mathf.RoundToInt(value);
                return true;
            case "ammospeed":
                unit.ammoSpeed = Mathf.RoundToInt(value);
                return true;
            case "firepenpct":
                unit.firePenPct = value;
                return true;
            case "firepenflat":
                unit.firePenFlat = value;
                return true;
            case "damageaoe":
                unit.damageAoe = Mathf.RoundToInt(value);
                return true;
            case "bayonetdamage":
                unit.bayonetDamage = Mathf.RoundToInt(value);
                return true;
            case "bayonetspeed":
                unit.bayonetSpeed = value;
                return true;
            case "bayonetrange":
                unit.bayonetRange = Mathf.RoundToInt(value);
                return true;
            case "bayonetpenpct":
                unit.bayonetPenPct = value;
                return true;
            case "bayonetpenflat":
                unit.bayonetPenFlat = value;
                return true;
            case "movespeed":
                unit.moveSpeed = value;
                return true;
            case "capturespeed":
                unit.captureSpeed = value;
                return true;
            case "threatvalue":
                unit.threatValue = Mathf.RoundToInt(value);
                return true;
            default:
                return false;
        }
    }

    private float EvaluateFormula(string formula, UnitLogic source, float fallback)
    {
        string cleaned = StripOuterBraces(formula);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return fallback;
        }

        FormulaParser parser = new FormulaParser(cleaned, name =>
        {
            if (TryGetStat(source, name, out float value))
            {
                return value;
            }

            return 0f;
        });

        return parser.TryEvaluate(out float result) ? result : fallback;
    }

    private UnitLogic GetFormulaContextUnit(FormulaContextMode formulaContextMode, UnitLogic source, UnitLogic target)
    {
        return formulaContextMode == FormulaContextMode.Target && target != null ? target : source;
    }

    private string SingleFormulaParam(string effectParams)
    {
        List<string> parts = SplitTopLevel(effectParams);
        return parts.Count > 0 ? parts[0] : effectParams;
    }

    private void TickTimedRuntimeEffects(float deltaTime)
    {
        for (int i = activeStatModifiers.Count - 1; i >= 0; i--)
        {
            ActiveStatModifier modifier = activeStatModifiers[i];
            if (modifier == null || modifier.Target == null)
            {
                activeStatModifiers.RemoveAt(i);
                continue;
            }

            if (modifier.IsPermanentForBattle)
            {
                continue;
            }

            modifier.RemainingSeconds -= deltaTime;
            if (modifier.RemainingSeconds <= 0f)
            {
                TryApplyStatDelta(modifier.Target, modifier.StatName, -modifier.Amount);
                activeStatModifiers.RemoveAt(i);
            }
        }

        for (int i = activeShields.Count - 1; i >= 0; i--)
        {
            ActiveShield shield = activeShields[i];
            if (shield == null || shield.Target == null)
            {
                activeShields.RemoveAt(i);
                continue;
            }

            if (shield.IsPermanentForBattle)
            {
                continue;
            }

            shield.RemainingSeconds -= deltaTime;
            if (shield.RemainingSeconds <= 0f)
            {
                shield.Target.RemoveTemporaryShield(shield.Amount);
                activeShields.RemoveAt(i);
            }
        }
    }

    private void CleanupRuntimeEffects()
    {
        for (int i = activeStatModifiers.Count - 1; i >= 0; i--)
        {
            ActiveStatModifier modifier = activeStatModifiers[i];
            if (modifier != null && modifier.Target != null)
            {
                TryApplyStatDelta(modifier.Target, modifier.StatName, -modifier.Amount);
            }
        }

        activeStatModifiers.Clear();

        for (int i = activeShields.Count - 1; i >= 0; i--)
        {
            ActiveShield shield = activeShields[i];
            if (shield != null && shield.Target != null)
            {
                shield.Target.RemoveTemporaryShield(shield.Amount);
            }
        }

        activeShields.Clear();
    }

    private void ClearSummonedUnits()
    {
        for (int i = summonedUnits.Count - 1; i >= 0; i--)
        {
            UnitLogic unit = summonedUnits[i];
            if (unit != null)
            {
                Destroy(unit.gameObject);
            }
        }

        summonedUnits.Clear();
    }

    private void CleanupNullRuntimeBooks()
    {
        scratchUnits.Clear();
        foreach (KeyValuePair<UnitLogic, UnitSkillRuntime> pair in runtimeByUnit)
        {
            if (pair.Key == null)
            {
                scratchUnits.Add(pair.Key);
            }
        }

        for (int i = 0; i < scratchUnits.Count; i++)
        {
            runtimeByUnit.Remove(scratchUnits[i]);
        }
    }

    private int ReadTriggerKind(SkillDefinition skill)
    {
        List<string> parts = SplitTopLevel(skill != null ? skill.TriggerType : string.Empty);
        return parts.Count > 0 ? ParseInt(parts[0], TriggerBattleStart) : TriggerBattleStart;
    }

    private float ReadTriggerNumber(SkillDefinition skill, float fallback)
    {
        List<string> parts = SplitTopLevel(skill != null ? skill.TriggerType : string.Empty);
        return parts.Count > 1 ? ParseFloat(parts[1], fallback) : fallback;
    }

    private static int FindHeaderRow(List<List<string>> rows, string requiredField)
    {
        string normalizedRequired = NormalizeKey(requiredField);
        int limit = Mathf.Min(8, rows.Count);
        for (int rowIndex = 0; rowIndex < limit; rowIndex++)
        {
            List<string> row = rows[rowIndex];
            for (int colIndex = 0; colIndex < row.Count; colIndex++)
            {
                if (NormalizeKey(row[colIndex]) == normalizedRequired)
                {
                    return rowIndex;
                }
            }
        }

        return -1;
    }

    private static Dictionary<string, int> BuildSkillColumnIndex(List<string> headerRow)
    {
        Dictionary<string, int> result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string[] fields = { "SkillID", "TargetType", "TriggerType", "EffectType", "EffectParams" };
        for (int i = 0; i < headerRow.Count; i++)
        {
            string normalized = NormalizeKey(headerRow[i]);
            for (int fieldIndex = 0; fieldIndex < fields.Length; fieldIndex++)
            {
                if (normalized == NormalizeKey(fields[fieldIndex]) && !result.ContainsKey(fields[fieldIndex]))
                {
                    result[fields[fieldIndex]] = i;
                }
            }

            if (normalized == "effecttpye" && !result.ContainsKey("EffectType"))
            {
                result["EffectType"] = i;
            }
        }

        for (int i = 0; i < fields.Length; i++)
        {
            if (!result.ContainsKey(fields[i]))
            {
                result[fields[i]] = i;
            }
        }

        return result;
    }

    private static string GetCell(List<string> row, Dictionary<string, int> columns, string fieldName)
    {
        if (row == null || columns == null || !columns.TryGetValue(fieldName, out int columnIndex))
        {
            return string.Empty;
        }

        return columnIndex >= 0 && columnIndex < row.Count ? CleanCell(row[columnIndex]) : string.Empty;
    }

    private static bool IsRowEmpty(List<string> row)
    {
        if (row == null)
        {
            return true;
        }

        for (int i = 0; i < row.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(CleanCell(row[i])))
            {
                return false;
            }
        }

        return true;
    }

    private static List<string> SplitTopLevel(string value)
    {
        List<string> result = new List<string>();
        string inner = StripOuterBraces(value);
        if (string.IsNullOrWhiteSpace(inner))
        {
            return result;
        }

        StringBuilder current = new StringBuilder();
        int braceDepth = 0;
        int parenDepth = 0;
        for (int i = 0; i < inner.Length; i++)
        {
            char c = inner[i];
            if (c == '{' || c == '[')
            {
                braceDepth++;
            }
            else if ((c == '}' || c == ']') && braceDepth > 0)
            {
                braceDepth--;
            }
            else if (c == '(')
            {
                parenDepth++;
            }
            else if (c == ')' && parenDepth > 0)
            {
                parenDepth--;
            }

            if (braceDepth == 0 && parenDepth == 0 && IsTopLevelSeparator(c))
            {
                result.Add(CleanCell(current.ToString()));
                current.Length = 0;
                continue;
            }

            current.Append(c);
        }

        result.Add(CleanCell(current.ToString()));
        return result;
    }

    private static string JoinParams(List<string> parts, int startIndex)
    {
        if (parts == null || startIndex >= parts.Count)
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder();
        for (int i = Mathf.Max(0, startIndex); i < parts.Count; i++)
        {
            if (builder.Length > 0)
            {
                builder.Append(',');
            }

            builder.Append(parts[i]);
        }

        return builder.ToString();
    }

    private static bool IsTopLevelSeparator(char value)
    {
        return value == ',' || value == ';' || value == '|' || value == '\uFF0C' || value == '\uFF1B' || value == '\u3001';
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

    private static int ParseInt(string value, int fallback)
    {
        string cleaned = CleanCell(value);
        if (int.TryParse(cleaned, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
        {
            return result;
        }

        if (float.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out float floatValue))
        {
            return Mathf.RoundToInt(floatValue);
        }

        return fallback;
    }

    private static float ParseFloat(string value, float fallback)
    {
        string cleaned = CleanCell(value);
        return float.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out float result) ? result : fallback;
    }

    private static string CleanCell(string value)
    {
        if (value == null)
        {
            return string.Empty;
        }

        return value.Replace("\uFEFF", string.Empty).Replace("\u00A0", string.Empty).Replace("\\xa0", string.Empty).Trim();
    }

    private static string NormalizeKey(string value)
    {
        return CleanCell(value)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty)
            .Replace(" ", string.Empty)
            .ToLowerInvariant();
    }

    private void AddUniqueUnit(List<UnitLogic> units, UnitLogic unit)
    {
        if (units == null || unit == null || units.Contains(unit))
        {
            return;
        }

        units.Add(unit);
    }

    private void WarnMissingSkillIdOnce(string skillId, UnitLogic unit)
    {
        if (warnedMissingSkillIds.Contains(skillId))
        {
            return;
        }

        warnedMissingSkillIds.Add(skillId);
        Debug.LogWarning("[SkillRuntime] Cannot find SkillID '" + skillId + "' for " + (unit != null ? unit.GetDisplayName() : "unit") + ".");
    }

    private void WarnMissingSkillIdOnce(string skillId, string context)
    {
        if (warnedMissingSkillIds.Contains(skillId))
        {
            return;
        }

        warnedMissingSkillIds.Add(skillId);
        Debug.LogWarning("[SkillRuntime] Cannot find SkillID '" + skillId + "' for " + (string.IsNullOrWhiteSpace(context) ? "trait tier" : context) + ".");
    }

    private void WarnUnsupportedEffectOnce(SkillDefinition skill, string message)
    {
        string key = skill != null ? skill.SkillID + "_" + skill.EffectType : message;
        if (warnedUnsupportedEffects.Contains(key))
        {
            return;
        }

        warnedUnsupportedEffects.Add(key);
        Debug.LogWarning("[SkillRuntime] " + message + " SkillID=" + (skill != null ? skill.SkillID : "unknown"));
    }

    private sealed class SkillDefinition
    {
        public string SkillID;
        public int TargetType;
        public string TriggerType;
        public int EffectType;
        public string EffectParams;
    }

    private sealed class TraitSkillRuntime
    {
        public readonly ActiveTraitTierModel ActiveTier;
        public readonly List<SkillDefinition> SkillDefinitions;
        public readonly List<UnitLogic> QualifyingUnits;
        private readonly Dictionary<string, float> periodicTimes = new Dictionary<string, float>();

        public TraitSkillRuntime(ActiveTraitTierModel activeTier, List<SkillDefinition> skillDefinitions)
        {
            ActiveTier = activeTier;
            SkillDefinitions = skillDefinitions ?? new List<SkillDefinition>();
            QualifyingUnits = activeTier != null && activeTier.QualifyingUnits != null
                ? new List<UnitLogic>(activeTier.QualifyingUnits)
                : new List<UnitLogic>();
        }

        public float AddPeriodicTime(string key, float deltaTime)
        {
            periodicTimes.TryGetValue(key, out float time);
            time += deltaTime;
            periodicTimes[key] = time;
            return time;
        }

        public void SetPeriodicTime(string key, float value)
        {
            periodicTimes[key] = Mathf.Max(0f, value);
        }
    }

    private sealed class UnitSkillRuntime
    {
        public readonly UnitLogic Owner;
        public readonly List<SkillDefinition> SkillDefinitions;
        private readonly Dictionary<string, int> triggerCounts = new Dictionary<string, int>();
        private readonly Dictionary<string, float> periodicTimes = new Dictionary<string, float>();

        public UnitSkillRuntime(UnitLogic owner, List<SkillDefinition> skillDefinitions)
        {
            Owner = owner;
            SkillDefinitions = skillDefinitions ?? new List<SkillDefinition>();
        }

        public int GetAndIncrementCount(string key)
        {
            triggerCounts.TryGetValue(key, out int count);
            count++;
            triggerCounts[key] = count;
            return count;
        }

        public float AddPeriodicTime(string key, float deltaTime)
        {
            periodicTimes.TryGetValue(key, out float time);
            time += deltaTime;
            periodicTimes[key] = time;
            return time;
        }

        public void SetPeriodicTime(string key, float value)
        {
            periodicTimes[key] = Mathf.Max(0f, value);
        }
    }

    private sealed class ActiveStatModifier
    {
        public readonly UnitLogic Target;
        public readonly string StatName;
        public readonly float Amount;
        public float RemainingSeconds;
        public bool IsPermanentForBattle => RemainingSeconds < 0f;

        public ActiveStatModifier(UnitLogic target, string statName, float amount, float duration)
        {
            Target = target;
            StatName = statName;
            Amount = amount;
            RemainingSeconds = duration;
        }
    }

    private sealed class ActiveShield
    {
        public readonly UnitLogic Target;
        public readonly int Amount;
        public float RemainingSeconds;
        public bool IsPermanentForBattle => RemainingSeconds < 0f;

        public ActiveShield(UnitLogic target, int amount, float duration)
        {
            Target = target;
            Amount = amount;
            RemainingSeconds = duration;
        }
    }

    private sealed class FormulaParser
    {
        private readonly string text;
        private readonly Func<string, float> resolveIdentifier;
        private int index;

        public FormulaParser(string text, Func<string, float> resolveIdentifier)
        {
            this.text = text ?? string.Empty;
            this.resolveIdentifier = resolveIdentifier;
        }

        public bool TryEvaluate(out float result)
        {
            index = 0;
            result = ParseExpression();
            SkipWhitespace();
            return index >= text.Length;
        }

        private float ParseExpression()
        {
            float value = ParseTerm();
            while (true)
            {
                SkipWhitespace();
                if (Match('+'))
                {
                    value += ParseTerm();
                }
                else if (Match('-'))
                {
                    value -= ParseTerm();
                }
                else
                {
                    return value;
                }
            }
        }

        private float ParseTerm()
        {
            float value = ParseFactor();
            while (true)
            {
                SkipWhitespace();
                if (Match('*'))
                {
                    value *= ParseFactor();
                }
                else if (Match('/'))
                {
                    float divisor = ParseFactor();
                    value = Mathf.Approximately(divisor, 0f) ? 0f : value / divisor;
                }
                else
                {
                    return value;
                }
            }
        }

        private float ParseFactor()
        {
            SkipWhitespace();
            if (Match('+'))
            {
                return ParseFactor();
            }

            if (Match('-'))
            {
                return -ParseFactor();
            }

            if (Match('('))
            {
                float value = ParseExpression();
                Match(')');
                return value;
            }

            if (index < text.Length && (char.IsLetter(text[index]) || text[index] == '_'))
            {
                string identifier = ReadIdentifier();
                return resolveIdentifier != null ? resolveIdentifier(identifier) : 0f;
            }

            return ReadNumber();
        }

        private string ReadIdentifier()
        {
            int start = index;
            while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] == '_'))
            {
                index++;
            }

            return text.Substring(start, index - start);
        }

        private float ReadNumber()
        {
            int start = index;
            while (index < text.Length && (char.IsDigit(text[index]) || text[index] == '.'))
            {
                index++;
            }

            if (start == index)
            {
                return 0f;
            }

            string numberText = text.Substring(start, index - start);
            return float.TryParse(numberText, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : 0f;
        }

        private bool Match(char expected)
        {
            SkipWhitespace();
            if (index < text.Length && text[index] == expected)
            {
                index++;
                return true;
            }

            return false;
        }

        private void SkipWhitespace()
        {
            while (index < text.Length && char.IsWhiteSpace(text[index]))
            {
                index++;
            }
        }
    }
}
