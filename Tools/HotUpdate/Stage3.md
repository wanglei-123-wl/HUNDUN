# 第三阶段：代码与资源配套加载

本阶段只生成隔离验收产物，不上传、不切换线上资源、不生成玩家安装包。正式发布工具与安装包分别属于后续阶段。

## 代码装载方式

完整游戏测试发现，仅在 Unity 启动后调用 Assembly.Load，虽然能恢复 MonoBehaviour 和简单字段，却会丢失物品定义等嵌套 Serializable 数据。物品总表因此无法取得远程图标地址。这与 [Unity 官方讨论中的说明](https://discussions.unity.com/t/advanced-use-case-assetbundles-and-dynamically-loaded-assemblies/756537) 一致。第二阶段简单组件验收不覆盖这一限制，第二阶段的直接动态装载方式已被本阶段替代。

固定启动客户端先在线检查签名发布清单，验证并缓存业务 DLL，再启动使用该版本 DLL 的隔离游戏进程。Unity 在这个进程初始化时便能登记完整的业务序列化类型。游戏进程还会重新校验选定清单、DLL 哈希及实际装载路径，再调用业务入口。它使用父启动进程已经选定的发布版本，不再次追随 latest。

安装目录保持不变。执行副本位于 `%LOCALAPPDATA%/QiDao/CodeExecution/`，按代码缓存位置、基础客户端 build GUID、代码 SHA256 隔离，完整身份写入 `.host-identity`。固定客户端文件从本地复制并逐个校验；代码包只下载业务 DLL，不从服务器重下整套客户端。执行目录采用较短的名字以避免 Windows Mono 的路径长度问题。多个客户端准备相同副本时使用进程间锁。不同代码版本使用不同目录，避免覆盖正在运行的 DLL。

普通启动会在新进程的窗口出现后结束旧启动进程；验收父进程等待其子进程结束以核对最终退出码。代码启动日志记录真实游戏进程，未完成初始化的进程退出后，下次启动可隔离该发布并恢复以前已验证的配套版本。执行副本的保留、清理及安装器行为需在后续阶段完成。

## 配套发布与资源隔离

签名的 CodeRelease 同时绑定代码 SHA256、资源版本 ID、不可变目录、catalog 的 Hash128/SHA256/长度、资源清单 SHA256/长度和脚本类型数据库哈希。资源清单列出每个包的文件 SHA256、CRC、长度、Unity 缓存名称及版本哈希。

新资源渠道为 `/client-hotupdate/windows-mono-v1/resources/<资源ID>/`。旧 `/StandaloneWindows64` 发布渠道继续供旧客户端使用。启动场景现有资源服务器选项继续用于选择服务器协议、主机与端口，新客户端仍通过独立代码渠道读取配套版本。

启动层只安装选定版本的 locator，校验目录及包元数据，移除其他游戏目录的 locator，再运行原有全量依赖下载、原生 CRC 缓存检查、字体/物品表/图标准备以及登录前复核。下载过程中更换 latest 不改变当前选定的目录。

基础客户端带空的本地游戏目录，只保留 Addressables provider 初始化，避免业务 DLL 就绪前解析旧游戏类型。资源构建将 builtin shader 包一起纳入配套远程目录。目录重写会重新生成内部依赖组别名，并逐个核对重写前后的资源键、资源类型及依赖顺序，避免加载器从错误的首个包查找资源。

只改资源可以复用相同代码 SHA256；只改方法逻辑且类型结构兼容时可以复用资源版本。代码缓存按 DLL SHA256 存储，Unity 包缓存继续按原有 BundleName/Hash 复用。不会清空旧缓存或启用 ClearOtherCachedVersionsWhenLoaded。启动下载与 CRC 检查使用共享缓存路径对应的进程间锁，避免两个客户端同时提交同一缓存包；网络失败与缓存修复仍有有限重试。

## 构建与校验

- `HotUpdateResourceBuild.BuildFromRequest`：仅可在 `C:/QiDaoBuild/` 隔离工程执行。编译 Player 类型、构建/导出配套目录，逐包比对复制前后 SHA256 并用 Unity 原生 CRC 加载验证。复制异常只重试尚未发布的副本，不修改预期 CRC。
- `HotUpdateCodeBuild.BuildCodeRelease`：传入 `resourceBuildReport`，要求完整 CRC 验证、依赖顺序验证、类型数据库兼容及控制文件 SHA256 均通过，然后签名绑定。
- 构建报告的资源 ID 必须与将要发布的目录一致。失败或未完成的目录不能上传为正式产物。本阶段仍无正式上传命令。

完整验收产物及结果在 `Build/HotUpdateMigration/Stage3/`。最终资源文件实际存放于 `C:/QiDaoBuild/Stage3FullResources/`，报告中的 catalogPath 指向对应不可变目录。

```powershell
python Tools/HotUpdate/Verify-ResourceBindingAcceptance.py --artifact Build/HotUpdateMigration/Stage3 --cache-parent C:/QiDaoBuild/Stage3ResourceCaches
python Tools/HotUpdate/Verify-ResourceBindingAcceptance.py --artifact Build/HotUpdateMigration/Stage3 --cache-parent C:/QiDaoBuild/Stage3FullResourceCaches --full
```

验收服务器只监听本机回环，脚本结束时关闭。原生验证使用验收专用产品名、代码缓存及 Unity 缓存，不操作玩家现有缓存。`--reuse-cache` 仅允许指向指定隔离缓存父目录内已有的目录，报告会注明复用了已有缓存。

验收覆盖代码/资源分别更新、嵌套序列化数据、并发启动、目录篡改、损坏包与缓存修复、启动中途切换 latest、失败入口及配套恢复。完整游戏验收下载和校验全部目录依赖，准备真实字体/物品图标，调用业务代码解析地图包并打开登录场景。无图形模式不用于证明画面效果；登录后的联网地图、怪物、小地图和输入交互仍需后续实机回归。

本机构建过程中曾出现资源复制后内容不同，底层原因尚未确认；失败日志保留，校验未放宽。只有所有包及完整游戏运行验证通过的最终目录才计入本阶段结果。
