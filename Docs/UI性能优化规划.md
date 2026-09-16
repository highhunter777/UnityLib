# UI 性能优化规划

> 制订日期：2026-09-17。定位：**UI 壳与控件库的性能治理施工图**——基于当前代码实测出的热点清单（每条带证据）＋分批落地顺序＋预算红线。
> 压力基准（2026-09-17 用户定案）：**M11 demo 规模**——8 人房 HUD（血条/护盾条/击杀条/飘字）＋结算页，**移动端 60fps**。
> 起步策略（2026-09-17 用户定案）：**直接改已确认热点**，测量与护栏后置为批④。
> 范围：`Assets/LiteGame/Scripts/Runtime/Shell/UI/**` ＋ `Assets/LiteGame/UI/Widgets/**`（prefab/构建器）＋ 相关 Editor 校验。**不碰 `LiteSim.Core`**。
> 相关文档：《动效设计方案》§4（性能红线，本规划承接其落地）、《UI控件库Prefab落地规划》§3（批⑧ 剩余缺口）、《编辑器设计规划》§2.1（校验面板）。

---

## 1. 现状盘点（证据表）

全部为本次读码实测，非推断。

| # | 事实 | 证据 |
|---|---|---|
| 1 | 每界面一个独立 Canvas（`AddComponent<Canvas>` + `overrideSorting`） | `Shell/UI/UIForm.cs:28-31` |
| 2 | 运行时**无 CanvasScaler**（仅编辑态示例有） | `UIService.cs:44-53` 建 `[UIRoot]` 无 Canvas/Scaler；`Editor/WidgetDemoMenu.cs:17-21` 是唯一出处 |
| 3 | **零图集**：无 `.spriteatlas`、无 `packingTag` | 全仓 `find Assets -name "*.spriteatlas*"` = 0；`grep packingTag Assets/LiteGame` = 0 |
| 4 | 关闭界面不销毁，`SetActive(false)` 落池（按 formId 常驻） | `UIService.cs:109-144`、`UIForm.cs:117-121` |
| 5 | 每帧遍历全部持有界面，仅 Active 转发 `OnUpdate` | `UIService.cs:263-267`；驱动 `GameEntry.cs:144` |
| 6 | Lua 回调走 `params object[]` → 每帧每界面一次数组分配 + float 装箱 | `LuaBehaviourAdapter.cs:90`（`Call(_onUpdate, "OnUpdate", deltaTime)`）、`:137-141` |
| 7 | `VirtualList.Refresh` 每次重复 `AddListener` | `Widgets/VirtualList.cs:64-65` |
| 8 | `VirtualList` 池**只增不毁**（无收缩/Trim） | `Widgets/VirtualList.cs:43-48` |
| 9 | `VirtualList` 裁剪靠 `SetActive` 开关，且滚动中每次事件重算 | `Widgets/VirtualList.cs:69-91`；脏标在 `:65` |
| 10 | `Countdown` 每帧无条件写 `Label.text`（mm:ss 每秒才变） | `Widgets/Countdown.cs:47-50,52-57` |
| 11 | `HpBar` 每帧 Lerp，收敛后仍每帧跑 | `Widgets/HpBar.cs:33-38` |
| 12 | `FlyTextPool` 每条飘字一个 `UniTask.NextFrame` 循环 + **每帧改 `TMP_Text.color`** | `Widgets/FlyTextPool.cs:41-67`（`:57-59` 改色 = 顶点 mesh 标脏） |
| 13 | `BindIndexBuilder` 每次 OnInit 全树 `GetComponentsInChildren`（含分配） | `Bind/BindIndexBuilder.cs:16`；调用点 `LuaBehaviourAdapter.cs:74` |
| 14 | `raycastTarget` 仅零星关闭，未成规范 | `Editor/WidgetPrefabBuilder.cs:113,130,254,271,529,743` |
| 15 | 红点已是事件驱动、零轮询（**已达标，无需优化**） | `Widgets/RedDot.cs:37-40`、`RedDotRegistry.cs` |
| 16 | 全工程 0 处 LayoutGroup / ContentSizeFitter（**无此开销**） | 控件库与 prefab 构建器均手动 `anchoredPosition` 排布 |
| 17 | 动效统一 DOTween（自更新，非 MonoBehaviour.Update），无原生协程 | `Anim/UiFx.cs`、`Strategies.cs:42-79` |
| 18 | 有 `IModuleStats` 契约与 `UIService.Snapshot`（无 ProfilerMarker） | `UIService.cs:269-290`、`LiteFramework` `IModuleStats.cs` |

