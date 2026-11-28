# 第四阶段范围补充：发布工具与旧版本清理

状态：第四阶段工具实现与本地隔离验收已完成。自动清理已接入新渠道发布成功后的流程，正式云端尚未部署这些工具，未切换线上版本，也未删除服务器文件。第三阶段验收产物及结果保持不变。

验收来源：[stage4-summary.json](../../Build/HotUpdateMigration/Stage4/stage4-summary.json)。发布工具测试通过 24 项，既有相关回归通过 12 组，原生客户端通过 18 个配套异常/恢复用例及 2 个完整游戏资源用例。实际 Windows PowerShell 构建命令、完整资源包暂存与缺少正式客户端凭证时禁止上线均已验证；受保护的 1830 个序列化资源文件未变化。Linux 特有权限/锁及真实云端 HTTP 配置尚未现场验证。

最终文件复核曾出现一次地图包校验不一致，立即进行的独立扫描和随后完整验收均匹配原签名清单，未改写资源或预期哈希；原因尚未确认。记录见 [package-integrity-event.json](../../Build/HotUpdateMigration/Stage4/package-integrity-event.json)。上传、上线仍执行完整校验，异常即停止。本次测试临时上传压缩包的删除被自动审批拦截，文件保留在本地 UploadOperations 中。

## 默认保留规则

建议每个受支持的兼容渠道保留最近成功发布的三套版本：当前线上版本及前两个可回退版本。这里的“三套”是本项目的建议默认值，不是通用标准，也不是根据已经读取的服务器剩余容量测出的唯一最优值。

规则的机器可读来源为 [RetentionPolicy.json](RetentionPolicy.json)。兼容渠道至少区分平台、Unity 版本和启动接口版本；不同受支持的基础客户端不能混在一起只取最近三套。

| 对象 | 保留与清理条件 |
| --- | --- |
| 当前版本 | 始终保留；核对实际线上指针，不能仅按目录修改时间判断。 |
| 历史正式版本 | 保留最近的三套成功发布版本；超出数量且退役缓冲期已结束、未受其他保护时才可删除。 |
| 刚退役版本 | 从被新版本替换下线的时间起保留七天；不从上传时间或首次发布时间起算。缓冲期内可能临时超过三套。 |
| 待发布版本 | 每个兼容渠道保留最新一套已校验候选；上传或发布仍在进行的版本另行保护。旧候选取消引用和操作状态确认后才可删除。 |
| 失败或中断上传 | 确认无活动上传/发布操作、未受候选或兼容保护，且停止活动已超过二十四小时后，清理其废弃文件；保留精简审计记录。时间应基于可靠的操作记录，不能只看文件时间。 |
| 多个版本共用的代码或资源 | 只要任一保留版本、待发布候选或有效操作仍在引用，就保留；不能因最初创建它的版本很旧而删除。 |
| 本地执行副本 | 另行接入进程与恢复目标保护；正在运行或恢复需要的副本不得删除。 |
| 旧客户端渠道与来源不明的文件 | 先盘点、建立兼容保护清单。识别不完整时保留并报告，不直接套用最近三套规则。 |

服务器当前没有覆盖所有在线客户端的版本使用心跳，不能声称能自动识别每一个仍在使用旧目录的玩家，也不为清理而修改游戏后端。退役缓冲是下载过渡策略，不是无限期在线兼容保证。仍受支持的旧客户端和必要的恢复版本应显式保护，停用后再解除保护。

## 接入发布流程的位置

1. 上传前盘点实际剩余空间、上传及发布所需临时空间，显示所需和可用字节数。空间不足时停止并提示，不提前删除受保护版本腾空间。
2. 完成待发布文件上传和校验。上传成功本身不构成淘汰线上旧版本的条件。
3. 新版本切换成功，公开地址与配套内容复核通过后，登记当前版本、成功发布顺序、退役时间、兼容保护与操作状态。
4. 在发布锁内生成保留项、待删除项、保护原因和预计可释放的实际磁盘空间清单；重新确认线上指针与引用快照未变，再执行符合规则的清理。失败的发布不触发历史正式版本清理。
5. 记录实际删除结果与释放空间。清理异常单独报告，不能回头破坏已验证的新版本，也不能虚报清理成功。

