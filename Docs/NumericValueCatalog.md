# 数值字段总表

这份表按“字段名”整理。字段名优先写代码和 CSV 里真实使用的英文名，因为以后查脚本、改表、问问题时最稳定。  
来源主要来自 `Assets/Scripts/Core`、`Assets/Scripts/Editor`、`Assets/Data`、`Assets/TraitSO`。

## 公式速查

| 系统 | 公式 |
| --- | --- |
| 最终伤害 | `effectiveArmor = max(0, targetArmor * (1 - clamp01(penetrationPct)) - max(0, penetrationFlat))` |
| 最终伤害 | `armorAdjustedDamage = max(0, incomingDamage) * 100 / max(1, 100 + effectiveArmor)` |
| 最终伤害 | `finalDamage = max(1, ceil(armorAdjustedDamage * ampProduct * reductionProduct - flatReduction))` |
| 独立增伤 | `ampProduct = product(1 + value)` |
| 独立减伤 | `reductionProduct = product(1 - clamp01(value))` |
| 固定减伤 | `flatReduction = sum(max(0, value))` |
| 暴击 | `normalizedCritRate = critRate > 1 ? critRate * 0.01 : critRate`；命中暴击后 `damage *= max(1, critDamage)` |
| 攻击间隔 | `attackInterval = 1 / max(0.01, speed)`；当前远程开火冷却用 `fireRate`，白刃冷却用 `bayonetSpeed` |
| 弹药消耗 | 每次远程攻击后 `currentAmmo -= max(1, ammoSpeed)`；弹药归 0 后进入白刃 |
| 白刃反噬 | `bayonetCost = "[flat,percent]"`，反噬伤害 `ceil(flat + maxHp * percent)`，直接扣自己血 |
| 升星兜底 | 找不到下一星数据资产时：`unitTier += 1`，`maxHp = ceil(maxHp * 1.5)`，`fireDamage = ceil(fireDamage * 1.35)`，`bayonetDamage = ceil(bayonetDamage * 1.35)` |
| 战斗修理费 | `currentHp == 0 ? unitPrice : ceil(unitPrice * 0.6 * ((maxHp - currentHp) / maxHp))` |
| 商店退役退款 | `floor(unitPrice * currentHp / maxHp)`；另一个旧入口是 `ceil(unitPrice * 0.5)` |
| 购买经验 | 花费 `UpgradeExpPurchaseCost`，增加 `UpgradeExpPerPurchase`，经验满后扣掉需求并升级 |
| 商店抽费用阶 | 权重总和 `total = WeightT1 + ... + WeightT5`，随机 `roll in [0,total)`，按累计权重落到 1-5 费 |
| 地图奖励 | `reward = max(0, BaseReward + (victory ? VictoryBonus : DefeatBonus))` |
| 母巢出兵间隔 | `interval = max(MinSpawnInterval, BaseSpawnInterval - battleElapsedSeconds * EnrageAcceleration)` |
| 战略线占领 | `strategicLineCaptureProgress += captureSpeed * deltaTime`，再 clamp 到 `0..strategicLineCaptureRequired` |
| 占点区占领 | `currentProgress += sum(player.captureSpeed) * deltaTime`，满值固定 `100` |
| 地图节点排布 | `rowWidth = (itemCount - 1) * horizontalSpacing`；`x = centerX - rowWidth * 0.5 + itemIndex * horizontalSpacing` |

## 单位表字段

单位字段来源：`PlayerUnits.csv`、`EnemyUnits.csv`、`UnitLogicDataSO`、`UnitLogic`。  
CSV 字段会导入到 `UnitLogicDataSO`，运行时再灌进 `UnitLogic` 的同名属性或 `runtime...` 字段。

