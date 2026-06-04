using UnityEngine;

/// <summary>
/// 挂在每个小兵预制体身上，负责把鼠标点击事件无缝上报给主控台。
/// </summary>
public class UnitClickTarget : MonoBehaviour
{
    private UnitLogic unitLogic;

    private void Awake()
    {
        unitLogic = GetComponent<UnitLogic>();
    }

    /// <summary>
    /// Unity自带的内置鼠标点击检测方法（当鼠标左键点击该物体的 Collider 时自动触发）
    /// </summary>
    private void OnMouseDown()
    {
        if (unitLogic != null && ShopTestController.Instance != null)
        {
            // 向主控台报到：我被点击了！
            ShopTestController.Instance.SelectUnit(unitLogic);
        }
    }
}
