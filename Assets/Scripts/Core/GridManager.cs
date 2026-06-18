using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 双容器网格管理器。
/// battlefieldContainer：5x5 物理战场，本地坐标严格对应 X:0~4, Y:0~4。
/// reserveContainer：9 格单排备战席，本地坐标严格对应 X:0~8, Y:0。
/// </summary>
public class GridManager : MonoBehaviour
{
    public static GridManager Instance { get; private set; }

    public const int MaxReserveSlots = 9;

    [Header("Grid Containers")]
    public Transform battlefieldContainer;
    public Transform reserveContainer;

    [Header("Neutral Capture Points")]
    [SerializeField] private bool useFullStrategicLineCapturePoints = true;
    [SerializeField] private List<Vector2Int> neutralCapturePoints = new List<Vector2Int>
    {
        new Vector2Int(0, GameFlowManager.StrategicLineY),
        new Vector2Int(1, GameFlowManager.StrategicLineY),
        new Vector2Int(2, GameFlowManager.StrategicLineY),
        new Vector2Int(3, GameFlowManager.StrategicLineY),
        new Vector2Int(4, GameFlowManager.StrategicLineY)
    };

    private readonly Dictionary<Vector2Int, UnitLogic> battlefieldOccupancy = new Dictionary<Vector2Int, UnitLogic>();
    private readonly Dictionary<Vector2Int, UnitLogic> reserveOccupancy = new Dictionary<Vector2Int, UnitLogic>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        RefreshOccupancy();
    }

    /// <summary>
    /// 整备阶段买人时调用：从左到右检查 9 个备战位，找到第一个空位。
    /// 如果全部占满，返回 false，上层商店逻辑应该拦截购买。
    /// </summary>
    public bool GetEmptyReserveSlot(out Vector2Int emptyPos)
    {
        RefreshOccupancy();

        for (int x = 0; x < MaxReserveSlots; x++)
        {
            Vector2Int checkPos = new Vector2Int(x, 0);
            if (!reserveOccupancy.ContainsKey(checkPos))
            {
                emptyPos = checkPos;
                return true;
            }
        }

        emptyPos = Vector2Int.zero;
        return false;
    }

    /// <summary>
    /// 买人成功后可直接调用此方法，把新单位吸附到第一个空备战位。
    /// </summary>
    public bool TryPlaceUnitToEmptyReserve(UnitLogic unit)
    {
        if (unit == null || reserveContainer == null)
        {
            return false;
        }

        Vector2Int emptyPos;
        if (!GetEmptyReserveSlot(out emptyPos))
        {
            return false;
        }

        PlaceUnitOnReserve(unit, emptyPos);
        return true;
    }

    /// <summary>
    /// 判断战场格是否被占用。
    /// ignoredUnit 用于拖拽中的自我忽略，避免单位放回原格时被自己挡住。
    /// </summary>
    public bool IsBattlefieldCellOccupied(Vector2Int gridPos, UnitLogic ignoredUnit)
    {
        RefreshOccupancy();
        UnitLogic occupiedUnit;
        return battlefieldOccupancy.TryGetValue(gridPos, out occupiedUnit)
            && occupiedUnit != null
            && occupiedUnit != ignoredUnit;
    }

    /// <summary>
    /// 判断备战席格是否被占用。
    /// ignoredUnit 用于拖拽中的自我忽略，避免单位放回原位时被自己挡住。
    /// </summary>
    public bool IsReserveSlotOccupied(Vector2Int reservePos, UnitLogic ignoredUnit)
    {
        RefreshOccupancy();
        UnitLogic occupiedUnit;
        return reserveOccupancy.TryGetValue(reservePos, out occupiedUnit)
            && occupiedUnit != null
            && occupiedUnit != ignoredUnit;
    }

    /// <summary>
    /// 将单位吸附到战场容器的指定本地整数坐标。
    /// 这里只负责坐标与父子关系，不负责部署阶段规则；规则由 UnitDragHandler / GameFlowManager 判断。
    /// </summary>
    public void PlaceUnitOnBattlefield(UnitLogic unit, Vector2Int gridPos)
    {
        if (unit == null || battlefieldContainer == null)
        {
            return;
        }

        Transform unitTransform = unit.transform;
        unitTransform.SetParent(battlefieldContainer, false);
        unitTransform.localPosition = new Vector3(gridPos.x, gridPos.y, 0f);
        unit.SetGridPosition(gridPos);
        RefreshOccupancy();
        NotifySynergyChanged();
    }

    /// <summary>
    /// 将单位吸附到备战席容器的指定本地整数坐标。
    /// 备战席只有一行，因此 Y 永远固定为 0。
    /// </summary>
    public void PlaceUnitOnReserve(UnitLogic unit, Vector2Int reservePos)
    {
        if (unit == null || reserveContainer == null)
        {
            return;
        }

        Transform unitTransform = unit.transform;
        unitTransform.SetParent(reserveContainer, false);
        unitTransform.localPosition = new Vector3(reservePos.x, 0f, 0f);
        unit.SetGridPosition(new Vector2Int(reservePos.x, 0));
        RefreshOccupancy();
        NotifySynergyChanged();
    }

    /// <summary>
    /// 根据两个容器的子物体本地坐标重建占用缓存。
    /// 这比手动维护更稳：拖拽、购买、退役后都能从场景层级恢复真实占用关系。
    /// </summary>
    public void RefreshOccupancy()
    {
        battlefieldOccupancy.Clear();
        reserveOccupancy.Clear();

        CacheContainerOccupancy(battlefieldContainer, battlefieldOccupancy, true);
        CacheContainerOccupancy(reserveContainer, reserveOccupancy, false);
    }

    private void CacheContainerOccupancy(
        Transform container,
        Dictionary<Vector2Int, UnitLogic> targetCache,
        bool isBattlefield)
    {
        if (container == null)
        {
            return;
        }

        for (int i = 0; i < container.childCount; i++)
        {
            Transform child = container.GetChild(i);
            UnitLogic unit = child.GetComponent<UnitLogic>();
            if (unit == null)
            {
                continue;
            }

            Vector3 localPos = child.localPosition;
            Vector2Int roundedPos = new Vector2Int(
                Mathf.RoundToInt(localPos.x),
                Mathf.RoundToInt(localPos.y));

            // 只缓存合法区域，防止临时拖拽或美术占位物污染格子占用。
            if (isBattlefield)
            {
                if (!IsInsideBattlefield(roundedPos))
                {
                    continue;
                }
            }
            else
            {
                if (!IsInsideReserve(roundedPos))
                {
                    continue;
                }
            }

            if (!targetCache.ContainsKey(roundedPos))
            {
                targetCache.Add(roundedPos, unit);
            }
        }
    }

    public bool IsInsideBattlefield(Vector2Int gridPos)
    {
        return gridPos.x >= 0
            && gridPos.x < GameFlowManager.BoardWidth
            && gridPos.y >= 0
            && gridPos.y < GameFlowManager.BoardHeight;
    }

    public bool IsInsidePlayerDeploymentArea(Vector2Int gridPos)
    {
        return gridPos.x >= 0
            && gridPos.x < GameFlowManager.BoardWidth
            && gridPos.y >= GameFlowManager.PlayerDeployMinY
            && gridPos.y <= GameFlowManager.PlayerDeployMaxY;
    }

    public bool IsInsideReserve(Vector2Int reservePos)
    {
        return reservePos.x >= 0
            && reservePos.x < MaxReserveSlots
            && reservePos.y == 0;
    }

    public bool TryGetNearestNeutralCapturePoint(Vector3 worldPosition, out Vector2Int capturePoint)
    {
        Vector3 battlefieldLocalPosition = WorldToBattlefieldLocalPosition(worldPosition);
        Vector2 currentGridPosition = new Vector2(battlefieldLocalPosition.x, battlefieldLocalPosition.y);
        bool foundPoint = false;
        float nearestDistance = float.MaxValue;
        capturePoint = default;

        if (useFullStrategicLineCapturePoints)
        {
            return TryGetNearestStrategicLineCapturePoint(currentGridPosition, out capturePoint);
        }

        if (neutralCapturePoints != null)
        {
            for (int i = 0; i < neutralCapturePoints.Count; i++)
            {
                Vector2Int candidate = neutralCapturePoints[i];
                if (!IsInsideBattlefield(candidate))
                {
                    continue;
                }

                float distance = Vector2.Distance(currentGridPosition, candidate);
                if (distance < nearestDistance)
                {
                    nearestDistance = distance;
                    capturePoint = candidate;
                    foundPoint = true;
                }
            }
        }

        if (foundPoint)
        {
            return true;
        }

        capturePoint = GetDefaultNeutralCapturePoint();
        return IsInsideBattlefield(capturePoint);
    }

    private bool TryGetNearestStrategicLineCapturePoint(Vector2 currentGridPosition, out Vector2Int capturePoint)
    {
        capturePoint = default;
        bool foundPoint = false;
        float nearestDistance = float.MaxValue;

        for (int x = 0; x < GameFlowManager.BoardWidth; x++)
        {
            Vector2Int candidate = new Vector2Int(x, GameFlowManager.StrategicLineY);
            if (!IsInsideBattlefield(candidate))
            {
                continue;
            }

            float distance = Vector2.Distance(currentGridPosition, candidate);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                capturePoint = candidate;
                foundPoint = true;
            }
        }

        return foundPoint;
    }

    public Vector3 GetBattlefieldWorldPosition(Vector2Int gridPos, float worldZ)
    {
        Vector3 localPosition = new Vector3(gridPos.x, gridPos.y, 0f);
        Vector3 worldPosition = battlefieldContainer != null
            ? battlefieldContainer.TransformPoint(localPosition)
            : localPosition;
        worldPosition.z = worldZ;
        return worldPosition;
    }

    private Vector3 WorldToBattlefieldLocalPosition(Vector3 worldPosition)
    {
        return battlefieldContainer != null
            ? battlefieldContainer.InverseTransformPoint(worldPosition)
            : worldPosition;
    }

    private Vector2Int GetDefaultNeutralCapturePoint()
    {
        return new Vector2Int(GameFlowManager.BoardWidth / 2, GameFlowManager.StrategicLineY);
    }

    private void NotifySynergyChanged()
    {
        if (SynergyManager.Instance != null)
        {
            SynergyManager.Instance.RecalculateSynergies();
        }
    }
}
