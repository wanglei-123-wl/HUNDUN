# 服务端权威怪物巡逻

2026-11-24，配套 GameServer 的 [345 巡逻规则](../../../gameserver/docs/345-怪物随机巡逻与GM配置.md) 和 [161 怪物状态协议](../../../gameserver/docs/161-客户端怪物身份与状态对接协议.md)。

## 版本及行为

首次进场、重连和切图继续共用 `MovementEventPolicy`，怪物能力声明为 `monsterSyncVersion=3`。原有无扩展的 Gate 回显 0 仍可进入；回显其他不匹配版本时沿现有进场错误提示停止，不能私自忽略版本检查。版本 3 可处理原有可选增量及路径，两项开关仍由 Gate 决定。

| 服务端状态 | 紧凑数字 | 客户端表现 |
| --- | --- | --- |
| Idle | 1 | 地面待机 |
| TakeOff | 2 | 沿服务端 Y 起飞 |
| Chase / Attack / Return / Landing / Dead | 3 / 4 / 5 / 6 / 7 | 沿现有战斗及飞行规则 |
| Patrol | 8 | 地面行走或飞行循环，跟随服务端坐标或有限地面段 |
| Hover | 9 | 飞行循环，移动速度参数为零，保留服务端高度 |

完整数组、增量和紧凑解码均识别 8/9。`StateFrameProcessor` 在修改任何实体或基线前拒绝非版本 3 的新状态；10 及以上未知行为仍拒绝。字符串完整快照沿原生命周期入口恢复，不新增消息或行字段。

巡逻点、范围、停留、速度、索敌、攻击和寻路由服务端决定。Unity 不随机选点，不用 Root Motion 推动网络坐标。所有已登记动画驱动增加 Patrol 的行走/飞行动画入口，飞行驱动单独处理 Hover，资源、GUID 和控制器保持原样。旧 Ls001 客户端资源兼容验证不恢复服务端默认怪物或建库种子。

## 呈现与生命周期

- 行为真正变化时才切换普通动画；持续位置、血量及相同行为不会反复从第零帧播放。攻击动作继续由原战斗事件驱动，动态 Attack 状态不重复起手。
- 行为切换清掉旧路径，再采用本包的新路径。巡逻、追击和返回可使用有限地面段。正常巡逻到点时停止预测，播完已确认的最后一段再切待机或悬停。
- 血量独立增量通过 `preserveTransform` 和 `preservePresentation` 保持位置、动画及移动参数，血量/战斗修订沿原合并规则。
- 晚进入视野和重连按当前完整状态及坐标建立模型；空中 Hover 不重新播放起飞。离开、断线和换图清除视图和路径；旧场景、连接、可见代次及生命代次继续过滤。
- 死亡动画及碰撞体保留原终止保护，晚到巡逻不能重新启用路径或碰撞体；重生创建新生命的视图。

### 怪物坐标播放

2026-11-25。所有怪物共用 `MonsterPositionPlayback`。收到有效短路径时按路径播放。无路径时，巡逻、追击、返回及起飞、降落使用两条实际收到的坐标按服务端时间插值，保留地面坡度和飞行高度。远距离 500ms 更新的常见播放缓冲约 570ms。仅血量、朝向、行为、路径更新及重复坐标不会补进移动样本。

位于地面的怪物在下个坐标稍晚时，只有最近三个有效坐标方向稳定、且当前地图通过身体尺寸的地面通行检查，才允许补最多 100ms、0.3 米的显示位移。每组样本只检查一次。飞行和升降沿收到的三维坐标插值。攻击事件使用原服务端 Tick 避免旧移动重新启动，也避免迟到的攻击打断新移动。进场快照、切图、重连及重生重新建立坐标。服务端碰撞、命中、发帧频率及协议沿用现有规则。

### 巡逻自然停步

`Patrol → Idle/Hover` 收包时立即停止预测和旧短路径。仍有实际收到的坐标样本时按原时间轴播完，并以本包确认的终点为准。停步包只带行为时沿用最后确认的坐标。没有可继续播放的样本时，从当前画面位置短时平滑收敛到确认点。切换待机或悬停前把模型落在终点。重复停步包保留当前收尾，攻击、死亡、新移动及完整快照会取消挂起的收尾。

范围限于怪物位置和动画。客户端技能距离预检及自动靠近、Gate 碰撞和技能命中沿用当前逻辑。可使用巡逻播放、状态转换及攻击专项回归验证，实际云端画面待复测。

用 `pwsh -NoProfile -File Tests/Run-MonsterPositionPlaybackTests.ps1 -UnityEditorData '<Unity Editor>/Data'` 验证 500ms 样本、坡地、预测上限、转弯、停止、飞行和长间隔。攻击时序验证沿 `Tests/CombatAttackPresentation.Tests.ps1`。

## 修改位置

网络模块：`MovementEventMessages`、`BattleSyncProtocolConstants`、`StateFrameArrayParser`、`CompactStateDecoder`、`StateFrameProcessor`。

怪物模块：`MonsterSnapshotManager`、`MonsterViewTypes`、`MonsterViewRoot` 和十二个现有动画驱动。没有修改玩家控制、登录 Token、地图、资源发布或生产数据库。

## 验证方式

`pwsh -NoProfile -File Tests/Run-MonsterPatrolValidation.ps1` 运行专用协议、原数组解析和十六组动画/目录/生命代次检查。旧 YAML 正则夹具通过内存归一化行尾执行，不重写资源。`MonsterGenerationPresentation` 和 `SL002MonsterStatePresentation` 的源代码切片夹具补齐现有视图/路径接口。

`Tests/MonsterPatrolPlayMode.cs` 是临时批处理入口源码：复制到 `Assets/Editor/MonsterPatrolPlayMode.cs` 后，用 Unity 2022.3.62f1 `-batchmode -nographics -projectPath <项目> -executeMethod MonsterPatrolPlayMode.Run -logFile <证据日志>` 执行。新建未保存的空场景，进入实际播放模式，加载目录中的真实预制体/骨架/控制器/动画，并测试网络视图状态。退出后删除临时脚本及其 meta；不保存场景或修改预制体。结果写到 `PATROL_EVIDENCE` 环境变量指向的证据目录。

本轮证据位于 `E:/gameserver/artifacts/unity-monster-patrol-20261124/`。服务端真实回环 TCP 验证两个观察者收到巡逻 8、当前快照及空中悬停 9；Unity 实际模型播放与网络状态重放属于本地验证。完整游戏双开画面、正式客户端包和云端实际带宽仍需配套发布时验收，不能把本地状态检查当作线上画面验收。

## 上线与回退

先提供本客户端及配套服务端，Gate 的 `BattleSync.EnableMonsterPatrol` 仍默认 false。确认客户端均支持版本 3 后，在 Gate 配置开启该能力并重启，再由 GM 逐个模板开启巡逻。已有失败候选需检查运行状态是否被接受，必要时重新保存并发布；已有怪物在正常重生时采用新行为。

回退前先关闭 GM 的巡逻配置并取得接受回执，再关闭 Gate 能力并回退配套客户端。不要在含巡逻的已接受配置下直接换回旧客户端。此次未部署、重启或更改云端配置。
