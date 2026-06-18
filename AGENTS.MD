# 项目协作说明

## 用户偏好

- 用户叫星鸟。
- 星鸟是代码小白，解释代码时要用大白话，不要默认对方懂 Unity/C# 黑话。
- 每次写完代码后，如果改动涉及 Unity 配置，必须明确告诉星鸟要在 Unity 里怎么配置，例如挂哪个脚本、拖哪个引用、改哪个 Inspector 字段、Prefab 是否要 Apply、场景是否要保存。
- 如果配置只能在 Unity 编辑器里手动完成，要直接说明“这一步我不能替你点，需要你在 Unity 里这样做”，不要假装已经完成。
- 修改项目前，先读当前项目结构和相关脚本，不要凭空改。
- 工作区可能有星鸟自己的改动，不能随手还原、覆盖、删除。

## 项目概况

- 这是一个 Unity 2D 自走棋项目，项目名是 `ASH_AUTO_CHESS`。
- 当前构建入口场景是 `Assets/Scenes/SampleScene.unity`。
- 主玩法是：地图选关 -> 整备/商店买单位 -> 部署到棋盘 -> 自动战斗 -> 结算奖励 -> 回到地图继续选下一节点。
- 核心棋盘是 5x5：
  - 战场大小：X=0~4，Y=0~4。
  - 玩家部署区：Y=0~1。
  - 战略线：Y=2。
  - 敌方/母巢区域：Y=4。
- 备战席是单排 9 格：X=0~8，Y=0。
- 项目使用 Unity 传统 `uGUI`、`Text` 和部分 TextMeshPro，不是 UI Toolkit。
- 主要包包括 Unity 2D、TextMeshPro、Timeline、Visual Scripting、Test Framework、uGUI。

## 当前主要目录

- `Assets/Scripts/Core`：核心玩法脚本。
- `Assets/Scripts/Editor`：CSV 导入和热重载编辑器脚本。
- `Assets/Scripts/Test`：商店测试和临时 UI 控制脚本。
- `Assets/Scenes/SampleScene.unity`：当前唯一启用场景。
- `Assets/Prefab/Chacter`：角色/单位相关 Prefab，包含 `basePrefab.prefab` 和旧角色 Prefab。
- `Assets/Prefab/Bullet`：我方和敌方子弹 Prefab。
- `Assets/Prefab/UI`：UI Prefab，例如 `SynergyItem_Prefab.prefab`、`Layer.prefab`。
- `Assets/Resources/Units/DataAssets`：单位与商店数据资产，包含大量 `UnitLogicDataSO` 和 `ShopManagerData.asset`。
- `Assets/Resources/Units/Icons`：单位图标资源，CSV 导入器会尝试按名字自动绑定。
- `Assets/TraitSO`：羁绊 ScriptableObject 资产，例如黑十字帝国学联、重装甲。
- `Assets/MapNode`：地图节点 ScriptableObject 资产，例如 `N1001.asset`、`N1002.asset`。
- `Assets/Data`：策划 CSV 表的默认目录，代码会寻找 `PlayerUnits.csv`、`EnemyUnits.csv`、`MapNode.csv`、`WaveNode.csv`、`ShopPool.csv`、`ShopProbability.csv`。
- `ProjectSettings`：Unity 项目设置。
- `Packages/manifest.json`：Unity 包依赖。

## 当前 Git 状态

- 最近一次扫描时，`git status --short --ignored` 没有显示未提交的普通文件改动，只显示 Unity/IDE 生成文件被 `.gitignore` 忽略。
- 被忽略的常见内容包括：`Library/`、`Temp/`、`Logs/`、`UserSettings/`、`.vscode/`、`.csproj`、`.sln` 等。
- `.gitignore` 明确不忽略 `.meta`，Unity 资源对应的 `.meta` 应该正常提交。

## 游戏主流程

