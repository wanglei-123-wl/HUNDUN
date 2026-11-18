# 服务端权威移动对接协议

版本：`1.0`

日期：`2026-07-17`

适用范围：Unity 客户端与游戏服务端之间的玩家移动、朝向和基础鼠标动作同步。

## 1. 对接目标

客户端不再向服务端提交自己的最终坐标，而是提交玩家输入。服务端根据输入、服务端角色属性和烘焙地图数据计算权威位置。

完整流程：

```text
客户端采集键鼠输入
    → 客户端立即预测移动
    → 客户端发送 player_input
    → 服务端按固定 Tick 处理输入
    → 服务端计算权威位置、速度和状态
    → 服务端返回 player_authoritative_state
    → 客户端删除已确认输入
    → 客户端恢复权威状态并重放剩余输入
    → 客户端平滑显示纠偏结果
```

最终位置、移动速度、碰撞、攻击合法性和伤害结果均以服务端为准。

## 2. 传输格式

当前网络层使用：

- TCP 长连接。
- UTF-8 JSON。
- 每条消息末尾使用换行符 `\n` 分隔。
- 每条消息必须包含 `msgId` 和 `type`。
- 当前客户端按 `msgId` 分发消息。

示例：

```json
{"msgId":"player_input","type":"player_input","sequence":1,"clientTick":1,"clientTimeMs":1784283900123,"keys":1,"actions":0,"lookYaw":90}
```

实际网络数据末尾必须追加：

```text
\n
```

## 3. 玩家身份规则

`player_input` 不携带 `userId`。

服务端必须通过当前 TCP 连接对应的登录会话确定玩家身份：

```text
TCP Connection
    → Authenticated Session
    → Player Entity
```

禁止根据客户端自行提交的 `userId` 决定移动哪个玩家。

只有以下条件全部满足时才能处理输入：

- 当前连接已完成认证。
- 当前连接已完成 `enter_world`。
- 当前连接已绑定有效玩家实体。
- 玩家当前处于允许接收游戏输入的场景。

## 4. Tick 与时间规则

### 4.1 当前客户端输入频率

当前客户端默认：

```text
inputTickRate = 20 Hz
fixedInputDeltaTime = 1 / 20 = 0.05 秒
```

正常情况下，客户端每 `50ms` 产生一条 `player_input`。

服务端第一版建议同样使用：

```text
serverMovementTickRate = 20 Hz
serverFixedDeltaTime = 0.05 秒
```

### 4.2 移动时间来源

移动距离必须使用服务端固定时间计算：

```text
移动距离 = 服务端角色移动速度 × serverFixedDeltaTime
```

不得使用下面的值直接决定移动距离：

- TCP 消息到达间隔。
- `clientTimeMs` 的差值。
- 客户端帧率。
- 客户端提交的任意持续时间。

### 4.3 clientTimeMs

`clientTimeMs` 是客户端产生输入时的 UTC Unix 毫秒时间。

用途：

- 日志排查。
- 网络延迟分析。
- 客户端卡顿分析。
- 输入产生时间观测。

它不是安全可信时间，不能用于：

- 移动距离。
- 攻击冷却。
- 技能持续时间。
- 无敌时间。
- 服务端战斗结算。

### 4.4 clientTick

`clientTick` 从玩家本次进入世界后的 `1` 开始递增。

用途：

- 检查客户端输入逻辑帧是否连续。
- 辅助排查客户端卡顿。
- 辅助检测异常批量输入。

当前客户端中，`clientTick` 和 `sequence` 通常同时递增，但服务端仍应分别保存，不能假设未来永远相等。

### 4.5 serverTick

`serverTick` 是服务端自己的逻辑 Tick 编号。

要求：

- 必须由服务端生成。
- 单调递增。
- 不受客户端时间影响。
- 随 `player_authoritative_state` 返回。

## 5. 客户端上行：player_input

### 5.1 消息定义

```json
{
  "msgId": "player_input",
  "type": "player_input",
  "sequence": 105,
  "clientTick": 105,
  "clientTimeMs": 1784283900123,
  "keys": 9,
  "actions": 1,
  "lookYaw": 90.0
}
```

### 5.2 字段说明

| 字段 | 类型 | 必填 | 说明 |
|---|---|---:|---|
| `msgId` | string | 是 | 固定为 `player_input` |
| `type` | string | 是 | 固定为 `player_input` |
| `sequence` | int64 | 是 | 输入序号，本次进入世界后从 1 单调递增 |
| `clientTick` | int64 | 是 | 客户端输入逻辑 Tick |
| `clientTimeMs` | int64 | 是 | 客户端产生输入时的 UTC Unix 毫秒，仅供观测 |
| `keys` | int32 | 是 | 持续按键位掩码 |
| `actions` | int32 | 是 | 单次动作位掩码 |
| `lookYaw` | float32/float64 | 是 | 玩家水平朝向角，单位为度 |

