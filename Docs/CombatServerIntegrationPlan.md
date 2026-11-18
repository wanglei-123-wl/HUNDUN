# Unity 客户端战斗联调改造步骤

版本：`v1.1`

日期：`2026-07-21`

范围：只描述 Unity 客户端需要怎么改，以及每个步骤对应的发送/接收消息格式。本文不包含服务端改造方案。

## 0. 联调目标

客户端完成后，应支持以下联调场景：

- 玩家点击视野内怪物，客户端发送 `combat_action`。
- 服务端返回 `combat_event=attack/damage/death/respawn` 后，客户端播放攻击、受击、死亡、复活表现。
- 服务端返回 `combat_state_upsert` 后，客户端按服务端绝对 HP 刷新玩家和怪物血条。
- AOI 内其他玩家能看到攻击者动作、目标 HP、受击和死亡表现。
- 客户端对带 `reliableMessageId` 的关键广播发送 `battle_outbox_ack`。

## 1. 补全网络消息模型

### 客户端改动

修改 `Assets/Scripts/NetWork/NetMessages.cs`。

新增发送模型：

- `CombatActionRequest`
- `BattleOutboxAckRequest`

给已有接收模型补字段：

- `PlayerStateBroadcast.reliableMessageId`
- `PlayerEnterBroadcast.reliableMessageId`
- `PlayerLeaveMessage.reliableMessageId`
- `MonsterUpsertMessage.reliableMessageId`
- `MonsterRemoveMessage.reliableMessageId`
- `CombatStateUpsertMessage.reliableMessageId`
- `CombatEventMessage.reliableMessageId`

### 发送消息格式：combat_action

客户端点击怪物普通攻击时发送：

```json
{
  "msgId": "combat_action",
  "type": "combat_action",
  "actionSequence": 1,
  "clientTick": 120,
  "actionId": "basic_attack",
  "targetType": "monster",
  "targetId": "mon_map001_i0000_sg003_Ls001_000001",
  "aimYaw": 90.0
}
```

字段规则：

| 字段 | 客户端填写规则 |
| --- | --- |
| `msgId` | 固定 `combat_action` |
| `type` | 固定 `combat_action` |
| `actionSequence` | 独立战斗动作序号，从 1 开始递增，不复用移动 `sequence` |
| `clientTick` | 当前客户端逻辑 Tick，必须大于 0 |
| `actionId` | 普通攻击固定 `basic_attack` |
| `targetType` | PvE 联调先固定 `monster` |
| `targetId` | 怪物实例 ID，即服务端下发的 `monsterInstanceId` |
| `aimYaw` | 当前攻击朝向，有限浮点数 |

### 发送消息格式：battle_outbox_ack

客户端成功应用可靠广播后发送：

```json
{
  "msgId": "battle_outbox_ack",
  "type": "battle_outbox_ack",
  "reliableMessageId": 101
}
```

字段规则：

| 字段 | 客户端填写规则 |
| --- | --- |
| `msgId` | 固定 `battle_outbox_ack` |
| `type` | 固定 `battle_outbox_ack` |
| `reliableMessageId` | 从收到的广播消息里原样取值，必须大于 0 |

### 接收消息字段要求

所有可靠广播都可能带：

```json
{
  "reliableMessageId": 101
}
```

客户端处理规则：

- `reliableMessageId > 0`：业务处理完成后回 ACK。
- `reliableMessageId <= 0` 或字段不存在：不回 ACK。
- 重复消息也要幂等处理；如果确认已处理，可以继续 ACK。

## 2. NetworkManager 增加发送接口

### 客户端改动

修改 `Assets/Scripts/NetWork/NetworkManager.cs`。

新增：

- `SendCombatAction(CombatActionRequest request)`
- `SendBattleOutboxAck(long reliableMessageId)`

不要把攻击继续塞到 `SendPlayerInput`。

### 发送消息格式：combat_action

`SendCombatAction` 发送的 JSON 必须是：

```json
{
  "msgId": "combat_action",
  "type": "combat_action",
  "actionSequence": 2,
  "clientTick": 121,
  "actionId": "basic_attack",
  "targetType": "monster",
  "targetId": "mon_map001_i0000_sg003_Ls001_000001",
  "aimYaw": 91.5
}
```

### 发送消息格式：battle_outbox_ack

`SendBattleOutboxAck` 发送的 JSON 必须是：

```json
{
  "msgId": "battle_outbox_ack",
  "type": "battle_outbox_ack",
  "reliableMessageId": 102
}
```

