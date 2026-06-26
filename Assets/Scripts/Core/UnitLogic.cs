using System;
using System.Collections.Generic;
using System.Reflection;
using System.Globalization;
using UnityEngine;
using UnityEngine.Serialization;

public enum UnitFaction
{
    Player = 0,
    Enemy = 1,
    Neutral = 2
}

public enum PlayerDirective
{
    PushLine = 0,
    Push = 0,
    Advance = 0,
    CapturePoint = 1,
    Capture = 1
}

public enum CombatModifierType
{
    IndependentAmp,
    IndependentReduction,
    FlatReduction
}

public enum DamageType
{
    Fire = 0,
    Bullet = 0,
    Ranged = 0,
    Shooting = 0,
    Bayonet = 1,
    Melee = 1
}

public enum AttackDamageTrack
{
    Fire,
    Bayonet
}

public sealed class UnitLogic : MonoBehaviour
{
    private const float ArmorFormulaBase = 100f;
    private const float TargetSearchInterval = 0.2f;
    private const float GridArrivalTolerance = 0.03f;

    private static readonly List<UnitLogic> activeUnits = new List<UnitLogic>();
    public static IReadOnlyList<UnitLogic> ActiveUnits => activeUnits;

    public static event Action<UnitLogic, UnitLogic, int, AttackDamageTrack> UnitDamaged;
    public static event Action<UnitLogic, UnitLogic> UnitDied;
    public static event Action<UnitLogic, UnitLogic> UnitKilled;
    public static event Action<UnitLogic, UnitLogic> FireAttackPerformed;
    public static event Action<UnitLogic, UnitLogic> BayonetAttackPerformed;

    [Header("Static Table Data")]
    public UnitLogicDataSO unitDataConfig;

    [Header("Projectile References")]
    public GameObject bulletPrefab;
    public Transform firePoint;

    [Header("Dynamic Model")]
    public Transform modelContainer;

    [Header("Synergy References")]
    public List<TraitSO> baseTraits = new List<TraitSO>();
    public string synergyIdentityOverride;

    [FormerlySerializedAs("faction")]
    [SerializeField] private UnitFaction runtimeFaction = UnitFaction.Player;
    [FormerlySerializedAs("playerDirective")]
    [SerializeField] private PlayerDirective runtimePlayerDirective = PlayerDirective.PushLine;
    [FormerlySerializedAs("unitCost")]
    [SerializeField] private int runtimeUnitCost = 1;
    [FormerlySerializedAs("unitPrice")]
    [SerializeField] private int runtimeUnitPrice = 1;
    [FormerlySerializedAs("unitRare")]
    [SerializeField] private int runtimeUnitRare = 1;
    [FormerlySerializedAs("unitTier")]
    [SerializeField] private int runtimeUnitTier = 1;
    [FormerlySerializedAs("unitType")]
    [SerializeField] private int runtimeUnitType;
    [FormerlySerializedAs("attackType")]
    [SerializeField] private int runtimeAttackType;
    [FormerlySerializedAs("maxHp")]
    [SerializeField] private int runtimeMaxHp = 100;
    [FormerlySerializedAs("currentHp")]
    [SerializeField] private int runtimeCurrentHp = 100;
    [FormerlySerializedAs("armor")]
    [SerializeField] private int runtimeArmor;
    [FormerlySerializedAs("bayonetArmor")]
    [SerializeField] private int runtimeBayonetArmor;
    [FormerlySerializedAs("critRate")]
    [SerializeField] private float runtimeCritRate;
    [FormerlySerializedAs("critDamage")]
    [SerializeField] private float runtimeCritDamage = 1.5f;
    [FormerlySerializedAs("damage")]
    [FormerlySerializedAs("fireDamage")]
    [SerializeField] private int runtimeFireDamage = 10;
    [FormerlySerializedAs("fireRate")]
    [SerializeField] private float runtimeFireRate = 8f;
    [FormerlySerializedAs("attackSpeed")]
    [SerializeField] private float runtimeFireSpeed = 1f;
    [FormerlySerializedAs("attackRange")]
    [SerializeField] private int runtimeFireRange = 3;
    [FormerlySerializedAs("maxAmmo")]
    [SerializeField] private int runtimeMaxAmmo = 6;
    [FormerlySerializedAs("currentAmmo")]
    [SerializeField] private int runtimeCurrentAmmo = 6;
    [FormerlySerializedAs("ammoSpeed")]
    [SerializeField] private int runtimeAmmoSpeed = 1;
    [FormerlySerializedAs("firePenPct")]
    [SerializeField] private float runtimeFirePenPct;
    [FormerlySerializedAs("firePenFlat")]
    [SerializeField] private float runtimeFirePenFlat;
    [FormerlySerializedAs("damageAoe")]
    [SerializeField] private int runtimeDamageAoe;
    [FormerlySerializedAs("bayonetId")]
    [SerializeField] private string runtimeBayonetId;
    [FormerlySerializedAs("bayonetDamage")]
    [SerializeField] private int runtimeBayonetDamage = 8;
    [FormerlySerializedAs("bayonetCost")]
    [SerializeField] private string runtimeBayonetCost;
    [FormerlySerializedAs("bayonetSpeed")]
    [SerializeField] private float runtimeBayonetSpeed = 1f;
    [FormerlySerializedAs("bayonetRange")]
    [SerializeField] private int runtimeBayonetRange = 1;
    [FormerlySerializedAs("bayonetPenPct")]
    [SerializeField] private float runtimeBayonetPenPct;
    [FormerlySerializedAs("bayonetPenFlat")]
    [SerializeField] private float runtimeBayonetPenFlat;
    [FormerlySerializedAs("moveSpeed")]
    [SerializeField] private float runtimeMoveSpeed = 1.5f;
    [FormerlySerializedAs("captureSpeed")]
    [SerializeField] private float runtimeCaptureSpeed = 5f;
    [FormerlySerializedAs("threatValue")]
    [SerializeField] private int runtimeThreatValue = 1;

    [FormerlySerializedAs("isVeteran")]
    [SerializeField] private bool runtimeIsVeteran;
    [SerializeField] private bool isPositionLocked;
    [SerializeField] private bool isCombatEnabled = true;
    [SerializeField] private bool hasGridPosition;
    [SerializeField] private Vector2Int gridPosition;
    [SerializeField] private bool runtimeInBayonetMode;
    [SerializeField] private bool runtimeIsSummoned;
    [SerializeField] private int runtimeTemporaryShield;

    private readonly Dictionary<string, float> independentAmpModifiers = new Dictionary<string, float>();
    private readonly Dictionary<string, float> independentReductionModifiers = new Dictionary<string, float>();
    private readonly Dictionary<string, float> flatReductionModifiers = new Dictionary<string, float>();

    private UnitLogic currentTarget;
    private float attackCooldown;
    private float targetSearchCooldown;
    private bool runtimeValuesInitialized;
    private bool hasTriggeredBayonetContact;
    private int temporaryArmorBonus;
    private float temporaryAttackSpeedMultiplier = 1f;
    private int temporaryTraitEffectIndex;
    private readonly List<string> temporaryTraitModifierKeys = new List<string>();

    public IReadOnlyDictionary<string, float> IndependentAmpModifiers => independentAmpModifiers;
    public IReadOnlyDictionary<string, float> IndependentReductionModifiers => independentReductionModifiers;
    public IReadOnlyDictionary<string, float> FlatReductionModifiers => flatReductionModifiers;

    public UnitFaction faction
    {
        get => ShouldPreviewConfig() ? ConvertFaction(unitDataConfig.faction) : runtimeFaction;
        set => runtimeFaction = value;
    }

    public PlayerDirective playerDirective
    {
        get => ShouldPreviewConfig() ? ConvertPlayerDirective(unitDataConfig.playerDirective) : runtimePlayerDirective;
        set => runtimePlayerDirective = value;
    }

    public int unitCost
    {
        get => ShouldPreviewConfig() ? Mathf.Max(0, unitDataConfig.unitCost) : runtimeUnitCost;
        set => runtimeUnitCost = Mathf.Max(0, value);
    }

    public int unitPrice
    {
        get => ShouldPreviewConfig() ? Mathf.Max(0, unitDataConfig.unitPrice) : runtimeUnitPrice;
        set => runtimeUnitPrice = Mathf.Max(0, value);
    }

    public int unitRare
    {
        get => ShouldPreviewConfig() ? Mathf.Max(0, unitDataConfig.unitRare) : runtimeUnitRare;
        set => runtimeUnitRare = Mathf.Max(0, value);
    }

    public int unitTier
    {
        get => ShouldPreviewConfig() ? Mathf.Max(1, unitDataConfig.unitTier) : runtimeUnitTier;
        set => runtimeUnitTier = Mathf.Max(1, value);
    }

    public int unitType
    {
        get => ShouldPreviewConfig() ? unitDataConfig.unitType : runtimeUnitType;
        set => runtimeUnitType = value;
    }

