using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 2D 占点区：用 Trigger Collider2D 统计站在区域内的我方单位，并按单位当前 captureSpeed 累加推进进度。
/// </summary>
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(Rigidbody2D))]
public class CaptureZone : MonoBehaviour
{
    private const float MaxProgressValue = 100f;

    [Header("Capture Progress")]
    [SerializeField] private float currentProgress;
    [SerializeField] private bool isCaptured;

    // 按需求保留 Collider2D 列表，方便从 Inspector/调试器确认现在有哪些碰撞体在点内。
    private readonly List<Collider2D> playerColliders = new List<Collider2D>();
    private readonly List<UnitLogic> playerUnits = new List<UnitLogic>();
    private readonly Dictionary<Collider2D, UnitLogic> unitByCollider = new Dictionary<Collider2D, UnitLogic>();
    private readonly Dictionary<UnitLogic, int> colliderCountByUnit = new Dictionary<UnitLogic, int>();

    public float CurrentProgress => currentProgress;
    public float MaxProgress => MaxProgressValue;
    public bool IsCaptured => isCaptured;
    public IReadOnlyList<Collider2D> PlayerColliders => playerColliders;
    public IReadOnlyList<UnitLogic> PlayerUnits => playerUnits;

    private void Awake()
    {
        currentProgress = Mathf.Clamp(currentProgress, 0f, MaxProgressValue);

        Collider2D zoneCollider = GetComponent<Collider2D>();
        if (zoneCollider != null && !zoneCollider.isTrigger)
        {
            zoneCollider.isTrigger = true;
        }

        Rigidbody2D zoneBody = GetComponent<Rigidbody2D>();
        if (zoneBody != null)
        {
            zoneBody.bodyType = RigidbodyType2D.Kinematic;
            zoneBody.gravityScale = 0f;
        }
    }

    private void Update()
    {
        if (isCaptured)
        {
            return;
        }

        CleanupInvalidUnits();

        float totalCaptureSpeed = CalculateTotalCaptureSpeed();
        if (totalCaptureSpeed <= 0f)
        {
            return;
        }

        currentProgress = Mathf.Clamp(currentProgress + totalCaptureSpeed * Time.deltaTime, 0f, MaxProgressValue);
        if (currentProgress >= MaxProgressValue)
        {
            OnCaptureComplete();
        }
    }

    private void OnDisable()
    {
        ClearTrackedUnits();
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (isCaptured || other == null || unitByCollider.ContainsKey(other))
        {
            return;
        }

        UnitLogic unit = other.GetComponentInParent<UnitLogic>();
        if (!IsValidPlayerUnit(unit))
        {
            return;
        }

        unitByCollider.Add(other, unit);
        playerColliders.Add(other);

        int colliderCount;
        colliderCountByUnit.TryGetValue(unit, out colliderCount);
        colliderCountByUnit[unit] = colliderCount + 1;

        if (colliderCount == 0)
        {
            playerUnits.Add(unit);
        }
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        if (other == null)
        {
            return;
        }

        UnitLogic unit;
        if (!unitByCollider.TryGetValue(other, out unit))
        {
            return;
        }

        unitByCollider.Remove(other);
        playerColliders.Remove(other);

        RemoveUnitCollider(unit);
    }

    private void RemoveUnitCollider(UnitLogic unit)
    {
        if (unit == null)
        {
            return;
        }

        int colliderCount;
        if (!colliderCountByUnit.TryGetValue(unit, out colliderCount))
        {
            return;
        }

        colliderCount--;
        if (colliderCount > 0)
        {
            colliderCountByUnit[unit] = colliderCount;
            return;
        }

        colliderCountByUnit.Remove(unit);
        playerUnits.Remove(unit);
    }

    private float CalculateTotalCaptureSpeed()
    {
        float total = 0f;
        for (int i = 0; i < playerUnits.Count; i++)
        {
            UnitLogic unit = playerUnits[i];
            if (!IsValidPlayerUnit(unit))
            {
                continue;
            }

            total += unit.captureSpeed;
        }

        return total;
    }

    private void CleanupInvalidUnits()
    {
        for (int i = playerColliders.Count - 1; i >= 0; i--)
        {
            Collider2D trackedCollider = playerColliders[i];
            UnitLogic unit;
            if (trackedCollider != null
                && unitByCollider.TryGetValue(trackedCollider, out unit)
                && IsValidPlayerUnit(unit))
            {
                continue;
            }

            if (!ReferenceEquals(trackedCollider, null))
            {
                UnitLogic removedUnit;
                if (unitByCollider.TryGetValue(trackedCollider, out removedUnit))
                {
                    RemoveUnitCollider(removedUnit);
                }

                unitByCollider.Remove(trackedCollider);
            }

            playerColliders.RemoveAt(i);
        }

        for (int i = playerUnits.Count - 1; i >= 0; i--)
        {
            UnitLogic unit = playerUnits[i];
            if (IsValidPlayerUnit(unit) && colliderCountByUnit.ContainsKey(unit))
            {
                continue;
            }

            playerUnits.RemoveAt(i);
            if (unit != null)
            {
                colliderCountByUnit.Remove(unit);
            }

        }
    }

    private bool IsValidPlayerUnit(UnitLogic unit)
    {
        return unit != null
            && unit.faction == UnitFaction.Player
            && unit.IsAlive
            && unit.gameObject.activeInHierarchy;
    }

    private void ClearTrackedUnits()
    {
        for (int i = playerUnits.Count - 1; i >= 0; i--)
        {
            UnitLogic unit = playerUnits[i];
        }

        playerUnits.Clear();
        playerColliders.Clear();
        unitByCollider.Clear();
        colliderCountByUnit.Clear();
    }

    protected virtual void OnCaptureComplete()
    {
        if (isCaptured)
        {
            return;
        }

        currentProgress = MaxProgressValue;
        isCaptured = true;
        SwitchPlayerCaptureUnitsToPushLine();
        ClearTrackedUnits();
        Debug.Log("[CaptureZone] 占点完成: " + gameObject.name);
    }

    private void SwitchPlayerCaptureUnitsToPushLine()
    {
        IReadOnlyList<UnitLogic> activeUnits = UnitLogic.ActiveUnits;
        for (int i = 0; i < activeUnits.Count; i++)
        {
            UnitLogic unit = activeUnits[i];
            if (!IsValidPlayerUnit(unit) || unit.playerDirective != PlayerDirective.CapturePoint)
            {
                continue;
            }

            unit.SwitchToPushLineDirective();
        }
    }
}