| CSV 字段 | SO/运行时字段 | 含义 | 数值规则/公式关系 |
| --- | --- | --- | --- |
| `ChessId` | `chessId` | 棋子 ID | 非数值，但用于找单位数据、图标、下一星资产 |
| `ChessName` | `chessName` | 显示名 | 非数值 |
| `UnionId` | `unionId` | 羁绊 ID 串 | 非数值，按分隔符拆成多个羁绊 |
| `Faction` | `faction` | 阵营 | `0=Player`，`1=Enemy`，`2=Neutral`，其他变 Neutral |
| `PlayerDirective` | `playerDirective` | 玩家单位指令 | `0=PushLine`，`1=CapturePoint` |
| `UnitCost` | `unitCost` | 上阵 Cost/人口占用 | clamp 到 `>=0`，部署时参与 Cost 上限判断 |
| `UnitPrice` | `unitPrice` | 价格 | clamp 到 `>=0`，购买/修理/退役会用 |
| `UnitRare` | `unitRare` | 费用阶/稀有度 | clamp 到 `>=0`，商店抽卡按它筛选 |
| `UnitTier` | `unitTier` | 星级 | clamp 到 `>=1`，三合一和下一星 ID 会用 |
| `UnitType` | `unitType` | 单位类型 | 被 `attackType` 过滤目标时使用 |
| `AttackType` | `attackType` | 攻击类型 | `1` 不能打 `unitType=1`；`2` 不能打 `unitType=0` |
| `BaseHp` | `baseHp` / `maxHp` | 最大生命 | clamp 到 `>=1` |
| `BaseArmor` | `baseArmor` / `armor` | 远程护甲 | 远程伤害轨道使用 |
| `BayonetArmor` | `bayonetArmor` | 白刃防护 | 白刃伤害轨道使用 |
| `CritRate` | `critRate` | 暴击率 | 大于 1 时按百分数处理，例如 20 变 20% |
| `CritDamage` | `critDamage` | 暴击伤害倍率 | 暴击后乘 `max(1, critDamage)` |
| `FireDamage` | `fireDamage` / `damage` | 远程基础伤害 | 先过暴击，再进最终伤害公式 |
| `FireRate` | `fireRate` | 远程开火频率 | 当前代码用它算远程攻击间隔：`1 / max(0.01, fireRate)` |
| `FireSpeed` | `fireSpeed` / `attackSpeed` | 子弹速度字段 | 当前代码用于子弹飞行速度；羁绊攻速也改它 |
| `FireRange` | `fireRange` / `attackRange` | 远程射程 | 目标距离必须 `<= fireRange` |
| `Ammo` | `ammo` / `maxAmmo` | 弹药量 | 战斗开始回满；`<=0` 进入白刃 |
| `AmmoSpeed` | `ammoSpeed` | 每次射击消耗弹药 | 每次至少消耗 `1` |
| `FirePenPct` | `firePenPct` | 远程百分比穿透 | 参与 `effectiveArmor` |
| `FirePenFlat` | `firePenFlat` | 远程固定穿透 | 参与 `effectiveArmor` |
| `DamageAoe` | `damageAoe` | AOE 数值 | 当前字段存在，核心伤害流程里未看到实际 AOE 结算 |
| `BayonetId` | `bayonetId` | 白刃配置 ID | 非数值 |
| `BayonetDamage` | `bayonetDamage` | 白刃基础伤害 | 先过暴击，再进最终伤害公式 |
| `BayonetCost` | `bayonetCost` | 白刃代价 | 字符串格式 `"[flat,percent]"`，用于反噬公式 |
| `BayonetSpeed` | `bayonetSpeed` | 白刃攻速 | 白刃攻击间隔：`1 / max(0.01, bayonetSpeed)` |
| `BayonetRange` | `bayonetRange` | 白刃射程 | 白刃模式下作为当前攻击射程 |
| `BayonetPenPct` | `bayonetPenPct` | 白刃百分比穿透 | 参与 `effectiveArmor` |
| `BayonetPenFlat` | `bayonetPenFlat` | 白刃固定穿透 | 参与 `effectiveArmor` |
| `MoveSpeed` | `moveSpeed` | 移动速度 | `MoveTowards(..., moveSpeed * deltaTime)` |
| `CaptureSpeed` | `captureSpeed` | 占领速度 | 战略线/占点区每秒推进量 |
| `ThreatValue` | `threatValue` | 威胁值 | 非 PushLine 选目标时 `score = threatValue * 1000 - distance + random` |
| `SpriteName` | `spriteName` / `unitSprite` | 图标名 | 非数值，导入器按名字找图标 |

### 单位运行时隐藏数值