**结论**：瓶颈不在"Update 数量"或"字典遍历"这类常见嫌疑（当前规模下可忽略），而在三处：**① 高频元素每帧触发 Canvas 重建（飘字/倒计时）② 缺失渲染面基础设施（图集/CanvasScaler/分层）③ 少量确定的泄漏与每帧分配**。

---

## 2. 预算与红线（M11 验收口径）

移动端中端机、8 人房满场战斗 30s 采样的预算：

| 指标 | 红线 | 采集方式 |
|---|---|---|
| UI 主线程耗时 | **≤ 2.0 ms/帧** | Profiler 抽样（批④ 记档） |
| UI drawcall（单页） | **≤ 25** | Frame Debugger / Profiler |
| overdraw | **≤ 2 层**（全屏动效叠加） | 承接《动效设计方案》§4 |
| 稳定帧 GC.Alloc（UI 路径） | **0 B/帧**（不含加载） | Profiler GC Alloc 列 |
| 界面打开（含实例化+OnInit） | ≤ 16 ms；结算页 ≤ 32 ms | 计时直读 |
| 控件模板自检 | **31/31 PASS 不许回归** | 菜单 `LiteGame/UI/校验控件模板` |
| L1 | 190 用例全绿不许回归 | `dotnet test Tests/Tests.slnx` |

---

## 3. 优化清单（按批次）

### 批① 热点修复（真 bug 与每帧浪费，立即可做，不依赖 M11）

| 项 | 现象与根因 | 改法 | 验收 |
|---|---|---|---|
| A1 | `VirtualList.Refresh` 每次都 `AddListener`（证据 #7）→ 监听累积，一次滚动触发 N 次 `ApplyCulling`，且界面复用后旧委托仍在 | `Awake`/`OnEnable` 一次性挂 + 幂等守卫；或挂前 `RemoveAllListeners` | 反复 Refresh 20 次后滚动一次，`ApplyCulling` 调用计数 = 1（加测试钩子断言） |
| A2 | 池只增不毁（证据 #8）→ 数据从 500 条降到 10 条后仍留 500 个实例常驻 | Refresh 时按 `n` 收缩（销毁富余实例），保留高水位上限；与"池的持有纪律"一致（池归控件自持） | 大数据量→小数据量切换后 `RealizedCount` 与池容量同步下降；池内实例数 ≤ n |
| A3 | 裁剪用 `SetActive`（证据 #9）→ 滚动中反复启用/禁用 Graphic，每次进出视口都吃一次 Canvas 重建 | 改为**窗口复用**：固定 N 个实例，滚出视口的实例直接换绑新索引（不 SetActive）；或至少把"逐项扫描"降为"按滚动增量算首末索引" | 滚动 500 条（视口容 8）时 `SetActive` 调用次数 ≤ 视口容量×2；`Bind(index, rt)` 语义不变 |
| A4 | `Countdown` 每帧写 `text`（证据 #10）→ TMP mesh 每秒 60 次重建，实际每秒才变 1 次 | 先算 `(int)totalSeconds`，与上帧相同即 return（避免插值字符串本身也每帧分配） | 倒计时运行 3s，`Render` 写文本次数 ≤ 4 |
| A5 | `HpBar` 收敛后仍每帧 Lerp（证据 #11） | 收敛判据（差值 < ε）即早退 + 自禁用 `enabled=false`，下次 Set 值再打开 | 静止血条 60 帧内 `Update` 自禁用 |
| A6 | `FlyTextPool` 每条飘字：一个 async 循环 + **每帧改 `TMP_Text.color`**（证据 #12）→ 顶点 mesh 每帧标脏，同屏 16 条 = 每帧 16 次重建 | ① 淡出改 `CanvasGroup.alpha`（不重建 mesh）② 改**单 ticker 集中驱动**：一个 Update 遍历活跃表推进全部飘字，替代 N 个 async 循环（红线：不用原生协程）③ 位置仍走 `anchoredPosition` | 同屏 16 条飘字稳定帧 `Canvas.SendWillRenderCanvases` 次数 ≤ 3；飘字生命周期与池归还语义不变（保留批⑦ 的销毁期守卫） |
| A7 | Lua 回调 `params object[]`（证据 #6）→ 每帧每界面一次数组分配 + float 装箱 | `OnUpdate` 走专用非分配路径（预置参数数组复用；其余回调低频保持现状） | Profiler 稳定帧 UI 路径 GC.Alloc = 0 B；每帧 Lua 回调无新数组（分配计数器断言） |

**A1–A3 是一组**（虚拟列表改造必须一次做完，避免"改一半语义漂移"）；**A4–A6 是一组**（高频元素去重建）；**A7 独立**。

### 批② 渲染面基础设施（M11 真机前置，必须与图集同批做）