### 不再这样发送攻击

不要再依赖下面这种 `player_input.actions` 触发真实伤害：

```json
{
  "msgId": "player_input",
  "type": "player_input",
  "sequence": 30,
  "clientTick": 120,
  "actions": 4
}
```

`player_input` 继续只负责移动、朝向、跳跃等输入。

## 3. 新增本地战斗输入控制器

### 客户端改动

新增脚本，建议命名：`LocalPlayerCombatController.cs`。

职责：

- 监听攻击键上升沿，例如鼠标左键点击。
- 判断当前是否允许攻击。
- 从鼠标射线命中的怪物读取 `monsterInstanceId`。
- 维护独立 `nextCombatActionSequence`。
- 组装并发送 `combat_action`。
- 发送失败时不跳号。

允许攻击条件：

- TCP 已连接。
- 已完成登录和 `enter_world`。
- 本地玩家处于 Alive。
- 当前没有打开会吞输入的 UI。
- 当前选中了合法怪物目标。

### 发送消息格式

点击怪物后发送：

```json
{
  "msgId": "combat_action",
  "type": "combat_action",
  "actionSequence": 1,
  "clientTick": 120,
  "actionId": "basic_attack",
  "targetType": "monster",
  "targetId": "mon_map001_i0000_sg003_Ls001_000001",
  "aimYaw": 90.0
}
```

### 接收消息格式

本步骤本身不直接接收消息，但发送后客户端应等待服务端广播：

```json
{
  "msgId": "combat_event",
  "type": "combat_event",
  "eventType": "attack",
  "actionSequence": 1,
  "source": { "entityType": "player", "entityId": "10001" },
  "target": { "entityType": "monster", "entityId": "mon_map001_i0000_sg003_Ls001_000001" }
}
```

收到这个 `attack` 事件，才代表服务端接受了这次攻击动作。

## 4. 怪物目标拾取

### 客户端改动

修改或扩展怪物预制体和 `MonsterNetworkView`。

需要做到：

- 怪物预制体有 Collider。
- 鼠标 Raycast 能命中怪物。
- 命中的对象能找到 `MonsterNetworkView`。
- `MonsterNetworkView.EntityId` 返回服务端下发的 `monsterInstanceId`。

### 接收消息格式：monster_snapshot

怪物首次进入视野时，客户端会收到类似：

```json
{
  "msgId": "monster_snapshot",
  "type": "monster_snapshot",
  "sceneId": "world_001",
  "serverTime": 1784544000000,
  "monsters": [
    {
      "monsterInstanceId": "mon_map001_i0000_sg003_Ls001_000001",
      "monsterId": "Ls001",
      "prefabId": "Ls001",
      "name": "Slime",
      "x": 10.0,
      "y": 0.0,
      "z": 12.0,
      "rotY": 180.0,
      "hp": 100,
      "maxHp": 100,
      "state": "Idle",
      "aoiRevision": 1,
      "combatRevision": 0,
      "spawnGeneration": 0,
      "lifeState": "Alive"
    }
  ]
}
```

客户端要把 `monsterInstanceId` 保存到怪物视图上，后续点击时作为 `combat_action.targetId`。

### 接收消息格式：monster_upsert

怪物更新时，客户端会收到类似：

```json
{
  "msgId": "monster_upsert",
  "type": "monster_upsert",
  "reliableMessageId": 201,
  "sceneId": "world_001",
  "aoiRevision": 2,
  "serverTime": 1784544000500,
  "monster": {
    "monsterInstanceId": "mon_map001_i0000_sg003_Ls001_000001",
    "monsterId": "Ls001",
    "prefabId": "Ls001",
    "x": 10.0,
    "y": 0.0,
    "z": 12.0,
    "rotY": 180.0,
    "hp": 90,
    "maxHp": 100,
    "state": "Idle",
    "aoiRevision": 2,
    "combatRevision": 1,
    "spawnGeneration": 0,
    "lifeState": "Alive"
  }
}
```

处理完成后回 ACK：

```json
{
  "msgId": "battle_outbox_ack",
  "type": "battle_outbox_ack",
  "reliableMessageId": 201
}
```

## 5. 建立战斗实体视图注册表

### 客户端改动

新增一个运行时注册表，建议命名：`CombatEntityViewRegistry.cs`。

它负责把协议里的实体 ID 映射到 Unity 场景对象。

需要支持的 key：

