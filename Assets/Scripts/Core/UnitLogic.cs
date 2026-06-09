using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 单位阵营。玩家单位与敌方单位会互相索敌；Neutral 预留给不可攻击物。
/// </summary>
public enum UnitFaction
{
    Player,
    Enemy,
    Neutral
}

/// <summary>
/// 玩家单位在战斗阶段的宏观指令倾向。
/// </summary>
public enum PlayerBattleDirective
{
    PushLine,
    CapturePoint
}

/// <summary>
/// 5x5 自走棋单位核心逻辑。
/// 统一承担属性数据、战斗 AI、三层索敌、弹道开火、触敌白刃、伤害结算与非对称死亡生命周期。
/// </summary>
public class UnitLogic : MonoBehaviour
{
    private static readonly List<UnitLogic> activeUnits = new List<UnitLogic>();

    [Header("基础身份")]
    [Tooltip("单位所属阵营：玩家、敌人或中立。战斗 AI 会根据阵营寻找敌对目标。")]
    public UnitFaction faction = UnitFaction.Player;

    [Tooltip("玩家单位的战斗宏观指令。敌方单位会忽略该字段。")]
    public PlayerBattleDirective playerDirective = PlayerBattleDirective.CapturePoint;

    [Header("羁绊 Trait")]
    [SerializeField, Tooltip("羁绊同名去重用身份标识。为空时使用单位物体名；同一兵种请填写相同标识。")]
    private string synergyIdentityOverride;

    [SerializeField, Tooltip("单位天生携带的基础羁绊。每个兵种建议至少配置两个，用于云顶式职业/种族组合。")]
    private List<TraitSO> baseTraits = new List<TraitSO>();

    private readonly List<TraitSO> runtimeTraits = new List<TraitSO>();

    [Header("经济属性")]
    [Tooltip("单位本身的购买原价，用于商店购买、退役返还和修理费用计算。")]
    public int unitPrice = 10;

    [Tooltip("该单位上阵占用的人口 Cost 值，例如步兵 1、坦克 3。")]
    public int unitCost = 1;

    [Tooltip("该单位的稀有度阶级。1=一阶卡，2=二阶卡，3=三阶卡。")]
    public int unitTier = 1;
    [Tooltip("单位当前星级。三张同名同星单位会自动融合为更高一星。")]
    public int starLevel = 1;

    [Header("星级独立数值矩阵")]
    [Tooltip("1星、2星、3星对应的最大生命值绝对值。")]
    public int[] hpStarValues = { 100, 180, 320 };
    [Tooltip("1星、2星、3星对应的常规远程伤害绝对值。")]
    public int[] damageStarValues = { 10, 18, 32 };
    [Tooltip("1星、2星、3星对应的白刃近战伤害绝对值。")]
    public int[] bayonetDamageStarValues = { 8, 14, 26 };

    [Header("生命属性")]
    [Tooltip("单位最大生命值。")]
    public int maxHp = 100;

    [Tooltip("单位当前生命值。玩家单位战后可以以 0HP 状态被收容复原。")]
    public int currentHp = 100;

    [Header("防御属性 Defense")]
    [Tooltip("护甲值。受到攻击时会从原始伤害中扣减，最终至少受到 1 点伤害。")]
    public int armor = 0;

    [Tooltip("是否为空中单位。True=飞行，False=地面。索敌时会被 canTargetAir/canTargetGround 限制。")]
    public bool isAirUnit = false;

    [Tooltip("实体拦截外壳。开启时白刃物理接触后会停止继续前倾，关闭时允许像幽灵一样继续穿透重叠。")]
    public bool hasSolidBodyShell = true;

    [Header("进攻属性 Offense")]
    [Tooltip("常规远程攻击的单次原始伤害。")]
    public int damage = 10;

    [Tooltip("常规远程攻速：每秒攻击次数。")]
    public float attackSpeed = 1f;

    [Tooltip("射程：以网格距离计算。白刃模式会强制把有效射程改写为 1。")]
    public int attackRange = 1;

    [Tooltip("是否为范围伤害。当前版本会对主目标造成伤害，该字段作为后续溅射接口保留。")]
    public bool isAoE = false;

    [Tooltip("白刃近战伤害值。玩家单位弹药耗尽后使用该数值挥刀。")]
    public int bayonetDamage = 8;

    [Tooltip("白刃近战攻速：每秒挥刀次数。例如 0.5 表示每 2 秒挥刀一次。")]
    public float bayonetAttackSpeed = 0.5f;

    [Header("特效表现 Visuals")]
    [Tooltip("常规远程攻击时发射的子弹投射物预制体，预制体必须挂载 BulletProjectile。")]
    public GameObject bulletPrefab;

    [Tooltip("子弹飞向目标的物理推进速度。")]
    public float bulletSpeed = 10f;

    [Tooltip("枪口发射点。为空时会使用单位自身 transform.position。")]
    public Transform firePoint;