核心状态机在 `Assets/Scripts/Core/GameFlowManager.cs`。

流程顺序是：

1. `Result`：结算阶段。
2. `Intermission`：整备阶段，可以开商店、买单位、刷新、买经验、修理、退役。
3. `Deployment`：部署阶段，可以把本轮新买的单位从备战席拖到玩家部署区。
4. `Battle`：战斗阶段，单位自动战斗。

重要规则：

- `GameFlowManager` 是跨场景单例，会 `DontDestroyOnLoad`。
- 金钱、人口上限、当前关卡编号通过 `PlayerPrefs` 保存。
- 结算确认后不是直接进入整备，而是优先显示地图 UI，让玩家选下一关节点。
- 选择地图节点后，`StageMapManager` 会保存当前节点和本次战斗波次，然后把流程切到 `Intermission`。
- 进入 `Intermission` 时，`ShopManager.HandlePostBattleRefresh()` 会处理战后商店刷新；如果商店锁定，则保留货架并自动解锁。
- 进入 `Deployment` 和 `Battle` 时会隐藏地图 UI 和结算 UI。
- 战斗胜负奖励会优先从 `StageMapManager.CalculateCurrentNodeReward()` 读取当前地图节点奖励。
- 战斗结束退出 Battle 时会调用 `StageMapManager.MarkCurrentNodeCompleted()`，让下一层节点可选。
- 战斗开始时会记录玩家单位部署位置，战斗结束后幸存单位回到开战前位置。
- 老兵单位会锁住，部署阶段不能随便换位置。
- 新买单位只在本轮部署阶段允许拖拽。

## 关卡地图系统

相关文件：

- `Assets/Scripts/Core/StageMapManager.cs`
- `Assets/Scripts/Core/StageMapUIView.cs`
- `Assets/Scripts/Core/StageMapUILayoutUtility.cs`
- `Assets/Scripts/Core/StageTableModels.cs`
- `Assets/Scripts/Core/StageTableParser.cs`
- `Assets/Scripts/Core/MapNodeSO.cs`
- `Assets/Scripts/Core/MapNodeButtonUI.cs`
- `Assets/Scripts/Editor/StageMapCSVImporter.cs`
- `Assets/MapNode/*.asset`

核心理解：

- 地图系统支持两套数据来源：
  - 新版：CSV 表 `Assets/Data/MapNode.csv` 和 `Assets/Data/WaveNode.csv`。
  - 旧版/备用：`MapNodeSO` 资产。
- `StageMapManager` 会优先加载 CSV 表，运行时保存：
  - 当前节点 ID。
  - 当前层数。
  - 当前节点是否完成。
  - 当前战斗波次 ID。
- `MapNode.csv` 解析成 `MapNodeData`，字段包括节点 ID、层数、候选战斗波次、下一节点、基础奖励、胜利奖励、失败奖励。
- `WaveNode.csv` 解析成 `WaveNodeData`，字段包括波次 ID、初始敌人、是否有 Boss、Boss 信息、Boss 出兵参数。
- `StageMapUIView` 可以根据 CSV 运行时节点自动生成地图按钮。
- `MapNodeButtonUI` 负责节点点击、锁定/可选颜色、选择节点。
- 第一层节点在没有当前节点时可选；之后必须当前节点已完成，且目标节点在下一层并且被当前节点连接。

Unity 配置重点：

- 场景里需要 `StageMapManager`，建议绑定 `MapNode.csv` 和 `WaveNode.csv` 的 TextAsset；不绑也会尝试从默认路径或 Resources 读取。
- `GameFlowManager` 的 `stageMapView` 要拖 `StageMapUIView` 引用，否则结算后无法显示地图。
- `StageMapUIView` 需要绑定 `mapRoot`、`nodeContainer`、`nodeButtonPrefab`。
- `nodeButtonPrefab` 必须挂 `MapNodeButtonUI`，并且有 `Button`、`Image`、可选 `Text`。
- 如果地图 UI 没生成节点，检查 CSV 是否在 `Assets/Data`、字段名是否正确、`StageMapUIView.generateRuntimeNodes` 是否开启。