| 项 | 现象与根因 | 改法 | 验收 |
|---|---|---|---|
| B1 | **无 CanvasScaler**（证据 #2）→ 真机多分辨率下 UI 尺寸失控 | `[UIRoot]` 挂 `Canvas` + `CanvasScaler`（`ScaleWithScreenSize`，参考 1920×1080，`MatchWidthOrHeight=0.5`），或按分辨率策略注入；每界面 Canvas 保留（隔离 rebuild 的既有红利） | 三档分辨率（16:9 / 18:9 / 4:3）下 HUD 与结算页布局不溢出、SafeArea 正确 |
| B2 | **零图集**（证据 #3）→ 跨纹理打断合批，HUD 图标 + 结算页图标 drawcall 高 | 建 `UI.spriteatlas`（收集 `Assets/LiteGame/UI/**` 与 `Art/UITextures`）；构建器给 Sprite 节点打 `packingTag`；与 YooAsset 收集组（tag `ui`）对齐 | 单页 drawcall ≤ 25；构建器重建 25 件模板后自检 31/31 不回退 |
| B3 | 每界面独立 Canvas（证据 #1）在 HUD 场景会失控（每个条目一个 Canvas = drawcall 断点爆炸） | **HUD 定案**：单 Canvas + **动态/静态分层**——静态层（准星/边框/小地图背景）与动态层（血条/飘字/击杀条）各一个子 Canvas；只有高频变化元素进动态层 | HUD 场景 Canvas 数 ≤ 3；`CanvasScaler` 只挂根 |
| B4 | 《动效设计方案》§4 的"动效元素再拆子 Canvas"**未落地** | 落地为构建器规则 + 校验器断言（动效件必须挂独立子 Canvas 或被列入白名单） | 校验器对 25 件模板跑规则 0 违规 |
| B5 | `raycastTarget` 未成规范（证据 #14）→ `GraphicRaycaster` 每次触摸遍历 canvas 上全部可命中 Graphic | 构建器**默认全关**、仅交互件显式开；校验器断言"非交互 Graphic 的 raycastTarget=true 即违规" | 校验器 0 违规；HUD 触摸帧耗时下降（批④ 记档） |
| B6 | overdraw 红线仅文档（§4） | 编辑器侧人工检查清单 + 全屏转场单 Image 约定（已实现，保持） | 人工过检记录 |

**顺序硬约束**：B2（图集）与 B3（分层 Canvas）必须同批——只分层不打图集 = 白增断点，只打图集不控重建面积 = 收益被 rebuild 吃掉。

### 批③ 数据面（M11 前定案，防返工）

| 项 | 现象与根因 | 改法 | 验收 |
|---|---|---|---|
| C1 | 若 HUD 走"每帧 Lua `OnUpdate` 逐字段拉 8 人血条"→ 每次 `Dispatch` 都构造 `LuaTable` payload（`LuaBehaviourAdapter.cs:106-133`），GC 与跨语言开销双高 | **定案：HUD 数据由 C# 直驱**（Sim 帧事件/快照差分 → HUD 组件，跨边界只传 id 与数值）；Lua 只管低频界面逻辑；确需批量时给批量 API（如 `Bridge.ui.SetHpBatch`），**禁逐字段每帧过 Lua** | 8 人房 HUD 稳定帧 Lua 调用次数 = 0（仅事件驱动时）；GC.Alloc = 0 B |
| C2 | `BindIndexBuilder` 每次 OnInit 全树扫描 + 数组分配（证据 #13）→ 结算页这类大页面打开时卡顿 | 按 prefab 资源路径**缓存"名字 → 节点路径"索引**，实例化后只按路径装配；`Editor/BindGen/` 目前为空，可作为生成侧落点 | 同 prefab 二次打开不再触发全树扫描；结算页打开 ≤ 32 ms |
| C3 | 红点/动效已达标（证据 #15/#17） | 保持不变，仅纳入批④ 统计位 | — |

### 批④ 测量与护栏（起步策略为后置，但 M11 前必须补齐）

| 项 | 内容 |
|---|---|
| D1 | `ProfilerMarker` 标注 UI 关键段：`UIService.Show`、`UIService.Tick`、`VirtualList.ApplyCulling`、FlyText ticker、`LuaBehaviourAdapter.OnUpdate` |
| D2 | `IModuleStats` 扩展（挂 DevHUD，禁止每帧分配）：活跃 tween 数、活跃飘字数、列表 realized 数、Canvas 数、UI GC 累计 |
| D3 | 基线记档：Editor + Android 真机，8 人房满场 30s，按 §2 表格采全指标（含改动前后对照） |
| D4 | 校验器规则进 CI：raycastTarget / 子 Canvas / packingTag / CanvasScaler 四项编辑态扫描，违规即红（与既有纪律扫描器同风格） |

