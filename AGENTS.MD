# 项目协作说明

## 用户偏好

- 用户叫星鸟。
- 星鸟是代码小白，解释代码时要尽量用大白话，不要默认对方懂 Unity/C# 黑话。
- 每次写完代码后，如果改动涉及 Unity 配置，必须明确告诉星鸟要在 Unity 里怎么配置，例如挂哪个脚本、拖哪个引用、改哪个 Inspector 字段、Prefab 是否要 Apply、场景是否要保存。
- 如果配置只能在 Unity 编辑器里手动完成，要直接说明“这一步我不能替你点，需要你在 Unity 里这样做”，不要假装已经完成。
- 修改项目前，先读当前项目结构和相关脚本；不要凭空改。

## 项目概况

- 这是一个 Unity 2D 自走棋项目，项目名是 `ASH_AUTO_CHESS`。
- 当前构建入口场景是 `Assets/Scenes/SampleScene.unity`。
- 项目主要玩法是“商店买单位 -> 部署到棋盘 -> 自动战斗 -> 结算/推进关卡”。
- 当前核心棋盘是 5x5：
  - 战场大小：X=0~4，Y=0~4。
  - 玩家部署区：Y=0~1。
  - 战略线：Y=2。
  - 敌方/母巢区域：Y=4。
- 备战席是单排 9 格：X=0~8，Y=0。
- 项目使用 Unity 传统 `uGUI` / `Text` 组件，不是 UI Toolkit。
- 已安装主要包包括 Unity 2D、TextMeshPro、Timeline、Visual Scripting、Test Framework、uGUI。

## 当前主要目录

- `Assets/Scripts/Core`：核心玩法脚本。
- `Assets/Scripts/Test`：商店测试和临时 UI 控制脚本。
- `Assets/Scenes/SampleScene.unity`：当前唯一启用场景。
- `Assets/Prefab/Chacter`：角色/单位预制体，目前有鼠式、预兆、青金石、长石、虎式、母巢等。
- `Assets/Prefab/Bullet`：我方和敌方子弹预制体。
- `Assets/Prefab/UI`：UI 预制体，目前包含 `SynergyItem_Prefab.prefab`。
- `Assets/TraitSO`：羁绊 ScriptableObject 资产，例如黑十字帝国学联、重装甲。
- `Assets/MapNode`：地图节点 ScriptableObject 资产，例如 `0.asset`、`1.asset`。
- `ProjectSettings`：Unity 项目设置。
- `Packages/manifest.json`：Unity 包依赖。

## 当前 Git 状态注意

最近一次扫描时，以下文件处于已修改或新增状态，改代码时不要误覆盖星鸟已有改动：

- 已修改：`Assets/MapNode/0.asset`
- 已修改：`Assets/MapNode/1.asset`
- 已修改：`Assets/Prefab/Chacter/长石.prefab`
- 已修改：`Assets/Prefab/Chacter/青金石.prefab`
- 已修改：`Assets/Scenes/SampleScene.unity`
- 已修改：`Assets/Scripts/Core/GameFlowManager.cs`
- 已修改：`Assets/Scripts/Core/ShopManager.cs`
- 已修改：`Assets/Scripts/Core/StageMapManager.cs`
- 已修改：`Assets/Scripts/Core/SynergyManager.cs`
- 已修改：`Assets/Scripts/Core/UnitDragHandler.cs`
- 已修改：`Assets/Scripts/Core/UnitLogic.cs`
- 新增：`Assets/Scripts/Core/LogisticsPanelUI.cs`
- 新增：`Assets/Scripts/Core/LogisticsPanelUI.cs.meta`

## 游戏主流程

核心状态机在 `Assets/Scripts/Core/GameFlowManager.cs`。

流程顺序是：

1. `Result`：结算阶段。
2. `Intermission`：整备阶段，可以开商店、买单位、刷新、升级、修理、退役。
3. `Deployment`：部署阶段，可以把本轮新买的单位从备战席拖到玩家部署区。
4. `Battle`：战斗阶段，单位自动战斗。

重要规则：