    public int attackType
    {
        get => ShouldPreviewConfig() ? unitDataConfig.attackType : runtimeAttackType;
        set => runtimeAttackType = value;
    }

    public int maxHp
    {
        get => ShouldPreviewConfig() ? Mathf.Max(1, unitDataConfig.baseHp) : runtimeMaxHp;
        set => runtimeMaxHp = Mathf.Max(1, value);
    }

    public int currentHp
    {
        get => ShouldPreviewConfig() ? Mathf.Max(1, unitDataConfig.baseHp) : runtimeCurrentHp;
        set => runtimeCurrentHp = Mathf.Clamp(value, 0, Mathf.Max(1, runtimeMaxHp));
    }

    public int armor
    {
        get => ShouldPreviewConfig() ? Mathf.Max(0, unitDataConfig.baseArmor) : runtimeArmor;
        set => runtimeArmor = Mathf.Max(0, value);
    }

    public int bayonetArmor
    {
        get => ShouldPreviewConfig() ? Mathf.Max(0, unitDataConfig.bayonetArmor) : runtimeBayonetArmor;
        set => runtimeBayonetArmor = Mathf.Max(0, value);
    }

    public float critRate
    {
        get => ShouldPreviewConfig() ? Mathf.Max(0f, unitDataConfig.critRate) : runtimeCritRate;
        set => runtimeCritRate = Mathf.Max(0f, value);
    }

    public float critDamage
    {
        get => ShouldPreviewConfig() ? Mathf.Max(0f, unitDataConfig.critDamage) : runtimeCritDamage;
        set => runtimeCritDamage = Mathf.Max(0f, value);
    }

    public int damage
    {
        get => fireDamage;
        set => fireDamage = value;
    }

    public int fireDamage
    {
        get => ShouldPreviewConfig() ? Mathf.Max(0, unitDataConfig.fireDamage) : runtimeFireDamage;
        set
        {
            runtimeFireDamage = Mathf.Max(0, value);
        }
    }

    public float fireRate
    {
        get => ShouldPreviewConfig() ? Mathf.Max(0f, unitDataConfig.fireRate) : runtimeFireRate;
        set => runtimeFireRate = Mathf.Max(0f, value);
    }

    public float attackSpeed
    {
        get => fireSpeed;
        set => fireSpeed = value;
    }

    public float fireSpeed
    {
        get => ShouldPreviewConfig() ? Mathf.Max(0.01f, unitDataConfig.fireSpeed) : runtimeFireSpeed;
        set => runtimeFireSpeed = Mathf.Max(0.01f, value);
    }

    public int attackRange
    {
        get => fireRange;
        set => fireRange = value;
    }

    public int fireRange
    {
        get => ShouldPreviewConfig() ? Mathf.Max(0, unitDataConfig.fireRange) : runtimeFireRange;
        set => runtimeFireRange = Mathf.Max(0, value);
    }

    public int maxAmmo
    {
        get => ShouldPreviewConfig() ? Mathf.Max(0, unitDataConfig.ammo) : runtimeMaxAmmo;
        set => runtimeMaxAmmo = Mathf.Max(0, value);
    }

    public int currentAmmo
    {
        get => ShouldPreviewConfig() ? Mathf.Max(0, unitDataConfig.ammo) : runtimeCurrentAmmo;
        set => runtimeCurrentAmmo = Mathf.Clamp(value, 0, Mathf.Max(0, runtimeMaxAmmo));
    }

    public int ammo
    {
        get => maxAmmo;
        set => maxAmmo = value;
    }

    public int ammoSpeed
    {
        get => ShouldPreviewConfig() ? Mathf.Max(1, unitDataConfig.ammoSpeed) : runtimeAmmoSpeed;
        set => runtimeAmmoSpeed = Mathf.Max(1, value);
    }

    public float firePenPct
    {
        get => ShouldPreviewConfig() ? Mathf.Max(0f, unitDataConfig.firePenPct) : runtimeFirePenPct;
        set => runtimeFirePenPct = Mathf.Max(0f, value);
    }

    public float firePenFlat
    {
        get => ShouldPreviewConfig() ? Mathf.Max(0f, unitDataConfig.firePenFlat) : runtimeFirePenFlat;
        set => runtimeFirePenFlat = Mathf.Max(0f, value);
    }

    public int damageAoe
    {
        get => ShouldPreviewConfig() ? Mathf.Max(0, unitDataConfig.damageAoe) : runtimeDamageAoe;
        set => runtimeDamageAoe = Mathf.Max(0, value);
    }

    public string bayonetId
    {
        get => ShouldPreviewConfig() ? unitDataConfig.bayonetId : runtimeBayonetId;
        set => runtimeBayonetId = value;
    }

    public int bayonetDamage
    {
        get => ShouldPreviewConfig() ? Mathf.Max(0, unitDataConfig.bayonetDamage) : runtimeBayonetDamage;
        set => runtimeBayonetDamage = Mathf.Max(0, value);
    }

    public string bayonetCost
    {
        get => ShouldPreviewConfig() ? unitDataConfig.bayonetCost : runtimeBayonetCost;
        set => runtimeBayonetCost = value;
    }

    public float bayonetSpeed
    {
        get => ShouldPreviewConfig() ? Mathf.Max(0.01f, unitDataConfig.bayonetSpeed) : runtimeBayonetSpeed;
        set => runtimeBayonetSpeed = Mathf.Max(0.01f, value);
    }

    public int bayonetRange
    {
        get => ShouldPreviewConfig() ? Mathf.Max(0, unitDataConfig.bayonetRange) : runtimeBayonetRange;
        set => runtimeBayonetRange = Mathf.Max(0, value);
    }

    public float bayonetPenPct
    {
        get => ShouldPreviewConfig() ? Mathf.Max(0f, unitDataConfig.bayonetPenPct) : runtimeBayonetPenPct;
        set => runtimeBayonetPenPct = Mathf.Max(0f, value);
    }

    public float bayonetPenFlat
    {
        get => ShouldPreviewConfig() ? Mathf.Max(0f, unitDataConfig.bayonetPenFlat) : runtimeBayonetPenFlat;
        set => runtimeBayonetPenFlat = Mathf.Max(0f, value);
    }

    public float moveSpeed
    {
        get => ShouldPreviewConfig() ? Mathf.Max(0f, unitDataConfig.moveSpeed) : runtimeMoveSpeed;
        set => runtimeMoveSpeed = Mathf.Max(0f, value);
    }

    public float captureSpeed
    {
        get => ShouldPreviewConfig() ? Mathf.Max(0f, unitDataConfig.captureSpeed) : runtimeCaptureSpeed;
        set => runtimeCaptureSpeed = Mathf.Max(0f, value);
    }

    public float CaptureSpeed => captureSpeed;

    public int threatValue
    {
        get => ShouldPreviewConfig() ? Mathf.Max(0, unitDataConfig.threatValue) : runtimeThreatValue;
        set => runtimeThreatValue = Mathf.Max(0, value);
    }

    public bool isInBayonetMode
    {
        get => runtimeInBayonetMode;
        set => runtimeInBayonetMode = value;
    }

    public int UnitPrice => unitPrice;
    public int MaxHp => maxHp;
    public int CurrentHp => currentHp;
    public int StarLevel => unitTier;
    public bool IsAlive => currentHp > 0 && gameObject.activeInHierarchy;
    public bool isVeteran
    {
        get => runtimeIsVeteran;
        set => runtimeIsVeteran = value;
    }

    public bool IsVeteran => runtimeIsVeteran;
    public bool IsPositionLocked => isPositionLocked;
    public bool IsSummoned => runtimeIsSummoned;
    public bool IsCombatEnabled => isCombatEnabled;
    public bool HasGridPosition => hasGridPosition;
    public Vector2Int GridPosition => gridPosition;
    public int TemporaryShield => runtimeTemporaryShield;
    public IReadOnlyList<TraitSO> RuntimeTraits => baseTraits;

    private void Awake()
    {
        ApplyUnitDataConfig(unitDataConfig, false);
    }

    private void OnEnable()
    {
        if (!activeUnits.Contains(this))
        {
            activeUnits.Add(this);
        }

        if (!runtimeValuesInitialized)
        {
            ApplyUnitDataConfig(unitDataConfig, false);
        }
    }

    private void OnDisable()
    {
        activeUnits.Remove(this);
    }

    private void OnDestroy()
    {
        activeUnits.Remove(this);
    }

    private void OnValidate()
    {
        SanitizeRuntimeValues();
    }