## 6. keys 持续按键位掩码

| 按键 | 数值 | 二进制位 | 当前用途 |
|---|---:|---:|---|
| 无按键 | 0 | - | 停止移动 |
| W | 1 | `1 << 0` | 向角色前方移动 |
| A | 2 | `1 << 1` | 向角色左方移动 |
| S | 4 | `1 << 2` | 向角色后方移动 |
| D | 8 | `1 << 3` | 向角色右方移动 |
| Shift | 16 | `1 << 4` | 预留冲刺输入 |
| Space | 32 | `1 << 5` | 预留跳跃输入 |

判断某个键是否按住：

```text
(keys & KEY_VALUE) != 0
```

示例：

```text
keys = 1  → W
keys = 8  → D
keys = 9  → W + D
keys = 6  → A + S
keys = 0  → 没有移动键
```

### 6.1 当前版本限制

当前客户端预测移动只实现 W/A/S/D。

虽然客户端已经采集 Shift 和 Space，但协议 `1.0` 中服务端不得让它们影响实际速度或高度，否则客户端预测结果会与服务端不一致。

在双方同时完成冲刺或跳跃实现后，再启用对应功能。

## 7. actions 单次动作位掩码

| 动作 | 数值 | 二进制位 | 语义 |
|---|---:|---:|---|
| 无动作 | 0 | - | 本输入帧没有新动作 |
| 鼠标左键按下 | 1 | `1 << 0` | 本输入帧发生一次左键按下事件 |
| 鼠标右键按下 | 2 | `1 << 1` | 本输入帧发生一次右键按下事件 |

`actions` 表示单次边沿事件，不是持续按住状态。

例如：

```text
sequence=100 actions=1 → 尝试触发一次左键攻击
sequence=101 actions=0 → 没有新攻击
sequence=102 actions=0 → 没有新攻击
```

服务端不能因为上一帧 `actions=1`，就在后续 Tick 中重复触发攻击。

### 7.1 当前动作约定

- 鼠标左键：普通攻击请求。
- 鼠标右键：已采集但业务用途暂时预留。
- 服务端必须校验普通攻击冷却和玩家状态。
- 客户端可以预测播放攻击动画，但攻击是否成功、是否命中、伤害多少由服务端决定。

## 8. lookYaw 朝向

`lookYaw` 表示 Unity Y 轴水平旋转角，单位为度。

推荐规范化范围：

```text
[0, 360)
```

服务端必须检查：

- 不是 NaN。
- 不是正无穷或负无穷。
- 可以规范化为合法角度。

规范化示例：

```text
normalizedYaw = ((lookYaw % 360) + 360) % 360
```

当前移动方向以 `lookYaw` 为角色朝向：

```text
W → 朝向正前方
S → 朝向正后方
A → 朝向左方
D → 朝向右方
```

## 9. 服务端移动方向计算

先得到本地二维输入：

```text
localX = 0
localZ = 0

W → localZ += 1
S → localZ -= 1
D → localX += 1
A → localX -= 1
```

如果向量长度大于 1，必须归一化：

```text
length = sqrt(localX² + localZ²)

if length > 1:
    localX /= length
    localZ /= length
```

否则 W+D 斜向移动会比只按 W 快约 1.414 倍。

然后使用 `lookYaw` 将本地方向转换为世界方向，再应用服务端角色速度。

## 10. sequence 处理规则

服务端必须为每个玩家维护：

```text
lastReceivedInputSequence
lastProcessedInputSequence
pendingInputQueue
```

### 10.1 正常输入

```text
sequence > lastReceivedInputSequence
```

通过字段检查后加入输入队列。

### 10.2 重复或过期输入

```text
sequence <= lastReceivedInputSequence
```

不得重复执行。

服务端可以：

- 静默丢弃。
- 或重新下发当前最新 `player_authoritative_state`。

### 10.3 大幅跳号

例如：

```text
上一条 sequence = 100
新消息 sequence = 100000
```

服务端应记录异常，不应立即补算中间所有输入。

建议：

- 设置单次允许的最大 sequence 跳跃。
- 设置输入队列最大长度。
- 超过阈值时丢弃、重新同步或断开连接。

### 10.4 进入世界和重连

客户端在成功进入世界并启用输入时，会将 `sequence` 和 `clientTick` 重置为 0，第一条实际输入为 1。

