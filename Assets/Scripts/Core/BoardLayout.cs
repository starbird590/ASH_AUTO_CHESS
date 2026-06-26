using UnityEngine;

/// <summary>
/// Shared board and reserve layout rules for the core game.
/// Keeps spatial rules separate from flow, spawning, dragging, and combat code.
/// </summary>
public static class BoardLayout
{
    public const int BattlefieldWidth = 5;
    public const int BattlefieldHeight = 5;
    public const int PlayerDeployMinY = 0;
    public const int PlayerDeployMaxY = 1;
    public const int StrategicLineY = 2;
    public const int EnemyNestY = BattlefieldHeight - 1;
    public const int ReserveSlotCount = 9;
    public const int ReserveY = 0;

    public static Vector2Int StrategicLineCenter => new Vector2Int(BattlefieldWidth / 2, StrategicLineY);
    public static Vector2Int EnemyNestCenter => new Vector2Int(BattlefieldWidth / 2, EnemyNestY);
    public static Vector2Int BattlefieldMaxPosition => new Vector2Int(BattlefieldWidth - 1, BattlefieldHeight - 1);

    public static bool IsInsideBattlefield(Vector2Int gridPosition)
    {
        return gridPosition.x >= 0
            && gridPosition.x < BattlefieldWidth
            && gridPosition.y >= 0
            && gridPosition.y < BattlefieldHeight;
    }

    public static bool IsInsidePlayerDeploymentArea(Vector2Int gridPosition)
    {
        return IsInsideBattlefield(gridPosition)
            && gridPosition.y >= PlayerDeployMinY
            && gridPosition.y <= PlayerDeployMaxY;
    }

    public static bool IsInsideReserve(Vector2Int reservePosition)
    {
        return reservePosition.x >= 0
            && reservePosition.x < ReserveSlotCount
            && reservePosition.y == ReserveY;
    }
}