    private void Update()
    {
        if (!CanTickCombat())
        {
            return;
        }

        UnitLogic attackTarget = FindBestTargetInCurrentAttackRange();
        if (attackTarget != null)
        {
            currentTarget = attackTarget;
            TickAttack();
            return;
        }

        if (faction == UnitFaction.Player && runtimePlayerDirective == PlayerDirective.CapturePoint)
        {
            TickCapturePointDirective();
            return;
        }

        targetSearchCooldown -= Time.deltaTime;
        if (targetSearchCooldown <= 0f || currentTarget == null || !currentTarget.IsAlive)
        {
            currentTarget = FindBestTarget();
            targetSearchCooldown = TargetSearchInterval;
        }

        if (currentTarget == null)
        {
            TickDirectiveMovement();
            return;
        }

        MoveToward(currentTarget.transform.position);
    }

    public void ApplyUnitDataConfig(UnitLogicDataSO nextConfig)
    {
        ApplyUnitDataConfig(nextConfig, false);
    }

    public void InitializeFromConfig(UnitLogicDataSO config, UnitFaction initialFaction, List<TraitSO> injectedTraits)
    {
        ResetTemporarySynergyModifiers();
        ClearCombatModifiers();
        ApplyUnitDataConfig(config, false);
        runtimeFaction = initialFaction;

        if (injectedTraits != null)
        {
            baseTraits = new List<TraitSO>(injectedTraits);
        }
        else
        {
            baseTraits = new List<TraitSO>();
        }

        runtimeCurrentHp = runtimeMaxHp;
        runtimeIsSummoned = false;
        runtimeTemporaryShield = 0;
        runtimeValuesInitialized = true;
    }

    public void ApplyUnitDataConfig(UnitLogicDataSO nextConfig, bool keepCurrentHpRatio)
    {
        float hpRatio = runtimeMaxHp > 0 ? (float)runtimeCurrentHp / runtimeMaxHp : 1f;
        unitDataConfig = nextConfig;

        if (unitDataConfig != null)
        {
            RefreshPresentationIdentity();
            runtimeFaction = ConvertFaction(unitDataConfig.faction);
            runtimePlayerDirective = ConvertPlayerDirective(unitDataConfig.playerDirective);
            runtimeUnitCost = Mathf.Max(0, unitDataConfig.unitCost);
            runtimeUnitPrice = Mathf.Max(0, unitDataConfig.unitPrice);
            runtimeUnitRare = Mathf.Max(0, unitDataConfig.unitRare);
            runtimeUnitTier = Mathf.Max(1, unitDataConfig.unitTier);
            runtimeUnitType = unitDataConfig.unitType;
            runtimeAttackType = unitDataConfig.attackType;
            runtimeMaxHp = Mathf.Max(1, unitDataConfig.baseHp);
            runtimeCurrentHp = keepCurrentHpRatio ? Mathf.Clamp(Mathf.CeilToInt(runtimeMaxHp * hpRatio), 1, runtimeMaxHp) : runtimeMaxHp;
            runtimeArmor = Mathf.Max(0, unitDataConfig.baseArmor);
            runtimeBayonetArmor = Mathf.Max(0, unitDataConfig.bayonetArmor);
            runtimeCritRate = Mathf.Max(0f, unitDataConfig.critRate);
            runtimeCritDamage = Mathf.Max(0f, unitDataConfig.critDamage);
            runtimeFireDamage = Mathf.Max(0, unitDataConfig.fireDamage);
            runtimeFireRate = Mathf.Max(0f, unitDataConfig.fireRate);
            runtimeFireSpeed = Mathf.Max(0.01f, unitDataConfig.fireSpeed);
            runtimeFireRange = Mathf.Max(0, unitDataConfig.fireRange);
            runtimeMaxAmmo = Mathf.Max(0, unitDataConfig.ammo);
            runtimeCurrentAmmo = runtimeMaxAmmo;
            runtimeAmmoSpeed = Mathf.Max(1, unitDataConfig.ammoSpeed);
            runtimeFirePenPct = Mathf.Max(0f, unitDataConfig.firePenPct);
            runtimeFirePenFlat = Mathf.Max(0f, unitDataConfig.firePenFlat);
            runtimeDamageAoe = Mathf.Max(0, unitDataConfig.damageAoe);
            runtimeBayonetId = unitDataConfig.bayonetId;
            runtimeBayonetDamage = Mathf.Max(0, unitDataConfig.bayonetDamage);
            runtimeBayonetCost = unitDataConfig.bayonetCost;
            runtimeBayonetSpeed = Mathf.Max(0.01f, unitDataConfig.bayonetSpeed);
            runtimeBayonetRange = Mathf.Max(0, unitDataConfig.bayonetRange);
            runtimeBayonetPenPct = Mathf.Max(0f, unitDataConfig.bayonetPenPct);
            runtimeBayonetPenFlat = Mathf.Max(0f, unitDataConfig.bayonetPenFlat);
            runtimeMoveSpeed = Mathf.Max(0f, unitDataConfig.moveSpeed);
            runtimeCaptureSpeed = Mathf.Max(0f, unitDataConfig.captureSpeed);
            runtimeThreatValue = Mathf.Max(0, unitDataConfig.threatValue);
        }

        SanitizeRuntimeValues();
        ApplyConfiguredUnitSprite();
        runtimeValuesInitialized = true;
        ResetCombatRuntimeOnly();
    }

    private void RefreshPresentationIdentity()
    {
        string displayName = GetDisplayName();
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            gameObject.name = displayName;
        }