服务端在新的 `enter_world` 会话确认后，也必须重置该玩家本次会话的输入序号状态。

## 11. 服务端处理顺序

服务端收到 `player_input` 时只做校验和入队，不要在网络收包线程中立即移动玩家。

推荐流程：

```text
收到 player_input
    → 根据连接找到玩家
    → 检查玩家是否已进入世界
    → 检查 sequence
    → 检查 keys/actions 非法位
    → 检查 lookYaw
    → 检查输入频率和队列长度
    → 加入玩家输入队列
```

服务端移动 Tick 中执行：

```text
取下一条输入
    → 更新朝向
    → 计算 W/A/S/D 方向
    → 斜向归一化
    → 应用角色状态限制
    → 使用服务端速度计算候选位置
    → 检查地图边界
    → 检查 walkable
    → 查询 heightfield
    → 检查 collider
    → 更新权威位置和速度
    → 处理鼠标动作
    → 更新 lastProcessedInputSequence
    → 生成 player_authoritative_state
```

## 12. 输入缺失和超时

即使当前使用 TCP，服务端仍应处理断线、卡顿和输入长时间未更新。

建议：

- 短暂缺少输入时，可以在极短时间内沿用最近的持续按键状态。
- 超过约 `200ms～300ms` 没有新输入时，清除 W/A/S/D，停止水平移动。
- `actions` 绝对不能沿用，只能执行一次。
- 玩家断开连接时立即清空输入队列和持续按键状态。

服务端不能在一个 Tick 中瞬间消费大量积压输入并全部产生位移。

建议第一版：

```text
每个服务端移动 Tick 最多消费一条正常输入帧
```

## 13. 服务端权威移动规则

服务端拥有以下数据的最终决定权：

- 基础移动速度。
- 移动速度加成和减速。
- 重力。
- 是否落地。
- 角色胶囊体尺寸。
- 地图边界。
- 地面高度。
- 可行走区域。
- 碰撞。
- 攻击期间是否允许移动。
- 玩家是否死亡、硬直或禁止输入。

客户端提交的输入只表示意图，不表示结果。

例如玩家按 W，但前方有墙：

- 该输入仍然可以视为已处理。
- `lastProcessedInputSequence` 正常推进。
- 服务端位置保持在墙前或执行滑墙结果。
- 客户端收到权威状态后自动纠偏。

不要因为玩家撞墙就不确认输入，否则客户端会一直保留并重放这条输入。

## 14. 服务端下行：player_authoritative_state

### 14.1 消息定义

```json
{
  "msgId": "player_authoritative_state",
  "type": "player_authoritative_state",
  "serverTick": 91820,
  "serverTimeMs": 1784283900200,
  "lastProcessedInputSequence": 105,
  "x": 12.5,
  "y": 1.2,
  "z": 30.7,
  "rotY": 90.0,
  "velocityX": 3.5355,
  "velocityY": 0.0,
  "velocityZ": 3.5355,
  "grounded": true,
  "movementState": 1,
  "actionState": 0,
  "actionSequence": 57
}
```

### 14.2 字段说明

| 字段 | 类型 | 必填 | 说明 |
|---|---|---:|---|
| `msgId` | string | 是 | 固定为 `player_authoritative_state` |
| `type` | string | 是 | 固定为 `player_authoritative_state` |
| `serverTick` | int64 | 是 | 产生该状态时的服务端逻辑 Tick |
| `serverTimeMs` | int64 | 是 | 服务端 UTC Unix 毫秒时间 |
| `lastProcessedInputSequence` | int64 | 是 | 服务端已经处理完成的最后一条输入序号 |
| `x` | float32/float64 | 是 | 服务端权威世界坐标 X |
| `y` | float32/float64 | 是 | 服务端权威世界坐标 Y |
| `z` | float32/float64 | 是 | 服务端权威世界坐标 Z |
| `rotY` | float32/float64 | 是 | 服务端权威水平朝向角，单位为度 |
| `velocityX` | float32/float64 | 是 | 服务端权威世界速度 X，单位/秒 |
| `velocityY` | float32/float64 | 是 | 服务端权威世界速度 Y，单位/秒 |
| `velocityZ` | float32/float64 | 是 | 服务端权威世界速度 Z，单位/秒 |
| `grounded` | bool | 是 | 服务端判定角色是否落地 |
| `movementState` | int32 | 是 | 当前移动状态 |
| `actionState` | int32 | 是 | 当前动作状态 |
| `actionSequence` | int64 | 是 | 服务端已接受动作的递增序号 |

## 15. movementState 约定

