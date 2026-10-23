# 开发与关联

本仓维护共用构建与核验工具（辅助仓）。游戏MOD的实际实现位于功能仓内，不在这里复制一套。

## 暂存与维护锁

审查Git差异并暂存有意提交的输入，再运行工具仓tools/Update-Lock.ps1 -RepositoryRoot <仓库>，暂存更新后的maintenance.lock.json，最后运行tools/Check-Commit.ps1。只检查一致性不等于构建或游戏实测通过。

新克隆用工具仓tools/Enable-Hooks.ps1 -CheckoutRoot <9仓目录>安装本机检查；Git不自动携带.git/hooks。本机钩子能被绕过，仍需人工审核与回归。不要向源码仓加入ZIP、日志、个人CFG或框架。必要的冻结DLL只允许repo.json登记路径。

## 安装包与提交状态

在干净源码提交上构建测试，用tools/Package-Release.ps1 -RepositoryRoot <仓库> -DestinationRoot <源码仓库之外> -Version <新标签>生成ZIP和包含准确源码提交的清单。

本地尚未提交时，仅可显式用-Preview做验收预览；清单中SourceCommit为空、ReleaseReady=false，BaseCommit只是修改前提交。预览不作为正式发布，不覆盖已发版本。构建输出记录当前输入锁与工具锁。

整合仓默认验证已发布精确提交；-LocalWorkspace验证未提交整理的当前锁、暂存内容及基准HEAD。未来获准提交后，再登记真实新提交并重新下载验收；保留原Releases标签和旧验收记录。

工具入口：Build-Repository读取各MOD显式SourceRoot/DataRoot；Verify-Repository扫描InputRoots并核对程序集依赖的GUID、硬/软标志及版本表达式；Repository-Paths阻止路径越界与链接路径。
