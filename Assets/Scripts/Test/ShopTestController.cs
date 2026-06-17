using UnityEngine;

public class ShopTestController : MonoBehaviour
{
    // 🌟 全局单例，方便小兵被点击时直接向主控台“报到”
    public static ShopTestController Instance { get; private set; }

    [Header("Runtime Selection")]
    [SerializeField] private UnitLogic currentlySelectedUnit; // 当前玩家用鼠标选中的小兵实例

    private void Awake()
    {
        Instance = this;
    }

    void Update()
    {
        // ⌨️ 大键盘 1~5 键购买商品
        if (Input.GetKeyDown(KeyCode.Alpha1)) BuySlot(0);
        if (Input.GetKeyDown(KeyCode.Alpha2)) BuySlot(1);
        if (Input.GetKeyDown(KeyCode.Alpha3)) BuySlot(2);
        if (Input.GetKeyDown(KeyCode.Alpha4)) BuySlot(3);
        if (Input.GetKeyDown(KeyCode.Alpha5)) BuySlot(4);

        if (Input.GetKeyDown(KeyCode.R)) TriggerRefresh();
        if (Input.GetKeyDown(KeyCode.L)) TriggerUpgrade();
    }

    // 鼠标点击流：商品购买、刷新、升级
    public void ClickBuySlot0() => BuySlot(0);
    public void ClickBuySlot1() => BuySlot(1);
    public void ClickBuySlot2() => BuySlot(2);
    public void ClickBuySlot3() => BuySlot(3);
    public void ClickBuySlot4() => BuySlot(4);

    public void TriggerRefresh()
    {
        if (ShopManager.Instance != null) ShopManager.Instance.RefreshShop();
    }

    public void TriggerUpgrade()
    {
        if (ShopManager.Instance != null) ShopManager.Instance.UpgradeLevel();
    }

    // ==========================================
    // 🎯 【重构】自由点选后勤交互流
    // ==========================================

    /// <summary>
    /// 公开方法：供小兵身上的点击脚本调用，用来切换当前选中的单位
    /// </summary>
    public void SelectUnit(UnitLogic unit)
    {
        currentlySelectedUnit = unit;
        if (unit != null)
        {
            Debug.Log($"<color=#00FFCC>[选择单位]</color> 你用鼠标选中了: <b>{unit.gameObject.name}</b> (血量:{unit.CurrentHp}/{unit.MaxHp})，现在可以对它执行后勤操作了！");
        }
    }

    /// <summary>
    /// UI点击：退役你当前选中的小兵
    /// </summary>
    public void TriggerRetireFirstUnit()
    {
        UnitLogic target = currentlySelectedUnit;
        if (ValidateTarget(target))
        {
            Debug.Log($"<color=yellow>[后勤执行]</color> 执行退役: [{target.gameObject.name}]...");
            ShopManager.Instance.RetireUnit(target);
            currentlySelectedUnit = null; // 退役后清空选择
        }
    }

    /// <summary>
    /// UI点击：修理你当前选中的小兵
    /// </summary>
    public void TriggerRepairFirstUnit()
    {
        UnitLogic target = currentlySelectedUnit;
        if (ValidateTarget(target))
        {
            Debug.Log($"<color=yellow>[后勤执行]</color> 执行修理: [{target.gameObject.name}]...");
            ShopManager.Instance.RepairUnit(target);
        }
    }

    /// <summary>
    /// UI点击：强行让你当前选中的小兵扣除30点血
    /// </summary>
    public void TriggerDamageFirstUnit()
    {
        UnitLogic target = currentlySelectedUnit;
        if (ValidateTarget(target))
        {
            target.SetCurrentHp(target.CurrentHp - 30);
            Debug.Log($"<color=red>[后勤执行]</color> 啪！强行让 [{target.gameObject.name}] 扣血30！当前状态: {target.CurrentHp}/{target.MaxHp}");
        }
    }

    // 安全检查门禁
    private bool ValidateTarget(UnitLogic target)
    {
        if (target == null)
        {
            Debug.LogWarning("<color=orange>[后勤拦截]</color> 你目前没有选中任何小兵！请先在场景/备战席里鼠标左键点击一个小兵。");
            return false;
        }

        if (GameFlowManager.Instance != null && GameFlowManager.Instance.CurrentState != GameState.Intermission)
        {
            Debug.LogWarning("<color=orange>[后勤拦截]</color> 只有整备阶段 Intermission 可以执行修理/退役。");
            return false;
        }

        if (target.faction != UnitFaction.Player)
        {
            currentlySelectedUnit = null;
            Debug.LogWarning("<color=orange>[后勤拦截]</color> 只能对玩家单位执行修理/退役。");
            return false;
        }

        // 0 HP 的玩家单位在整备阶段仍然是合法后勤对象，不能用 IsAlive 把它们挡掉。
        return true;
    }

    private void BuySlot(int slotIndex)
    {
        if (ShopManager.Instance != null)
        {
            ShopManager.Instance.BuyUnit(slotIndex);
        }
    }
}