## CSV 和数据热重载

相关文件：

- `Assets/Scripts/Editor/UnitCSVPostprocessor.cs`
- `Assets/Scripts/Editor/UnitCSVImporter.cs`
- `Assets/Scripts/Editor/ShopManagerCSVImporter.cs`
- `Assets/Scripts/Editor/StageMapCSVImporter.cs`

核心理解：

- `UnitCSVPostprocessor` 会监听导入资源变化，触发静默导入。
- 改动 `PlayerUnits.csv` 或 `EnemyUnits.csv` 时，会生成/覆盖 `Assets/Resources/Units/DataAssets/*.asset` 的单位数据。
- 改动 `Assets/Resources/Units/Icons` 下的图片时，也会重新导入单位数据，方便自动绑定图标。
- 改动 `ShopPool.csv` 或 `ShopProbability.csv` 时，会生成/覆盖 `Assets/Resources/Units/DataAssets/ShopManagerData.asset`。
- 改动 `Assets/Data/MapNode.csv` 时，会生成/覆盖 `Assets/MapNode/*.asset`。
- `WaveNode.csv` 主要由运行时 `StageMapManager` 读取成表数据，不一定生成单独资产。

CSV 约定：

- 单位表文件名：`PlayerUnits.csv`、`EnemyUnits.csv`。
- 商店表文件名：`ShopPool.csv`、`ShopProbability.csv`。
- 地图表文件名：`MapNode.csv`、`WaveNode.csv`。
- 单位表会导出到 `Assets/Resources/Units/DataAssets`。
- 单位图标默认从 `Assets/Resources/Units/Icons/{spriteName}.png` 查找。
- `UnitCSVImporter` 会尝试按 `chessId`、`chessName`、去星级名称等自动绑定匹配的 Prefab。

## 单位数据系统

相关文件：

- `Assets/Scripts/Core/UnitLogicDataSO.cs`
- `Assets/Scripts/Core/UnitDataManager.cs`
- `Assets/Scripts/Core/UnitLogic.cs`
- `Assets/Resources/Units/DataAssets/*.asset`

核心理解：

- 当前单位属性已经数据驱动，不再主要依赖每个角色 Prefab 手填属性。
- `UnitLogicDataSO` 保存单个单位的配置，例如：
  - `chessId`
  - `chessName`
  - `unitPrefab`
  - `unitSprite`
  - `unionId`
  - `faction`
  - `playerDirective`
  - `unitCost`
  - `unitPrice`
  - `unitRare`
  - `unitTier`
  - 生命、护甲、暴击、远程攻击、弹药、穿透、白刃、移动、占线、威胁值等。
- `UnitDataManager` 会从 `Resources.LoadAll<UnitLogicDataSO>("Units/DataAssets")` 加载单位数据。
- `UnitDataManager` 还会加载 `TraitSO`，并按 `unionId` 解析单位羁绊。
- 生成单位时，`UnitDataManager.SpawnUnitOnBoard(chessId, position, faction)` 会：
  - 用 `baseUnitPrefab` 实例化标准单位壳子。
  - 按 `chessId` 找到 `UnitLogicDataSO`。
  - 把 `unitPrefab` 作为模型挂到 `UnitLogic.modelContainer` 或单位自身下面。
  - 调用 `UnitLogic.InitializeFromConfig()` 灌入属性和羁绊。
- `UnitLogic` 内部保留运行时字段，但属性 getter 在未初始化时会预览 `unitDataConfig`。
- `UnitLogic` 支持战斗修饰器，包括独立增伤、独立减伤、固定减伤。

Unity 配置重点：