协议 `1.0` 定义：

| 值 | 名称 | 含义 |
|---:|---|---|
| 0 | Idle | 没有水平移动 |
| 1 | Moving | 正在进行普通水平移动 |

当前客户端把非零值视为正在移动。

未来增加冲刺、跳跃等状态时，需双方同步更新枚举定义。

## 16. actionState 约定

协议 `1.0` 定义：

| 值 | 名称 | 含义 |
|---:|---|---|
| 0 | None | 当前没有锁定移动的动作 |
| 1 | NormalAttack | 普通攻击进行中，当前客户端会锁定移动 |

当前客户端把任意非零 `actionState` 视为动作期间锁定移动。

因此服务端不能随意使用其他非零值表示“不锁移动”的状态；如果未来需要区分，需要同步升级客户端状态规则。

## 17. actionSequence 规则

`actionSequence` 由服务端维护，每当服务端接受一个新的单次动作时递增。

例如：

```text
玩家第一次合法攻击 → actionSequence=1
玩家第二次合法攻击 → actionSequence=2
攻击冷却中重复点击被拒绝 → actionSequence保持2
```

它用于远端动画和特效去重，不等同于输入 `sequence`。

## 18. 权威状态返回频率

建议：

```text
最低 10 Hz
推荐 20 Hz，与服务端移动 Tick 一致
```

每次下发必须携带当时最新的：

```text
lastProcessedInputSequence
```

服务端可以每处理一条输入就返回一次，也可以合并后按 10～20Hz 返回最新状态。

如果合并返回，必须确认到最新已处理输入，不能长期只返回旧 sequence。

## 19. 客户端收到权威状态后的行为

服务端需要理解客户端会这样使用返回值：

```text
1. 忽略比当前已处理状态更旧的权威消息
2. 删除 sequence <= lastProcessedInputSequence 的本地输入
3. 将本地逻辑状态恢复到服务端位置、速度和朝向
4. 按固定 50ms 重放仍未确认的输入
5. 对显示模型进行平滑纠偏
```

因此 `lastProcessedInputSequence` 必须准确。

如果服务端错误地确认了尚未处理的输入，客户端会提前删除输入。

如果服务端处理了输入却不推进该字段，客户端会不断重复重放旧输入。

## 20. 非法动作和受阻移动的确认方式

### 20.1 撞墙或地图阻挡

输入应正常消费并确认：

```text
lastProcessedInputSequence = 当前输入 sequence
位置 = 服务端碰撞后的合法位置
```

### 20.2 攻击冷却中点击

移动部分仍可正常处理；攻击请求忽略：

```text
lastProcessedInputSequence = 当前输入 sequence
actionState = 当前服务端真实动作状态
actionSequence 不递增
```

### 20.3 玩家死亡、硬直或禁止移动

输入仍应消费并确认，但服务端不产生非法移动：

```text
lastProcessedInputSequence = 当前输入 sequence
位置 = 当前合法权威位置
movementState = 0
actionState = 当前真实状态
```

### 20.4 完全畸形消息

例如无法解析 JSON、字段类型错误或包含非法浮点值：

- 不进入模拟队列。
- 记录安全日志。
- 根据策略累计异常次数。
- 必要时断开连接。

对于已经拥有合法 `sequence` 但业务上被拒绝的输入，建议仍消费该 sequence，避免客户端未确认队列永久积压。

## 21. 输入安全限制建议

服务端至少实现：

- 单连接每秒输入消息数量限制。
- 单玩家输入队列长度限制。
- sequence 重复检查。
- sequence 大幅跳跃检查。
- `keys` 非法位检查。
- `actions` 非法位检查。
- `lookYaw` 非法浮点检查。
- 输入超时自动停止移动。
- 每 Tick 最大消费输入数量限制。
- 服务端移动速度和加速度限制。
- 服务端地图边界和碰撞检查。
- 攻击冷却和角色状态检查。

协议 `1.0` 合法掩码：

```text
keys合法位总和 = 1 | 2 | 4 | 8 | 16 | 32 = 63
actions合法位总和 = 1 | 2 = 3
```

检查示例：

```text
if (keys & ~63) != 0:
    存在未定义按键位

if (actions & ~3) != 0:
    存在未定义动作位
```

## 22. 进入世界初始化

现有 `enter_world_result` 必须继续返回服务端出生位置：

```json
{
  "msgId": "enter_world_result",
  "type": "enter_world_result",
  "success": true,
  "userId": "player_001",
  "characterId": "character_001",
  "sceneId": "world_001",
  "x": 10.0,
  "y": 2.0,
  "z": 30.0,
  "rotY": 90.0,
  "serverTime": 1784283900000
}
```

