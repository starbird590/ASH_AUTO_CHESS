using UnityEngine;

/// <summary>
/// 双容器单位拖拽交互。
/// 使用 Unity 2D 的 OnMouseDown / OnMouseDrag / OnMouseUp，天然兼容鼠标点击与移动端触屏点击。
/// </summary>
[RequireComponent(typeof(UnitLogic))]
public class UnitDragHandler : MonoBehaviour
{
    [Header("Drag Settings")]
    [SerializeField] private int dragSortingOrderBoost = 1000;
    [SerializeField] private float reserveYOffsetTolerance = 0.5f;

    private UnitLogic unitLogic;
    private SpriteRenderer spriteRenderer;
    private Transform originalParent;
    private Vector3 originalLocalPosition;
    private int originalSortingOrder;
    private float originalWorldZ;
    private bool isDragging;

    private void Awake()
    {
        unitLogic = GetComponent<UnitLogic>();
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void OnMouseDown()
    {
        // 【位置 1】：在函数的第一行最开头插入
        Debug.Log("=== [TEST 1] 物理层打通：Unity 成功捕捉到了你的点击！ ===");

        GameFlowManager flowManager = GameFlowManager.Instance;
        if (flowManager == null)
        {
            Debug.LogWarning("=== [拦截] 逻辑层卡住：场景中找不到 GameFlowManager！ ===");
            return;
        }

        if (flowManager.CurrentState != GameState.Deployment)
        {
            Debug.Log("=== [拦截] 逻辑层卡住：当前不是 Deployment 阶段！当前实际是: " + flowManager.CurrentState + " ===");
            return;
        }

        // 老兵来自上一关幸存单位，部署阶段位置完全锁定，不能被拖拽改位。
        if (unitLogic == null || unitLogic.isVeteran)
        {
            // 【位置 3】：在老兵锁定的 return 之前插入
            Debug.Log("=== [拦截] 逻辑层卡住：该单位是老兵(isVeteran==true)，坐标已被强行锁死！ ===");
            return;
        }

        // 与核心状态机联动：只有整备阶段新购买、且部署阶段允许移动的单位才能拖。
        if (!flowManager.CanDragUnit(unitLogic))
       {
            Debug.Log("=== [TEST 2] 这个不是新买的单位 ===");
            return;
        }

        originalParent = transform.parent;
        originalLocalPosition = transform.localPosition;
        originalWorldZ = transform.position.z;
        isDragging = true;
        
        if (spriteRenderer != null)
        {
            originalSortingOrder = spriteRenderer.sortingOrder;
            spriteRenderer.sortingOrder = originalSortingOrder + dragSortingOrderBoost;
        }
    }

    private void OnMouseDrag()
    {
        if (!isDragging)
        {
            return;
        }

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            return;
        }

        Vector3 screenPos = Input.mousePosition;
        screenPos.z = Mathf.Abs(mainCamera.transform.position.z - originalWorldZ);

        Vector3 worldPos = mainCamera.ScreenToWorldPoint(screenPos);
        worldPos.z = originalWorldZ;
        transform.position = worldPos;
    }

    private void OnMouseUp()
    {
        if (!isDragging)
        {
            return;
        }

        isDragging = false;
        RestoreSortingOrder();

        GridManager gridManager = GridManager.Instance;
        if (gridManager == null)
        {
            BounceBack();
            return;
        }

        // 步骤 A：优先尝试将世界坐标投影到物理战场容器的本地空间。
        if (TryDropToBattlefield(gridManager))
        {
            
            return;
        }

        // 步骤 B：如果不在有效战场部署区，再尝试投影到备战席容器的本地空间。
        if (TryDropToReserve(gridManager))
        {
            return;
        }

        // 步骤 C：两个空间都不合法，直接回弹到拖拽前的父容器与本地坐标。
        BounceBack();
    }