- 场景里需要 `UnitDataManager`。
- `UnitDataManager.baseUnitPrefab` 必须绑定 `Assets/Prefab/Chacter/basePrefab.prefab` 或同类标准单位壳子。
- 标准单位壳子必须挂 `UnitLogic`，并具备拖拽、点击、碰撞、Sprite/模型容器等运行所需组件。
- `UnitLogicDataSO.unitPrefab` 是美术模型/外观 Prefab，不一定是完整战斗单位。
- 如果生成单位失败，优先检查 `UnitDataManager` 是否存在、`baseUnitPrefab` 是否绑定、`chessId` 是否存在于 DataAssets。

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

当前战斗重点：

- `UnitFaction`：玩家、敌人、中立。
- `PlayerDirective`：推线或占点。
- `DamageType` / `AttackDamageTrack`：区分远程/白刃伤害。
- 单位会按威胁值、距离和战斗指令寻找目标。
- 玩家单位可以占领战略线；战略线进度由 `GameFlowManager` 记录。
- 远程攻击使用 `BulletProjectile`，子弹追踪发射时指定的目标。
- 玩家单位弹药耗尽后进入白刃，白刃会使用白刃伤害、白刃射程和白刃穿透。
- 敌人死亡会销毁对象。
- 玩家死亡会退出战场/隐藏，方便战后修理或收容。
- 羁绊和其他系统可以通过 `AddCombatModifier` / `RemoveCombatModifier` 改战斗伤害。

Unity 配置重点：

- 子弹 Prefab 要挂 `BulletProjectile`，并有 Trigger Collider 或 Collider2D。
- 单位的 `bulletPrefab`、`firePoint`、`modelContainer` 要按需要绑定。
- 若单位点击/拖拽不生效，先查 Collider、相机、Layer、`UnitDragHandler`、当前 GameState。

## 敌人生成和波次

核心脚本是 `Assets/Scripts/Core/EnemySpawnManager.cs`。

核心理解：

- `EnemySpawnManager` 监听 `GameFlowManager` 状态：
  - `Deployment`：显示敌军预警投影。
  - `Battle`：生成真实敌人和 Boss。
  - `Result/Intermission`：停止生命周期。
- 新版优先使用 `StageMapManager.CurrentBattleWaveId` 对应的 `WaveNodeData`。
- 表格波次里敌人用 `ChessId` 表示，生成时通过 `UnitDataManager.SpawnUnitOnBoard()` 创建。
- 如果没有表格波次，会回落到 Inspector 里的 `enemyWaveConfigs` 或 `initialEnemyConfigs`。
- Boss/母巢存在时，周期出兵只在 Boss 存活时进行。
- Boss 出兵间隔会随战斗时间缩短，但不会低于最小间隔。

Unity 配置重点：

- `warningProjectionPrefab` 要绑定部署期预警 Prefab。
- 若使用 CSV 波次，确保 `WaveNode.csv` 的 ChessId 能在 `UnitLogicDataSO` 中找到。
- 若使用旧 Prefab 波次，敌方 Prefab 必须挂 `UnitLogic`。
- 所有敌人坐标必须在 5x5 战场内。

## 商店、经济和后勤

核心脚本：

- `Assets/Scripts/Core/ShopManager.cs`
- `Assets/Scripts/Core/ShopManagerDataSO.cs`
- `Assets/Scripts/Core/ShopSlotUI.cs`
- `Assets/Scripts/Core/LogisticsPanelUI.cs`

商店当前规则：

- 商店有 5 个货架。
- 商店等级最高 5 级。
- 刷新费用默认 2。
- 买经验费用是 4，每次加 4 经验。
- 升级所需经验来自 `upgradeLevelCosts`，默认 10、15、20、25。
- 人口/Cost 上限默认 5、7、9、11、13，并同步到 `GameFlowManager.PopulationLimit`。
- 商店现在使用公共牌库：
  - `ShopPool.csv` 决定不同稀有度的初始牌数。
  - `ShopProbability.csv` 决定不同商店等级抽 1~5 费的权重。
  - `ShopManagerData.asset` 是导入后的数据资产。