    [Header("目标限制 Targeting Caps")]
    [Tooltip("能否攻击空中单位。")]
    public bool canTargetAir = true;

    [Tooltip("能否攻击地面单位。")]
    public bool canTargetGround = true;

    [Header("战术属性 Tactical")]
    [Tooltip("移动速度。按战场容器本地坐标推进，数值表示每秒移动多少个网格单位。")]
    public float moveSpeed = 2f;

    [Tooltip("占线速度。在 Y=2 战略线上累加占领进度的权重。")]
    public float captureSpeed = 10f;

    [Tooltip("威胁值/嘲讽值。索敌时优先攻击威胁值最高的目标。")]
    public int threatValue = 1;

    [Header("弹药与绝境白刃系统 (全阵营通用)")]
    [Tooltip("单位每进行一次常规远程开火射击所需要消耗的弹药数量")]
    public int ammoSpeed = 1;
    [Tooltip("玩家单位最大弹药量。战斗开始时 currentAmmo 会恢复到该值。")]
    public int maxAmmo = 6;

    [Tooltip("玩家单位当前弹药量。每次常规远程攻击消耗 1 点，归零后进入白刃。")]
    public int currentAmmo = 6;

    [Tooltip("是否处于白刃状态。白刃状态会把有效射程强制改写为 1。")]
    public bool isInBayonetMode = false;

    [Tooltip("白刃攻击时自身承受的固定反噬伤害。")]
    public int bayonetSelfDamage = 2;

    [Tooltip("白刃攻击时额外承受自身最大生命值百分比反噬。0.05 表示 5%。")]
    [Range(0f, 1f)]
    public float bayonetSelfDamagePercent = 0f;

    [Header("网格状态")]
    [SerializeField, Tooltip("单位当前网格坐标。战场与备战席都使用各自容器的本地整数坐标。")]
    private Vector2Int gridPosition;

    [SerializeField, Tooltip("单位当前是否拥有有效网格坐标。")]
    private bool hasGridPosition;

    [Header("运行时锁定状态")]
    [SerializeField, Tooltip("是否为上一关幸存或收容复原的老兵。老兵在部署阶段不可拖拽换位。")]
    private bool veteran;

    [SerializeField, Tooltip("是否锁定网格位置。战斗与整备阶段一般会锁定。")]
    private bool isPositionLocked;

    [SerializeField, Tooltip("是否启用战斗 AI。GameFlowManager 进入 Battle 后会自动打开。")]
    private bool isCombatEnabled;

    private UnitLogic lockedTarget;
    private float attackCooldownTimer;
    private int attackRangeBeforeBayonet;
    private bool hasAttackRangeBeforeBayonet;
    private bool hasTriggeredContactBayonetInCurrentCooldown;
    private bool hasContactedCurrentTarget;
    private bool deathProcessed;
    private bool hasCachedSynergyBaseStats;
    private int baseDamageForSynergy;
    private int baseArmorForSynergy;
    private float baseAttackSpeedForSynergy;

    public static IReadOnlyList<UnitLogic> ActiveUnits => activeUnits;

    public IReadOnlyList<TraitSO> BaseTraits => baseTraits;
    public IReadOnlyList<TraitSO> RuntimeTraits => runtimeTraits;
    public int UnitPrice => unitPrice;
    public int StarLevel => starLevel;
    public int MaxHp => maxHp;
    public int CurrentHp => currentHp;
    public Vector2Int GridPosition => gridPosition;
    public bool HasGridPosition => hasGridPosition;
    public bool isVeteran => veteran;
    public bool IsVeteran => veteran;
    public bool IsPositionLocked => isPositionLocked;
    public bool IsCombatEnabled => isCombatEnabled;
    public bool IsGroundUnit => !isAirUnit;
    public bool IsAlive => currentHp > 0;
    public int EffectiveAttackRange => isInBayonetMode ? 1 : Mathf.Max(0, attackRange);

    private void Awake()
    {
        CacheSynergyBaseStats();
        ResetRuntimeTraitsToBase();
    }

    private void OnEnable()
    {
        if (!activeUnits.Contains(this))
        {
            activeUnits.Add(this);
        }
    }

    private void OnDisable()
    {
        if (faction == UnitFaction.Player && deathProcessed && currentHp == 0)
        {
            return;
        }

        activeUnits.Remove(this);
    }

    private void OnDestroy()
    {
        activeUnits.Remove(this);
    }