| 字段 | 含义 | 规则 |
| --- | --- | --- |
| `currentHp` / `runtimeCurrentHp` | 当前生命 | clamp 到 `0..maxHp` |
| `currentAmmo` / `runtimeCurrentAmmo` | 当前弹药 | clamp 到 `0..maxAmmo` |
| `runtimeInBayonetMode` | 是否白刃模式 | `maxAmmo <= 0` 或弹药耗尽后为 true |
| `attackCooldown` | 攻击冷却 | 每帧减 `Time.deltaTime`，小于等于 0 才能攻击 |
| `targetSearchCooldown` | 搜敌冷却 | 默认每 `0.2` 秒重新搜敌 |
| `ArmorFormulaBase` | 护甲公式基数 | 固定 `100` |
| `GridArrivalTolerance` | 到格子容差 | 固定 `0.03` |
| `isVeteran` / `runtimeIsVeteran` | 老兵标记 | 老兵部署期锁位置 |
| `isPositionLocked` | 位置锁 | true 时不可拖动 |
| `hasGridPosition` / `gridPosition` | 棋盘坐标 | 坐标是整数格 |

## 战斗公式字段

| 字段 | 来源 | 用法 |
| --- | --- | --- |
| `incomingDamage` | `ReceiveDamage(...)` | 进入伤害公式前的伤害 |
| `targetArmor` | `armor` 或 `bayonetArmor` | 远程用 `armor`，白刃用 `bayonetArmor` |
| `penetrationPct` | `firePenPct` / `bayonetPenPct` | 百分比穿透，先 clamp 到 `0..1` |
| `penetrationFlat` | `firePenFlat` / `bayonetPenFlat` | 固定穿透，最小 0 |
| `effectiveArmor` | 公式中间值 | `max(0, targetArmor * (1 - clamp01(penetrationPct)) - max(0, penetrationFlat))` |
| `armorAdjustedDamage` | 公式中间值 | `incomingDamage * 100 / (100 + effectiveArmor)` |
| `independentAmpModifiers` | 战斗修饰器字典 | 每个值独立相乘，`product(1 + value)` |
| `independentReductionModifiers` | 战斗修饰器字典 | 每个值独立相乘，`product(1 - clamp01(value))` |
| `flatReductionModifiers` | 战斗修饰器字典 | 直接相加后从最终伤害里扣 |
| `finalDamage` | 最终伤害 | `max(1, ceil(...))`，所以只要进了伤害流程，至少 1 点 |
| `critRate` | 单位字段 | 支持 `0.2` 或 `20` 两种写法 |
| `critDamage` | 单位字段 | 暴击倍率 |
| `fireRate` | 单位字段 | 当前远程攻击冷却使用 |
| `fireSpeed` | 单位字段 | 当前子弹飞行速度使用 |
| `bayonetSpeed` | 单位字段 | 白刃攻击冷却使用 |

## 羁绊字段

| 字段 | 含义 | 公式/规则 |
| --- | --- | --- |
| `traitName` | 羁绊名 | UI 显示和匹配辅助 |
| `thresholds` | 门槛数组 | 默认 `[2,4,6]`，有效档位取“已达到的最高下标” |
| `tierEffects` | 每档效果数组 | 下标应和 `thresholds` 对齐 |
| `damageBonusPercent` | 伤害增幅 | 当前资产字段存在 |
| `armorBonus` | 护甲加成 | 当前资产字段存在 |
| `attackIntervalReduction` | 攻击间隔缩短 | 当前资产字段存在 |
| `currentTraitCounts` | 当前羁绊人数账本 | 只统计战场上的我方存活单位，同名兵种去重 |

当前已有羁绊资产示例：

| 羁绊 | 门槛 | 当前配置 |
| --- | --- | --- |
| `重装甲` | `2 / 4 / 6` | `armorBonus = 2 / 4 / 6` |
| `黑十字帝国学联` | `1 / 2 / 3` | `damageBonusPercent = 20 / 40 / 60` |

注意：当前 `SynergyManager` 会把 `TraitEffect` 交给 `UnitLogic.ApplyTraitEffect(object effect)`，但 `ApplyTraitEffect` 是按 `effectType/type/statType/kind` 和 `value/amount/effectValue/modifierValue` 这种通用字段名读取的。现有 `TraitEffect` 字段名是 `damageBonusPercent / armorBonus / attackIntervalReduction`，按现在代码看，实际战斗加成有可能没有成功读取，需要单独修。