        MonoBehaviour[] components = GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < components.Length; i++)
        {
            MonoBehaviour component = components[i];
            if (component == null || component == this)
            {
                continue;
            }

            TryInvokePresentationRefresh(component, "RefreshName", displayName);
            TryInvokePresentationRefresh(component, "RefreshDisplayName", displayName);
            TryInvokePresentationRefresh(component, "SetDisplayName", displayName);
            TryInvokePresentationRefresh(component, "SetName", displayName);
        }
    }

    private void TryInvokePresentationRefresh(MonoBehaviour component, string methodName, string displayName)
    {
        try
        {
            MethodInfo[] methods = component.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 0)
                {
                    method.Invoke(component, null);
                    return;
                }

                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string))
                {
                    method.Invoke(component, new object[] { displayName });
                    return;
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("[UnitLogic] Failed to refresh presentation name on " + component.GetType().Name + ": " + exception.Message);
        }
    }

    public void ApplyStarUpgrade()
    {
        UnitLogicDataSO nextConfig = FindNextStarDataConfig();
        if (nextConfig != null)
        {
            ApplyUnitDataConfig(nextConfig, false);
            return;
        }

        runtimeUnitTier = Mathf.Max(1, runtimeUnitTier + 1);
        runtimeMaxHp = Mathf.Max(1, Mathf.CeilToInt(runtimeMaxHp * 1.5f));
        runtimeCurrentHp = runtimeMaxHp;
        runtimeFireDamage = Mathf.CeilToInt(runtimeFireDamage * 1.35f);
        runtimeBayonetDamage = Mathf.CeilToInt(runtimeBayonetDamage * 1.35f);
        runtimeValuesInitialized = true;

        Debug.LogWarning("[UnitLogic] Could not find next UnitLogicDataSO for " + GetDisplayName() + ". Applied a runtime fallback star upgrade instead.");
    }

    public void ResetForBattle()
    {
        ResetCombatRuntimeOnly();
        if (runtimeCurrentHp > runtimeMaxHp)
        {
            runtimeCurrentHp = runtimeMaxHp;
        }
    }

    public void SetCombatEnabled(bool enabled)
    {
        isCombatEnabled = enabled;
        if (!enabled)
        {
            currentTarget = null;
            attackCooldown = 0f;
            targetSearchCooldown = 0f;
        }
    }

    public void SwitchToPushLineDirective()
    {
        runtimePlayerDirective = PlayerDirective.PushLine;
        currentTarget = null;
    }

    public void SetFaction(UnitFaction nextFaction)
    {
        runtimeFaction = nextFaction;
    }

    public void SetVeteran(bool veteran)
    {
        runtimeIsVeteran = veteran;
    }

    public void SetSummoned(bool summoned)
    {
        runtimeIsSummoned = summoned;
    }

    public void SetPositionLocked(bool locked)
    {
        isPositionLocked = locked;
    }

    public void SetGridPosition(Vector2Int nextGridPosition)
    {
        gridPosition = nextGridPosition;
        hasGridPosition = true;
    }

    public void ClearGridPosition()
    {
        hasGridPosition = false;
        gridPosition = default;
    }

    public void SetCurrentHp(int hp)
    {
        currentHp = hp;
    }

    public void AddTemporaryShield(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        runtimeTemporaryShield = Mathf.Max(0, runtimeTemporaryShield + amount);
    }

    public void RemoveTemporaryShield(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        runtimeTemporaryShield = Mathf.Max(0, runtimeTemporaryShield - amount);
    }

    public void ReviveWithHp(int hp)
    {
        runtimeCurrentHp = Mathf.Clamp(hp, 1, runtimeMaxHp);
        if (!gameObject.activeSelf)
        {
            gameObject.SetActive(true);
        }

        currentTarget = null;
        attackCooldown = 0f;
        targetSearchCooldown = 0f;
        isCombatEnabled = GameFlowManager.Instance == null || GameFlowManager.Instance.CurrentState == GameState.Battle;
    }

    public string GetDisplayName()
    {
        if (unitDataConfig != null && !string.IsNullOrWhiteSpace(unitDataConfig.chessName))
        {
            return unitDataConfig.chessName;
        }

        return string.IsNullOrWhiteSpace(name) ? "Unit" : name.Replace("(Clone)", string.Empty).Trim();
    }

    public string GetColoredDisplayName()
    {
        string color = "#FFFFFF";
        if (faction == UnitFaction.Player)
        {
            color = "#00FFCC";
        }
        else if (faction == UnitFaction.Enemy)
        {
            color = "#FF6666";
        }

        return "<color=" + color + ">" + GetDisplayName() + "</color>";
    }

    public string GetSynergyIdentityKey()
    {
        string dataKey = GetSynergyIdentityKey(unitDataConfig);
        if (!string.IsNullOrWhiteSpace(dataKey))
        {
            return dataKey;
        }

        if (!string.IsNullOrWhiteSpace(synergyIdentityOverride))
        {
            return synergyIdentityOverride.Trim();
        }

        return GetDisplayName();
    }

    public static string GetSynergyIdentityKey(UnitLogicDataSO unitData)
    {
        if (unitData == null)
        {
            return string.Empty;
        }

        if (!string.IsNullOrWhiteSpace(unitData.chessId))
        {
            return StripTrailingTierDigit(unitData.chessId);
        }

        if (!string.IsNullOrWhiteSpace(unitData.chessName))
        {
            return StripStarSymbols(unitData.chessName);
        }

        if (!string.IsNullOrWhiteSpace(unitData.unionId))
        {
            return unitData.unionId.Trim();
        }

        return string.Empty;
    }

    public void AddCombatModifier(CombatModifierType modifierType, string sourceKey, float value)
    {
        Dictionary<string, float> targetBook = GetModifierBook(modifierType);
        if (targetBook == null || string.IsNullOrWhiteSpace(sourceKey))
        {
            return;
        }

        targetBook[sourceKey.Trim()] = value;
    }

    public void AddCombatModifier(string modifierType, string sourceKey, float value)
    {
        if (TryParseModifierType(modifierType, out CombatModifierType parsedType))
        {
            AddCombatModifier(parsedType, sourceKey, value);
        }
    }

    public void RemoveCombatModifier(CombatModifierType modifierType, string sourceKey)
    {
        Dictionary<string, float> targetBook = GetModifierBook(modifierType);
        if (targetBook == null || string.IsNullOrWhiteSpace(sourceKey))
        {
            return;
        }

        targetBook.Remove(sourceKey.Trim());
    }

    public void RemoveCombatModifier(string modifierType, string sourceKey)
    {
        if (TryParseModifierType(modifierType, out CombatModifierType parsedType))
        {
            RemoveCombatModifier(parsedType, sourceKey);
        }
    }

    public void ClearCombatModifiers()
    {
        independentAmpModifiers.Clear();
        independentReductionModifiers.Clear();
        flatReductionModifiers.Clear();
    }

    public void ResetTemporarySynergyModifiers()
    {
        for (int i = 0; i < temporaryTraitModifierKeys.Count; i++)
        {
            independentAmpModifiers.Remove(temporaryTraitModifierKeys[i]);
        }

        temporaryTraitModifierKeys.Clear();

        if (temporaryArmorBonus != 0)
        {
            runtimeArmor = Mathf.Max(0, runtimeArmor - temporaryArmorBonus);
            runtimeBayonetArmor = Mathf.Max(0, runtimeBayonetArmor - temporaryArmorBonus);
            temporaryArmorBonus = 0;
        }

        if (!Mathf.Approximately(temporaryAttackSpeedMultiplier, 1f) && temporaryAttackSpeedMultiplier > 0f)
        {
            runtimeFireSpeed = Mathf.Max(0.01f, runtimeFireSpeed / temporaryAttackSpeedMultiplier);
            runtimeBayonetSpeed = Mathf.Max(0.01f, runtimeBayonetSpeed / temporaryAttackSpeedMultiplier);
            temporaryAttackSpeedMultiplier = 1f;
        }

        temporaryTraitEffectIndex = 0;
    }

    public void ApplyTraitEffect(object effect)
    {
        if (effect == null)
        {
            return;
        }

        string effectKind = ReadEffectKind(effect);
        float value = ReadEffectValue(effect);
        if (string.IsNullOrWhiteSpace(effectKind) || Mathf.Approximately(value, 0f))
        {
            return;
        }

        string normalizedKind = effectKind.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
        if (normalizedKind.Contains("damage"))
        {
            float ampValue = value > 1f ? value - 1f : value;
            string key = "SynergyTrait_" + GetInstanceID() + "_" + temporaryTraitEffectIndex++;
            AddCombatModifier(CombatModifierType.IndependentAmp, key, ampValue);
            temporaryTraitModifierKeys.Add(key);
            return;
        }

        if (normalizedKind.Contains("armor"))
        {
            int armorBonus = Mathf.RoundToInt(value);
            runtimeArmor = Mathf.Max(0, runtimeArmor + armorBonus);
            runtimeBayonetArmor = Mathf.Max(0, runtimeBayonetArmor + armorBonus);
            temporaryArmorBonus += armorBonus;
            return;
        }

        if (normalizedKind.Contains("attackspeed") || normalizedKind.Contains("firerate") || normalizedKind.Contains("interval"))
        {
            float multiplier = value > 1f ? value : 1f + value;
            multiplier = Mathf.Max(0.01f, multiplier);
            runtimeFireSpeed = Mathf.Max(0.01f, runtimeFireSpeed * multiplier);
            runtimeBayonetSpeed = Mathf.Max(0.01f, runtimeBayonetSpeed * multiplier);
            temporaryAttackSpeedMultiplier *= multiplier;
        }
    }

    public void ReceiveDamage(int incomingDamage)
    {
        ReceiveDamage((float)incomingDamage, 0f, 0f, AttackDamageTrack.Fire, null);
    }

    public void ReceiveDamage(float incomingDamage)
    {
        ReceiveDamage(incomingDamage, 0f, 0f, AttackDamageTrack.Fire, null);
    }

    public void ReceiveDamage(int incomingDamage, DamageType damageType)
    {
        ReceiveDamage((float)incomingDamage, 0f, 0f, ConvertDamageType(damageType), null);
    }

    public void ReceiveDamage(float incomingDamage, DamageType damageType)
    {
        ReceiveDamage(incomingDamage, 0f, 0f, ConvertDamageType(damageType), null);
    }

    public void ReceiveDamage(UnitLogic attacker, int incomingDamage, DamageType damageType)
    {
        ReceiveDamage((float)incomingDamage, 0f, 0f, ConvertDamageType(damageType), attacker);
    }

    public void ReceiveDamage(UnitLogic attacker, float incomingDamage, DamageType damageType)
    {
        ReceiveDamage(incomingDamage, 0f, 0f, ConvertDamageType(damageType), attacker);
    }

    public void ReceiveDamage(UnitLogic attacker, int incomingDamage, DamageType damageType, float penetrationPct)
    {
        ReceiveDamage((float)incomingDamage, penetrationPct, 0f, ConvertDamageType(damageType), attacker);
    }

    public void ReceiveDamage(UnitLogic attacker, float incomingDamage, DamageType damageType, float penetrationPct)
    {
        ReceiveDamage(incomingDamage, penetrationPct, 0f, ConvertDamageType(damageType), attacker);
    }

    public void ReceiveDamage(UnitLogic attacker, int incomingDamage, DamageType damageType, float penetrationPct, float penetrationFlat)
    {
        ReceiveDamage((float)incomingDamage, penetrationPct, penetrationFlat, ConvertDamageType(damageType), attacker);
    }

    public void ReceiveDamage(UnitLogic attacker, float incomingDamage, DamageType damageType, float penetrationPct, float penetrationFlat)
    {
        ReceiveDamage(incomingDamage, penetrationPct, penetrationFlat, ConvertDamageType(damageType), attacker);
    }

    public void ReceiveDamage(UnitLogic attacker, int incomingDamage, float penetrationPct, float penetrationFlat, DamageType damageType)
    {
        ReceiveDamage((float)incomingDamage, penetrationPct, penetrationFlat, ConvertDamageType(damageType), attacker);
    }

    public void ReceiveDamage(UnitLogic attacker, float incomingDamage, float penetrationPct, float penetrationFlat, DamageType damageType)
    {
        ReceiveDamage(incomingDamage, penetrationPct, penetrationFlat, ConvertDamageType(damageType), attacker);
    }

    public void ReceiveDamage(UnitLogic attacker, int incomingDamage)
    {
        ReceiveDamage((float)incomingDamage, 0f, 0f, AttackDamageTrack.Fire, attacker);
    }

    public void ReceiveDamage(UnitLogic attacker, float incomingDamage)
    {
        ReceiveDamage(incomingDamage, 0f, 0f, AttackDamageTrack.Fire, attacker);
    }

    public void ReceiveDamage(UnitLogic attacker, params object[] args)
    {
        float parsedDamage = 0f;
        float parsedPenPct = 0f;
        float parsedPenFlat = 0f;
        DamageType parsedDamageType = DamageType.Fire;
        bool foundDamage = false;

        if (args != null)
        {
            for (int i = 0; i < args.Length; i++)
            {
                object arg = args[i];
                if (arg == null)
                {
                    continue;
                }

                if (arg is DamageType damageTypeArg)
                {
                    parsedDamageType = damageTypeArg;
                    continue;
                }

                if (arg is AttackDamageTrack damageTrackArg)
                {
                    parsedDamageType = damageTrackArg == AttackDamageTrack.Bayonet ? DamageType.Bayonet : DamageType.Fire;
                    continue;
                }

                if (TryConvertFloat(arg, out float numericValue))
                {
                    if (!foundDamage)
                    {
                        parsedDamage = numericValue;
                        foundDamage = true;
                    }
                    else if (Mathf.Approximately(parsedPenPct, 0f))
                    {
                        parsedPenPct = numericValue;
                    }
                    else
                    {
                        parsedPenFlat = numericValue;
                    }
                }
            }
        }

        ReceiveDamage(parsedDamage, parsedPenPct, parsedPenFlat, ConvertDamageType(parsedDamageType), attacker);
    }

    public void ReceiveDamage(int incomingDamage, UnitLogic attacker)
    {
        ReceiveDamage((float)incomingDamage, 0f, 0f, AttackDamageTrack.Fire, attacker);
    }

    public void ReceiveDamage(float incomingDamage, UnitLogic attacker)
    {
        ReceiveDamage(incomingDamage, 0f, 0f, AttackDamageTrack.Fire, attacker);
    }

    public void ReceiveDamage(int incomingDamage, float penetrationPct, float penetrationFlat)
    {
        ReceiveDamage((float)incomingDamage, penetrationPct, penetrationFlat, AttackDamageTrack.Fire, null);
    }

    public void ReceiveDamage(float incomingDamage, float penetrationPct, float penetrationFlat)
    {
        ReceiveDamage(incomingDamage, penetrationPct, penetrationFlat, AttackDamageTrack.Fire, null);
    }

    public void ReceiveDamage(float incomingDamage, float penetrationPct, float penetrationFlat, DamageType damageType)
    {
        ReceiveDamage(incomingDamage, penetrationPct, penetrationFlat, ConvertDamageType(damageType), null);
    }

    public void ReceiveDamage(float incomingDamage, float penetrationPct, float penetrationFlat, AttackDamageTrack damageTrack, UnitLogic attacker)
    {
        if (currentHp <= 0)
        {
            return;
        }

        int targetArmor = damageTrack == AttackDamageTrack.Bayonet ? bayonetArmor : armor;
        int finalDamage = CalculateFinalDamage(incomingDamage, targetArmor, penetrationPct, penetrationFlat);
        int hpDamage = AbsorbDamageWithTemporaryShield(finalDamage);
        if (hpDamage > 0)
        {
            currentHp -= hpDamage;
        }

        UnitDamaged?.Invoke(this, attacker, hpDamage, damageTrack);

        if (currentHp <= 0)
        {
            Die(attacker);
        }
    }

    private int AbsorbDamageWithTemporaryShield(int incomingDamage)
    {
        int remainingDamage = Mathf.Max(0, incomingDamage);
        if (runtimeTemporaryShield <= 0 || remainingDamage <= 0)
        {
            return remainingDamage;
        }

        int absorbedDamage = Mathf.Min(runtimeTemporaryShield, remainingDamage);
        runtimeTemporaryShield -= absorbedDamage;
        remainingDamage -= absorbedDamage;
        return remainingDamage;
    }

    private int CalculateFinalDamage(float incomingDamage, int targetArmor, float penetrationPct, float penetrationFlat)
    {
        float baseDamage = Mathf.Max(0f, incomingDamage);
        float ampProduct = MultiplyAmpModifiers();
        float reductionProduct = MultiplyReductionModifiers();
        float flatReduction = SumFlatReductionModifiers();
        float effectiveArmor = Mathf.Max(0f, targetArmor * (1f - Mathf.Clamp01(penetrationPct)) - Mathf.Max(0f, penetrationFlat));
        float denominator = Mathf.Max(1f, ArmorFormulaBase + effectiveArmor);
        float armorAdjustedDamage = baseDamage * ArmorFormulaBase / denominator;
        float finalDamage = armorAdjustedDamage * ampProduct * reductionProduct - flatReduction;
        float armorResistanceRatio = ArmorFormulaBase / denominator;
        int roundedFinalDamage = Mathf.Max(1, Mathf.CeilToInt(finalDamage));
        bool triggeredMinimumDamage = roundedFinalDamage == 1 && finalDamage < 1.0f;
        string highlightColor = triggeredMinimumDamage ? "#FF9900" : "#00FFCC";

        Debug.Log(
            $"<color={highlightColor}><b>[Damage Debug] {(triggeredMinimumDamage ? "Minimum Damage Floor Triggered" : "Normal Damage")}</b></color>\n" +
            $"<b>Input</b>\n" +
            $"  incomingDamage: {incomingDamage:F2}\n" +
            $"<b>Armor / Penetration</b>\n" +
            $"  targetArmor: {targetArmor}\n" +
            $"  penetrationPct: {penetrationPct:F2}\n" +
            $"  penetrationFlat: {penetrationFlat:F2}\n" +
            $"<b>Step 1 - Armor Reduction</b>\n" +
            $"  effectiveArmor: {effectiveArmor:F2}\n" +
            $"  100 / (100 + effectiveArmor): {armorResistanceRatio:F4}\n" +
            $"  armorAdjustedDamage: {armorAdjustedDamage:F2}\n" +
            $"<b>Step 2 - Multiplicative Modifiers</b>\n" +
            $"  ampProduct: {ampProduct:F2}\n" +
            $"  reductionProduct: {reductionProduct:F2}\n" +
            $"<b>Step 3 - Subtractive Modifiers</b>\n" +
            $"  flatReduction: {flatReduction:F2}\n" +
            $"<b>Core Formula Output</b>\n" +
            $"  finalDamage: {finalDamage:F4}\n" +
            $"<color={highlightColor}><b>Final Output</b>\n" +
            $"  Mathf.Max(1, Mathf.CeilToInt(finalDamage)): {roundedFinalDamage}</color>");

        return Mathf.Max(1, Mathf.CeilToInt(finalDamage));
    }

    private AttackDamageTrack ConvertDamageType(DamageType damageType)
    {
        return damageType == DamageType.Bayonet || damageType == DamageType.Melee
            ? AttackDamageTrack.Bayonet
            : AttackDamageTrack.Fire;
    }

    private bool CanTickCombat()
    {
        if (!isCombatEnabled || !IsAlive)
        {
            return false;
        }

        return GameFlowManager.Instance == null || GameFlowManager.Instance.CurrentState == GameState.Battle;
    }

    private void TickAttack()
    {
        attackCooldown -= Time.deltaTime;
        if (attackCooldown > 0f)
        {
            return;
        }

        if (runtimeInBayonetMode)
        {
            UnitLogic bayonetTarget = currentTarget;
            bool canPerformBayonetAttack = bayonetTarget != null && bayonetTarget.IsAlive;
            PerformBayonetAttack(bayonetTarget);
            if (canPerformBayonetAttack)
            {
                BayonetAttackPerformed?.Invoke(this, bayonetTarget);
            }

            attackCooldown = GetAttackInterval(bayonetSpeed);
            return;
        }

        if (runtimeCurrentAmmo <= 0)
        {
            EnterBayonetMode();
            return;
        }

        UnitLogic fireTarget = currentTarget;
        bool canPerformFireAttack = fireTarget != null && fireTarget.IsAlive;
        PerformFireAttack(fireTarget);
        if (canPerformFireAttack)
        {
            FireAttackPerformed?.Invoke(this, fireTarget);
        }

        runtimeCurrentAmmo -= Mathf.Max(1, runtimeAmmoSpeed);
        if (runtimeCurrentAmmo <= 0)
        {
            runtimeCurrentAmmo = 0;
            EnterBayonetMode();
        }

        attackCooldown = GetAttackInterval(fireRate);
    }

    private void PerformFireAttack(UnitLogic target)
    {
        if (target == null || !target.IsAlive)
        {
            return;
        }

        float outgoingDamage = RollCriticalDamage(fireDamage);
        bool projectileLaunched = false;
        if (bulletPrefab != null)
        {
            projectileLaunched = SpawnProjectileVisual(target, outgoingDamage, AttackDamageTrack.Fire);
        }

        if (!projectileLaunched)
        {
            target.ReceiveDamage(outgoingDamage, firePenPct, firePenFlat, AttackDamageTrack.Fire, this);
        }
    }

    private void PerformBayonetAttack(UnitLogic target)
    {
        if (target == null || !target.IsAlive)
        {
            return;
        }

        float outgoingDamage = RollCriticalDamage(bayonetDamage);
        target.ReceiveDamage(outgoingDamage, bayonetPenPct, bayonetPenFlat, AttackDamageTrack.Bayonet, this);
        int backlashDamage = ApplyBayonetBacklash();
        if (backlashDamage > 0)
        {
            Debug.LogWarning("[白刃战触发] " + GetDisplayName() + " 发动了白刃攻击！反噬真实伤害: " + backlashDamage);
        }
    }

    private int ApplyBayonetBacklash()
    {
        if (string.IsNullOrWhiteSpace(runtimeBayonetCost))
        {
            return 0;
        }

        string cleanedCost = runtimeBayonetCost.Trim().TrimStart('[').TrimEnd(']');
        string[] parts = cleanedCost.Split(',');
        if (parts.Length < 2)
        {
            return 0;
        }

        if (!float.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float flatDamage))
        {
            flatDamage = 0f;
        }

        if (!float.TryParse(parts[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float percentDamage))
        {
            percentDamage = 0f;
        }

        float finalBacklashDamage = flatDamage + runtimeMaxHp * percentDamage;
        if (finalBacklashDamage <= 0f)
        {
            return 0;
        }

        int roundedBacklashDamage = Mathf.CeilToInt(finalBacklashDamage);
        runtimeCurrentHp -= roundedBacklashDamage;
        if (runtimeCurrentHp <= 0)
        {
            Die(null);
        }

        return roundedBacklashDamage;
    }

    private bool SpawnProjectileVisual(UnitLogic target, float outgoingDamage, AttackDamageTrack damageTrack)
    {
        Vector3 spawnPosition = firePoint != null ? firePoint.position : transform.position;
        GameObject projectileObject = Instantiate(bulletPrefab, spawnPosition, Quaternion.identity);
        ProjectileLaunchContext context = new ProjectileLaunchContext(this, target, outgoingDamage, fireSpeed, firePenPct, firePenFlat, damageTrack);
        return TryInvokeProjectileMethod(projectileObject, "Initialize", context)
            || TryInvokeProjectileMethod(projectileObject, "Setup", context)
            || TryInvokeProjectileMethod(projectileObject, "Launch", context);
    }

    private bool TryInvokeProjectileMethod(GameObject projectileObject, string methodName, ProjectileLaunchContext context)
    {
        if (projectileObject == null || context == null)
        {
            return false;
        }

        MonoBehaviour[] behaviours = projectileObject.GetComponents<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null)
            {
                continue;
            }

            System.Reflection.MethodInfo[] methods = behaviour.GetType().GetMethods(
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic);

            for (int methodIndex = 0; methodIndex < methods.Length; methodIndex++)
            {
                System.Reflection.MethodInfo method = methods[methodIndex];
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                {
                    continue;
                }

                object[] arguments = BuildProjectileArguments(method.GetParameters(), context);
                if (arguments == null)
                {
                    continue;
                }

                try
                {
                    method.Invoke(behaviour, arguments);
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[UnitLogic] Projectile " + methodName + " failed on " + behaviour.GetType().Name + ": " + ex.Message);
                    return false;
                }
            }
        }

        return false;
    }

    private object[] BuildProjectileArguments(System.Reflection.ParameterInfo[] parameters, ProjectileLaunchContext context)
    {
        if (parameters == null)
        {
            return null;
        }

        object[] arguments = new object[parameters.Length];
        int unitLogicIndex = 0;
        int numericIndex = 0;

        for (int i = 0; i < parameters.Length; i++)
        {
            Type parameterType = parameters[i].ParameterType;
            string parameterName = parameters[i].Name != null ? parameters[i].Name.ToLowerInvariant() : string.Empty;

            if (parameterType.IsAssignableFrom(typeof(ProjectileLaunchContext)))
            {
                arguments[i] = context;
                continue;
            }

            if (parameterType == typeof(UnitLogic))
            {
                if (parameterName.Contains("target"))
                {
                    arguments[i] = context.target;
                }
                else if (parameterName.Contains("attacker") || parameterName.Contains("owner") || parameterName.Contains("source") || parameterName.Contains("shooter"))
                {
                    arguments[i] = context.attacker;
                }
                else
                {
                    arguments[i] = unitLogicIndex == 0 ? context.attacker : context.target;
                }

                unitLogicIndex++;
                continue;
            }

            if (parameterType == typeof(DamageType))
            {
                arguments[i] = context.damageTrack == AttackDamageTrack.Bayonet ? DamageType.Bayonet : DamageType.Shooting;
                continue;
            }

            if (parameterType == typeof(AttackDamageTrack))
            {
                arguments[i] = context.damageTrack;
                continue;
            }

            if (parameterType == typeof(float))
            {
                arguments[i] = GetProjectileNumericArgument(parameterName, numericIndex, context);
                numericIndex++;
                continue;
            }

            if (parameterType == typeof(int))
            {
                arguments[i] = Mathf.RoundToInt(GetProjectileNumericArgument(parameterName, numericIndex, context));
                numericIndex++;
                continue;
            }

            return null;
        }

        return arguments;
    }

    private float GetProjectileNumericArgument(string parameterName, int numericIndex, ProjectileLaunchContext context)
    {
        if (parameterName.Contains("damage") || parameterName.Contains("dmg"))
        {
            return context.damage;
        }

        if (parameterName.Contains("speed") || parameterName.Contains("rate"))
        {
            return context.projectileSpeed;
        }

        if (parameterName.Contains("pct") || parameterName.Contains("percent"))
        {
            return context.penetrationPct;
        }

        if (parameterName.Contains("flat") || parameterName.Contains("pen"))
        {
            return context.penetrationFlat;
        }

        if (numericIndex == 0)
        {
            return context.damage;
        }

        if (numericIndex == 1)
        {
            return context.projectileSpeed;
        }

        if (numericIndex == 2)
        {
            return context.penetrationPct;
        }

        return context.penetrationFlat;
    }

    private float RollCriticalDamage(float baseDamage)
    {
        if (critRate <= 0f)
        {
            return baseDamage;
        }

        float normalizedCritRate = critRate > 1f ? critRate * 0.01f : critRate;
        if (UnityEngine.Random.value > normalizedCritRate)
        {
            return baseDamage;
        }

        return baseDamage * Mathf.Max(1f, critDamage);
    }

    private UnitLogic FindBestTarget()
    {
        return FindBestTarget(float.MaxValue);
    }

    private UnitLogic FindBestTargetInCurrentAttackRange()
    {
        return FindBestTarget(GetCurrentAttackRange());
    }

    private UnitLogic FindBestTarget(float maxDistance)
    {
        UnitLogic bestTarget = null;
        float bestScore = float.MinValue;
        for (int i = 0; i < activeUnits.Count; i++)
        {
            UnitLogic candidate = activeUnits[i];
            if (candidate == null || candidate == this || !candidate.IsAlive || !IsHostileTo(candidate) || !CanTargetUnitType(candidate))
            {
                continue;
            }

            float distance = Vector2.Distance(transform.position, candidate.transform.position);
            if (distance > maxDistance)
            {
                continue;
            }

            float score = faction == UnitFaction.Player && runtimePlayerDirective == PlayerDirective.PushLine
                ? -distance + UnityEngine.Random.Range(0f, 0.01f)
                : candidate.threatValue * 1000f - distance + UnityEngine.Random.Range(0f, 0.01f);
            if (score > bestScore)
            {
                bestScore = score;
                bestTarget = candidate;
            }
        }

        return bestTarget;
    }

    private float GetCurrentAttackRange()
    {
        return runtimeInBayonetMode ? bayonetRange : fireRange;
    }

    private float GetMovementAggroRange()
    {
        return Mathf.Max(fireRange, bayonetRange) + 1f;
    }

    private bool IsHostileTo(UnitLogic other)
    {
        if (other == null || faction == UnitFaction.Neutral || other.faction == UnitFaction.Neutral)
        {
            return false;
        }

        return faction != other.faction;
    }

    private bool CanTargetUnitType(UnitLogic candidate)
    {
        if (candidate == null)
        {
            return false;
        }

        if (runtimeAttackType == 1 && candidate.runtimeUnitType == 1)
        {
            return false;
        }

        if (runtimeAttackType == 2 && candidate.runtimeUnitType == 0)
        {
            return false;
        }

        return true;
    }

    private void TickCapturePointDirective()
    {
        Vector2Int captureGridPosition;
        Vector3 captureWorldPosition;
        if (TryGetNearestNeutralCapturePoint(out captureGridPosition, out captureWorldPosition)
            && Vector2.Distance(transform.position, captureWorldPosition) <= GridArrivalTolerance)
        {
            currentTarget = null;
            transform.position = captureWorldPosition;
            SetGridPosition(captureGridPosition);
            TickCaptureProgress();
            return;
        }

        UnitLogic movementTarget = currentTarget;
        if (!IsValidMovementAggroTarget(movementTarget))
        {
            movementTarget = FindBestTarget(GetMovementAggroRange());
            currentTarget = movementTarget;
            targetSearchCooldown = TargetSearchInterval;
        }

        if (movementTarget != null)
        {
            MoveToward(movementTarget.transform.position);
            return;
        }

        currentTarget = null;
        if (TryGetNearestNeutralCapturePoint(out captureGridPosition, out captureWorldPosition))
        {
            MoveToward(captureWorldPosition);
        }
    }

    private bool IsValidMovementAggroTarget(UnitLogic target)
    {
        if (target == null || !target.IsAlive || !IsHostileTo(target) || !CanTargetUnitType(target))
        {
            return false;
        }

        return Vector2.Distance(transform.position, target.transform.position) <= GetMovementAggroRange();
    }

    private bool TryGetNearestNeutralCapturePoint(out Vector2Int captureGridPosition, out Vector3 captureWorldPosition)
    {
        GridManager gridManager = GridManager.Instance;
        if (gridManager != null && gridManager.TryGetNearestNeutralCapturePoint(transform.position, out captureGridPosition))
        {
            captureWorldPosition = gridManager.GetBattlefieldWorldPosition(captureGridPosition, transform.position.z);
            return true;
        }

        captureGridPosition = BoardLayout.StrategicLineCenter;
        captureWorldPosition = new Vector3(captureGridPosition.x, captureGridPosition.y, transform.position.z);
        return BoardLayout.IsInsideBattlefield(captureGridPosition);
    }

    private void TickCaptureProgress()
    {
        if (GameFlowManager.Instance != null)
        {
            GameFlowManager.Instance.AddStrategicLineCapture(captureSpeed * Time.deltaTime);
        }
    }

    private void TickDirectiveMovement()
    {
        if (faction == UnitFaction.Player)
        {
            if (runtimePlayerDirective == PlayerDirective.CapturePoint)
            {
                TickCapturePointDirective();
                return;
            }

            UnitLogic nearestTarget = FindNearestTargetForMovement();
            if (nearestTarget != null)
            {
                MoveToward(nearestTarget.transform.position);
                return;
            }

            if (IsAtForwardBattlefieldEdge())
            {
                return;
            }

            MoveToward(transform.position + new Vector3(0f, 1f, 0f));
            return;
        }

        if (faction == UnitFaction.Enemy && IsAtForwardBattlefieldEdge())
        {
            return;
        }

        MoveToward(transform.position + new Vector3(0f, -1f, 0f));
    }

    private UnitLogic FindNearestTargetForMovement()
    {
        UnitLogic nearestTarget = null;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < activeUnits.Count; i++)
        {
            UnitLogic candidate = activeUnits[i];
            if (candidate == null || candidate == this || !candidate.IsAlive || !IsHostileTo(candidate) || !CanTargetUnitType(candidate))
            {
                continue;
            }

            float distance = Vector2.Distance(transform.position, candidate.transform.position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestTarget = candidate;
            }
        }

        return nearestTarget;
    }

    private void MoveToward(Vector3 targetPosition)
    {
        if (moveSpeed <= 0f)
        {
            return;
        }

        transform.position = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);
        ClampPositionToBattlefieldBounds();
    }

    private bool IsAtForwardBattlefieldEdge()
    {
        GetBattlefieldWorldBounds(out _, out _, out float minY, out float maxY);

        if (faction == UnitFaction.Player)
        {
            return transform.position.y >= maxY;
        }

        if (faction == UnitFaction.Enemy)
        {
            return transform.position.y <= minY;
        }

        return false;
    }

    private void ClampPositionToBattlefieldBounds()
    {
        GetBattlefieldWorldBounds(out float minX, out float maxX, out float minY, out float maxY);

        Vector3 clampedPosition = transform.position;
        clampedPosition.x = Mathf.Clamp(clampedPosition.x, minX, maxX);
        clampedPosition.y = Mathf.Clamp(clampedPosition.y, minY, maxY);
        transform.position = clampedPosition;
    }

    private void GetBattlefieldWorldBounds(out float minX, out float maxX, out float minY, out float maxY)
    {
        Vector3 minWorldPosition = new Vector3(0f, 0f, transform.position.z);
        Vector3 maxWorldPosition = new Vector3(
            BoardLayout.BattlefieldWidth - 1f,
            BoardLayout.BattlefieldHeight - 1f,
            transform.position.z);

        GridManager gridManager = GridManager.Instance;
        if (gridManager != null && gridManager.battlefieldContainer != null)
        {
            minWorldPosition = gridManager.GetBattlefieldWorldPosition(Vector2Int.zero, transform.position.z);
            maxWorldPosition = gridManager.GetBattlefieldWorldPosition(
                BoardLayout.BattlefieldMaxPosition,
                transform.position.z);
        }

        minX = Mathf.Min(minWorldPosition.x, maxWorldPosition.x);
        maxX = Mathf.Max(minWorldPosition.x, maxWorldPosition.x);
        minY = Mathf.Min(minWorldPosition.y, maxWorldPosition.y);
        maxY = Mathf.Max(minWorldPosition.y, maxWorldPosition.y);
    }

    private void EnterBayonetMode()
    {
        runtimeInBayonetMode = true;
        hasTriggeredBayonetContact = false;
        attackCooldown = 0f;
    }

    private void ApplyConfiguredUnitSprite()
    {
        if (unitDataConfig == null || unitDataConfig.unitSprite == null)
        {
            return;
        }

        SpriteRenderer unitModelSprite = GetComponentInChildren<SpriteRenderer>(true);
        if (unitModelSprite != null)
        {
            unitModelSprite.sprite = unitDataConfig.unitSprite;
        }
    }

    private void ResetCombatRuntimeOnly()
    {
        runtimeCurrentAmmo = runtimeMaxAmmo;
        runtimeInBayonetMode = runtimeMaxAmmo <= 0;
        attackCooldown = 0f;
        targetSearchCooldown = 0f;
        currentTarget = null;
        hasTriggeredBayonetContact = false;
        runtimeTemporaryShield = 0;
    }

    private void Die(UnitLogic attacker)
    {
        runtimeCurrentHp = 0;
        isCombatEnabled = false;
        currentTarget = null;

        UnitDied?.Invoke(this, attacker);
        if (runtimeCurrentHp > 0)
        {
            return;
        }

        if (attacker != null && attacker != this && attacker.IsAlive)
        {
            UnitKilled?.Invoke(attacker, this);
        }

        if (faction == UnitFaction.Player)
        {
            gameObject.SetActive(false);
            return;
        }

        Destroy(gameObject);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryTriggerFirstBayonetStrike(other != null ? other.GetComponent<UnitLogic>() : null);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        ResetBayonetContactIfLeaving(other != null ? other.GetComponent<UnitLogic>() : null);
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        TryTriggerFirstBayonetStrike(collision != null && collision.collider != null ? collision.collider.GetComponent<UnitLogic>() : null);
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        ResetBayonetContactIfLeaving(collision != null && collision.collider != null ? collision.collider.GetComponent<UnitLogic>() : null);
    }

    private void TryTriggerFirstBayonetStrike(UnitLogic other)
    {
        if (!runtimeInBayonetMode || hasTriggeredBayonetContact || other == null || !other.IsAlive || !IsHostileTo(other))
        {
            return;
        }

        currentTarget = other;
        PerformBayonetAttack(other);
        hasTriggeredBayonetContact = true;
        attackCooldown = GetAttackInterval(bayonetSpeed);
    }

    private void ResetBayonetContactIfLeaving(UnitLogic other)
    {
        if (other != null && other == currentTarget)
        {
            hasTriggeredBayonetContact = false;
        }
    }

    private float MultiplyAmpModifiers()
    {
        float result = 1f;
        foreach (float value in independentAmpModifiers.Values)
        {
            result *= 1f + value;
        }

        return Mathf.Max(0f, result);
    }

    private float MultiplyReductionModifiers()
    {
        float result = 1f;
        foreach (float value in independentReductionModifiers.Values)
        {
            result *= 1f - Mathf.Clamp01(value);
        }

        return Mathf.Max(0f, result);
    }

    private float SumFlatReductionModifiers()
    {
        float result = 0f;
        foreach (float value in flatReductionModifiers.Values)
        {
            result += Mathf.Max(0f, value);
        }

        return result;
    }

    private Dictionary<string, float> GetModifierBook(CombatModifierType modifierType)
    {
        switch (modifierType)
        {
            case CombatModifierType.IndependentAmp:
                return independentAmpModifiers;
            case CombatModifierType.IndependentReduction:
                return independentReductionModifiers;
            case CombatModifierType.FlatReduction:
                return flatReductionModifiers;
            default:
                return null;
        }
    }

    private bool TryParseModifierType(string modifierType, out CombatModifierType parsedType)
    {
        parsedType = CombatModifierType.IndependentAmp;
        if (string.IsNullOrWhiteSpace(modifierType))
        {
            return false;
        }

        string key = modifierType.Trim().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
        if (key == "amp" || key == "independentamp" || key == "independentampmodifiers")
        {
            parsedType = CombatModifierType.IndependentAmp;
            return true;
        }

        if (key == "reduction" || key == "independentreduction" || key == "independentreductionmodifiers")
        {
            parsedType = CombatModifierType.IndependentReduction;
            return true;
        }

        if (key == "flat" || key == "flatreduction" || key == "flatreductionmodifiers")
        {
            parsedType = CombatModifierType.FlatReduction;
            return true;
        }

        return Enum.TryParse(modifierType, true, out parsedType);
    }

    private string ReadEffectKind(object effect)
    {
        Type type = effect.GetType();
        object value = ReadMemberValue(type, effect, "effectType");
        if (value == null)
        {
            value = ReadMemberValue(type, effect, "type");
        }

        if (value == null)
        {
            value = ReadMemberValue(type, effect, "statType");
        }

        if (value == null)
        {
            value = ReadMemberValue(type, effect, "kind");
        }

        return value != null ? value.ToString() : string.Empty;
    }

    private float ReadEffectValue(object effect)
    {
        Type type = effect.GetType();
        object value = ReadMemberValue(type, effect, "value");
        if (value == null)
        {
            value = ReadMemberValue(type, effect, "amount");
        }

        if (value == null)
        {
            value = ReadMemberValue(type, effect, "effectValue");
        }

        if (value == null)
        {
            value = ReadMemberValue(type, effect, "modifierValue");
        }

        if (value == null)
        {
            return 0f;
        }

        try
        {
            return Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return 0f;
        }
    }

    private object ReadMemberValue(Type type, object instance, string memberName)
    {
        System.Reflection.FieldInfo field = type.GetField(memberName);
        if (field != null)
        {
            return field.GetValue(instance);
        }

        System.Reflection.PropertyInfo property = type.GetProperty(memberName);
        if (property != null && property.CanRead)
        {
            return property.GetValue(instance, null);
        }

        return null;
    }

    private bool TryConvertFloat(object value, out float result)
    {
        try
        {
            result = Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
            return true;
        }
        catch
        {
            result = 0f;
            return false;
        }
    }

    private float GetAttackInterval(float speed)
    {
        return 1f / Mathf.Max(0.01f, speed);
    }

    private bool ShouldPreviewConfig()
    {
        return !runtimeValuesInitialized && unitDataConfig != null;
    }

    private void SanitizeRuntimeValues()
    {
        runtimeUnitCost = Mathf.Max(0, runtimeUnitCost);
        runtimeUnitPrice = Mathf.Max(0, runtimeUnitPrice);
        runtimeUnitRare = Mathf.Max(0, runtimeUnitRare);
        runtimeUnitTier = Mathf.Max(1, runtimeUnitTier);
        runtimeMaxHp = Mathf.Max(1, runtimeMaxHp);
        runtimeCurrentHp = Mathf.Clamp(runtimeCurrentHp, 0, runtimeMaxHp);
        runtimeArmor = Mathf.Max(0, runtimeArmor);
        runtimeBayonetArmor = Mathf.Max(0, runtimeBayonetArmor);
        runtimeCritRate = Mathf.Max(0f, runtimeCritRate);
        runtimeCritDamage = Mathf.Max(0f, runtimeCritDamage);
        runtimeFireDamage = Mathf.Max(0, runtimeFireDamage);
        runtimeFireRate = Mathf.Max(0f, runtimeFireRate);
        runtimeFireSpeed = Mathf.Max(0.01f, runtimeFireSpeed);
        runtimeFireRange = Mathf.Max(0, runtimeFireRange);
        runtimeMaxAmmo = Mathf.Max(0, runtimeMaxAmmo);
        runtimeCurrentAmmo = Mathf.Clamp(runtimeCurrentAmmo, 0, runtimeMaxAmmo);
        runtimeAmmoSpeed = Mathf.Max(1, runtimeAmmoSpeed);
        runtimeFirePenPct = Mathf.Max(0f, runtimeFirePenPct);
        runtimeFirePenFlat = Mathf.Max(0f, runtimeFirePenFlat);
        runtimeDamageAoe = Mathf.Max(0, runtimeDamageAoe);
        runtimeBayonetDamage = Mathf.Max(0, runtimeBayonetDamage);
        runtimeBayonetSpeed = Mathf.Max(0.01f, runtimeBayonetSpeed);
        runtimeBayonetRange = Mathf.Max(0, runtimeBayonetRange);
        runtimeBayonetPenPct = Mathf.Max(0f, runtimeBayonetPenPct);
        runtimeBayonetPenFlat = Mathf.Max(0f, runtimeBayonetPenFlat);
        runtimeMoveSpeed = Mathf.Max(0f, runtimeMoveSpeed);
        runtimeCaptureSpeed = Mathf.Max(0f, runtimeCaptureSpeed);
        runtimeThreatValue = Mathf.Max(0, runtimeThreatValue);
    }

    private UnitFaction ConvertFaction(int value)
    {
        if (Enum.IsDefined(typeof(UnitFaction), value))
        {
            return (UnitFaction)value;
        }

        return UnitFaction.Neutral;
    }

    private PlayerDirective ConvertPlayerDirective(int value)
    {
        if (value == 1)
        {
            return PlayerDirective.CapturePoint;
        }

        return PlayerDirective.PushLine;
    }

    private static string StripStarSymbols(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Replace("★", string.Empty).Replace("*", string.Empty).Trim();
    }

    private static string StripTrailingTierDigit(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length == 1)
        {
            return value;
        }

        char last = value[value.Length - 1];
        return char.IsDigit(last) ? value.Substring(0, value.Length - 1) : value;
    }

    private UnitLogicDataSO FindNextStarDataConfig()
    {
        if (unitDataConfig == null || string.IsNullOrWhiteSpace(unitDataConfig.chessId))
        {
            return null;
        }

        int nextTier = Mathf.Max(1, unitDataConfig.unitTier + 1);
        string nextChessId = BuildNextTierChessId(unitDataConfig.chessId, unitDataConfig.unitTier, nextTier);
        if (string.IsNullOrWhiteSpace(nextChessId))
        {
            return null;
        }

        UnitLogicDataSO resourceConfig = Resources.Load<UnitLogicDataSO>("Units/DataAssets/" + nextChessId);
        if (resourceConfig != null)
        {
            return resourceConfig;
        }

        resourceConfig = Resources.Load<UnitLogicDataSO>("Settings/Units/DataAssets/" + nextChessId);
        if (resourceConfig != null)
        {
            return resourceConfig;
        }

#if UNITY_EDITOR
        string[] guids = UnityEditor.AssetDatabase.FindAssets(nextChessId + " t:UnitLogicDataSO");
        for (int i = 0; i < guids.Length; i++)
        {
            string assetPath = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[i]);
            UnitLogicDataSO editorConfig = UnityEditor.AssetDatabase.LoadAssetAtPath<UnitLogicDataSO>(assetPath);
            if (editorConfig != null && string.Equals(editorConfig.chessId, nextChessId, StringComparison.OrdinalIgnoreCase))
            {
                return editorConfig;
            }
        }
