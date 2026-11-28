资源分包迁移工具

prepare_migration.py 只生成方案。普通放行要求完整源文件校验通过。
--metadata-only-with-build-hold 仅允许已证明的图片读取差异：直接读取、Git身份和隔离副本匹配原基线，最后的重复读取一致，全部导入元数据正常。
该选项不放行资源构建，不改变任何预期哈希，不覆盖图片。

PackingMetadataMigration.cs 只复制到 C:/QiDaoBuild 隔离工程的 Assets/Editor 中执行。
迁移只保存 Addressables 分组、模式与显式入口，保留原地址、标签、资产GUID和导入依赖。
物品表与轻量掉落物预制体的互相引用保留在同包中；动画FBX避免作为整文件显式根而引入无用对象。

check_plan_dependencies.py 检查源导入依赖预测的跨包回环。
commit_metadata.py 在两次隔离验证、身份稳定和原文件匹配后，只提交 Addressables 元数据，保存备份并提供失败回退。
verify_source_metadata.py 对源项目核对入口、GUID、分组身份与Schema引用。

packing-state.json 记录新资源基线需求和读取问题。迁移后旧资源报告不再代表当前分包。
Build-HotUpdateRelease.ps1 会拒绝复用旧报告，也会在读取一致性尚未确认时停止正式发布构建。
第三阶段：增量构建流程

resource_workflow.py 管理不可覆盖的源校验、原始完整基线、布局兼容约定和每版下载差量。
HotUpdateResourceWorkflow.cs 在隔离工程中校验输入及布局。更新始终使用首次完整基线的 addressables_content_state.bin；后续更新不能替换它。
每个资源版本依然包含完整可用清单和全部被引用的包，方便现有发布校验、回退、版本保留与垃圾回收。未变化包保留原生缓存名称与Hash；下载地址变化不强制缓存失效。
上传仍使用现有按SHA识别服务器缺失文件的工具；无需改服务器协议或资源启动下载代码。

构建模式：
  Baseline：首次分包、升级Unity/Addressables、改变已有分组身份/打包配置、变更序列化类型等情况显式建立完整基线。
  Auto：和上次已验证报告比较资源输入，只有代码方法体等改变时复用资源；资源改变时走内容更新。
  Update：强制走内容更新；未变化也允许构建并证明零差量。布局不兼容时停止，不能静默回退成全量构建。
  Reuse：只复用资源；任何资源输入变化都会拒绝。代码编译仍需证明序列化类型与资源匹配。
  原 -RebuildResources 等同 Baseline，原 -ResourceBuildReport 可作为 PreviousResourceReport 的兼容别名。

调用示例（仅供正式构建前置条件满足后使用；路径中的版本ID均应替换为实际的新ID）：
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "F:\游戏项目\HUNDUN\Tools\ResourcePacking\Verify-ResourceInputs.ps1" -ReportPath "C:\QiDaoBuild\Verification\new-source-check.json"
  powershell.exe -NoProfile -ExecutionPolicy Bypass -File "F:\游戏项目\HUNDUN\Tools\HotUpdate\Build-HotUpdateRelease.ps1" -Sequence 递增发布序号 -BuildProjectDirectory "C:\QiDaoBuild\DedicatedProject" -ResourceMode Baseline -SourceVerificationReport "C:\QiDaoBuild\Verification\new-source-check.json"
  后续使用 -ResourceMode Auto -PreviousResourceReport "上次ResourceBuild\resource-build-report.json"，同时提供新的 SourceVerificationReport。
校验报告应在构建前一小时内产生，构建前后输入必须一致；报告文件不能覆盖。使用固定C盘隔离工程，不在用户正在编辑的工程构建。

任何不一致读取都会使校验失败并留存证据，不修改预期Hash，不自动删除玩家缓存。历史读取异常根因仍未确认，packing-state.json 的正式构建待校验状态没有被移除。
状态文件、源校验报告、布局约定与差量报告只留在本地，不进入公开Package；必须保留原始基线及最近资源报告的本地构建目录。
下载差量按原生缓存名称和Hash比较，不按URL或仅按相同字节判断。预估假设旧缓存完整有效；缓存缺失/损坏、依赖一起变化会增加下载。重分包首次上线本身可能使玩家下载新包，后续收益需实测。

Verify-IncrementalAcceptance.py 只使用隔离小型原生测试项目和回环HTTP，验证完整基线、修改资源、无变化更新、客户端冷/更新/热启动以及公开Package边界。
第三阶段未构建正式游戏资源或新的客户端/安装程序，没有上传或切换服务器。第四阶段再做真实游戏包体积与下载验收。
