# Unity Pipeline 包本地补丁（com.unity.pipeline 跑在 Unity 2022.3）

> 更新：2026-09-15。目的：把 **Unity 官方 CLI（`unity`）** 的"连接运行中编辑器"能力接入本工程。
> 现状：**已跑通** —— 包编译成功、Pipeline HTTP 服务在 **端口 7800** 运行、CLI 可执行 **151 条命令**。
> ⚠️ 前提认知：上游包 **只支持 Unity 6000.0**（registry 里 7 个版本全声明 `"unity": "6000.0"`）；本工程是 **2022.3.55f1**，因此下列补丁是**为 2022.3 定制的兼容层**。升级到 Unity 6 后应还原上游原包（补丁均带 `#if` 守门或可逆）。

## 0. 来源与落位

| 项 | 值 |
| --- | --- |
| 来源 | `https://packages.unity.com/com.unity.pipeline`（**直连可达，无需代理**） |
| 版本 | **0.7.0-exp.1**（dist-tags.latest；全版本均 exp） |
| 包体 | `https://download.packages.unity.com/com.unity.pipeline/-/com.unity.pipeline-0.7.0-exp.1.tgz` |
| 落位 | `Packages/com.unity.pipeline/`（embedded，823 文件；**已加入 `.gitignore`**，见 §4） |

## 1. 必需依赖（UPM 自动解析）

`com.unity.nuget.mono-cecil 1.11.6`（registry）、`com.unity.nuget.newtonsoft-json`（工程已有 3.2.1）、`com.unity.test-framework 1.1.33`（已有）、`com.unity.inputsystem`（已有 1.11.2）。

## 2. 补丁清单（7 处；重抓后按此重放）

| # | 文件 | 改动 | 原因 |
| --- | --- | --- | --- |
| 1 | `Runtime/Plugins/CodeAnalysis/*.dll.meta`（5 个）与 `Runtime/Analyzers/IlInterpreterAnalyzer.dll.meta` | **转为 2022.3 原生 v2 格式**（`serializedVersion: 2`）+ 平台对齐（**Editor ✓ / Standalone Win·Win64·Linux64·OSX ✓**） | 上游 meta 用 `serializedVersion: 3`（Unity 6 格式），**2022.3 误读为全平台禁用**（`GetCompatibleWithEditor()=False`）→ 插件 DLL 未生效 → `IlInterpreter` 找不到 `UnityPipeline.*` 命名空间。修法：`PluginImporter.SetCompatibleWithEditor(true)` + `SetCompatibleWithPlatform(Standalone…)` + `SaveAndReimport()` —— Unity 随之把 meta **重写为 v2 格式**（已核验 6/6 均为 `serializedVersion: 2`，Editor/Win64 兼容均为 ✓） |
| 2 | `Tests/` → **`Tests~`** | 重命名目录 | 包自带测试程序集引用 nunit.framework，2022.3 下解析失败；`~` 后缀被 Unity 忽略（源码保留） |
| 3 | **新增** `Editor/AnalyticInfoCompat_2022.cs` | `#if !UNITY_6000_0_OR_NEWER` 下提供 `UnityEngine.Analytics.IAnalytic`（含嵌套 `IData`）与 `AnalyticInfoAttribute` | 这两个类型是 Unity 6 新增，2022.3 编辑器内**不存在**（实测全程序集扫描为 0） |
| 4 | `Editor/PipelineAnalytics.cs` | `s_Send` 在 2022.3 下置空（`analytic => { }`） | 2022.3 的 `EditorAnalytics` 只有 `SendEventWithLimit` 系列，**没有 `SendAnalytic(IAnalytic)`** |
| 5 | `Editor/Console/EditorConsoleGroundTruth.cs` | `ConsoleWindowUtility.consoleLogsChanged` 订阅与 `GetConsoleLogCounts(...)` 在 2022.3 下 `#if` 排除（计数置零） | `ConsoleWindowUtility` 为 Unity 6 API |
| 6 | `Editor/Commands/Materials/MaterialCommands.cs` | `mat.rawRenderQueue` → 2022.3 用 `mat.renderQueue` | Unity 6 新增 `rawRenderQueue`（-1=继承）；2022.3 无 → **读回值退化为有效值**（写入侧 `renderQueue:-1` 仍可用） |
| 7 | `Editor/Commands/Assets/AssetCommands.cs` | 文件头加 `using PhysicsMaterial = UnityEngine.PhysicMaterial;`（`#if !UNITY_6000_0_OR_NEWER`） | Unity 6 更名 `PhysicMaterial` → `PhysicsMaterial` |

**副作用（可接受）**：分析遥测（#3/#4）与 console 计数对账（#5）在 2022.3 下不生效；材料 renderQueue 读回语义降级（#6）。**Pipeline 服务/命令能力不受影响。**

## 3. 验证记录（2026-09-15）

| 检查 | 结果 |
| --- | --- |
| 程序集产物 | `Unity.Pipeline.dll`(354KB) / `Unity.Pipeline.Editor.dll`(**672KB**) / `IlInterpreter.dll`(361KB) / `Attributes.dll` / `CodeGen.dll` —— **全部产出** |
| 程序集加载 | 5 个 `Unity.Pipeline*` 全部载入 AppDomain，`PipelineServerStartup` 类型可达 |
| 服务 | `Library/Pipeline/.unity-pipeline-port` → **port 7800 / pid 215852 / mode=editor**；`unity status` → `7800 ready`；`unity pipeline list` → **Server Reachable=true** |
| CLI 往返 | `unity command console_status` → `success:true`（返回 `compilationFailed:false`、`counts{error:1,warn:8}`）；`unity command recompile_status` → `{"status":"idle","failed":false}` |
| 命令目录 | `unity list` → **151 条命令**（GameObject/组件/prefab/Animator/Timeline/build/tests/截图/console/audit/navmesh/batch…） |
| 控制台 | 编译 error **0** |

## 4. 使用方式与注意事项

```bash
# 前置：Unity CLI 已在 PATH（C:\Users\hunter\AppData\Local\Unity\bin\unity）
unity status                                  # 看已连接编辑器与端口
unity list    --project-path "E:\unityProject\Test"     # 列可用命令（151 条）
unity command console_status --project-path "E:\unityProject\Test"
unity command recompile_status --project-path "E:\unityProject\Test"
```

- ⚠️ **本机需带 `--project-path`**：CLI 当前把工程登记了两次（`E:\...` 与 `e:\...`，后者无 PID），不带参数会报 "Multiple Unity Editor instances"（Hub 注册表为空，疑为端口描述符大小写变体所致；功能不受影响）
- ⚠️ 服务由包的 `[InitializeOnLoad]`（`PipelineServerStartup`）**自动启动**；本次是首次导入后手动调 `EnsureServerStarted()` 拉起，**重启 Unity 后应自动就绪**
- ⚠️ 描述符自带提示：编辑器**非自动化模式**启动时可能卡在模态对话框；无人值守场景建议用 `unity open` 启动编辑器
- ⚠️ 已知无害告警：`Runtime/Analyzers/IlInterpreterAnalyzer.dll` 加载失败（Roslyn 分析器，2022.3 下版本不匹配）——仅诊断功能缺失，可忽略；如要消除可移除其 meta 的 `RoslynAnalyzer` 标签

## 5. git 策略

`Packages/com.unity.pipeline/` **不入库**（第三方包 + 本地补丁，与 `Packages/MCPForUnity/` 同策略）；**本文档入库**——重抓包后按 §2 重放补丁即可复原。