    private void OnValidate()
    {
        ammoSpeed = Mathf.Max(1, ammoSpeed);
        unitPrice = Mathf.Max(0, unitPrice);
        unitCost = Mathf.Max(0, unitCost);
        unitTier = Mathf.Clamp(unitTier, 1, 3);
        starLevel = Mathf.Clamp(starLevel, 1, 3);
        EnsureStarValueArrays();
        maxHp = Mathf.Max(1, maxHp);
        currentHp = Mathf.Clamp(currentHp, 0, maxHp);
        armor = Mathf.Max(0, armor);
        damage = Mathf.Max(0, damage);
        attackSpeed = Mathf.Max(0.01f, attackSpeed);
        attackRange = Mathf.Max(0, attackRange);
        bayonetDamage = Mathf.Max(0, bayonetDamage);
        bayonetAttackSpeed = Mathf.Max(0.01f, bayonetAttackSpeed);
        bulletSpeed = Mathf.Max(0.01f, bulletSpeed);
        moveSpeed = Mathf.Max(0f, moveSpeed);
        captureSpeed = Mathf.Max(0f, captureSpeed);
        threatValue = Mathf.Max(0, threatValue);
        maxAmmo = Mathf.Max(0, maxAmmo);
        currentAmmo = Mathf.Clamp(currentAmmo, 0, maxAmmo);
        bayonetSelfDamage = Mathf.Max(0, bayonetSelfDamage);

        if (faction == UnitFaction.Player && CountConfiguredBaseTraits() < 2)
        {
            Debug.LogWarning("<color=#FFD700>[羁绊配置警告]</color> " + name + " 的初始基础羁绊少于 2 个，请至少配置两个 TraitSO。", this);
        }
    }

    private void Update()
    {
        if (!CanRunBattleAi())
        {
            return;
        }

        if (!isInBayonetMode)
        {
            SyncGridPositionFromTransform();
        }

        TickBattleAi(Time.deltaTime);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryTriggerFirstBayonetStrike(other.GetComponentInParent<UnitLogic>());
    }

    private void OnTriggerEnter(Collider other)
    {
        TryTriggerFirstBayonetStrike(other.GetComponentInParent<UnitLogic>());
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        ResetBayonetContactIfLeaving(other.GetComponentInParent<UnitLogic>());
    }

    private void OnTriggerExit(Collider other)
    {
        ResetBayonetContactIfLeaving(other.GetComponentInParent<UnitLogic>());
    }

    /// <summary>
    /// 战斗爆发瞬间由 GameFlowManager/EnemySpawnManager 调用，重置弹药、白刃状态与锁敌状态。
    /// </summary>
    public void ResetForBattle()
    {
        CacheSynergyBaseStats();
        ResetTemporarySynergyModifiers();
        ResetRuntimeTraitsToBase();

        if (hasAttackRangeBeforeBayonet)
        {
            attackRange = attackRangeBeforeBayonet;
            hasAttackRangeBeforeBayonet = false;
        }

        currentAmmo = maxAmmo;
        isInBayonetMode = false;
        attackCooldownTimer = 0f;
        hasTriggeredContactBayonetInCurrentCooldown = false;
        hasContactedCurrentTarget = false;
        lockedTarget = null;
        deathProcessed = currentHp <= 0;
    }

    public void SetFaction(UnitFaction value)
    {
        faction = value;
    }

    public void SetCurrentHp(int value)
    {
        currentHp = Mathf.Clamp(value, 0, maxHp);
        if (currentHp > 0)
        {
            deathProcessed = false;
        }
        else
        {
            HandleDeath(null);
        }
    }

    public void ReceiveDamage(int rawDamage, UnitLogic attacker)
    {
        if (!IsAlive)
        {
            return;
        }

        int finalDamage = Mathf.Max(1, rawDamage - armor);
        currentHp = Mathf.Clamp(currentHp - finalDamage, 0, maxHp);
        CombatLogManager.LogDamage(attacker, this, finalDamage);

        if (currentHp <= 0)
        {
            HandleDeath(attacker);
        }
    }

    public void SetGridPosition(Vector2Int value)
    {
        gridPosition = value;
        hasGridPosition = true;
    }

    public void ClearGridPosition()
    {
        gridPosition = Vector2Int.zero;
        hasGridPosition = false;
    }

    public void SetVeteran(bool value)
    {
        veteran = value;
    }

    public void SetPositionLocked(bool value)
    {
        isPositionLocked = value;
    }

    public void SetCombatEnabled(bool value)
    {
        isCombatEnabled = value;
        if (!value)
        {
            lockedTarget = null;
        }
    }

    public void ApplyStarUpgrade()
    {
        starLevel = Mathf.Clamp(starLevel + 1, 1, 3);
        ApplyStarLevelStats();
    }

    public void ApplyStarLevelStats()
    {
        EnsureStarValueArrays();
        int starIndex = Mathf.Clamp(starLevel, 1, 3) - 1;

        maxHp = hpStarValues[starIndex];
        currentHp = maxHp;
        damage = damageStarValues[starIndex];
        bayonetDamage = bayonetDamageStarValues[starIndex];

        CacheSynergyBaseStatsFromCurrentValues();
    }

    public bool AddRuntimeTrait(TraitSO trait)
    {
        if (trait == null || runtimeTraits.Contains(trait))
        {
            return false;
        }

        runtimeTraits.Add(trait);
        NotifyRuntimeTraitsChanged();
        return true;
    }