---

## 4. 不做清单（明确排除，避免重复讨论）

- **不换 UI 框架、不自研合批**：UGUI + TMP 保持（《自研框架设计方案》§141 控件归壳的定案不变）。
- **不优化 `UIService.Tick` 的字典遍历**（证据 #5）：M11 十个界面量级下开销可忽略；待批④ 测出真实占比再议。
- **不使用 LayoutGroup 排布控件模板与列表**（证据 #16，实测全工程 0 处含第三方 prefab/scene）。这不是禁忌，是三条具体判据：①**与池化/裁剪对冲**——LayoutGroup 的契约是"子集合或尺寸一变就重排全部"，而 `VirtualList` 靠 `SetActive` 裁剪 + 手动 `anchoredPosition` + 实例复用换绑（`Widgets/VirtualList.cs:75-91`），挂上后每次进出视口的开关都触发整组重排、并按 active 子集覆盖我算好的窗口位置与滚动范围；②**开销是响应式的**——代价在"标脏 → `CanvasUpdateRegistry` → 重算 + 推父链 + mesh 重建"，而血条/飘字/倒计时恰好每帧在变，正是批① 要消灭的东西；③**模板由构建器确定性生成、结构固定**（`WidgetPrefabBuilder`），排布在编辑态一次算清、运行期成本为 0，用 LayoutGroup 是把"算一次"换成"每次变化重算"。
  **该用的判据（不搞一刀切）**：内容不定长需按内容自适应尺寸（文本长度决定宽高/自动换行/卡片流）＋ 变化频率低（刷新一次变一次而非每帧）＋ 数量小（几十项内），或纯编辑器期试错布局——满足即用，比手算划算。当前 25 件模板与两个列表都不属于该形态；未来背包/商城/聊天面板若命中该形态**可以引入**。
- **不动 DOTween / 不改动效时长**：动效是表现决策，不是性能手段；低端机降级走策略换短时长（《动效设计方案》§4 已有口径）。
- **不碰 `LiteSim.Core`**：UI 优化不得引入 Sim 侧改动或确定性影响。
- **不做 UI 分帧加载器 / 异步实例化管线**：当前打开耗时未实测超标（批④ 先量）。

---

## 5. 风险与坑

| 风险 | 说明 | 对策 |
|---|---|---|
| 虚拟列表改造动 Lua 契约 | `IVirtualListSource.Bind(index, rt)` 是 Lua 侧已公开语义（《UI控件Lua用法表》） | A3 只改内部复用策略，**签名与语义不变**；改前跑模板自检 + Lua shim 实测 |
| 图集打包属序列化内容 | 贴图合并会动资源引用 | **必须经 Unity/Pipeline 操作**（`UNITY-GUIDE.md` 红线）；禁手改 `.meta`/`.spriteatlas` 文本 |
| 分层 Canvas 与图集顺序做错 | 只分层 = 白增 drawcall 断点 | B2/B3 同批，用 drawcall 实测对照验收 |
| 池收缩策略 | A2 销毁富余实例可能与"界面关闭不销毁"的既有池化收益冲突（下次打开要重建） | 只收缩"列表内部实例池"，不动 UIForm 的常驻回收；收缩阈值可配 |
| 批量改控件源文件 | 历史事故：机械拆分脚本因同名覆盖三个源文件（《UI控件库Prefab落地规划》§8.1） | 一次一件、改完即跑 31/31 自检；批量前先算文件名冲突 |
| `await` 之后访问 Unity 对象 | 批⑦ 已抓出 `MissingReferenceException` 两例 | FlyText 改造后保留注销复检与销毁期守卫；任何 await 后复检 `this == null` |

---

## 6. 排期挂接

- **批①（A1–A7）**：立即可做，不阻塞联机线（M9/M10 在推进，UI 壳改动与其无交集）。建议一次提交一件，每件附自检。
- **批②（B1–B6）**：挂 **M11 前置**——M11 demo 消费 UI 壳/控件库/表现层，渲染面基础设施不到位则真机验收会返工。B2/B3 同批。
- **批③（C1–C2）**：与批② 同期定案（C1 是架构决策，越晚改越贵）。
- **批④（D1–D4）**：批①② 收口后补齐并记档；D4 进 CI 后成为长期护栏。

**与既有规划的接口**：`《UI控件库Prefab落地规划》` 批⑧（P1/P2 剩余缺口）落地时**必须遵守本规划 B3/B4/B5 三条构建器规则**，避免"先建、后改"的二遍工。