- `ShopManager.BuildSupplyPool()` 会从所有单位数据里筛选玩家单位、1 星、价格大于 0 的单位加入牌库。
- 货架保存的是 `chessId`，购买时再用 `UnitDataManager` 生成单位。
- 购买成功会从公共牌库扣 1 张，退役会把该单位对应 `chessId` 还回牌库。
- 商店支持锁定：锁定后战后保留货架，进入下一次整备时自动解除锁定。

后勤 UI：

- `LogisticsPanelUI` 显示资金、当前上阵 Cost、后勤等级、经验进度。
- 文本槽可以是旧版 `Text`，也可以是 `TMP_Text`。
- 经验条使用 `Slider`。
- 资金变化通过订阅 `GameFlowManager.FundsChanged` 更新；Cost/等级/经验会动态刷新。

Unity 配置重点：

- 场景里需要 `ShopManager` 和 `UnitDataManager`。
- `ShopManagerData.asset` 必须在 `Assets/Resources/Units/DataAssets/ShopManagerData.asset`，否则商店牌数和概率可能为空。
- `ShopSlotUI.slotIndex` 必须对应 0~4。
- 商店按钮 OnClick 应绑定购买对应槽位的方法。
- `LogisticsPanelUI` 的资金、Cost、等级、经验文本和 Slider 需要在 Inspector 绑定。

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
- 单位数据里的 `unionId` 会被 `UnitDataManager` 解析成 `TraitSO` 列表。
- 每个羁绊有名称、图标、描述、门槛数组、效果数组。
- 当前支持效果包括伤害百分比增加、护甲增加、攻击间隔缩短。
- `SynergyManager` 只统计战场上的玩家单位。
- 同名兵种会用 `GetSynergyIdentityKey` 去重，避免同一种单位重复刷羁绊人数。
- 部署期主要重算羁绊账本和 UI。
- 战斗开始前会先清旧 Buff，再重算并灌入本局临时属性。
- 战斗结束会清掉临时羁绊加成。

Unity 配置重点：

- `TraitSO` 的名字、`traitName` 或其他可读 ID 要能被 `unionId` 对上。
- 羁绊 UI 需要 `SynergyListUI.itemPrefab` 和 `contentRoot`。
- 单条羁绊 UI 需要配置图标、文字、背景图等引用。
- `Assets/Prefab/UI/SynergyItem_Prefab.prefab` 是当前羁绊条目 Prefab。

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
- 结算确认按钮调用 `GameFlowManager.ConfirmResultAndEnterIntermission()`，然后显示地图。
- `CombatLogManager` 可以绑定 `Text` 和 `ScrollRect`，没有 UI 实例时也会输出到 Console。
- `ShopSlotUI` 根据 `ShopManager.GetShopSlotUnitData()` 刷新按钮文字。
- `ShopUIController` 在 `Intermission` 自动显示商店 UI，其他阶段隐藏。
- `ShopTestController` 支持键盘 1~5 买货架，`R` 刷新，`L` 升级，并支持选中单位后退役/修理/扣血测试。
- `UnitClickTarget` 把单位点击转发给 `ShopTestController`。
- 地图 UI 负责显示关卡节点和处理节点按钮点击。
- 后勤 UI 负责显示资金、Cost、等级和经验。

## 预制体配置常见要求

标准单位壳子 `basePrefab` 通常需要：

- `UnitLogic`
- `UnitDragHandler`
- `UnitClickTarget`，如果要支持点击选中
- `SpriteRenderer` 或可承载模型的子节点
- `modelContainer`，用于挂载 `UnitLogicDataSO.unitPrefab`
- 能接收鼠标点击/碰撞的 Collider 或 Collider2D
- 如果要白刃接触触发，需要合适的 Trigger 碰撞配置
- `bulletPrefab` 指向带 `BulletProjectile` 的子弹 Prefab
- `firePoint` 可选，没有就从单位自身位置发射

