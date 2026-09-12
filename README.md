# LiteFramework

轻量自研游戏框架：骨架九件（事件 / 引用池 / 对象池 / 游戏时钟 / FSM / Setting / 日志 / FileSys / 装配）+ DI + Lua 逻辑注册表。

## 目录结构

| 目录 | 说明 |
| --- | --- |
| `Assets/LiteFramework` | 框架本体（UPM 本地包 `com.litegame.framework`） |
| `Assets/LiteFramework/Scripts/Core` | `LiteFramework.Core`：纯 C#，无引擎依赖 |
| `Assets/LiteFramework/Scripts/Unity` | `LiteFramework.Unity`：Unity 封装层，依赖 UniTask |
| `Assets/Plugins/UniTask` | 框架唯一第三方依赖（随库内置） |
| `Docs` | 配套文档：设计方案、实施手册、帧同步 / 状态同步方案等 |
| `Tests` | dotnet 单元测试（xUnit，net8.0） |

## 运行单元测试

```bash
cd Tests
dotnet test
```

双轨编译：dotnet 侧的 bin/obj 重定向到 `Assets/LiteFramework/.dotnet/`，不会污染 Unity 资产数据库。