客户端收到成功结果后会：

- 将本地角色放到服务端出生位置。
- 清空未确认输入。
- 将 sequence 和 clientTick 重置。
- 开始发送 `player_input`。

服务端也必须在本次进入世界时初始化：

```text
lastReceivedInputSequence = 0
lastProcessedInputSequence = 0
pendingInputQueue = empty
```

## 23. 当前远端玩家广播

本协议新增的 `player_authoritative_state` 主要用于发送给玩家本人，完成预测纠偏。

其他玩家目前仍然可以继续使用现有：

```text
player_state_broadcast
```

进行位置广播。

但远端玩家广播中的位置必须来自服务端权威状态，不能直接转发客户端提交的数据。

## 24. 完整交互示例

### 24.1 客户端按住 W

客户端发送：

```json
{
  "msgId": "player_input",
  "type": "player_input",
  "sequence": 1,
  "clientTick": 1,
  "clientTimeMs": 1784283900050,
  "keys": 1,
  "actions": 0,
  "lookYaw": 90.0
}
```

服务端处理后返回：

```json
{
  "msgId": "player_authoritative_state",
  "type": "player_authoritative_state",
  "serverTick": 5001,
  "serverTimeMs": 1784283900090,
  "lastProcessedInputSequence": 1,
  "x": 10.25,
  "y": 2.0,
  "z": 30.0,
  "rotY": 90.0,
  "velocityX": 5.0,
  "velocityY": 0.0,
  "velocityZ": 0.0,
  "grounded": true,
  "movementState": 1,
  "actionState": 0,
  "actionSequence": 0
}
```

### 24.2 客户端 W+D 并点击左键

客户端发送：

```json
{
  "msgId": "player_input",
  "type": "player_input",
  "sequence": 20,
  "clientTick": 20,
  "clientTimeMs": 1784283901000,
  "keys": 9,
  "actions": 1,
  "lookYaw": 90.0
}
```

如果服务端接受攻击并规定攻击期间不能移动：

```json
{
  "msgId": "player_authoritative_state",
  "type": "player_authoritative_state",
  "serverTick": 5020,
  "serverTimeMs": 1784283901040,
  "lastProcessedInputSequence": 20,
  "x": 14.5,
  "y": 2.0,
  "z": 32.0,
  "rotY": 90.0,
  "velocityX": 0.0,
  "velocityY": 0.0,
  "velocityZ": 0.0,
  "grounded": true,
  "movementState": 0,
  "actionState": 1,
  "actionSequence": 6
}
```

### 24.3 玩家撞墙

客户端继续发送 W，但服务端检测到墙体。

服务端仍然确认输入：

```json
{
  "msgId": "player_authoritative_state",
  "type": "player_authoritative_state",
  "serverTick": 5030,
  "serverTimeMs": 1784283901540,
  "lastProcessedInputSequence": 30,
  "x": 15.0,
  "y": 2.0,
  "z": 32.0,
  "rotY": 90.0,
  "velocityX": 0.0,
  "velocityY": 0.0,
  "velocityZ": 0.0,
  "grounded": true,
  "movementState": 0,
  "actionState": 0,
  "actionSequence": 6
}
```

## 25. 服务端完成标准

服务端完成以下项目后，客户端和服务端可以开始正式联调：

- 能解析换行分隔的 `player_input` JSON。
- 通过连接会话找到玩家，不依赖客户端 `userId`。
- 维护每个玩家的输入序号和输入队列。
- 使用固定 Tick 处理输入。
- 能根据 W/A/S/D 和 `lookYaw` 计算方向。
- 斜向移动已归一化。
- 移动速度来自服务端配置。
- 能使用烘焙地图检查边界、高度、可行走和碰撞。
- 撞墙等受阻输入仍会推进 `lastProcessedInputSequence`。
- 能返回完整 `player_authoritative_state`。
- 返回的 `msgId` 精确为 `player_authoritative_state`。
- `lastProcessedInputSequence` 与实际处理进度一致。
- 输入超时后能够停止角色移动。
- 鼠标左键事件只消费一次。
- 攻击是否合法由服务端决定。
- 服务端广播给其他玩家的位置来自权威状态。

## 26. 客户端对应代码

协议消息定义：

```text
Assets/Scripts/NetWork/NetMessages.cs
```

输入位定义：

```text
Assets/Scripts/Player/AuthorityMovement/PlayerInputFrame.cs
```

输入采集和发送：

```text
Assets/Scripts/Player/AuthorityMovement/LocalPlayerInputController.cs
```