    public bool RemoveRuntimeTrait(TraitSO trait)
    {
        if (trait == null)
        {
            return false;
        }

        bool removed = runtimeTraits.Remove(trait);
        if (removed)
        {
            NotifyRuntimeTraitsChanged();
        }

        return removed;
    }

    public void ResetRuntimeTraitsToBase()
    {
        runtimeTraits.Clear();
        if (baseTraits == null)
        {
            return;
        }

        for (int i = 0; i < baseTraits.Count; i++)
        {
            TraitSO trait = baseTraits[i];
            if (trait != null && !runtimeTraits.Contains(trait))
            {
                runtimeTraits.Add(trait);
            }
        }
    }

    public void ResetTemporarySynergyModifiers()
    {
        CacheSynergyBaseStats();
        damage = baseDamageForSynergy;
        armor = baseArmorForSynergy;
        attackSpeed = baseAttackSpeedForSynergy;
    }

    public void ApplyTraitEffect(TraitEffect effect)
    {
        if (effect == null)
        {
            return;
        }

        CacheSynergyBaseStats();

        if (!Mathf.Approximately(effect.damageBonusPercent, 0f))
        {
            damage += Mathf.RoundToInt(baseDamageForSynergy * effect.damageBonusPercent * 0.01f);
        }

        armor += Mathf.Max(0, effect.armorBonus);

        if (effect.attackIntervalReduction > 0f)
        {
            float currentInterval = 1f / Mathf.Max(0.01f, attackSpeed);
            currentInterval = Mathf.Max(0.05f, currentInterval - effect.attackIntervalReduction);
            attackSpeed = 1f / currentInterval;
        }
    }

    public string GetSynergyIdentityKey()
    {
        if (!string.IsNullOrEmpty(synergyIdentityOverride))
        {
            return synergyIdentityOverride.Trim();
        }

        string displayName = GetDisplayName();
        if (string.IsNullOrEmpty(displayName))
        {
            return GetInstanceID().ToString();
        }

        return displayName.Trim();
    }

    private void CacheSynergyBaseStats()
    {
        if (hasCachedSynergyBaseStats)
        {
            return;
        }

        CacheSynergyBaseStatsFromCurrentValues();
    }

    private void CacheSynergyBaseStatsFromCurrentValues()
    {
        baseDamageForSynergy = damage;
        baseArmorForSynergy = armor;
        baseAttackSpeedForSynergy = Mathf.Max(0.01f, attackSpeed);
        hasCachedSynergyBaseStats = true;
    }

    private void EnsureStarValueArrays()
    {
        hpStarValues = EnsureFixedStarArray(hpStarValues, maxHp);
        damageStarValues = EnsureFixedStarArray(damageStarValues, damage);
        bayonetDamageStarValues = EnsureFixedStarArray(bayonetDamageStarValues, bayonetDamage);
    }

    private int[] EnsureFixedStarArray(int[] source, int fallbackValue)
    {
        int safeFallback = Mathf.Max(1, fallbackValue);
        int[] result = source;
        if (result == null || result.Length != 3)
        {
            int[] oldValues = result;
            result = new int[3] { safeFallback, safeFallback, safeFallback };
            if (oldValues != null)
            {
                int copyCount = Mathf.Min(oldValues.Length, result.Length);
                for (int i = 0; i < copyCount; i++)
                {
                    result[i] = oldValues[i];
                }
            }
        }

        for (int i = 0; i < result.Length; i++)
        {
            result[i] = Mathf.Max(1, result[i]);
        }

        return result;
    }

    private void NotifyRuntimeTraitsChanged()
    {
        if (!SynergyManager.HasInstance)
        {
            return;
        }

        if (GameFlowManager.Instance != null && GameFlowManager.Instance.CurrentState == GameState.Battle)
        {
            SynergyManager.Instance.RefreshAndApplySynergies();
        }
        else
        {
            SynergyManager.Instance.RecalculateSynergies();
        }
    }

    private int CountConfiguredBaseTraits()
    {
        if (baseTraits == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < baseTraits.Count; i++)
        {
            if (baseTraits[i] != null)
            {
                count++;
            }
        }

        return count;
    }

    private bool CanRunBattleAi()
    {
        GameFlowManager flowManager = GameFlowManager.Instance;
        return flowManager != null
            && flowManager.CurrentState == GameState.Battle
            && isCombatEnabled
            && IsAlive
            && faction != UnitFaction.Neutral
            && IsUnitOnBattlefield(this);
    }

    private void TickBattleAi(float deltaTime)
    {
        attackCooldownTimer -= deltaTime;
        if (attackCooldownTimer <= 0f)
        {
            hasTriggeredContactBayonetInCurrentCooldown = false;
        }

        if (!IsLockedTargetStillValid())
        {
            UnitLogic previousTarget = lockedTarget;
            lockedTarget = FindBestTargetInRange();
            if (lockedTarget == null || lockedTarget != previousTarget)
            {
                hasContactedCurrentTarget = false;
            }
        }

        if (lockedTarget != null)
        {
            TryAttackLockedTarget();
            if (isInBayonetMode || ShouldContinueCaptureWhileAttacking() || ShouldInterceptWhilePushing())
            {
                ExecuteMoveOrCapture(deltaTime);
            }
        }
        else
        {
            hasContactedCurrentTarget = false;
            ExecuteMoveOrCapture(deltaTime);
        }
    }