| entityType | entityId 来源 | Unity 对象 |
| --- | --- | --- |
| `player` | 玩家 `characterDbId` 字符串 | 本地玩家或远端玩家 |
| `monster` | `monsterInstanceId` | 怪物对象 |

建议每个可战斗对象实现类似接口：

```csharp
public interface ICombatEntityView
{
    string EntityType { get; }
    string EntityId { get; }
    void PlayAttack(string actionId, long actionSequence);
    void PlayHit(int damage, bool critical);
    void PlayDeath();
    void PlayRespawn();
    void ApplyHealth(int hp, int maxHp);
}
```

注册时机：

- 本地玩家进场完成后注册 `player:{localCharacterDbId}`。
- `EntityManager` 创建远端玩家后注册 `player:{characterDbId}`。
- `MonsterSnapshotManager` 创建怪物后注册 `monster:{monsterInstanceId}`。

注销时机：

- 远端玩家离开 AOI。
- 怪物被 `monster_remove` 移除。
- 重新进场或切场景。

### 接收消息格式

注册表主要服务下面的 combat event：

```json
{
  "msgId": "combat_event",
  "type": "combat_event",
  "eventType": "damage",
  "source": { "entityType": "player", "entityId": "10001" },
  "target": { "entityType": "monster", "entityId": "mon_map001_i0000_sg003_Ls001_000001" }
}
```

客户端通过 `source` 找攻击者，通过 `target` 找受击者。

## 6. 处理 combat_event=attack

### 客户端改动

修改 `Assets/Scripts/NetWork/CombatMessageReceiver.cs`。

当前不能继续忽略 `eventType == "attack"`。要改成：

- 去重 `sceneId + eventId`。
- 根据 `source` 找到实体视图。
- 调用 `PlayAttack(actionId, actionSequence)`。
- 如果源实体不在当前 AOI，不补播历史动画。
- 处理完成后 ACK。

### 接收消息格式：玩家攻击怪物

```json
{
  "msgId": "combat_event",
  "type": "combat_event",
  "reliableMessageId": 301,
  "sceneId": "world_001",
  "eventId": 8801,
  "eventType": "attack",
  "actionId": "basic_attack",
  "actionSequence": 1,
  "source": { "entityType": "player", "entityId": "10001" },
  "target": { "entityType": "monster", "entityId": "mon_map001_i0000_sg003_Ls001_000001" },
  "damage": 0,
  "critical": false,
  "hpAfter": 0,
  "maxHp": 0,
  "lethal": false,
  "combatRevision": 0,
  "serverTick": 4510,
  "serverTime": 1784544000450
}
```

处理表现：

- `source=player:10001` 是本地玩家：确认本地攻击动作，可修正预测表现。
- `source=player:10001` 是远端玩家：播放远端玩家攻击动作。
- `target=monster:xxx` 不在场景内：只 ACK，不补播。

ACK：

```json
{
  "msgId": "battle_outbox_ack",
  "type": "battle_outbox_ack",
  "reliableMessageId": 301
}
```

### 接收消息格式：怪物攻击玩家

```json
{
  "msgId": "combat_event",
  "type": "combat_event",
  "reliableMessageId": 302,
  "sceneId": "world_001",
  "eventId": 8802,
  "eventType": "attack",
  "actionId": "monster_basic_attack",
  "actionSequence": 12,
  "source": { "entityType": "monster", "entityId": "mon_map001_i0000_sg003_Ls001_000001" },
  "target": { "entityType": "player", "entityId": "10001" },
  "serverTick": 4511,
  "serverTime": 1784544000500
}
```

处理表现：

- 找到怪物视图，播放怪物攻击动作。
- 不在 `attack` 事件里扣玩家 HP，等待 `damage` 或 `combat_state_upsert`。

ACK：

```json
{
  "msgId": "battle_outbox_ack",
  "type": "battle_outbox_ack",
  "reliableMessageId": 302
}
```

## 7. 处理 combat_event=damage

### 客户端改动

修改 `CombatMessageReceiver`，并调用 `CombatStateStore.MergeEventHealth(...)`。

收到 `damage` 后要做：

- 去重 `sceneId + eventId`。
- 用 `target`、`hpAfter`、`maxHp`、`combatRevision` 更新 `CombatStateStore`。
- 找到目标实体视图，调用 `ApplyHealth(hpAfter, maxHp)`。
- 播放目标受击表现和伤害飘字。
- 如果 `lethal=true`，可以立即进入死亡表现，后续 `death` 事件再幂等确认。
- 处理完成后 ACK。