## 经济和商店字段

| 字段 | 默认/来源 | 含义 |
| --- | --- | --- |
| `funds` | `GameFlowManager` | 当前资金 |
| `populationLimit` | 默认 `5` | 当前 Cost 上限 |
| `currentStageIndex` | 默认 `1` | 当前关卡编号存档字段 |
| `defaultStageReward` | 默认 `10` | 没有地图节点时的胜利奖励 |
| `defaultDefeatReward` | 默认 `5` | 没有地图节点时的失败奖励 |
| `pendingStageReward` | 运行时 | 本次结算待发奖励，进入 Result 后发放 |
| `battleTimeLimitSeconds` | 默认 `90` | 战斗时间上限；当前超时算胜利 |
| `ShopSlotCount` | 常量 `5` | 商店货架数 |
| `MaxShopLevel` | 常量 `5` | 商店最高等级 |
| `UpgradeExpPurchaseCost` | 常量 `4` | 买一次经验的花费 |
| `UpgradeExpPerPurchase` | 常量 `4` | 买一次经验获得的经验 |
| `refreshCost` | 默认 `2` | 刷新商店费用 |
| `upgradeLevelCosts` | 默认 `[10,15,20,25]` | 1->2、2->3、3->4、4->5 需要的经验 |
| `maxCostLimits` | 默认 `[5,7,9,11,13]` | 各商店等级对应 Cost 上限 |
| `level` | 默认 `1` | 商店等级/后勤等级 |
| `currentExp` | 运行时 | 当前后勤经验 |
| `currentTotalCost` | 运行时 | 当前已上阵 Cost 总和 |
| `unitRemainingCounts` | 运行时 | 公共牌库里每个 ChessId 剩余数量 |
| `shopSlotChessIds` | 长度 5 | 每个货架当前单位 ChessId |
| `soldOutSlots` | 长度 5 | 每个货架是否已售出 |
| `isShopLocked` | 运行时 | 商店锁定状态 |

商店 CSV 字段：

| CSV | 字段 | 含义 |
| --- | --- | --- |
| `ShopPool.csv` | `UnitRare` | 费用阶/稀有度 |
| `ShopPool.csv` | `CardCount` | 该费用阶每个单位初始牌数 |
| `ShopProbability.csv` | `ShopLevel` | 商店等级 |
| `ShopProbability.csv` | `WeightT1..WeightT5` | 1-5 费抽卡权重 |

当前商店配置快照：

| 配置 | 当前值 |
| --- | --- |
| 牌数 | 1费 29，2费 22，3费 18，4费 12，5费 10 |
| 1级概率 | 100 / 0 / 0 / 0 / 0 |
| 2级概率 | 80 / 20 / 0 / 0 / 0 |
| 3级概率 | 55 / 35 / 10 / 0 / 0 |
| 4级概率 | 30 / 40 / 25 / 5 / 0 |
| 5级概率 | 15 / 25 / 35 / 20 / 5 |

经济公式：

| 行为 | 公式 |
| --- | --- |
| 加钱 | `funds += amount`，`amount <= 0` 时不处理 |
| 花钱 | `funds >= amount` 才成功，`amount <= 0` 直接成功 |
| 买单位 | 花费 `unitPrice`，并从公共牌库扣 1 张 |
| 商店刷新 | 花费 `refreshCost` 后重新生成 5 个货架 |
| 上阵 Cost 判断 | `currentTotalCost + max(0, additionalCost) <= CurrentMaxCostLimit` |
| 当前上阵 Cost | 战场容器内我方单位 `sum(max(0, unitCost))` |
| 修理费 | 优先用 `GameFlowManager.CalculateRepairCost` |
| Shop 退役退款 | `floor(unitPrice * currentHp / maxHp)`，并把牌还回公共牌库 |
| GameFlow 旧退役退款 | `ceil(unitPrice * 0.5)` |

## 地图和波次字段

地图 CSV：`MapNode.csv`