- `GameFlowManager` 是跨场景单例，会 `DontDestroyOnLoad`。
- 金钱、人口上限、当前关卡编号通过 `PlayerPrefs` 保存。
- 战斗开始时会记录玩家单位的部署位置。
- 战斗结束后，幸存玩家单位会回到开战前的位置。
- 老兵单位会被锁住，部署阶段不能随便换位置。
- 新买单位只在本轮部署阶段允许拖拽。
- 战斗胜利会推进关卡编号。
- 商店资金、人口上限等全局数据要优先考虑和 `GameFlowManager` 同步，不能只改 `ShopManager`。

## 关卡地图系统

相关文件：

- `Assets/Scripts/Core/StageMapManager.cs`
- `Assets/Scripts/Core/StageMapUIView.cs`
- `Assets/Scripts/Core/MapNodeSO.cs`
- `Assets/Scripts/Core/MapNodeButtonUI.cs`
- `Assets/MapNode/0.asset`
- `Assets/MapNode/1.asset`

从文件结构判断，项目已经开始加入“关卡地图/节点选择”系统：

- `MapNodeSO` 应该是地图节点数据资产，用来保存某个关卡节点的信息。
- `StageMapManager` 应该负责当前关卡地图数据、节点选择、推进或进入关卡。
- `StageMapUIView` 应该负责把地图节点显示到 UI 上。
- `MapNodeButtonUI` 应该是单个地图节点按钮的 UI 控制脚本。
- `Assets/MapNode` 里的 `.asset` 是具体地图节点配置。

Unity 配置重点：

- 如果新增地图节点，需要创建或复制 `MapNodeSO` 资产，并放到 `Assets/MapNode`。
- 如果地图 UI 没显示节点，要检查 `StageMapManager` / `StageMapUIView` 的 Inspector 引用是否绑定了节点列表、按钮预制体、按钮父物体。
- 如果点击节点没反应，要检查 `MapNodeButtonUI` 的 Button 事件和它绑定的节点数据。

## 棋盘和拖拽

相关脚本：

- `GridManager.cs`
- `UnitDragHandler.cs`

核心理解：

- `GridManager` 管两个容器：
  - `battlefieldContainer`：5x5 战场。
  - `reserveContainer`：9 格备战席。
- 单位的位置靠父物体和 `localPosition` 表示，本地整数坐标就是棋盘格。
- 拖拽用 Unity 的 `OnMouseDown / OnMouseDrag / OnMouseUp`，所以单位必须有能接收点击的 Collider。
- 上战场时会检查：
  - 当前是否是 `Deployment`。
  - 单位是否是本轮新买的。
  - 目标格是否在玩家部署区。
  - 目标格是否已占用。
  - 上阵后 Cost 是否超过当前人口上限。
- 放回备战席时会检查 X 是否在 0~8，Y 偏移是否在容差内，目标备战格是否被占用。

## 单位战斗

核心脚本是 `Assets/Scripts/Core/UnitLogic.cs`。

单位重要字段：

- `faction`：阵营，玩家/敌人/中立。
- `playerDirective`：玩家单位战斗指令，推线或占点。
- `unitPrice`：价格。
- `unitCost`：占用人口。
- `unitTier`：商店阶级，1~3 阶。
- `maxHp/currentHp`：生命值。
- `armor`：护甲，伤害至少为 1。
- `damage/attackSpeed/attackRange`：远程攻击。
- `maxAmmo/currentAmmo`：玩家单位弹药。
- `isInBayonetMode`：弹药耗尽后进入白刃。
- `baseTraits`：单位基础羁绊。
- `synergyIdentityOverride`：同名去重用的身份标识。

战斗逻辑：

- `UnitLogic.ActiveUnits` 记录当前活跃单位。
- 战斗 AI 只在 `GameState.Battle` 且 `isCombatEnabled=true` 时运行。
- 单位会按威胁值、距离、随机平局来选目标。
- 子弹由 `BulletProjectile` 追踪发射时指定的目标，不会中途换目标。
- 玩家单位弹药耗尽后进入白刃，攻击范围强制变成 1。
- 白刃可以通过碰撞触发第一次近身攻击。
- 敌人死亡会 `Destroy(gameObject)`。
- 玩家死亡会 `SetActive(false)`，方便战后收容/修理。

## 敌人生成

核心脚本是 `Assets/Scripts/Core/EnemySpawnManager.cs`。

它负责：

- `Deployment` 阶段显示敌军预警投影。
- `Battle` 阶段生成真实初始敌人。
- 生成母巢 Boss。
- 战斗中按时间间隔从母巢周期出兵。
- 战斗越久，出兵间隔越短，但不会低于最小间隔。