    private bool ShouldContinueCaptureWhileAttacking()
    {
        return faction == UnitFaction.Player
            && !isInBayonetMode
            && playerDirective == PlayerBattleDirective.CapturePoint
            && GameFlowManager.Instance != null
            && !GameFlowManager.Instance.IsStrategicLineCaptured
            && gridPosition.y >= GameFlowManager.StrategicLineY;
    }

    private bool ShouldInterceptWhilePushing()
    {
        return faction == UnitFaction.Player
            && !isInBayonetMode
            && lockedTarget != null
            && lockedTarget.IsAlive
            && (playerDirective == PlayerBattleDirective.PushLine
                || (GameFlowManager.Instance != null && GameFlowManager.Instance.IsStrategicLineCaptured));
    }

    private bool IsLockedTargetStillValid()
    {
        return lockedTarget != null
            && lockedTarget.IsAlive
            && IsLegalTarget(lockedTarget)
            && IsTargetInRange(lockedTarget);
    }

    private UnitLogic FindBestTargetInRange()
    {
        UnitLogic bestTarget = null;
        float bestDistance = float.MaxValue;
        int bestThreat = int.MinValue;
        int equalScoreCount = 0;

        for (int i = 0; i < activeUnits.Count; i++)
        {
            UnitLogic candidate = activeUnits[i];
            if (candidate == null || candidate == this || !candidate.IsAlive)
            {
                continue;
            }

            if (!IsLegalTarget(candidate) || !IsTargetInRange(candidate))
            {
                continue;
            }

            float distance = Vector2Int.Distance(gridPosition, candidate.GridPosition);
            bool betterThreat = candidate.threatValue > bestThreat;
            bool sameThreatCloser = candidate.threatValue == bestThreat && distance < bestDistance - 0.001f;
            bool exactTie = candidate.threatValue == bestThreat && Mathf.Abs(distance - bestDistance) <= 0.001f;

            if (betterThreat || sameThreatCloser)
            {
                bestTarget = candidate;
                bestThreat = candidate.threatValue;
                bestDistance = distance;
                equalScoreCount = 1;
            }
            else if (exactTie)
            {
                equalScoreCount++;
                if (Random.Range(0, equalScoreCount) == 0)
                {
                    bestTarget = candidate;
                }
            }
        }

        return bestTarget;
    }

    private bool IsLegalTarget(UnitLogic target)
    {
        if (target == null || target.faction == faction || target.faction == UnitFaction.Neutral)
        {
            return false;
        }

        if (!target.gameObject.activeInHierarchy || !IsUnitOnBattlefield(target))
        {
            return false;
        }

        if (target.isAirUnit && !canTargetAir)
        {
            return false;
        }

        if (!target.isAirUnit && !canTargetGround)
        {
            return false;
        }

        return target.HasGridPosition;
    }

    private bool IsTargetInRange(UnitLogic target)
    {
        return target != null
            && target.HasGridPosition
            && Vector2Int.Distance(gridPosition, target.GridPosition) <= EffectiveAttackRange + 0.001f;
    }

    private void TryAttackLockedTarget()
    {
        if (attackCooldownTimer > 0f || lockedTarget == null)
        {
            return;
        }

        if (isInBayonetMode)
        {
            if (!hasContactedCurrentTarget)
            {
                return;
            }

            ExecuteBayonetStrike(lockedTarget);
            attackCooldownTimer = 1f / Mathf.Max(0.01f, bayonetAttackSpeed);
            return;
        }

        UnitLogic targetForThisShot = lockedTarget;
        ConsumeAmmoAndUpdateBayonetState();

        FireBulletAtTarget(targetForThisShot);
        attackCooldownTimer = 1f / Mathf.Max(0.01f, attackSpeed);
    }

    private void FireBulletAtTarget(UnitLogic targetForThisShot)
    {
        if (bulletPrefab == null || targetForThisShot == null)
        {
            Debug.LogWarning("<color=orange>[弹道警告]</color> " + GetDisplayName() + " 缺少 bulletPrefab 或锁定目标，无法发射子弹。");
            return;
        }

        Vector3 spawnPosition = firePoint != null ? firePoint.position : transform.position;
        GameObject bulletObject = Instantiate(bulletPrefab, spawnPosition, Quaternion.identity);
        BulletProjectile projectile = bulletObject.GetComponent<BulletProjectile>();
        if (projectile == null)
        {
            Debug.LogError("<color=red>[弹道错误]</color> 子弹预制体缺少 BulletProjectile，已销毁该子弹实例。");
            Destroy(bulletObject);
            return;
        }

        projectile.Launch(this, targetForThisShot, damage, bulletSpeed);
    }

