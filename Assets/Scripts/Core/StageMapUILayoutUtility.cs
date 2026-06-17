using UnityEngine;

public static class StageMapUILayoutUtility
{
    public static Vector2 CalculateCenteredRowPosition(int itemIndex, int itemCount, float horizontalSpacing, float centerX, float y)
    {
        int safeCount = Mathf.Max(1, itemCount);
        float safeSpacing = Mathf.Max(0f, horizontalSpacing);
        float rowWidth = (safeCount - 1) * safeSpacing;
        float startX = centerX - rowWidth * 0.5f;
        return new Vector2(startX + itemIndex * safeSpacing, y);
    }
}