| 字段 | 含义 | 规则 |
| --- | --- | --- |
| `NodeId` | 节点 ID | 当前节点、连接、存档都用它 |
| `LayerIndex` | 层级 | 首层是 `0` |
| `BattleWaveIds` | 候选波次 ID 列表 | 选节点时随机抽一个 |
| `NextNodeId` | 后续节点 ID 列表 | 决定下一层哪些点可选 |
| `BaseReward` | 基础奖励 | 奖励公式的一部分 |
| `VictoryBonus` | 胜利奖励加值 | 奖励公式的一部分 |
| `DefeatBonus` | 失败补偿加值 | 奖励公式的一部分 |

当前地图配置快照：

| 节点 | 层 | 波次候选 | 下一节点 | 奖励 |
| --- | --- | --- | --- | --- |
| `N1001` | `0` | `W1001/W1002/W1003` | `N1002` | `10 + 胜5 / 败3` |
| `N1002` | `1` | `W1004/W1005/W1006` | 空 | `10 + 胜5 / 败3` |

地图运行时字段：

| 字段 | 默认/含义 |
| --- | --- |
| `currentNodeId` | 当前节点 ID |
| `currentLayerIndex` | 默认 `-1`，表示还没选过节点 |
| `currentNodeCompleted` | 当前节点是否已完成 |
| `currentBattleWaveId` | 当前战斗波次 ID |

节点可选规则：

1. 没有当前节点时，只能选 `LayerIndex == 0` 的节点。
2. 有当前节点时，必须当前节点已完成。
3. 目标节点必须是 `currentLayerIndex + 1`。
4. 目标节点必须在当前节点的 `NextNodeId/nextNodes` 连接里。

波次 CSV：`WaveNode.csv`

| 字段 | 含义 | 格式 |
| --- | --- | --- |
| `WaveId` | 波次 ID | 例如 `W1001` |
| `InitialEnemyConfigs` | 初始敌人 | `"{ChessId,x,y;ChessId,x,y}"` |
| `HaveBoss` | 是否有母巢 | 当前规则：`0=有母巢`，`1=无母巢` |
| `BossInfo` | 母巢信息 | `"{BossChessId,x,y,SpawnPoolChessId...}"` |
| `BossSpawn` | 母巢出兵参数 | `"{BaseSpawnInterval,EnrageAcceleration,MinSpawnInterval}"` |

敌人生成 Inspector 兜底字段：

| 字段 | 默认 | 含义 |
| --- | --- | --- |
| `gridPosition` | `(2, EnemyNestY)` | 初始敌人坐标 |
| `hiveGridPosition` | `(2, EnemyNestY)` | 母巢坐标 |
| `hiveMaxHp` | `800` | 旧 Prefab 波次母巢血量覆盖 |
| `hiveArmor` | `20` | 旧 Prefab 波次母巢护甲覆盖 |
| `hiveThreatValue` | `50` | 旧 Prefab 波次母巢威胁值覆盖 |
| `baseSpawnInterval` | `6` | 初始出兵间隔 |
| `enrageAcceleration` | `0.05` | 每秒缩短多少出兵间隔 |
| `minSpawnInterval` | `1.2` | 最小出兵间隔 |
| `spawnCountdown` | 运行时 | 下一次刷怪倒计时 |
| `lastSpawnX` | 默认 `-1` | 避免连续刷在同一 X |

当前波次配置快照：

| 波次 | 初始敌人 | Boss |
| --- | --- | --- |
| `W1001` | `E1011@(0,3)`，`E1131@(0,4)` | 无 |
| `W1002` | `E1011@(0,3)`，`E1011@(0,4)` | 无 |
| `W1003` | `E1131@(0,3)`，`E1131@(0,4)` | 无 |
| `W1004` | `E1012@(2,3)` | 无 |
| `W1005` | `E1011@(0,3)`，`E1131@(0,4)` | `E6011@(2,4)`，出兵池 `E1011`，出兵 `{6,0.05,5}` |
| `W1006` | `E1132@(2,3)` | 无 |

## 棋盘、部署、拖拽