### 接收消息格式：玩家打怪扣血

```json
{
  "msgId": "combat_event",
  "type": "combat_event",
  "reliableMessageId": 401,
  "sceneId": "world_001",
  "eventId": 8803,
  "eventType": "damage",
  "actionId": "basic_attack",
  "actionSequence": 1,
  "source": { "entityType": "player", "entityId": "10001" },
  "target": { "entityType": "monster", "entityId": "mon_map001_i0000_sg003_Ls001_000001" },
  "damage": 10,
  "critical": false,
  "hpAfter": 90,
  "maxHp": 100,
  "lethal": false,
  "combatRevision": 1,
  "serverTick": 4512,
  "serverTime": 1784544000550
}
```

客户端处理结果：

- 怪物 HP 显示 `90 / 100`。
- 怪物播放受击表现。
- 怪物头顶飘字 `10`。
- 三个在 AOI 内的客户端都应显示同样 HP。

ACK：

```json
{
  "msgId": "battle_outbox_ack",
  "type": "battle_outbox_ack",
  "reliableMessageId": 401
}
```

### 接收消息格式：怪物打玩家扣血

```json
{
  "msgId": "combat_event",
  "type": "combat_event",
  "reliableMessageId": 402,
  "sceneId": "world_001",
  "eventId": 8804,
  "eventType": "damage",
  "actionId": "monster_basic_attack",
  "actionSequence": 12,
  "source": { "entityType": "monster", "entityId": "mon_map001_i0000_sg003_Ls001_000001" },
  "target": { "entityType": "player", "entityId": "10001" },
  "damage": 9,
  "critical": false,
  "hpAfter": 991,
  "maxHp": 1000,
  "lethal": false,
  "combatRevision": 8,
  "serverTick": 4513,
  "serverTime": 1784544000600
}
```

客户端处理结果：

- 如果 `target` 是本地玩家，刷新本地 HUD 血条。
- 如果 `target` 是远端玩家，刷新远端玩家头顶血条。
- 目标玩家播放受击表现。

ACK：

```json
{
  "msgId": "battle_outbox_ack",
  "type": "battle_outbox_ack",
  "reliableMessageId": 402
}
```

## 8. 处理 combat_event=death

### 客户端改动

收到 `death` 后：

- 去重 `sceneId + eventId`。
- 用 `target` 更新 HP 为 `hpAfter`，通常是 0。
- 目标实体播放死亡表现。
- 如果目标是本地玩家，进入死亡输入门控。
- 处理完成后 ACK。

### 接收消息格式：怪物死亡

```json
{
  "msgId": "combat_event",
  "type": "combat_event",
  "reliableMessageId": 501,
  "sceneId": "world_001",
  "eventId": 8805,
  "eventType": "death",
  "actionId": "basic_attack",
  "actionSequence": 5,
  "source": { "entityType": "player", "entityId": "10001" },
  "target": { "entityType": "monster", "entityId": "mon_map001_i0000_sg003_Ls001_000001" },
  "damage": 20,
  "critical": false,
  "hpAfter": 0,
  "maxHp": 100,
  "lethal": true,
  "combatRevision": 10,
  "serverTick": 4520,
  "serverTime": 1784544001000
}
```

处理表现：

- 怪物 HP 显示 `0 / 100`。
- 怪物播放死亡动画。
- 关闭怪物可攻击选择，避免继续发送攻击。

ACK：

```json
{
  "msgId": "battle_outbox_ack",
  "type": "battle_outbox_ack",
  "reliableMessageId": 501
}
```

### 接收消息格式：玩家死亡

```json
{
  "msgId": "combat_event",
  "type": "combat_event",
  "reliableMessageId": 502,
  "sceneId": "world_001",
  "eventId": 8806,
  "eventType": "death",
  "actionId": "monster_basic_attack",
  "actionSequence": 20,
  "source": { "entityType": "monster", "entityId": "mon_map001_i0000_sg003_Ls001_000001" },
  "target": { "entityType": "player", "entityId": "10001" },
  "damage": 50,
  "critical": false,
  "hpAfter": 0,
  "maxHp": 1000,
  "lethal": true,
  "combatRevision": 30,
  "serverTick": 4521,
  "serverTime": 1784544001050
}
```

如果 `target.entityId` 是本地玩家：

