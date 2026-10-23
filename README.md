# GTFO-Mod-Build-and-Verification-Tools

共用构建、身份迁移、输入/暂存核验和打包工具；本仓不是游戏MOD。

- src/：BrandTransform与ModuleMigration通用实现。
- tools/：路径检查、构建、测试调度、锁更新、提交检查、钩子安装、打包。
- tests/：配置迁移回归；BoosterSelection夹具是生产代码副本，后续去重需验证，不能直接删除。

工具服务7个功能仓，共用工具修改后应重建并回归全部功能仓。具体MOD的CombatPolicy测试兼容类已归机器人仓的Captain.Commands/tests/Fixtures。使用方式见 [DEVELOPMENT.md](DEVELOPMENT.md)。