   private bool TryDropToBattlefield(GridManager gridManager)
    {
        if (gridManager.battlefieldContainer == null)
        {
            //Debug.Log("<color=red>[战场拦截]</color> 找不到 battlefieldContainer 父容器，请检查 Inspector 连线！");
            return false;
        }

        Vector3 battlefieldLocalPos = gridManager.battlefieldContainer.InverseTransformPoint(transform.position);
        Vector2Int targetGridPos = new Vector2Int(
            Mathf.RoundToInt(battlefieldLocalPos.x),
            Mathf.RoundToInt(battlefieldLocalPos.y));

        // 【核心日志 1】
        //Debug.Log($"<color=yellow>[放手计算-战场]</color> 物理坐标转换成功！本地网格计算为: ({targetGridPos.x}, {targetGridPos.y})");

        // 只允许吸附到我方部署区：X 0~4，Y 0~1。
        if (!gridManager.IsInsidePlayerDeploymentArea(targetGridPos))
        {
            //Debug.Log($"<color=orange>[战场拦截]</color> 坐标 ({targetGridPos.x}, {targetGridPos.y}) 不在我方部署区范围(X:0~4, Y:0~1)内！");
            return false;
        }

        // 战场目标格已有其他单位时拒绝落位。
        if (gridManager.IsBattlefieldCellOccupied(targetGridPos, unitLogic))
        {
            //Debug.Log($"<color=orange>[战场拦截]</color> 网格 ({targetGridPos.x}, {targetGridPos.y}) 已经被占用，无法重叠落位！");
            return false;
        }

        ShopManager shopManager = ShopManager.Instance;
        if (shopManager == null)
        {
            Debug.LogWarning("<color=red>[上阵拦截]</color> 找不到 ShopManager，无法校验 Cost 上限！");
            BounceBack();
            return true;
        }

        // 只有“从备战区拖上战场”的瞬间才新增 Cost。
        // 如果单位原本就在战场内，只是在部署区内换格，不应该重复占用人口。
        bool wasOnBattlefield = originalParent == gridManager.battlefieldContainer;
        if (!wasOnBattlefield && !shopManager.CanDeployAdditionalCost(unitLogic.unitCost))
        {
            Debug.LogWarning("<color=orange>[上阵拦截]</color> Cost 上限不足！");
            BounceBack();
            return true;
        }

        //Debug.Log($"<color=green>[成功上阵]</color> 所有门禁通过！成功吸附到战场坐标: {targetGridPos}");
        gridManager.PlaceUnitOnBattlefield(unitLogic, targetGridPos);
        shopManager.RefreshCurrentTotalCost();
        return true;
    }

    private bool TryDropToReserve(GridManager gridManager)
    {
        if (gridManager.reserveContainer == null)
        {
            //Debug.Log("<color=red>[备战拦截]</color> 找不到 reserveContainer 父容器，请检查 Inspector 连线！");
            return false;
        }

        Vector3 reserveLocalPos = gridManager.reserveContainer.InverseTransformPoint(transform.position);
        Vector2Int targetReservePos = new Vector2Int(Mathf.RoundToInt(reserveLocalPos.x), 0);

        // 【核心日志 2】
        //Debug.Log($"<color=yellow>[放手计算-备战]</color> 本地 X 轴算得: {targetReservePos.x} | 局部 Y 轴真实位移为: {reserveLocalPos.y:F2}");

        // 备战席只有 X 0~8 的一行
        if (targetReservePos.x < 0 || targetReservePos.x >= GridManager.MaxReserveSlots)
        {
           // Debug.Log($"<color=orange>[备战拦截]</color> X 轴坐标 {targetReservePos.x} 越界！有效范围是 0~8。");
            return false;
        }

        // Y 偏离过大说明玩家没有真的放到备战席附近
        if (Mathf.Abs(reserveLocalPos.y) >= reserveYOffsetTolerance)
        {
           // Debug.Log($"<color=orange>[备战拦截]</color> Y 轴垂直偏离值 ({reserveLocalPos.y:F2}) 超过了容差值 {reserveYOffsetTolerance}，系统判定你只是路过没想放进备战席！");
            return false;
        }

        // 目标备战位已有其他单位时拒绝落座。
        if (gridManager.IsReserveSlotOccupied(targetReservePos, unitLogic))
        {
            //Debug.Log($"<color=orange>[备战拦截]</color> 备战槽位 X={targetReservePos.x} 已经被占用！");
            return false;
        }

       // Debug.Log($"<color=green>[成功落座备战席]</color> 所有门禁通过！成功吸附到备战席 X: {targetReservePos.x}");
        gridManager.PlaceUnitOnReserve(unitLogic, targetReservePos);
        if (ShopManager.Instance != null)
        {
            ShopManager.Instance.RefreshCurrentTotalCost();
        }

        return true;
    }

    private void BounceBack()
    {
        if (originalParent != null)
        {
            transform.SetParent(originalParent, false);
        }

        transform.localPosition = originalLocalPosition;

        GridManager gridManager = GridManager.Instance;
        if (gridManager != null)
        {
            gridManager.RefreshOccupancy();
        }

        if (ShopManager.Instance != null)
        {
            ShopManager.Instance.RefreshCurrentTotalCost();
        }
    }

    private void RestoreSortingOrder()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.sortingOrder = originalSortingOrder;
        }
    }
}