- 停止发送 `player_input`。
- 停止发送 `combat_action`。
- 清理本地 pending 攻击。
- 清理或暂停移动预测。
- 播放死亡动画/UI。
- 保持 heartbeat。

ACK：

```json
{
  "msgId": "battle_outbox_ack",
  "type": "battle_outbox_ack",
  "reliableMessageId": 502
}
```

## 9. 处理 combat_event=respawn

### 客户端改动

收到 `respawn` 后：

- 去重 `sceneId + eventId`。
- 更新目标玩家 HP。
- 清除死亡 UI。
- 如果是本地玩家，重新启用移动和攻击输入。
- 等待或应用服务端后续位置同步，不用本地猜复活点。
- 处理完成后 ACK。

### 接收消息格式

```json
{
  "msgId": "combat_event",
  "type": "combat_event",
  "reliableMessageId": 601,
  "sceneId": "world_001",
  "eventId": 8807,
  "eventType": "respawn",
  "actionId": "respawn",
  "actionSequence": 1,
  "source": { "entityType": "player", "entityId": "10001" },
  "target": { "entityType": "player", "entityId": "10001" },
  "damage": 0,
  "critical": false,
  "hpAfter": 1000,
  "maxHp": 1000,
  "lethal": false,
  "combatRevision": 31,
  "serverTick": 4620,
  "serverTime": 1784544006050
}
```

ACK：

```json
{
  "msgId": "battle_outbox_ack",
  "type": "battle_outbox_ack",
  "reliableMessageId": 601
}
```

## 10. 处理 combat_state_upsert

### 客户端改动

继续使用并完善 `CombatStateStore`。

收到 `combat_state_upsert` 后：

- 按 `sceneId` 校验当前场景。
- 对每个 state 按 `spawnGeneration + combatRevision` 合并。
- 合并成功后刷新对应实体 HP。
- 不播放攻击/受击/飘字等一次性表现。
- 处理完成后 ACK。

合并规则：

- `spawnGeneration` 小于本地缓存：丢弃。
- `spawnGeneration` 大于本地缓存：接受并替换。
- `spawnGeneration` 相同，`combatRevision` 小于本地缓存：丢弃。
- `combatRevision` 相同：幂等处理，不重复刷新表现。
- 其他情况：接受服务端绝对 HP。

### 接收消息格式：怪物 HP 校正

```json
{
  "msgId": "combat_state_upsert",
  "type": "combat_state_upsert",
  "reliableMessageId": 701,
  "sceneId": "world_001",
  "serverTick": 4513,
  "serverTime": 1784544000600,
  "states": [
    {
      "entity": { "entityType": "monster", "entityId": "mon_map001_i0000_sg003_Ls001_000001" },
      "hp": 90,
      "maxHp": 100,
      "lifeState": "Alive",
      "combatRevision": 1,
      "spawnGeneration": 0
    }
  ]
}
```

ACK：

```json
{
  "msgId": "battle_outbox_ack",
  "type": "battle_outbox_ack",
  "reliableMessageId": 701
}
```

### 接收消息格式：玩家 HP 校正

```json
{
  "msgId": "combat_state_upsert",
  "type": "combat_state_upsert",
  "reliableMessageId": 702,
  "sceneId": "world_001",
  "serverTick": 4514,
  "serverTime": 1784544000650,
  "states": [
    {
      "entity": { "entityType": "player", "entityId": "10001" },
      "hp": 991,
      "maxHp": 1000,
      "lifeState": "Alive",
      "combatRevision": 8,
      "spawnGeneration": 0
    }
  ]
}
```

ACK：

```json
{
  "msgId": "battle_outbox_ack",
  "type": "battle_outbox_ack",
  "reliableMessageId": 702
}
```

## 11. 绑定本地玩家、远端玩家、怪物血条

### 客户端改动

当前本地玩家 HUD 已有 `PlayerHealthBarPresenter`。还需要补：

- 远端玩家头顶血条。
- 怪物头顶血条。
- 统一从 `CombatStateStore.StateChanged` 刷新。

建议新增通用组件：`CombatHealthPresenter.cs`。

配置字段：

- `entityType`：`player` 或 `monster`
- `entityId`：运行时绑定
- `fillImage`
- `healthText`
- `stateStore`

刷新来源：

- `combat_event=damage/death/respawn`
- `combat_state_upsert`
- `monster_upsert` 作为怪物 HP 兜底

### 接收消息格式

血条主要消费 `combat_state_upsert`：

