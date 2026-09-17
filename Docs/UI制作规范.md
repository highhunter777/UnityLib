# UI 制作规范（布局 · 动画 · 粒子 · Shader）

> 2026-09-17。定位：**以后做 UI 时的参考基准**——布局怎么摆、动画怎么做、粒子上不上、Shader 怎么选，每条给判据与坑位，不写成空泛口号。
> 与既有文档的边界：《UI性能优化规划》（性能预算与批② 渲染面落地）、《动效设计方案》（动效分类与时钟分轨）、《动作与特效设计》（特效归属：VFX 服务 vs UI）、《UI控件库Prefab落地规划》（控件件与模板）、《UI扩展能力设计》（跳转/本地化/富文本/覆盖层）。
> **本文区分两级**：**硬规则**（进构建器/校验器，违规即红，标 🔴）与**建议**（团队约定，标 ⚪）。硬规则是"违反会出 bug/踩坑"，建议是"违反会不好维护"。

---

## 1. 渲染层级模型（所有布局/特效决策的地基）

### 1.1 现状（实测）

| 事实 | 出处 |
|---|---|
| **每个界面一个 `Canvas`，`renderMode = ScreenSpaceOverlay`** | `UIForm.cs:28-31` |
| 组间深度按 `BaseDepth` 步进 100（Bottom=100 / Window=200 / Top=300），组内递增槽位 | `UILayerGroup.cs:13` |
| `[UIRoot]` 自身**无 Canvas、无 CanvasScaler** | `UIService.cs:44-53` |
| 零图集、零 TMP Sprite Asset、零自研 UI Shader | 全仓扫描 |

### 1.2 🔴 Overlay 的硬限制：粒子/3D 永远在 UI 之下

`ScreenSpaceOverlay` 的 Canvas **在所有相机之后绘制** → **世界空间粒子、3D 模型、任何相机渲染物都不可能盖在 UI 之上**。这不是配置问题，是渲染顺序的物理事实。

**后果**：现在无法做"按钮后面冒光效""结算页角色展示""UI 内嵌粒子"。

**目标形态（M11 前落地，即《UI性能优化规划》批② B1/B3）**：

```
主相机（世界）
 └─ UI 相机（ScreenSpaceCamera Canvas，depth 更高，cullingMask 只留 UI 层）
      ├─ 根 Canvas（挂 CanvasScaler，1920×1080，MatchWidthOrHeight=0.5）
      │    ├─ 底/内容/顶 三层（嵌套 Canvas + sortingOrder = 组 BaseDepth，既有语义不变）
      │    └─ UI 特效层（sortingOrder 夹在内容与顶之间，见 §4）
      └─ UI 相机渲染的世界空间粒子（同一 camera 下按 sortingOrder/距离排序）
```

**层序契约（钉死，所有 UI 制作遵守）**：

| sortingOrder 段 | 用途 |
|---|---|
| 100–199 / 200–299 / 300–399 | Bottom / Window / Top 三层界面（既有分配器） |
| **150 / 250 / 350** | **各层内的"UI 特效夹层"**（夹在本层界面之间，见 §4.1 方案 a） |
| 1000+ | 全屏遮罩/加载层（Top 组内保留高位） |

⚪ 3D 模型嵌 UI（角色展示/武器预览）两条路：**RenderTexture**（相机渲到 RAW Image，最稳、可缩放、可被 UI 裁）优先；**专用相机 + 世界空间摆位**仅用于需要真实交互（拖拽旋转）的场景。

---

## 2. 布局规范

### 2.1 🔴 硬规则

| 规则 | 理由 |
|---|---|
| **不用 `LayoutGroup` / `ContentSizeFitter`** | 与池化/裁剪对冲、开销是响应式的（判据见《UI性能优化规划》§4）；模板与列表一律锚点 + 手动 `anchoredPosition` |
| **不用 `Outline` / `Shadow` 组件**（列表项内绝对禁止） | 每个额外效果 = 额外 mesh + 额外 drawcall；需要描边走 shader 或贴图 |
| **`raycastTarget` 默认关**，仅交互件开 | `GraphicRaycaster` 每次触摸遍历全 Canvas 的可命中 Graphic |
| **动态元素与静态元素分层**（动态进子 Canvas） | 一个元素变 → 整 Canvas 重建；分层把重建面积压到最小 |
| **界面 prefab 根 = 界面本体**（自带 Canvas + CanvasGroup，控件模板根 = 组件本体） | 既有约定（`UIForm.cs:28`、`WidgetPrefabBuilder`） |
| **文本不放文案**，只放 key 或留空 | 本地化硬纪律（《UI扩展能力设计》§2.3） |
| **`RectMask2D` 优先于 `Mask`** | `Mask` 需要额外 stencil 通道；`RectMask2D` 纯矩形裁剪更便宜 |