    private void ConsumeAmmoAndUpdateBayonetState()
    {
        if (maxAmmo <= 0)
        {
            EnterBayonetMode();
            return;
        }

        currentAmmo = Mathf.Max(0, currentAmmo - Mathf.Max(1, ammoSpeed));
        if (currentAmmo == 0 || currentAmmo < Mathf.Max(1, ammoSpeed))
        {
            EnterBayonetMode();
            lockedTarget = null;
        }
    }

    private void EnterBayonetMode()
    {
        if (!hasAttackRangeBeforeBayonet)
        {
            attackRangeBeforeBayonet = attackRange;
            hasAttackRangeBeforeBayonet = true;
        }

        if (!isInBayonetMode)
        {
            CombatLogManager.LogRaw("<color=#FFFF00>【警报】" + GetColoredDisplayName() + " 弹药用尽，拔出刺刀进入绝境白刃状态！</color>");
        }

        isInBayonetMode = true;
        hasTriggeredContactBayonetInCurrentCooldown = false;
        hasContactedCurrentTarget = false;
        attackRange = 1;
    }

    private void TryTriggerFirstBayonetStrike(UnitLogic target)
    {
        // 彻底解耦：只要自身处于白刃战状态且存活，物理碰撞到的目标只要不等于自身阵营且非中立，即视为合法敌对目标
        if (target == null
            || !isInBayonetMode
            || !IsAlive
            || target.faction == faction
            || target.faction == UnitFaction.Neutral
            || !target.IsAlive)
        {
            return;
        }

        lockedTarget = target;
        hasContactedCurrentTarget = true;

        if (hasTriggeredContactBayonetInCurrentCooldown)
        {
            return;
        }

        ExecuteBayonetStrike(target);
        attackCooldownTimer = 1f / Mathf.Max(0.01f, bayonetAttackSpeed);
        hasTriggeredContactBayonetInCurrentCooldown = true;
    }

    private void ResetBayonetContactIfLeaving(UnitLogic target)
    {
        if (target == null || target != lockedTarget)
        {
            return;
        }

        hasContactedCurrentTarget = false;
    }

    private void ExecuteBayonetStrike(UnitLogic target)
    {
        if (target == null || !target.IsAlive)
        {
            return;
        }

        target.ReceiveDamage(bayonetDamage, this);
        ApplyBayonetRecoilIfNeeded();
        CombatLogManager.LogRaw("<color=#FFFF00>【白刃】</color>" + GetColoredDisplayName() + " 对 " + target.GetColoredDisplayName() + " 发起近身肉搏！");
    }

    private void ApplyBayonetRecoilIfNeeded()
    {
        if (!isInBayonetMode || !IsAlive)
        {
            return;
        }

        int percentDamage = Mathf.CeilToInt(maxHp * bayonetSelfDamagePercent);
        int recoilDamage = Mathf.Max(0, bayonetSelfDamage + percentDamage);
        if (recoilDamage > 0)
        {
            currentHp = Mathf.Clamp(currentHp - recoilDamage, 0, maxHp);
            if (currentHp <= 0)
            {
                HandleDeath(this);
            }
        }
    }

    private void ExecuteMoveOrCapture(float deltaTime)
    {
        if (moveSpeed <= 0f)
        {
            return;
        }

        if (faction == UnitFaction.Player)
        {
            ExecutePlayerMovement(deltaTime);
        }
        else if (faction == UnitFaction.Enemy)
        {
            ExecuteEnemyMovement(deltaTime);
        }
    }

   private void ExecutePlayerMovement(float deltaTime)
{
    // 【核心修复】：在方法最顶端统一声明变量，防止作用域冲突
    UnitLogic nearestEnemy = null;

    if (isInBayonetMode)
    {
        if (lockedTarget != null && lockedTarget.IsAlive)
        {
            MoveTowardGridPosition(lockedTarget.GridPosition, deltaTime);
            return;
        }

        // 【修改点】：去掉前面的 UnitLogic 前缀，直接赋值
        nearestEnemy = FindNearestLivingEnemyUnit();
        if (nearestEnemy != null)
        {
            MoveTowardGridPosition(nearestEnemy.GridPosition, deltaTime);
            return;
        }

        MoveTowardGridPosition(new Vector2Int(GameFlowManager.BoardWidth / 2, GameFlowManager.EnemyNestY), deltaTime);
        return;
    }

    if (playerDirective == PlayerBattleDirective.CapturePoint)
    {
        if (GameFlowManager.Instance != null && GameFlowManager.Instance.IsStrategicLineCaptured)
        {
            playerDirective = PlayerBattleDirective.PushLine;
        }
        else
        {
            if (gridPosition.y < GameFlowManager.StrategicLineY)
            {
                MoveTowardGridPosition(new Vector2Int(gridPosition.x, GameFlowManager.StrategicLineY), deltaTime);
                return;
            }

            if (GameFlowManager.Instance != null)
            {
                GameFlowManager.Instance.AddStrategicLineCapture(captureSpeed * deltaTime);
            }

            return;
        }
    }

    if (lockedTarget != null && lockedTarget.IsAlive)
    {
        MoveTowardGridPosition(new Vector2Int(lockedTarget.GridPosition.x, gridPosition.y), deltaTime);
        return;
    }

    // 【修改点】：去掉前面的 UnitLogic 前缀，直接赋值
    nearestEnemy = FindNearestLivingEnemyUnit();
    if (nearestEnemy != null)
    {
        MoveTowardGridPosition(nearestEnemy.GridPosition, deltaTime);
        return;
    }

    MoveTowardGridPosition(new Vector2Int(gridPosition.x, GameFlowManager.EnemyNestY), deltaTime);
}