```json
{
  "msgId": "combat_state_upsert",
  "type": "combat_state_upsert",
  "reliableMessageId": 703,
  "sceneId": "world_001",
  "states": [
    {
      "entity": { "entityType": "monster", "entityId": "mon_map001_i0000_sg003_Ls001_000001" },
      "hp": 75,
      "maxHp": 100,
      "lifeState": "Alive",
      "combatRevision": 3,
      "spawnGeneration": 0
    }
  ]
}
```

也消费 `damage` 事件里的即时 HP：

```json
{
  "msgId": "combat_event",
  "type": "combat_event",
  "reliableMessageId": 704,
  "eventType": "damage",
  "target": { "entityType": "monster", "entityId": "mon_map001_i0000_sg003_Ls001_000001" },
  "hpAfter": 75,
  "maxHp": 100,
  "combatRevision": 3
}
```

注意：客户端显示 HP 时必须显示服务端绝对值，例如 `75 / 100`，不要本地执行 `hp -= damage` 后再等服务端校正。

## 12. 处理玩家 AOI 广播并 ACK

### 客户端改动

修改 `EntityManager` 相关处理。

需要做：

- `player_enter_broadcast` 创建/更新远端玩家后 ACK。
- `player_state_broadcast` 应用远端状态后 ACK。
- `player_leave_broadcast` 删除远端玩家后 ACK。
- 远端玩家创建后注册到 `CombatEntityViewRegistry`。
- 远端玩家删除后从注册表注销。

### 接收消息格式：player_enter_broadcast

```json
{
  "msgId": "player_enter_broadcast",
  "type": "player_enter_broadcast",
  "reliableMessageId": 801,
  "serverTime": 1784544000000,
  "player": {
    "characterDbId": 10002,
    "characterId": "role_002",
    "nickname": "PlayerB",
    "sceneId": "world_001",
    "x": 12.0,
    "y": 0.0,
    "z": 10.0,
    "rotY": 180.0,
    "animation": "Idle",
    "hp": 1000,
    "maxHp": 1000,
    "lifeState": "Alive",
    "combatRevision": 0
  }
}
```

ACK：

```json
{
  "msgId": "battle_outbox_ack",
  "type": "battle_outbox_ack",
  "reliableMessageId": 801
}
```

### 接收消息格式：player_state_broadcast

```json
{
  "msgId": "player_state_broadcast",
  "type": "player_state_broadcast",
  "reliableMessageId": 802,
  "player": {
    "characterDbId": 10002,
    "sceneId": "world_001",
    "x": 13.0,
    "y": 0.0,
    "z": 10.5,
    "rotY": 90.0,
    "animation": "Running",
    "serverTick": 4515,
    "hp": 1000,
    "maxHp": 1000,
    "lifeState": "Alive",
    "combatRevision": 0
  }
}
```

ACK：

```json
{
  "msgId": "battle_outbox_ack",
  "type": "battle_outbox_ack",
  "reliableMessageId": 802
}
```

### 接收消息格式：player_leave_broadcast

```json
{
  "msgId": "player_leave_broadcast",
  "type": "player_leave_broadcast",
  "reliableMessageId": 803,
  "characterDbId": 10002,
  "characterId": "role_002",
  "nickname": "PlayerB",
  "reason": "aoi_leave",
  "serverTime": 1784544003000
}
```

ACK：

```json
{
  "msgId": "battle_outbox_ack",
  "type": "battle_outbox_ack",
  "reliableMessageId": 803
}
```

## 13. 处理怪物 AOI 广播并 ACK

### 客户端改动

修改 `MonsterSnapshotManager`。

需要做：

- `monster_upsert` 创建/更新怪物后 ACK。
- `monster_remove` 删除怪物后 ACK。
- 怪物创建后注册到 `CombatEntityViewRegistry`。
- 怪物删除后从注册表注销。
- 怪物 HP 继续可以从 `monster_upsert` 刷新，但不能覆盖更高版本的 `CombatStateStore`。

### 接收消息格式：monster_upsert

```json
{
  "msgId": "monster_upsert",
  "type": "monster_upsert",
  "reliableMessageId": 901,
  "sceneId": "world_001",
  "aoiRevision": 5,
  "serverTime": 1784544003000,
  "monster": {
    "monsterInstanceId": "mon_map001_i0000_sg003_Ls001_000001",
    "monsterId": "Ls001",
    "prefabId": "Ls001",
    "x": 10.0,
    "y": 0.0,
    "z": 12.0,
    "rotY": 180.0,
    "hp": 75,
    "maxHp": 100,
    "state": "Idle",
    "aoiRevision": 5,
    "combatRevision": 3,
    "spawnGeneration": 0,
    "lifeState": "Alive"
  }
}
```

