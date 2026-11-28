# 第二阶段：签名代码包与动态加载

本页记录第二阶段历史实现。完整游戏验收发现嵌套序列化数据限制后，代码实际装载方式已由 [第三阶段](Stage3.md) 替代；当前行为与验收结果应以第三阶段记录为准。

当前目标平台为 Windows x64 Mono。固定启动层先检查代码发布，再运行资源更新与业务准备。编辑器 Play Mode 使用已经编译的业务程序集；实际 Windows Player 必须从已验证字节加载 QiDao.GameLogic，若发现业务程序集已被基础客户端预加载，会停止启动。

## 已实现

- 独立编译完整业务程序集，输出 DLL、签名发布清单与构建报告。
- 固定启动层使用内置公钥验证 RSA/SHA256 签名，再检查 Unity 版本、基础接口版本、运行平台、程序集名、大小、SHA256 和受限下载路径。
- 下载体积有上限，失败可重试。完整文件写入独立代码缓存后再加载；不读写或清空 Unity/Addressables 资源缓存。
- 已有有效代码复用，不重复下载或重写。缓存写入和健康版本切换使用跨进程锁，启动日志按进程区分。
- 下载程序集的入口显式调用业务启动初始化，登录场景加载后补齐 AfterSceneLoad 初始化。字体和场景事件的原有生命周期保留。
- 成功完成代码入口初始化后就确认代码健康，普通资源下载期间退出不会导致代码版本被误判为崩溃。
- 初始化失败的代码会被隔离，下次启动可恢复以前验证通过的代码。已经装载过程序集后需要新的进程恢复；界面重试会启动恢复进程。
- 生产 Player 构建会排除预加载的业务 DLL，并保留程序集注册名以恢复预制体上的脚本。要求关闭 managed/engine stripping，避免以后热更代码需要的基础 API 被裁剪。

## 配置和签名密钥

固定客户端配置：`Assets/Resources/Startup/HotUpdateBootstrap.json`，包含公钥、代码清单地址、兼容版本和下载限制。

当前签名私钥保存在本机 `C:\QiDaoBuild\Keys\QiDaoHotUpdate-RSA.xml`。此文件只用于本地发布，不在工程、不在代码包、不在验证服务器目录，也不上传服务器。它是持续发布所需的文件，应单独备份并从清理构建目录的操作中排除。迁移该文件和调整 ACL 的工具操作被自动审批拒绝，故保留原位置与权限。

## 代码包编译入口

Unity batchmode 方法：`HotUpdateCodeBuild.BuildCodeRelease`，通过 `-hotupdate-request <JSON文件>` 传入：

```json
{
  "outputDirectory": "C:/QiDaoCodeReleases/example",
  "releaseId": "0123456789abcdef0123456789abcdef",
  "sequence": 1,
  "signingKeyPath": "C:/QiDaoBuild/Keys/QiDaoHotUpdate-RSA.xml"
}
```

上面是字段示意，实际发布使用新的 releaseId 和递增 sequence。输出 `current.json`、`releases/<releaseId>/QiDao.GameLogic.dll.bytes` 和 `code-build-report.json`。发布标识不可用来覆盖另一份代码。生产编译拒绝验收专用的 QIDAO_HOTUPDATE_PROBE 宏。

第三、四阶段会补齐资源绑定和完整发布命令。当前默认更新地址是预留的新代码渠道，尚未上传或切换线上服务。

## 验证入口

```powershell
pwsh -NoProfile -File Tests/HotUpdateCodeCore.Tests.ps1
python Tests/HotUpdateAssemblyBoundary.Tests.py
```

独立原生验证工程：`C:\QiDaoBuild\HotUpdateMigration-Stage2`。

`HotUpdateCodeBuild.BuildAcceptanceBase` 仅允许在该独立工程运行。测试组件只有在验收宏下进入 Player；业务验收组件仅供测试代码包使用，不进入生产代码包。正式构建仍执行原网络设置校验；仅独立的空场景代码验证不要求游戏世界场景存在。

本地回环资源服务器由 `Serve-Acceptance.py` 提供，原生验证入口为：

```powershell
python Tools/HotUpdate/Verify-NativeAcceptance.py --artifact Build/HotUpdateMigration/Stage2 --cache-parent C:/QiDaoBuild/HotUpdateMigration-Stage2/CodeAcceptanceCaches
```

覆盖同一基础 EXE 运行 A/B 代码、动态添加组件、从 AssetBundle 恢复 MonoBehaviour/ScriptableObject、缓存复用、并发首次启动、坏签名、损坏代码、旧清单重放、失败入口隔离、恢复版本与首次安装失败后的重新获取。

## 仍待后续阶段

本阶段的 CodeRelease 已预留资源版本信息，但尚未完成代码与资源的配套校验、资源渠道切换和发布入口统一。原生验证程序不是玩家安装包。不能把本阶段产物切换为线上正式版本，也不能据此认为原线上 AssetBundle 已兼容新程序集。

下一阶段会将选择的代码版本与资源目录固定为一套，继续验证原有 Addressables 下载、CRC、缓存复用、地图准备及登录流程。