    private void ExecuteEnemyMovement(float deltaTime)
    {
        UnitLogic nearestPlayer = FindNearestLivingPlayerUnit();
        if (nearestPlayer != null)
        {
            MoveTowardGridPosition(nearestPlayer.GridPosition, deltaTime);
        }
        else
        {
            MoveTowardGridPosition(new Vector2Int(gridPosition.x, GameFlowManager.PlayerDeployMinY), deltaTime);
        }
    }

    private UnitLogic FindNearestLivingPlayerUnit()
    {
        UnitLogic nearest = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < activeUnits.Count; i++)
        {
            UnitLogic candidate = activeUnits[i];
            if (candidate == null || candidate.faction != UnitFaction.Player || !candidate.IsAlive || !candidate.HasGridPosition)
            {
                continue;
            }

            if (!candidate.gameObject.activeInHierarchy || !IsUnitOnBattlefield(candidate))
            {
                continue;
            }

            float distance = Vector2Int.Distance(gridPosition, candidate.GridPosition);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearest = candidate;
            }
        }

        return nearest;
    }

    private void MoveTowardGridPosition(Vector2Int targetGridPosition, float deltaTime)
    {
        GridManager gridManager = GridManager.Instance;
        if (gridManager == null || gridManager.battlefieldContainer == null || transform.parent != gridManager.battlefieldContainer)
        {
            return;
        }

        Vector2Int clampedTarget = ClampToBattlefield(targetGridPosition);
        Vector2Int nextCell = GetNextStepCell(clampedTarget);
        if (nextCell == gridPosition)
        {
            return;
        }

        if (IsBattlefieldCellBlocked(nextCell))
        {
            UnitLogic blocker = GetUnitAtBattlefieldCell(nextCell);
            if (blocker != null && isInBayonetMode && blocker.faction != faction && blocker.faction != UnitFaction.Neutral && blocker.IsAlive)
            {
                MoveTowardBayonetContact(blocker, deltaTime);
                return;
            }

            if (blocker != null && blocker.faction == faction)
            {
                Vector2Int alternateCell = GetAlternateStepCell(clampedTarget, nextCell);
                if (alternateCell != gridPosition)
                {
                    UnitLogic alternateBlocker = GetUnitAtBattlefieldCell(alternateCell);
                    if (alternateBlocker == null)
                    {
                        MoveToBattlefieldCell(alternateCell, deltaTime);
                        return;
                    }

                    if (alternateBlocker.faction != faction && isInBayonetMode && alternateBlocker.faction != UnitFaction.Neutral && alternateBlocker.IsAlive)
                    {
                        MoveTowardBayonetContact(alternateBlocker, deltaTime);
                        return;
                    }
                }
            }

            return;
        }

        MoveToBattlefieldCell(nextCell, deltaTime);
    }

    private void MoveToBattlefieldCell(Vector2Int targetCell, float deltaTime)
    {
        Vector3 targetLocalPosition = new Vector3(targetCell.x, targetCell.y, transform.localPosition.z);
        transform.localPosition = Vector3.MoveTowards(transform.localPosition, targetLocalPosition, moveSpeed * deltaTime);
        SyncGridPositionFromTransform();
    }

    private void MoveTowardBayonetContact(UnitLogic blocker, float deltaTime)
    {
        if (blocker == null || !blocker.IsAlive || blocker.faction == faction || blocker.faction == UnitFaction.Neutral)
        {
            return;
        }

        Vector2Int logicalGridPosition = gridPosition;
        if (hasSolidBodyShell && hasContactedCurrentTarget && lockedTarget == blocker)
        {
            SetGridPosition(logicalGridPosition);
            return;
        }

        Vector3 blockerLocalPosition = blocker.transform.localPosition;
        blockerLocalPosition.z = transform.localPosition.z;

        transform.localPosition = Vector3.MoveTowards(transform.localPosition, blockerLocalPosition, moveSpeed * deltaTime);

        // 前倾突刺只是为了让 Collider 发生真实重叠，不能让逻辑网格被局部坐标四舍五入污染。
        SetGridPosition(logicalGridPosition);
        SetGridPosition(logicalGridPosition);
    }

    private UnitLogic FindNearestLivingEnemyUnit()
    {
        UnitLogic nearestEnemy = null;
        float bestDistance = float.MaxValue;
        IReadOnlyList<UnitLogic> units = ActiveUnits;
        for (int i = 0; i < units.Count; i++)
        {
            UnitLogic candidate = units[i];
            if (candidate == null || candidate == this || candidate.faction != UnitFaction.Enemy || !candidate.IsAlive)
            {
                continue;
            }

            if (!candidate.gameObject.activeInHierarchy || !candidate.HasGridPosition || !IsUnitOnBattlefield(candidate))
            {
                continue;
            }

            float distance = Vector2Int.Distance(gridPosition, candidate.GridPosition);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                nearestEnemy = candidate;
            }
        }

        return nearestEnemy;
    }

    private Vector2Int GetNextStepCell(Vector2Int targetGridPosition)
    {
        Vector2Int delta = targetGridPosition - gridPosition;
        if (delta == Vector2Int.zero)
        {
            return gridPosition;
        }

        if (Mathf.Abs(delta.y) >= Mathf.Abs(delta.x))
        {
            return ClampToBattlefield(new Vector2Int(gridPosition.x, gridPosition.y + IntegerSign(delta.y)));
        }

        return ClampToBattlefield(new Vector2Int(gridPosition.x + IntegerSign(delta.x), gridPosition.y));
    }

    private Vector2Int GetAlternateStepCell(Vector2Int targetGridPosition, Vector2Int primaryCell)
    {
        Vector2Int delta = targetGridPosition - gridPosition;
        if (delta == Vector2Int.zero)
        {
            return gridPosition;
        }

        bool primaryIsVertical = primaryCell.x == gridPosition.x && primaryCell.y != gridPosition.y;
        if (primaryIsVertical)
        {
            if (delta.x == 0)
            {
                return gridPosition;
            }

            return ClampToBattlefield(new Vector2Int(gridPosition.x + IntegerSign(delta.x), gridPosition.y));
        }

        if (delta.y == 0)
        {
            return gridPosition;
        }

        return ClampToBattlefield(new Vector2Int(gridPosition.x, gridPosition.y + IntegerSign(delta.y)));
    }

    private int IntegerSign(int value)
    {
        if (value > 0)
        {
            return 1;
        }

        if (value < 0)
        {
            return -1;
        }

        return 0;
    }

    private bool IsBattlefieldCellBlocked(Vector2Int targetCell)
    {
        return GetUnitAtBattlefieldCell(targetCell) != null;
    }

    private UnitLogic GetUnitAtBattlefieldCell(Vector2Int targetCell)
    {
        for (int i = 0; i < activeUnits.Count; i++)
        {
            UnitLogic unit = activeUnits[i];
            if (unit == null || unit == this || !unit.IsAlive || !unit.HasGridPosition)
            {
                continue;
            }

            if (IsUnitOnBattlefield(unit) && unit.GridPosition == targetCell)
            {
                return unit;
            }
        }

        return null;
    }

    private Vector2Int ClampToBattlefield(Vector2Int value)
    {
        return new Vector2Int(
            Mathf.Clamp(value.x, 0, GameFlowManager.BoardWidth - 1),
            Mathf.Clamp(value.y, 0, GameFlowManager.BoardHeight - 1));
    }

    private void SyncGridPositionFromTransform()
    {
        GridManager gridManager = GridManager.Instance;
        if (gridManager != null && gridManager.battlefieldContainer != null && transform.parent == gridManager.battlefieldContainer)
        {
            Vector3 localPos = transform.localPosition;
            SetGridPosition(new Vector2Int(Mathf.RoundToInt(localPos.x), Mathf.RoundToInt(localPos.y)));
        }
    }

    private bool IsUnitOnBattlefield(UnitLogic unit)
    {
        GridManager gridManager = GridManager.Instance;
        return unit != null
            && gridManager != null
            && gridManager.battlefieldContainer != null
            && unit.transform.parent == gridManager.battlefieldContainer;
    }

    private void HandleDeath(UnitLogic killer)
    {
        if (deathProcessed)
        {
            return;
        }

        CombatLogManager.LogDeath(this);

        currentHp = 0;
        deathProcessed = true;
        lockedTarget = null;
        SetCombatEnabled(false);

        if (faction == UnitFaction.Enemy)
        {
            Destroy(gameObject);
            return;
        }

        if (faction == UnitFaction.Player)
        {
            gameObject.SetActive(false);
        }
    }

    public string GetDisplayName()
    {
        return string.IsNullOrEmpty(gameObject.name) ? "单位" : gameObject.name.Replace("(Clone)", string.Empty);
    }

    public string GetColoredDisplayName()
    {
        string color = faction == UnitFaction.Player ? "#00FFCC" : "#FF3333";
        return "<color=" + color + ">" + GetDisplayName() + "</color>";
    }
}