本地预测：

```text
Assets/Scripts/Player/AuthorityMovement/PredictedCharacterMotor.cs
```

权威纠偏和输入重放：

```text
Assets/Scripts/Player/AuthorityMovement/LocalPlayerReconciler.cs
```

消息注册和角色组件绑定：

```text
Assets/Scripts/GameSession.cs
```

## 附录 A：开工确认参数

以下参数为协议 `1.0` 的确定值，服务端第一版按此实现。

### A.1 普通移动速度

```text
普通移动速度 = 5.0 Unity单位/秒
```

客户端 `PredictedCharacterMotor` 会从原 `ThirdPersonMove.moveSpeed` 复制该值，当前默认值为 `5f`。

服务端必须使用服务端角色配置中的 `5.0` 进行权威计算，不能接受客户端提交速度。

如果以后策划调整速度，需要同时修改服务端角色配置和客户端预测配置。

### A.2 输入队列最大长度

```text
服务端单玩家待处理输入队列最大长度 = 20条
客户端未确认输入历史最大长度 = 60条
```

在 20Hz 下：

```text
服务端20条 = 1秒输入积压
客户端60条 = 3秒纠偏重放历史
```

服务端队列达到 20 条后，不得继续无上限积压，也不得在一个 Tick 中瞬间补执行全部输入。

第一版推荐处理：

- 将该连接标记为输入同步异常。
- 停止继续接受移动输入。
- 断开连接或要求重新进入世界，使双方 sequence 从 0 重新初始化。
- 不要通过高速消费队列让玩家瞬间移动。

### A.3 sequence 最大跳号

当前协议使用 TCP 单条顺序消息，并且客户端只有在发送成功后才会提交本地 sequence。

因此协议 `1.0` 使用严格连续规则：

```text
期望差值 = newSequence - lastReceivedInputSequence
正常值 = 1
单次允许的最大前跳差值 = 1
```

处理规则：

```text
newSequence == lastReceived + 1 → 正常入队
newSequence <= lastReceived     → 重复或过期，丢弃
newSequence > lastReceived + 1  → 输入链路失步，拒绝并触发重新同步
```

首次进入世界：

```text
lastReceivedInputSequence = 0
第一条输入 sequence 必须为 1
```

如果未来改成 UDP、批量输入包或冗余重发，再升级该规则；当前版本不需要为不存在的丢包场景放宽跳号。

## 附录 B：colliders.bin 二进制格式

### B.1 基础格式

```text
文件魔数：GCOL
文件版本：1
文件头大小：20字节
单条记录大小：48字节
字节序：Little Endian
浮点格式：IEEE 754 float32
坐标系统：UnityLeftHanded_YUp
```

当前实际文件：

```text
Collider数量：1946
Box记录：1946
Sphere记录：0
Capsule记录：0
TriangleMesh记录：0
文件大小：93428字节
计算：20 + 1946 × 48 = 93428
```

### B.2 文件头布局

| 偏移 | 大小 | 类型 | 字段 | 说明 |
|---:|---:|---|---|---|
| 0 | 4 | byte[4] | magic | ASCII `GCOL`，字节为 `47 43 4F 4C` |
| 4 | 2 | uint16 | version | 当前固定为 `1` |
| 6 | 2 | uint16 | headerSize | 当前固定为 `20` |
| 8 | 4 | uint32 | colliderCount | 碰撞体记录数量 |
| 12 | 4 | uint32 | recordBytes | 当前固定为 `48` |
| 16 | 4 | uint32 | reserved | 当前写入 `0`，服务端读取但忽略 |

第一条碰撞记录从文件偏移 `20` 开始。

### B.3 单条记录布局

每条记录固定 `48` 字节，不包含字符串名称。

| 记录内偏移 | 文件首条绝对偏移 | 大小 | 类型 | 字段 |
|---:|---:|---:|---|---|
| 0 | 20 | 4 | uint32 | colliderId |
| 4 | 24 | 4 | uint32 | colliderType |
| 8 | 28 | 4 | uint32 | flags |
| 12 | 32 | 4 | float32 | centerX |
| 16 | 36 | 4 | float32 | centerY |
| 20 | 40 | 4 | float32 | centerZ |
| 24 | 44 | 4 | float32 | rotationX |
| 28 | 48 | 4 | float32 | rotationY |
| 32 | 52 | 4 | float32 | rotationZ |
| 36 | 56 | 4 | float32 | sizeX |
| 40 | 60 | 4 | float32 | sizeY |
| 44 | 64 | 4 | float32 | sizeZ |

后续记录位置：

