# GTFO-Mod-Build-and-Verification-Tools

共用构建与核验工具。本仓是辅助仓库，不是游戏MOD。

[定位入口](#modules) · [依赖与联动](#relations) · [开发与排查](#development) · [来源说明](#sources)

<a id="modules"></a>
## 定位入口

| 入口 | 用途 |
| --- | --- |
| [tools](tools) | 编译、测试调度、路径与输入核验、维护锁、钩子和打包 |
| [src/BrandTransform.cs](src/BrandTransform.cs) | 生成PreLuna插件身份，合并所需迁移逻辑并检查原方法保留 |
| [src/ModuleMigration.cs](src/ModuleMigration.cs) | 旧配置／保存记录迁移与新旧插件互斥支持，构建时合入输出DLL |
| [Test.ps1](Test.ps1)／[tests](tests) | 配置／保存迁移回归，当前72项检查 |

普通玩家使用各功能仓Releases，无需下载或运行本仓。开发者将本仓与功能仓放在同一父目录；构建工具是开发配套，out才是可再生缓存。

<a id="relations"></a>
## 依赖与联动

工具实现变更需回归7个功能仓及既有测试入口。CombatPolicy测试夹具在机器人仓Captain.Commands/tests/Fixtures；本仓tests/Fixtures/BoosterSelection.cs仍是生产代码副本，去重须另行验证。源码仓不依赖本地历史目录。

<a id="development"></a>
## 开发与排查

本仓使用支持C#12的PowerShell 7和.NET 6；它没有根Build.ps1，辅助C#按功能仓构建需求编译。配置迁移测试从本仓运行：

```powershell
./Test.ps1 -RuntimeRoot 'C:/Program Files/dotnet/shared/Microsoft.NETCore.App/6.0.16'
```

它使用受控夹具验证旧文件保留、目标配置优先、失败拒绝和互斥规则，不启动游戏。

<a id="maintenance"></a>
## 共用维护流程

各仓保留独立.git；以下命令从工具仓目录运行，把目标路径替换为实际仓库。先审查差异并暂存有意输入，再显式更新锁并暂存锁，最后核验：

```powershell
$repo = '../GTFO-Bot-Commands-and-Control'
git -C $repo diff
git -C $repo add -A
./tools/Update-Lock.ps1 -RepositoryRoot $repo
git -C $repo add maintenance.lock.json
./tools/Check-Commit.ps1 -RepositoryRoot $repo
```

锁一致只证明输入匹配。随后按变更范围构建／运行实际存在的测试入口；Git提交会调用本机钩子，不使用--no-verify绕过。新克隆需显式安装钩子（Git不会携带.git/hooks）：

```powershell
./tools/Enable-Hooks.ps1 -CheckoutRoot '..'
```

实现变更回归7个功能仓及现有6个测试入口；纯文档变更检查链接、当前路径、署名保留、代码／数据未变及维护锁即可，不冒充重跑构建。安装ZIP、个人CFG、框架、日志不进入源码Git，仅允许repo.json登记的冻结DLL输入。

<a id="packaging"></a>
## 发布与打包

从干净提交重新构建、核验和测试后调用tools/Package-Release.ps1。DestinationRoot必须选择整个开发工作区之外，不在本地开发目录保存安装包或维护一份安装包文件清单。脚本当前只强制拒绝源码仓内输出，操作者还须遵守工作区外输出约定。

```powershell
./tools/Package-Release.ps1 -RepositoryRoot '../GTFO-Bot-Commands-and-Control' -DestinationRoot 'D:/GTFO-Releases' -Version 'candidate-next'
```

未提交改动仅显式使用-Preview做本地验证，其SourceCommit为空、ReleaseReady=false，仍输出到工作区外。正式发布按当次授权执行，不覆盖已有标签／附件；整合仓登记真实源码提交和正式ZIP哈希。玩家只需要Releases安装包及必要前置。

<a id="sources"></a>
## 来源说明

本仓库维护 PreLuna 的构建或整合资料。

保留源文件中的作者与版权声明。部分模组包含第三方派生实现或冻结基线，本说明不替代上游许可证，也不新增再分发授权；本仓库没有统一重授权声明。