Unity 配置重点：

- `warningProjectionPrefab`：部署期预警用预制体。
- `initialEnemyConfigs`：本关初始敌人和坐标。
- `hiveBossPrefab`：母巢 Boss 预制体，必须有 `UnitLogic`。
- `hiveEnemyPool`：母巢周期出兵卡池，里面的预制体也必须有 `UnitLogic`。
- 所有敌人坐标必须在 5x5 战场内。

## 商店、经济和后勤

核心脚本：

- `Assets/Scripts/Core/ShopManager.cs`
- `Assets/Scripts/Core/LogisticsPanelUI.cs`

商店负责：

- 初始资金。
- 5 个商店货架。
- 刷新商店。
- 升级商店等级。
- 按商店等级和阶级概率抽卡。
- 买单位并放到备战席。
- 退役单位。
- 修理单位。
- 统计当前上阵 Cost。

重要规则：

- 商店操作通常只允许在 `Intermission`。
- 购买前会检查货架、资金、备战席空位。
- 买到的单位会先实例化，再通过 `GridManager.PlaceUnitOnReserve` 放入备战席。
- 购买成功后会调用 `GameFlowManager.RegisterPurchasedUnitFromShop` 登记新单位。
- 商店资金和人口上限会同步到 `GameFlowManager`，`GameFlowManager` 才是跨关保存的“大管家”。
- 商店等级上限是 5 级。
- 默认人口/Cost 上限数组是 5、7、9、11、13。
- 当前上阵 Cost 通过扫描 `battlefieldContainer` 子物体统计。

后勤系统：

- 当前项目新增了 `LogisticsPanelUI.cs`，从名字判断它负责退役、修理、补给等后勤操作面板。
- 如果后勤按钮不生效，要检查按钮 OnClick 是否绑定到 `LogisticsPanelUI`，以及当前是否选中了合法单位。
- 后勤操作大概率也应该只在 `Intermission` 生效，避免战斗中改血量/退役导致状态错乱。

Unity 配置重点：

- `unitSupplyPool` 里要放可抽到的单位预制体。
- 单位预制体必须挂 `UnitLogic`，并正确配置 `unitTier/unitPrice/unitCost`。
- 商店 UI 的 5 个按钮槽位要对应 `ShopSlotUI.slotIndex` 的 0~4。
- 后勤 UI 如果新增按钮，需要在 Inspector 里绑定按钮事件、选中单位显示文本、修理/退役按钮等引用。

## 羁绊系统

相关脚本：

- `TraitSO.cs`
- `SynergyManager.cs`
- `SynergyListUI.cs`
- `SynergyItemUI.cs`
- `Assets/TraitSO/*.asset`

核心理解：

- `TraitSO` 是羁绊配置资产，可以在 Unity 菜单 `ASH Auto Chess/Trait` 创建。
- 当前项目已有羁绊资产，例如黑十字帝国学联、重装甲。
- 每个羁绊有名称、图标、描述、门槛数组、效果数组。
- 当前支持的效果包括：
  - 伤害百分比增加。
  - 护甲增加。
  - 攻击间隔缩短。
- `SynergyManager` 只统计战场上的玩家单位。
- 同名兵种会用 `GetSynergyIdentityKey` 去重，避免同一种单位重复刷羁绊人数。
- 部署期主要重算羁绊账本和 UI。
- 战斗开始前会先清旧 Buff，再重算并灌入本局临时属性。
- 战斗结束会清掉临时羁绊加成。

Unity 配置重点：

- 玩家单位最好至少配置两个基础 `TraitSO`。
- 同兵种的不同实例要有相同 `synergyIdentityOverride`，否则同名去重可能不符合预期。
- 羁绊 UI 需要 `SynergyListUI.itemPrefab` 和 `contentRoot`。
- 单条羁绊 UI 需要配置图标、文字、背景图等引用。
- `Assets/Prefab/UI/SynergyItem_Prefab.prefab` 是当前羁绊条目预制体。

## UI 和日志

相关脚本：

- `BattleResultUIManager.cs`
- `CombatLogManager.cs`
- `ShopSlotUI.cs`
- `ShopUIController.cs`
- `ShopTestController.cs`
- `UnitClickTarget.cs`
- `StageMapUIView.cs`
- `MapNodeButtonUI.cs`
- `LogisticsPanelUI.cs`

