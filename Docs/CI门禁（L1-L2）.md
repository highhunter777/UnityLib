# CI 门禁：L1（dotnet）与 L2（Unity 侧）

> 建立：2026-09-15。依据：《测试开发方案》§4 ①（L1 dotnet 单测）与 §7.2 缺口 a（Unity 侧无门禁）。
> 触发原因：**2026-09-15 事故**——上游提交的 16 个 `.meta` 把 `guid` 写成 64 位 base64，Unity 拒绝导入这些资源 → `Unity.Pipeline`/`GameScheduler` 等类型在编译中消失 → 依赖方 9 条 `CS0246`；而 **L1 全程绿色**（dotnet 按路径 glob 编译、不读 `.meta`、不经 Unity 编译器）。

## 1. 两级门禁的分工

| 级别 | 在哪跑 | 抓什么 | 时长 |
| --- | --- | --- | --- |
| **L1** | GitHub-hosted `windows-latest`（`.github/workflows/ci.yml` → `l1-dotnet-test`） | `dotnet test Tests/Tests.slnx`：框架层纯 C# 单测（LiteFramework.Core / LiteSim.Core / DisciplineScanner） | 秒级 |
| **L2** | **自托管 runner**（Windows + 完整工程）→ `l2-unity-gate` | ① **非法 meta/GUID 扫描**（纯文件）② **Unity 侧编译/诊断状态**（经 Unity Pipeline 或 batchmode EditMode 测试） | ①秒级 ②分钟级 |

**L2 的独有价值**：它是唯一能发现"**资源导入/Unity 编译**"类故障的一层——L1 结构性看不见。

## 2. 脚本用法（`scripts/l2-unity-gate.ps1`）

```powershell
# Windows PowerShell 5.1 亦可（本机未装 pwsh）
powershell -NoProfile -File scripts/l2-unity-gate.ps1                     # 全量（自动选模式）
powershell -NoProfile -File scripts/l2-unity-gate.ps1 -MetaScanOnly       # 只跑①（无 Unity 环境也能用）
powershell -NoProfile -File scripts/l2-unity-gate.ps1 -RunEditModeTests   # 强制 batchmode EditMode 测试
powershell -NoProfile -File scripts/l2-unity-gate.ps1 -UnityExe 'D:\Unity\2022.3.55f1c1\Editor\Unity.exe'
```

**自动选择的两种模式**：

| 模式 | 触发条件 | 做法 | 特点 |
| --- | --- | --- | --- |
| **A. Pipeline**（推荐） | `Library/Pipeline/.unity-pipeline-port` 存在（编辑器在跑） | `unity command recompile_status` + `console_status` | **无需关闭编辑器**；复用已连接的编辑器会话 |
| **B. batchmode** | 编辑器未跑，或显式 `-RunEditModeTests` | `unity test --mode EditMode --output TestResults/editmode-results.xml` | 需要授权激活，且**不得有其它实例占用该工程**（脚本会检测并明确报错） |

退出码：**0 = 通过，1 = 有 FAIL**（可直接用作 pre-push/CI 门禁）。

## 3. 为什么 L2 不进 GitHub-hosted CI

| # | 原因 |
| --- | --- |
| 1 | **全新克隆缺件**：按仓库约定只提交自研代码/预制品/文档；`ProjectSettings/`、美术素材、场景、Odin 授权包均不入库（见《克隆后自备清单》）→ GitHub runner 上工程**无法编译** |
| 2 | **编辑器版本不匹配**：本机是 **Unity 中国版 2022.3.55f1c1**，GitHub 上的 Unity 镜像与 EULA/授权都不对应 |
| 3 | **授权**：Unity 需激活（Personal/Pro），在共享 runner 上做激活需要把凭据放进 Secrets，成本与风险都高于收益（demo 阶段） |

→ 结论：**L2 走自托管 runner（本机/内网机器）**，用仓库变量 `L2_ENABLED=true` 开启；未开启时该作业直接跳过（不产生红叉）。

**开启步骤**：仓库 Settings → Secrets and variables → Actions → Variables → 新建 `L2_ENABLED` = `true`；并在该机器上注册 self-hosted runner（标签 `self-hosted`、`windows`）。

## 4. 建议的本地用法（比 CI 更实用）

- 大改（改 `.meta`、动包、动资源）后：`powershell -NoProfile -File scripts/l2-unity-gate.ps1` —— 编辑器开着时走 Pipeline，**几秒出结论**
- 纯代码改（不碰资源）：L1 足够（`dotnet test Tests/Tests.slnx`）
- 可选：装成 pre-push 钩子（`powershell -NoProfile -File scripts/l2-unity-gate.ps1 -MetaScanOnly`，零成本拦截"坏 meta"）

## 5. 现状与后续

- ✅ L1：已在 CI 跑（`l1-dotnet-test`）
- ✅ L2：脚本 + 作业就绪（作业默认跳过，待 `L2_ENABLED=true` + 自托管 runner）
- ⏳ 后续可加：把「非法 meta 扫描」这条规则**并入 `Assets/Tools/DisciplineScanner`**（则 L1 也能拦住它，L2 只留 Unity 编译/测试）——**这是性价比最高的一步**