```text
recordOffset = 20 + recordIndex × 48
```

其中 `recordIndex` 从 0 开始。

### B.4 colliderType

| 数值 | 类型 | 当前是否导出 |
|---:|---|---:|
| 1 | Box | 是 |
| 2 | Sphere | 是 |
| 3 | Capsule | 是，仅导出 Unity direction=Y 的胶囊体 |
| 4 | TriangleMesh | 枚举已保留，但当前收集器不导出 |

当前导出器还会忽略：

- `isTrigger=true` 的碰撞体。
- 被禁用的碰撞体。
- `TerrainCollider`。
- 不在服务端碰撞 LayerMask 中的碰撞体。
- 带有 `ServerRegion` 的区域碰撞体。
- 非当前场景碰撞体。
- 不与烘焙世界边界相交的碰撞体。
- X轴或Z轴方向的 CapsuleCollider。
- 当前版本中的 MeshCollider。

### B.5 Box 字段语义

```text
center   = BoxCollider中心转换后的世界坐标
rotation = Transform世界旋转转换后的标准化欧拉角，单位为度
size     = BoxCollider.size乘绝对lossyScale后的世界完整尺寸
```

`size` 是完整长宽高，不是 half extents。

服务端需要时可计算：

```text
halfExtents = size × 0.5
```

### B.6 Sphere 字段语义

```text
center   = SphereCollider中心转换后的世界坐标
rotation = (0, 0, 0)
size     = (diameter, diameter, diameter)
```

其中：

```text
diameter = radius × 2 × max(abs(scaleX), abs(scaleY), abs(scaleZ))
服务端半径 = sizeX × 0.5
```

### B.7 Capsule 字段语义

当前只导出 `CapsuleCollider.direction == 1`，即本地 Y 轴胶囊体。

```text
center   = CapsuleCollider中心转换后的世界坐标
rotation = Transform世界旋转转换后的标准化欧拉角，单位为度
sizeX    = diameter
sizeY    = fullHeight
sizeZ    = diameter
```

计算方式：

```text
radius = collider.radius × max(abs(scaleX), abs(scaleZ))
height = max(collider.height × abs(scaleY), radius × 2)
size = (radius × 2, height, radius × 2)
```

服务端恢复：

```text
radius = sizeX × 0.5
fullHeight = sizeY
cylinderSegmentHeight = max(0, fullHeight - radius × 2)
```

### B.8 flags 和名称

当前所有导出的 `flags` 都为 `0`，字段为未来版本预留。

`ColliderRecordData` 内部虽然存在 `Name`，但 `colliders.bin` 当前不会写入名称字符串，因此服务端不能从二进制文件读取层级名称。

### B.9 服务端读取伪代码

```text
read 4 bytes magic
assert magic == "GCOL"

version = readUInt16LE()
headerSize = readUInt16LE()
count = readUInt32LE()
recordBytes = readUInt32LE()
reserved = readUInt32LE()

assert version == 1
assert headerSize == 20
assert recordBytes == 48

for i in 0 .. count-1:
    colliderId = readUInt32LE()
    colliderType = readUInt32LE()
    flags = readUInt32LE()

    center = readFloat3LE()
    rotationEulerDegrees = readFloat3LE()
    size = readFloat3LE()
```

服务端读取前应校验文件大小至少满足：

```text
expectedBytes = headerSize + colliderCount × recordBytes
```

## 附录 C：玩家权威碰撞体

### C.1 实际生效的碰撞体来源

本地玩家的权威逻辑对象是运行时创建的：

```text
LocalPlayerRoot
```

`GameSession` 会在该对象上动态添加一个 Unity `CharacterController`，角色模型预制体只是作为视觉子节点挂到 `LocalPlayerRoot` 下。

角色模型预制体中自带的 `CharacterController` 会被禁用，因此不能把模型预制体上的碰撞参数当成当前玩家移动碰撞参数。

当前所有角色统一使用同一套 `LocalPlayerRoot` 玩家胶囊体，不区分 characterId。

### C.2 玩家位置语义

协议中的玩家权威位置：

```text
x, y, z
```

表示 `LocalPlayerRoot.transform.position`，不是胶囊体中心位置，也不是模型骨骼位置。

服务端必须先使用玩家根位置加上胶囊体中心偏移，得到实际胶囊体中心。

### C.3 确认参数

```text
Collider类型：Vertical Capsule，Y轴竖直胶囊体
Height：2.0
Radius：0.5
Center Offset：(0.0, 1.1, 0.0)
Slope Limit：45度
Step Offset：0.3
Skin Width：0.08
Min Move Distance：0.001
Detect Collisions：true
Enable Overlap Recovery：true
```