ACK：

```json
{
  "msgId": "battle_outbox_ack",
  "type": "battle_outbox_ack",
  "reliableMessageId": 901
}
```

### 接收消息格式：monster_remove

```json
{
  "msgId": "monster_remove",
  "type": "monster_remove",
  "reliableMessageId": 902,
  "sceneId": "world_001",
  "monsterInstanceId": "mon_map001_i0000_sg003_Ls001_000001",
  "aoiRevision": 6,
  "reason": "death_lifecycle_done",
  "serverTime": 1784544010000
}
```

ACK：

```json
{
  "msgId": "battle_outbox_ack",
  "type": "battle_outbox_ack",
  "reliableMessageId": 902
}
```

## 14. 本地玩家死亡后停止输入

### 客户端改动

修改本地输入控制：

- `LocalPlayerInputController`
- `LocalPlayerCombatController`
- 本地玩家状态管理脚本

进入死亡状态后：

- 停止发送 `player_input`。
- 停止发送 `combat_action`。
- 清空 pending 攻击。
- 清空或暂停移动预测。
- heartbeat 不停。

复活后：

- 收到 `respawn` 或 `combat_state_upsert` 显示本地玩家 `lifeState=Alive` 且 `hp>0`。
- 恢复 `player_input`。
- 恢复 `combat_action`。
- HP 以服务端值显示。

### 接收消息格式：死亡触发

```json
{
  "msgId": "combat_event",
  "type": "combat_event",
  "reliableMessageId": 1001,
  "sceneId": "world_001",
  "eventType": "damage",
  "target": { "entityType": "player", "entityId": "10001" },
  "hpAfter": 0,
  "maxHp": 1000,
  "lethal": true,
  "combatRevision": 20
}
```

或：

```json
{
  "msgId": "combat_state_upsert",
  "type": "combat_state_upsert",
  "reliableMessageId": 1002,
  "sceneId": "world_001",
  "states": [
    {
      "entity": { "entityType": "player", "entityId": "10001" },
      "hp": 0,
      "maxHp": 1000,
      "lifeState": "Dead",
      "combatRevision": 20,
      "spawnGeneration": 0
    }
  ]
}
```

### 接收消息格式：复活恢复

```json
{
  "msgId": "combat_event",
  "type": "combat_event",
  "reliableMessageId": 1003,
  "sceneId": "world_001",
  "eventType": "respawn",
  "target": { "entityType": "player", "entityId": "10001" },
  "hpAfter": 1000,
  "maxHp": 1000,
  "combatRevision": 21
}
```

或：

```json
{
  "msgId": "combat_state_upsert",
  "type": "combat_state_upsert",
  "reliableMessageId": 1004,
  "sceneId": "world_001",
  "states": [
    {
      "entity": { "entityType": "player", "entityId": "10001" },
      "hp": 1000,
      "maxHp": 1000,
      "lifeState": "Alive",
      "combatRevision": 21,
      "spawnGeneration": 0
    }
  ]
}
```

对应 ACK：

```json
{
  "msgId": "battle_outbox_ack",
  "type": "battle_outbox_ack",
  "reliableMessageId": 1004
}
```

## 15. 客户端开发顺序

### 步骤 1：补消息模型

改动：`NetMessages.cs`

完成标准：Unity 能序列化 `combat_action` 和 `battle_outbox_ack`，接收模型能读取 `reliableMessageId`。

涉及消息：

- 发送：`combat_action`
- 发送：`battle_outbox_ack`
- 接收：所有带 `reliableMessageId` 的广播

### 步骤 2：补发送接口

改动：`NetworkManager.cs`

完成标准：能从代码调用 `SendCombatAction` 和 `SendBattleOutboxAck`。

涉及消息：

```json
{ "msgId": "combat_action", "type": "combat_action" }
```

```json
{ "msgId": "battle_outbox_ack", "type": "battle_outbox_ack" }
```

### 步骤 3：补怪物目标拾取

改动：怪物预制体、`MonsterNetworkView`、目标选择脚本

完成标准：鼠标点击怪物能拿到 `monsterInstanceId`。

涉及消息：

- 接收：`monster_snapshot`
- 接收：`monster_upsert`
- 发送：`combat_action.targetId = monsterInstanceId`

