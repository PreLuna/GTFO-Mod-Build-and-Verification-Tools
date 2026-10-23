# 共用构建与核验

7 个功能仓库的 Build.ps1 调用 tools/Build-Repository.ps1。它读取 repo.json、验证 maintenance.lock.json、编译源码或转换冻结基线，将安装候选写入功能仓库 out/package。游戏目录仅提供编译引用。

- src/BrandTransform.cs：旧身份映射、配置迁移注入及 IL 核验。
- src/ModuleMigration.cs：运行时配置迁移与冲突保护。
- tools/Compile.ps1：PowerShell 7 自带 Roslyn 编译器入口，要求支持 C# 12。
- tools/Verify-Repository.ps1：输入哈希和构建程序集身份、依赖核验。
- tools/Test-Recipes.ps1：运行无游戏进程的离线测试。

执行 `./Test.ps1 -RuntimeRoot '<.NET6目录>'` 验证配置迁移。修改共享工具后，需重建全部 7 个功能仓库。冻结模组主体仍依赖各仓库 baseline/ 中的 5 个锁定 DLL。

源文件修改后可用 `tools/Update-Lock.ps1 -RepositoryRoot '<仓库目录>'` 显式刷新锁，先人工审核 Git 差异再采用新的锁文件。

## 提交前检查

各仓目录是后续开发的唯一源码入口，安装 ZIP 与 out/ 均为构建产物。
修改后先审查差异并暂存有意提交的文件，再执行工具仓库的 tools/Update-Lock.ps1 -RepositoryRoot '<仓库>'，暂存更新后的锁文件，最后执行 tools/Check-Commit.ps1 -RepositoryRoot '<仓库>'。
新增文件、中文路径、遗漏暂存、未锁定源码及误入暂存区的输出文件都会被检查。更新锁仅表示确认输入，不等于构建或测试通过；相关模组仍需重建和回归。

首次克隆整套仓库后运行工具仓库 tools/Enable-Hooks.ps1 -CheckoutRoot '<9仓所在目录>'，安装本机提交前检查。Git 不会随克隆自动携带 .git/hooks。此检查可由 Git 的 --no-verify 绕过，不能代替人工审核与发布验收。

## 生成安装包

在干净、已提交的源码上构建并测试后，运行 tools/Package-Release.ps1 -RepositoryRoot '<功能仓库>' -DestinationRoot '<源码仓库之外的发布产物目录>' -Version '<版本标签>'。脚本核对构建身份、工具链锁、数据与全部安装文件，输出 ZIP 和包含源码提交号的哈希清单，拒绝覆盖已有版本。它不会上传，也不会把离线检查标成游戏实测。