保留规则优先于删除条件，先生成可核对的预演结果，再启用删除。这类设计与 [Google Artifact Registry 官方清理策略中的保留优先、保留近期版本及 dry run 机制](https://docs.cloud.google.com/artifact-registry/docs/repositories/cleanup-policy)一致；本项目的版本数量和缓冲时间是自己的配置，不来自该文档的默认推荐。

## 同时处理重复占用

现有 [Publish-StagedResources.sh](../Publish-StagedResources.sh) 使用 `cp -an -- "$previous/." "$version/"`，把历史目录中缺少的文件补入新版本，以继续提供旧目录的哈希资源地址。因此单纯删除早期版本文件夹，不能消除仍被复制进新目录的所有历史资源。

新热更渠道采用不可变资源目录与完整引用清单：代码更新可以直接引用已有资源版本，内容相同的不可变包尽量共享存储，不把全部历史包再次继承到新版本。清理先处理废弃发布/暂存副本，随后只回收未被任何保护对象引用的资源内容。共享存储不能通过原地改写文件实现，以免改到仍在使用的版本。

旧 `/StandaloneWindows64` 渠道需要单独盘点并导入资源引用及客户端兼容信息；盘点完成前不自动修剪其在用目录。游戏后端目录、签名私钥和非发布数据不在清理范围。

## 验收要求

验收覆盖当前版本保护、回退顺序、刚下线但首次发布时间很早的版本、快速连续发布超过保留数量、共享资源引用、兼容渠道隔离、活动上传和中断恢复、未知目录保护、清理与发布竞争、磁盘不足、路径越界及符号链接、重复执行。正式云端部署和现场检查仍需按下面的流程进行。

## 工具及调用入口

这些是新热更渠道的命令，不能用旧 `Upload-StagedResources.ps1` 发布代码包。命令在本机 Windows PowerShell 中执行。服务器需要 Python 3.11 或以上版本，私有存储和公共资源目录需处于同一文件系统，以共享不可变包的硬链接。

| 命令 | 功能 |
| --- | --- |
| `Build-HotUpdateRelease.ps1` | 在专用隔离 Unity 工程同步源码并编译代码；按需重建资源；生成完整配套 Package。构建路径限制为 C:/QiDaoBuild 下的专用目录，禁止使用密钥目录。 |
| `Initialize-HotUpdateServer.ps1` | 首次创建独立渠道的私有注册表和公共目录；服务器工具存入其完整哈希对应的目录，不覆盖正在运行的旧工具。`-UpdateToolsOnly` 只安装匹配工具版本，不重置信任配置和历史记录。 |
| `Upload-HotUpdateRelease.ps1` | 校验签名和本地全部文件；服务器比对已有内容哈希后，仅传缺少的数据；支持再次执行。暂存成功不切换线上。 |
| `Publish-HotUpdateRelease.ps1` | 检查正式配套基础客户端凭证、递增序号与完整目录；原子替换签名指针，通过 HTTP 逐个复核公开文件，再自动执行清理。公开验证失败时恢复旧指针与注册表。 |
| `Rollback-HotUpdateRelease.ps1` | 从保留的成功版本取代码/资源配套信息，在本地重新签发更高序号的新发布。默认仅暂存，加 `-Publish` 才切换。不能直接把旧 current.json 覆盖回去绕过防降级。 |
| `Clear-HotUpdateServerHistory.ps1` | 默认生成清理预演；`-Apply` 应用同一注册表/策略/线上指针快照对应的计划。快照变化或发现未知文件时拒绝删除。 |
| `Set-HotUpdateReleaseProtection.ps1` | `-Protect` 固定保留兼容或恢复需要的正式版本；`-Unprotect` 解除额外保护。当前版本仍始终受到保护。 |
| `Get-HotUpdateServerState.ps1` | 只读查询版本、磁盘可用空间与待恢复事务；不通过查询自动修复指针。 |
| `Get-LegacyResourceInventory.ps1` | 只读盘点旧上传、旧发布目录和当前目标；不删除或修剪旧渠道。逻辑字节数不等于去重后的实际磁盘占用。 |
| `New-HotUpdateBaseClientProof.ps1` | 核对基础客户端构建元数据、全部静态文件哈希、代码预载与完整资源/地图/登录验收，再本地签名。验收客户端凭证标为 acceptanceOnly，不能授权正式发布。 |
| `Clear-LocalHotUpdateExecutions.ps1` | 只清理指定代码缓存对应的无用执行副本；默认预演。保护活跃/恢复代码、缓冲期内副本、运行进程、被占用的使用锁和未知目录，不操作 Unity 资源缓存。 |

正式发布包有独立 `Package` 子目录，包含签名 current.json、业务 DLL、完整资源和可选配套客户端凭证；编译中间文件、签名私钥及工具不进入这个包。UploadOperations 与收据放在 Package 外部，重传不会污染不可变包。

尚无正式新基础客户端及其验收凭证时，可以构建和暂存完整包，但服务器拒绝上线。这是首次迁移的一次配套限制；之后兼容的业务更新复用同一份正式基础客户端凭证。

### 本机构建示例

`-Sequence` 必须高于该渠道已发布序号，可先用 Get-HotUpdateServerState 查询；不要把应用显示版本号当作这个序号。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "F:\游戏项目\HUNDUN\Tools\HotUpdate\Build-HotUpdateRelease.ps1" -Sequence <新发布序号> -BuildProjectDirectory "C:\QiDaoBuild\HotUpdateBuild\HUNDUN" -RebuildResources
```

只改代码且类型数据库兼容时，省略 `-RebuildResources`，通过 `-ResourceBuildReport <之前已校验的资源报告路径>` 复用资源。兼容校验失败需重建资源，不能跳过检查。首次创建隔离工程可能需要较大的本地磁盘空间，脚本会先估算新增复制量。

### 服务器一次性准备

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "F:\游戏项目\HUNDUN\Tools\HotUpdate\Initialize-HotUpdateServer.ps1"
```

默认只创建 `/srv/hundun-hotupdate/windows-mono-v1`、对应的哈希工具目录及 `/srv/hundun-resources/client-hotupdate/windows-mono-v1`，不修改游戏后端。它不会擅自修改现有 HTTP 服务配置。需要确认公共地址确实服务该目录；如果现有服务是 nginx，可审查合并 `nginx-hotupdate.location.conf`，通过 nginx -t 后再自行安排 reload。当前尚未在真实云端安装或验证此配置。

### 暂存与发布示例

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "F:\游戏项目\HUNDUN\Tools\HotUpdate\Upload-HotUpdateRelease.ps1" -ReleaseDirectory "<Build-HotUpdateRelease 输出目录>"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "F:\游戏项目\HUNDUN\Tools\HotUpdate\Publish-HotUpdateRelease.ps1" -ReleaseId "<已暂存的发布ID>"
```

只有 `Publish` 才切换新渠道。所有内容与旧 `/StandaloneWindows64` 渠道分开。事务记录支持中途退出后在下一次写操作恢复；只读查询与清理预演遇到未完成事务会报告或拒绝继续，不偷偷改线上状态。成功发布后的清理失败单独报告，不把已经成功的发布误报为已回退。

### 回退与清理示例

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "F:\游戏项目\HUNDUN\Tools\HotUpdate\Rollback-HotUpdateRelease.ps1" -TargetReleaseId "<保留的成功版本ID>" -OutputDirectory "<新的本地回退记录目录>" -Publish
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "F:\游戏项目\HUNDUN\Tools\HotUpdate\Clear-HotUpdateServerHistory.ps1" -PlanPath "C:\QiDaoResources\cleanup-preview.json"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "F:\游戏项目\HUNDUN\Tools\HotUpdate\Clear-HotUpdateServerHistory.ps1" -PlanPath "C:\QiDaoResources\cleanup-preview.json" -Apply
```

回退私钥默认仍位于 `C:\QiDaoBuild\Keys\QiDaoHotUpdate-RSA.xml`，只在本地签名，不上传。清理当前策略与注册表的锁覆盖整个上传接收/发布/删除过程；仍持有锁的实际上传不会被清理。已经中断且锁已释放的上传按记录的最后活动时间计算缓冲，不能永久冒充“正在上传”。

本地执行副本使用 `.usage-v1` 共享读锁，游戏和启动进程均持有使用保护；清理须取得排他写锁。旧版本没有这一标记的目录保守保留并报告。用户安装目录、私钥和其他账号缓存不在清理范围。