### 步骤 4：发送普通攻击

改动：新增 `LocalPlayerCombatController.cs`

完成标准：点击怪物发送合法 `combat_action`，服务端不返回 `invalid_combat_action`。

发送消息：

```json
{
  "msgId": "combat_action",
  "type": "combat_action",
  "actionSequence": 1,
  "clientTick": 120,
  "actionId": "basic_attack",
  "targetType": "monster",
  "targetId": "mon_map001_i0000_sg003_Ls001_000001",
  "aimYaw": 90.0
}
```

### 步骤 5：播放攻击动作

改动：`CombatMessageReceiver`、远端玩家/怪物表现脚本

完成标准：A 攻击怪物，B/C 能看到 A 的攻击动画。

接收消息：

```json
{
  "msgId": "combat_event",
  "type": "combat_event",
  "eventType": "attack",
  "source": { "entityType": "player", "entityId": "10001" },
  "target": { "entityType": "monster", "entityId": "mon_map001_i0000_sg003_Ls001_000001" }
}
```

发送 ACK：

```json
{ "msgId": "battle_outbox_ack", "type": "battle_outbox_ack", "reliableMessageId": 301 }
```

### 步骤 6：刷新 HP 和受击表现

改动：`CombatMessageReceiver`、`CombatStateStore`、血条表现脚本

完成标准：A/B/C 看到怪物 HP 一致下降。

接收消息：

```json
{
  "msgId": "combat_event",
  "type": "combat_event",
  "eventType": "damage",
  "target": { "entityType": "monster", "entityId": "mon_map001_i0000_sg003_Ls001_000001" },
  "damage": 10,
  "hpAfter": 90,
  "maxHp": 100,
  "combatRevision": 1
}
```

接收校正：

```json
{
  "msgId": "combat_state_upsert",
  "type": "combat_state_upsert",
  "states": [
    {
      "entity": { "entityType": "monster", "entityId": "mon_map001_i0000_sg003_Ls001_000001" },
      "hp": 90,
      "maxHp": 100,
      "lifeState": "Alive",
      "combatRevision": 1,
      "spawnGeneration": 0
    }
  ]
}
```

### 步骤 7：处理死亡/复活

改动：`CombatMessageReceiver`、本地输入状态、表现脚本

完成标准：玩家死亡后停止移动和攻击，复活后恢复。

接收死亡：

```json
{
  "msgId": "combat_event",
  "type": "combat_event",
  "eventType": "death",
  "target": { "entityType": "player", "entityId": "10001" },
  "hpAfter": 0,
  "maxHp": 1000,
  "lethal": true,
  "combatRevision": 20
}
```

接收复活：

```json
{
  "msgId": "combat_event",
  "type": "combat_event",
  "eventType": "respawn",
  "target": { "entityType": "player", "entityId": "10001" },
  "hpAfter": 1000,
  "maxHp": 1000,
  "combatRevision": 21
}
```

### 步骤 8：补 ACK 闭环

改动：所有接收 handler

完成标准：所有带 `reliableMessageId > 0` 的关键消息，应用成功后都发 `battle_outbox_ack`。

接收任意可靠消息：

```json
{
  "msgId": "combat_event",
  "type": "combat_event",
  "reliableMessageId": 401
}
```

发送 ACK：

```json
{
  "msgId": "battle_outbox_ack",
  "type": "battle_outbox_ack",
  "reliableMessageId": 401
}
```

## 16. 最小联调验收清单

完成下面这些，就可以和当前服务端正式联调多人打怪：

- 客户端能发送 `combat_action`，且 `actionSequence` 独立递增。
- 客户端能通过点击怪物拿到正确 `monsterInstanceId`。
- 客户端收到 `combat_event=attack` 后能播放玩家/怪物攻击动画。
- 客户端收到 `combat_event=damage` 后能刷新 HP、受击、飘字。
- 客户端收到 `combat_event=death` 后能播放死亡并禁用死亡玩家输入。
- 客户端收到 `combat_event=respawn` 后能恢复玩家输入。
- 客户端收到 `combat_state_upsert` 后能按服务端绝对值校正 HP。
- 本地玩家、远端玩家、怪物都有可刷新的 HP UI。
- 所有带 `reliableMessageId` 的关键广播都能回 `battle_outbox_ack`。
- 三个客户端同 AOI 打同一怪物时，三端看到一致 HP 和关键动画。