### 2.2 ⚪ 建议（尺寸与结构）

- **参考分辨率 1920×1080**；`CanvasScaler.ScaleWithScreenSize` + `MatchWidthOrHeight = 0.5`（竖屏项目另议，本项目横屏）。
- **字号阶梯**：12 / 14 / 16 / 20 / 24 / 32 / 40（正文 16 起，HUD 数值 20+）；不出现阶梯外字号。
- **间距阶梯**：8 的倍数（8/16/24/32/48/64）；安全边距 32。
- **图标尺寸**：24 / 32 / 48 / 64 / 96（正方形，按九宫格/图集切）。
- **目录归属**：`Assets/LiteGame/UI/Screens/`（界面，单界面单包 `PackSeparately`）、`Assets/LiteGame/UI/Widgets/`（控件模板，单目录包）、`Assets/LiteGame/UI/Patches/`（覆盖层补丁）。**不得再新建 UI 根目录**（收集组按路径划分，新增根 = 漏收）。
- **prefab 层级深度** ≤ 5 层（含根）；超过说明该拆控件。
- **`BindName` 不在模板里预设**（多实例重名会炸界面级索引，命名归界面作者）——既有定案。
- **SafeArea**：所有全屏界面根下挂 `SafeArea` 控件（已交付），不手写适配代码。

---

## 3. UI 动画规范

### 3.1 三轨分工（既有，不新造）

| 动画类型 | 载体 | 时钟 | 判定 |
|---|---|---|---|
| UI 动效/转场 | **DOTween**（`UiFx` / `ITransitionStrategy`） | `IUIClock`（`SetUpdate(true)`，时停不停） | 不携带 |
| 剧情/技能时序 | 序列执行器（M4b，`ITimelineRunner`） | `IWorldClock`（时停冻结） | 携带（逻辑层） |
| 非对局演出 | Unity Timeline（`PlayableDirector`） | 世界轨 | 纯表现 |

**联机技能表现不跑 `PlayableDirector`**（走序列执行器，避开与快照/和解重对齐的摩擦）——既有定案。

### 3.2 🔴 硬规则

| 规则 | 理由 |
|---|---|
| **淡入淡出只改 `CanvasGroup.alpha`，不改 `TMP_Text.color`** | 改顶点色 = 标脏 mesh → Canvas 重建（飘字曾因此每帧重建 16 次） |
| **位移/缩放只改 `transform`/`anchoredPosition`** | 不触发 mesh 重建（纯 transform） |
| **动效元素挂独立子 Canvas** | 防整页 rebatch（动效方案 §4 原定，未落地 → 进校验器） |
| **`SetLink(KillOnDisable)` + 界面关闭必杀** | 界面关了 tween 还在跑 = 泄漏（动效方案定为红线） |
| **禁用原生协程**，异步一律 UniTask | 项目红线 |
| **转场期间禁交互由壳统一管** | 不依赖策略自觉（《UI扩展能力设计》§1.5.4） |

### 3.3 ⚪ 时长与缓动

- **时长阶梯**：快 0.15s（反馈类：按下/开关）、标准 0.25s（转场/弹窗）、慢 0.35s（全屏切换）。
- **缓动**：入场 `OutQuad`/`OutBack`（弹窗用 `OutBack` 有回弹），离场 `InQuad`；**禁止 `Linear`**（除滚动/流光）。
- **列表项入场**：错峰间隔 0.02–0.04s，最多错峰前 8 项（后面的一起出，避免长列表排队到天荒地老）。
- **动效不阻塞逻辑**：动画播完与否不影响状态迁移（既有纪律：`PlayTransition*` 容错吞异常）。

---

## 4. UI 粒子特效规范

### 4.0 🔴 第一原则：**能不用就不用**（默认否定，2026-09-17 用户定案）