移动相关参数：

```text
普通移动速度：5.0单位/秒
重力：-9.8单位/秒²
落地时向下保持速度：-2.0单位/秒
```

当前角色根节点缩放为：

```text
(1, 1, 1)
```

服务端不需要再对玩家胶囊体尺寸乘额外缩放。

### C.4 胶囊体世界空间计算

设服务端玩家根位置为：

```text
P = (playerX, playerY, playerZ)
```

胶囊体中心：

```text
C = P + (0, 1.1, 0)
```

胶囊体参数：

```text
radius = 0.5
height = 2.0
halfHeight = 1.0
segmentHalfLength = height / 2 - radius = 0.5
```

上下半球球心：

```text
bottomSphereCenter = C - (0, 0.5, 0)
                  = P + (0, 0.6, 0)

topSphereCenter = C + (0, 0.5, 0)
               = P + (0, 1.6, 0)
```

胶囊体最低点和最高点：

```text
capsuleBottomY = playerY + 0.1
capsuleTopY = playerY + 2.1
```

因此服务端如果使用“两球心 + 半径”的胶囊体表示，应使用：

```text
segmentStart = (playerX, playerY + 0.6, playerZ)
segmentEnd   = (playerX, playerY + 1.6, playerZ)
radius       = 0.5
```

### C.5 水平移动碰撞

普通地面移动时，服务端使用该竖直胶囊体从当前位置向候选位置执行 Capsule Sweep。

建议处理顺序：

```text
1. 根据W/A/S/D计算水平位移
2. 使用玩家胶囊体做水平Sweep
3. 如果命中墙体，截断位移
4. 将剩余位移投影到碰撞平面，执行滑墙
5. 处理Step Offset
6. 处理坡度限制
7. 查询地面并更新最终Y
```

### C.6 Skin Width

Unity 客户端当前 `Skin Width = 0.08`。

服务端可以选择：

- 将它作为碰撞容差。
- 或在Sweep时使用略小于实际半径的内部检测半径。

第一版建议服务端保持明确的实体半径：

```text
实体Radius = 0.5
Skin/Tolerance = 0.08
```

不要直接把玩家权威半径改成 `0.42`；服务端实体边界仍应按 `0.5` 管理，`0.08` 只作为碰撞求解容差。

### C.7 Step Offset

```text
Step Offset = 0.3
```

表示玩家允许尝试跨越最大约 `0.3` 单位的台阶。

服务端不能只检查候选点是否被墙阻挡，还需要在水平阻挡较低时尝试：

```text
向上抬升最多0.3
    → 再执行水平移动
    → 再向下落到合法地面
```

如果上方空间不足或落点不可行走，则台阶移动失败。

### C.8 Slope Limit

```text
Slope Limit = 45度
```

服务端根据地面法线计算坡度：

```text
slopeAngle = acos(dot(groundNormal, worldUp))
```

规则：

```text
slopeAngle <= 45度 → 可以作为正常可站立地面
slopeAngle > 45度  → 不允许正常向上行走，应阻挡或下滑
```

### C.9 Grounded 建议

客户端落地时会将垂直速度设为约 `-2.0`，用于让 `CharacterController` 稳定贴地。

服务端建议使用：

```text
groundProbeDistance = Skin Width + 少量容差
```

判断胶囊体下方是否存在合法地面。

服务端返回的 `grounded` 必须来自服务端检测结果，不能接受客户端提交。

### C.10 与地图碰撞数据的关系

玩家碰撞体与地图 `colliders.bin` 是两类不同数据：

```text
玩家碰撞体：运行时动态胶囊体
地图碰撞体：colliders.bin中的静态Box/Sphere/Capsule记录
```

当前地图 `colliders.bin` 实际包含 `1946` 个 Box 记录，因此第一版主要碰撞是：

```text
玩家Vertical Capsule vs 地图Oriented Box
```

地图Box的 `rotation` 是世界欧拉角，服务端需要构建 OBB，而不是全部当作无旋转AABB。

### C.11 角色预制体差异说明

当前角色视觉预制体中确实存在不同的 `CharacterController` 配置，例如部分模型为：

```text
Height = 1.6
Radius = 0.5
Center = (0, 0.7, -0.15)
```

但这些预制体控制器在本地角色实例化后会被禁用，不参与实际移动和碰撞。

协议 `1.0` 后端不要按角色模型预制体分别配置胶囊体，应统一使用本附录确认的：

```text
Height = 2.0
Radius = 0.5
Center Offset = (0, 1.1, 0)
```
