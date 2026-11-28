# 代码热更迁移记录

当前配套资源与代码装载方式见 [Stage3.md](Stage3.md)。第二阶段历史实现及验证见 [Stage2.md](Stage2.md)；其启动后直接装载 DLL 的方式已由第三阶段受控执行进程替代，以保留完整业务序列化数据。以下保留第一阶段的边界与检查存档。

发布工具与旧版本自动清理规则见[第四阶段](Stage4.md)，默认配置见 [RetentionPolicy.json](RetentionPolicy.json)。第四阶段工具实现与本地隔离验收已完成，清理已接入新渠道发布工具；真实云端尚未部署，未触及旧渠道和游戏后端。下一阶段为实际窗口交接及联机玩法回归，正式基础客户端安装包尚未构建。

## 第一阶段历史记录

本阶段完成程序集与启动依赖分离。业务程序集当前仍由 Unity 随工程加载；下载、验证和动态加载新版 DLL 属于第二阶段，尚未实现。本阶段不发布新的客户端或资源。

## 程序集边界

| 程序集 | 内容 |
| --- | --- |
| QiDao.Contracts | 不依赖 Unity 和业务类型的启动接口、服务注册入口，以及启动显示文字 |
| QiDao.Bootstrap | 启动场景控制、资源目录同步、下载计划、缓存检查、服务器地址设置和启动显示 |
| QiDao.GameLogic | 全部自主业务运行时代码，以及原先未独立分程序集的美术、地图、特效辅助脚本 |
| QiDao.Editor | 项目编辑器工具；不进入 Player |
| EZhex1991.EZSoftBone | 原有独立第三方库；作为基础依赖保留 |

`Assets/QiDao.GameLogic.asmdef` 是默认业务程序集。以后在 Assets 中新增普通业务脚本会默认归入该程序集，包含 Assets/Scripts 以外的目录。Editor 子目录需要引用 QiDao.Editor；检查工具会拒绝把 Editor 脚本编入 Player，以及未经归属的新运行时程序集。

固定启动层只能引用基础接口和既有 SDK，不能引用 QiDao.GameLogic。业务层也不引用启动层，双方经 `IGameStartupContent` 和 `GameStartupServices` 协作。物品表、地图目录、字体与图标准备及登录场景加载由业务层实现。资源下载范围依旧遍历目录里的全部远端依赖；必需地址集合由业务层传入。

脚本移动保留原始 `.meta` GUID 和类型全名。程序集名发生变化，因此后续必须按第三阶段构建配套资源并使用新发布渠道；此时不能把原线上资源当作新客户端的已兼容资源。

## 检查入口

在项目根目录运行：

```powershell
python Tools/HotUpdate/Verify-AssemblyBoundary.py --output Build/HotUpdateMigration/Stage1/assembly-inventory.json
python Tests/HotUpdateAssemblyBoundary.Tests.py
```

Unity 菜单：`Tools > Hot Update > Verify Assembly Boundary`。同一检查也在 Player 构建前执行。

`HotUpdateAssemblyBoundary.VerifyStage1` 支持 batchmode，参数为 `-hotUpdateStage1Output <报告路径>` 和 `-hotUpdateStage1Inventory <清单路径>`。它会核对实际 Player 编译归属、资产引用的 MonoScript GUID 与类型归属，并编译 Windows Player 脚本，不构建或上传资源。

验收记录位于 `Build/HotUpdateMigration/Stage1/`，包含原始源码快照、移动映射、全量清单、Unity 编译结果、Player DLL 引用、回归检查和历史失败对照。

本阶段检查中，GrassRenderDiagnostics、RemoteItemIconIsolation、UsedGameContentMigration 的旧测试在迁移前的源码快照中同样失败，分别仍要求旧诊断调用位置、固定的历史样例数量、旧的按标签下载实现。原始失败日志与当前结果均保留；不把这些检查算作通过，也不据此修改实际资源数据。

## 当前限制与下一阶段

- 第一阶段没有代码下载器，也不支持已经发布的客户端自动获得本次改造。
- 第二阶段需生成独立 DLL，并在业务资产加载前执行动态加载、初始化和异常恢复。
- 现有 RuntimeInitializeOnLoadMethod 的业务初始化仍由 Unity 执行；动态加载阶段必须显式处理它们，不能假定运行时加载 DLL 会再次触发 Unity 启动回调。
- 第三、四阶段完成资源渠道隔离、代码与资源绑定及发布兼容检查后，才进入正式客户端打包。