| 字段 | 默认/值 | 含义 |
| --- | --- | --- |
| `BoardWidth` | `5` | 战场宽 |
| `BoardHeight` | `5` | 战场高 |
| `PlayerDeployMinY` | `0` | 玩家部署区最小 Y |
| `PlayerDeployMaxY` | `1` | 玩家部署区最大 Y |
| `StrategicLineY` | `2` | 战略线 Y |
| `EnemyNestY` | `4` | 敌方/母巢区域 Y |
| `MaxReserveSlots` | `9` | 备战席格数 |
| `neutralCapturePoints` | `(0..4, StrategicLineY)` | 默认战略线占点 |
| `useFullStrategicLineCapturePoints` | `true` | true 时整条战略线都可作为占点 |
| `dragSortingOrderBoost` | `1000` | 拖拽时临时提高 Sprite 排序 |
| `reserveYOffsetTolerance` | `0.5` | 放回备战席时允许的 Y 偏差 |

棋盘公式/规则：

| 行为 | 规则 |
| --- | --- |
| 战场合法格 | `0 <= x < BoardWidth` 且 `0 <= y < BoardHeight` |
| 玩家部署格 | 战场合法，且 `0 <= y <= 1` |
| 备战席合法格 | `0 <= x < 9` 且 `y == 0` |
| 世界坐标落格 | 先转容器本地坐标，再 `RoundToInt(x/y)` |
| 战场摆放 | `localPosition = (gridPos.x, gridPos.y, 0)` |
| 备战席摆放 | `localPosition = (reservePos.x, 0, 0)` |
| 最近战略线占点 | 在 `x=0..4, y=2` 里选距离最近的点 |
| 默认占点 | `(BoardWidth / 2, StrategicLineY)`，即当前 `(2,2)` |

## 子弹、占点、UI 和测试数值

| 字段 | 默认/含义 | 公式/规则 |
| --- | --- | --- |
| `BulletProjectile.maxLifeTime` | `5` 秒 | 超时销毁 |
| `BulletProjectile.speed` | 来自 `fireSpeed` | clamp 到 `>=0.01` |
| `BulletProjectile.damage` | 发射时锁定 | 命中指定目标才结算 |
| `CaptureZone.MaxProgressValue` | `100` | 占点区满值 |
| `CaptureZone.currentProgress` | 当前占点进度 | `+= totalCaptureSpeed * deltaTime` |
| `CombatLogManager.maxLogLines` | `80` | 战报最多保留行数 |
| `StageMapUIView.horizontalSpacing` | `180` | 地图节点横向间距 |
| `StageMapUIView.verticalSpacing` | `160` | 地图节点纵向间距 |
| `StageMapUIView.firstLayerAnchoredPosition` | `(0,0)` | 第一层节点 UI 起点 |
| `StageMapUIView.positiveLayerMovesDown` | `true` | 层级增加时 UI 往下排 |
| `StageMapUIView.neededHeight` | 运行时 | `abs(maxLayer-minLayer) * verticalSpacing + verticalSpacing` |
| `ShopSlotUI.slotIndex` | `0..4` | UI 货架索引 |
| `LogisticsPanelUI.expSlider.value` | 运行时 | `targetExp > 0 ? clamp01(currentExp / targetExp) : 0` |
| `BattleResultUIManager.canvasGroup.alpha` | `1/0` | 显示/隐藏结算面板 |
| `MapNodeButtonUI.lockedColor` | RGBA `0.35,0.35,0.35,0.45` | 锁定节点颜色 |
| `SynergyItemUI` 颜色字段 | 多个 RGBA | 只影响显示，不影响战斗 |
| `ShopTestController.TriggerDamageFirstUnit` | `30` | 测试按钮扣当前选中单位 30 血 |

## 当前需要特别留意的点

1. `FireRate` 和 `FireSpeed` 的实际职责容易混：远程开火冷却当前用 `fireRate`，子弹速度用 `fireSpeed`，但羁绊攻速逻辑改的是 `fireSpeed` 和 `bayonetSpeed`。
2. `DamageAoe` 字段已经在表和数据资产里，但核心战斗流程目前没有看到 AOE 伤害结算。
3. 羁绊资产的 `damageBonusPercent / armorBonus / attackIntervalReduction` 与 `UnitLogic.ApplyTraitEffect` 的通用反射读取方式不匹配，实际 Buff 生效需要修一下。
4. 退役有两个入口两套公式：`ShopManager.RetireUnit` 是按当前血量比例退钱，`GameFlowManager.TryRetireUnit` 是固定 50% 退钱。当前测试 UI 调的是 ShopManager 入口。