数据资产 `UnitLogicDataSO` 通常需要：

- 正确的 `chessId`
- 正确的 `chessName`
- `unitPrefab` 绑定美术模型 Prefab
- `unitSprite` 绑定图标
- `unionId` 能匹配已有 `TraitSO`
- 正确的价格、人口、稀有度、星级、属性

场景中通常需要：

- `GameFlowManager`
- `StageMapManager`
- `StageMapUIView`
- `UnitDataManager`
- `GridManager`，并绑定 `battlefieldContainer`、`reserveContainer`
- `ShopManager`
- `EnemySpawnManager`
- `SynergyManager`，也可以由代码自动创建，但场景里显式放一个更好找
- `CombatLogManager`，如果需要战斗日志 UI
- `LogisticsPanelUI`
- 对应的商店、结算、羁绊、地图、后勤 UI 引用

## 改代码时的注意事项

- 先看现有单例和状态机，不要绕开 `GameFlowManager` 直接改流程。
- 涉及地图选关、奖励、敌人波次时，要同时看 `StageMapManager`、`StageTableParser`、`EnemySpawnManager`。
- 涉及单位属性时，优先改 CSV 或 `UnitLogicDataSO`，不要只改旧 Prefab 上的属性。
- 涉及生成单位时，要确认 `UnitDataManager.baseUnitPrefab`、`UnitLogicDataSO.unitPrefab`、`chessId` 三者关系。
- 涉及商店时，要同时想清楚 `ShopManager`、`ShopManagerDataSO`、`ShopPool.csv`、`ShopProbability.csv`、公共牌库数量。
- 涉及资金、人口上限时，要注意 `ShopManager` 和 `GameFlowManager` 的同步关系。
- 涉及部署和格子占用时，优先用 `GridManager` 提供的方法，不要自己乱算父物体和坐标。
- 涉及战斗开始/结束时，要注意 `ResetForBattle`、`SetCombatEnabled`、羁绊 Buff 清理、敌人清理、地图节点完成标记。
- 涉及 UI 时，要注意很多 UI 是通过事件刷新，部分 UI 也会在 `Update` 里动态刷新。
- 涉及预制体字段时，改完代码后一定要告诉星鸟 Unity Inspector 里要补哪些引用。
- `.csproj`、`.sln`、`Library`、`Temp`、`Logs`、`.vscode` 等是 Unity/IDE 生成或本地文件，通常不提交。

## 验证建议

改核心玩法后，建议在 Unity 里按这个顺序手动验证：

1. 打开 `SampleScene`。
2. 等待脚本编译完成。
3. 进入 Play Mode。
4. 如果出现地图，选择一个第一层节点，确认进入 `Intermission`。
5. 在商店买单位，确认资金减少、公共牌库数量减少、单位进入备战席。
6. 点击买经验/升级，确认后勤等级、经验条、Cost 上限变化。
7. 进入 `Deployment`，拖新单位到 Y=0~1 的部署区。
8. 确认敌人预警投影按当前 WaveNode 显示。
9. 开始 `Battle`，确认敌人用 ChessId 生成、单位会攻击、子弹能命中。
10. 如果当前波次有 Boss，确认 Boss 存活时会周期出兵，死亡后不再出兵。
11. 战斗结束后，确认进入结算，奖励按地图节点配置发放。
12. 确认结算后回到地图，当前节点完成，下一层连接节点可选。
13. 如果改了羁绊，确认左侧羁绊 UI 和战斗属性加成都正确。
14. 如果改了 CSV，确认 Unity 自动生成/更新对应资产，并检查 Console 是否有导入警告。
15. 如果改了后勤面板，确认资金、Cost、等级、经验显示同步刷新。