UI 里出现粒子**必须有理由**——它默认是"例外手段"，不是"效果更好所以用"。判断顺序固定：

1. **先问：能用帧动画/顶点色/UV 动画表达吗？** 能 → **一律不用粒子**（零额外渲染物，性能差一个数量级）。
2. 再问：**必须跟随 UI 元素吗？必须被 UI 裁剪（`Mask`/`RectMask2D`）吗？** 是 → 才考虑方案 b（内嵌粒子）。
3. 最后问：**是全屏/需要真实 3D 观感，且 UI 相机已就位吗？** 是 → 才考虑方案 a（独立特效层）。

**经验判据**：一个特效如果"8~16 帧图集序列能表达 80% 的观感" → **直接走帧动画**，不进粒子评审。
**评审门槛**：任何 UI prefab 里出现 `ParticleSystem` 前，要能回答上述三个问题（答不上来 = 用帧动画）。

### 4.1 三种做法与判据（本框架下必须选对）

| 方案 | 做法 | 适用 | 代价 |
|---|---|---|---|
| **c. UI 帧动画替代**（**默认**） | 图集序列帧（`AnimatedImage` 已交付）/ 简单缩放淡出（`UiFx`）/ UV 动画 shader | 小范围、低烈度（点击涟漪、图标呼吸、升级闪光） | 观感上限低 |
| **b. Canvas 内嵌 ParticleSystem** | 粒子作为 UI 节点的子物体（自研 `UIParticle` 式：手动把粒子网格塞进 Canvas 渲染） | **必须跟随 UI 元素**、**需被 `Mask` 裁剪**（两者之一即满足，且帧动画表达不了） | 断批、`RectMask2D` **不裁**粒子、粒子材质必须 UI 系；实现成本高 |
| **a. 独立特效相机 + 夹层 sortingOrder** | UI 相机下挂世界空间 ParticleSystem，sortingOrder 落在 §1.2 的夹层段 | 全屏/大范围（升级光效、结算烟花）、需要真实 3D 观感，**且 UI 相机（批② B1/B3）已就位** | 分层排序要设计；无 UI 相机时方案 a 根本不成立 |

**判据一句话**：**默认 c；"跟着 UI 元素跑或被 UI 裁"才给 b；"全屏 + 3D 观感"才给 a。**

### 4.2 🔴 边界（三权分立，别混）

| 归属 | 管什么 | 不管什么 |
|---|---|---|
| **VFX 服务**（M11，《动作与特效设计》） | 世界空间特效：挂点跟随实体、预算/降级、`silenceUntilFrame` 防重播 | **不碰 UI**（不并进 UI 动效） |
| **UiFx / UI 动效** | UI 位移/缩放/淡入出/流光（DOTween） | **不接粒子**（`UiFx` 不是特效播放器） |
| **UI 特效层**（本文 §4.1 a/b） | UI 内嵌/夹层粒子 | 不进 Sim、不参与判定 |

🔴 **UI 特效不携带任何玩法判定**（命中反馈的"判定"在 Sim，UI 只播表现）。

### 4.3 ⚪ 性能

- 粒子材质用 UI 系（`UI/Default`-like）或自带 Unlit；**关 `ZWrite`**，`ZTest` 按层序决定。
- 同屏粒子系统 ≤ 3 个（M11 HUD 预算），overdraw 计入《UI性能优化规划》§2 的"≤2 层"。
- 低端机降级：**换"帧动画替代"（c）而不是减时长**（表现语义不变，成本降一个数量级）。

---

## 5. UI Shader 规范

### 5.1 🔴 硬规则

| 规则 | 理由 |
|---|---|
| **UI 件 shader 一律继承 `UI/Default`**（含 `_Stencil`/ClipRect 变体） | 否则 `Mask`/`RectMask2D`/`CanvasGroup` 裁剪全部失效（最常见坑） |
| **TMP 必须用 TMP SDF shader 变体**，换 shader 时保留 SDF 关键字 | 换错 = 字变糊/描边丢失 |
| **`<sprite>` 富文本需要 TMP Sprite Asset** | **当前工程没有**（实测为 0）→ 要用先建资产，别以为 TMP 自带 |
| **禁 `MaterialPropertyBlock` 式按实例改材质属性** | UGUI 合批条件 = 同 shader + 同贴图 + **同材质实例**；改材质 = 断批 |
| 按实例变化的效果走**顶点色 / UV 通道传参**，或预生成少量共享材质变体 | 上述的替代方案 |