#endif

        return null;
    }

    private string BuildNextTierChessId(string currentChessId, int currentTier, int nextTier)
    {
        if (string.IsNullOrWhiteSpace(currentChessId))
        {
            return string.Empty;
        }

        string trimmed = currentChessId.Trim();
        string currentTierText = Mathf.Max(0, currentTier).ToString();
        if (trimmed.EndsWith(currentTierText, StringComparison.Ordinal))
        {
            return trimmed.Substring(0, trimmed.Length - currentTierText.Length) + nextTier;
        }

        char last = trimmed[trimmed.Length - 1];
        if (char.IsDigit(last))
        {
            return trimmed.Substring(0, trimmed.Length - 1) + nextTier;
        }

        return trimmed + nextTier;
    }

    [Serializable]
    public sealed class ProjectileLaunchContext
    {
        public UnitLogic attacker;
        public UnitLogic target;
        public float damage;
        public float projectileSpeed;
        public float penetrationPct;
        public float penetrationFlat;
        public AttackDamageTrack damageTrack;

        public ProjectileLaunchContext(UnitLogic attacker, UnitLogic target, float damage, float projectileSpeed, float penetrationPct, float penetrationFlat, AttackDamageTrack damageTrack)
        {
            this.attacker = attacker;
            this.target = target;
            this.damage = damage;
            this.projectileSpeed = projectileSpeed;
            this.penetrationPct = penetrationPct;
            this.penetrationFlat = penetrationFlat;
            this.damageTrack = damageTrack;
        }
    }
}