核心理解：

- `BattleResultUIManager` 监听 `GameFlowManager.StateEntered`，进入 `Result` 时显示结算面板。
- 结算确认按钮会调用 `ConfirmResultAndEnterIntermission`。
- `CombatLogManager` 可以绑定 `Text` 和 `ScrollRect`，没有 UI 实例时也会输出到 Console。
- `ShopSlotUI` 根据 `ShopManager` 的货架数据刷新按钮文字。
- `ShopUIController` 在 `Intermission` 自动显示商店 UI，其他阶段隐藏。
- `ShopTestController` 支持键盘 1~5 买货架，`R` 刷新，`L` 升级，并支持选中单位后退役/修理/扣血测试。
- `UnitClickTarget` 把单位点击转发给 `ShopTestController`。
- 地图 UI 相关脚本负责显示关卡节点和处理节点按钮点击。
- 后勤 UI 相关脚本负责显示/触发单位修理、退役等操作。

## 预制体配置常见要求

单位预制体通常需要：

- `UnitLogic`
- `UnitDragHandler`
- `UnitClickTarget`，如果要支持点击选中
- `SpriteRenderer`
- 能接收鼠标点击/碰撞的 Collider 或 Collider2D
- 如果要白刃接触触发，需要合适的 Trigger 碰撞配置
- `bulletPrefab` 指向带 `BulletProjectile` 的子弹预制体
- `firePoint` 可选，没有就从单位自身位置发射
- 玩家单位配置基础羁绊 `baseTraits`

子弹预制体需要：

- `BulletProjectile`
- Collider 或 Collider2D，通常设为 Trigger

场景中通常需要：

- `GameFlowManager`
- `GridManager`，并绑定 `battlefieldContainer`、`reserveContainer`
- `ShopManager`，并配置商店卡池和概率
- `EnemySpawnManager`，并配置敌人、母巢、预警投影
- `SynergyManager`，也可以让代码自动创建，但场景里显式放一个更好找
- `CombatLogManager`，如果需要战斗日志 UI
- `StageMapManager` 和地图 UI，如果启用关卡地图系统
- `LogisticsPanelUI` 和后勤 UI，如果启用修理/退役面板
- 对应的商店、结算、羁绊、地图、后勤 UI 引用

## 改代码时的注意事项

- 先看现有单例和状态机，不要绕开 `GameFlowManager` 直接改流程。
- 涉及买人、资金、人口上限时，要同时想清楚 `ShopManager` 和 `GameFlowManager` 的同步关系。
- 涉及部署和格子占用时，优先用 `GridManager` 提供的方法，不要自己乱算父物体和坐标。
- 涉及战斗开始/结束时，要注意 `ResetForBattle`、`SetCombatEnabled`、羁绊 Buff 清理、敌人清理。
- 涉及 UI 时，要注意很多 UI 是通过事件刷新，不一定每帧都刷。
- 涉及预制体字段时，改完代码后一定要告诉星鸟 Unity Inspector 里要补哪些引用。
- 代码里现在有一些调试日志和带表情的注释/文本，如果整理代码，先确认是不是星鸟正在用来调试，不要随手删。
- 当前项目有不少未提交修改，动手前必须先确认改动范围，不能误还原、误覆盖星鸟的内容。
- `.csproj`、`.sln`、`Library`、`Temp`、`Logs`、`.vscode` 等是 Unity/IDE 生成或本地文件，通常不提交。

## 验证建议

改核心玩法后，建议在 Unity 里按这个顺序手动验证：

1. 打开 `SampleScene`。
2. 等待脚本编译完成。
3. 进入 Play Mode。
4. 从 `Result` 点确认进入 `Intermission`。
5. 在商店买单位，确认资金减少、单位进入备战席。
6. 进入 `Deployment`，拖新单位到 Y=0~1 的部署区。
7. 开始 `Battle`，确认敌人生成、单位会攻击、子弹能命中。
8. 战斗结束后，确认进入结算，奖励发放，幸存单位回到开战前位置。
9. 如果改了羁绊，确认左侧羁绊 UI 和战斗属性加成都正确。
10. 如果改了地图节点，确认地图 UI 能显示节点，节点按钮能进入正确流程。
11. 如果改了后勤面板，确认选中单位后可以正确修理/退役，资金和单位列表同步刷新。