### 5.2 ⚪ 常见效果的落地路线

| 效果 | 推荐做法 | 不要做 |
|---|---|---|
| 灰度/禁用态 | 顶点色（TMP/Image 的 color）+ 灰化贴图变体 | 每实例换材质 |
| 描边/阴影 | 预烘焙进图集（美术出带描边的图）；必须动态的走 shader | `Outline`/`Shadow` 组件（额外 mesh + drawcall） |
| 流光/扫光 | 单共享材质 + UV 动画 shader（`_Time` 驱动，**零每帧 CPU**） | 用 DOTween 每帧改 UV（吃 CPU） |
| 模糊/毛玻璃 | URP Renderer Feature 抓背景（**禁用于列表/多层叠加**） | 每元素一个 blur material |
| 进度/遮罩 | `Image.Type = Filled`（Radial/Radial360）或 `RectMask2D` + 位移 | 每帧重建 mesh |
| 颜色叠加/品质色 | 顶点色（TMP `<color>` 富文本）+ 图集 | 多材质变体 |

### 5.3 ⚪ 变体与包体

- 自定义 UI shader 走 `ShaderVariantCollection` + URP 剥离设置（M6 打包前置）。
- 单 shader 关键字组合 ≤ 8；超过就拆 shader 而不是堆关键字。
- 自研 UI shader 必须先过 §1.2 的层序契约（能否与 `Mask` 共存、是否支持 `CanvasGroup` alpha）。

---

## 6. 新做 UI 的检查清单（逐条勾）

**层与布局**
- [ ] 🔴 根带 `Canvas` + `CanvasGroup`；界面放在 `Screens/`（不得新开 UI 根目录）
- [ ] 🔴 无 `LayoutGroup` / `ContentSizeFitter` / `Outline` / `Shadow`
- [ ] 🔴 非交互 `Graphic` 的 `raycastTarget = false`
- [ ] 🔴 文本只放 key（本地化纪律）
- [ ] ⚪ 字号/间距/图标尺寸落在阶梯上；层级 ≤ 5；全屏界面挂 `SafeArea`

**动效与特效**
- [ ] 🔴 高频变化元素挂子 Canvas；淡出用 `CanvasGroup.alpha`
- [ ] 🔴 关界面必杀 tween（`KillOnDisable`）；禁协程；动效不携带判定
- [ ] 🔴 **粒子默认不用**（§4.0）：先证明帧动画表达不了，再按 a/b/c 判据选；出现 `ParticleSystem` 需能回答三问
- [ ] 🔴 特效不携带玩法判定（判定在 Sim）

**Shader 与资产**
- [ ] 🔴 自定义 shader 继承 `UI/Default`；TMP 用 SDF 变体
- [ ] 🔴 不按实例改材质；按实例变化走顶点色/UV
- [ ] ⚪ `<sprite>` 前先确认 TMP Sprite Asset 已建

---

## 7. 现状缺口与落地批次（规范 ↔ 工程的对账）

规范里写的"目标形态"有些**工程还不是这样**，落地批次对应关系：

| 规范条目 | 现状 | 落地在哪 |
|---|---|---|
| §1.2 UI 相机 + ScreenSpaceCamera + CanvasScaler | ❌ Overlay、无 Scaler | 《UI性能优化规划》批② **B1/B3** |
| §2.1 图集（同纹理合批） | ❌ 零图集 | 批② **B2**（与 B3 同批） |
| §3.2 动效元素子 Canvas、§2.1 raycastTarget 默认关 | ❌ 未成规范 | 批② **B4/B5**（进构建器 + 校验器） |
| §5.1 TMP Sprite Asset | ❌ 无 | 用到再建（富文本 `<sprite>` 的前置） |
| §4 UI 特效层（夹层排序） | ❌ 无（Overlay 挡死） | 依赖 §1.2 落地（批② B1/B3）之后 |
| 本文的 🔴 硬规则进校验器 | ❌ 未做 | 并入批② **D4**（校验规则进 CI）+ 新增本文几条 |

**顺序**：批② 落地 §1.2/§2.1/§3.2 → 本文 🔴 规则进校验器（D4）→ §4 UI 特效层可用 → M11 HUD 按本文施工。
