# UI 控件库 Prefab 模板落地规划

> 定案 2026-09-14（用户）：**UI 控件 prefab 模板要落地**。本规划补齐 M4c 遗留——控件库有 C# 实现，**prefab 资产为 0**（现状核对：23 个 C# 件、0 个 .prefab、Demo 页为代码构建）。
> 关系：清单沿用《M4实施指导》§2.10（20 余件）；绑定约定沿用《自研框架设计方案》§4.6；本规划是《编辑器设计规划》UI 编辑器两阶段的素材与验收对象。

---

## 1. 现状核对（2026-09-14）

| 项 | 状态 |
| --- | --- |
| 控件 C# 实现（`Runtime/Shell/UI/Widgets/`） | ✅ 23 件（UIWidget 基座 / VirtualList / StateButton / TabGroup / Dialog / Toast / Progress / HpBar / StarRating / Stepper / RedDot / SafeAreaReceiver / FlyText / UIDemoPage …） |
| Play 自检 | ✅ 5/5 PASS（UIDataBinder 去重 / StarRating 钳制 / Stepper 钳制 / RedDot 树传播 / Countdown 回零） |
| **prefab 模板** | ❌ **0 个**——全部代码构建 |
| Demo 页 | ⚠️ 代码构建（`UIDemoPage.cs` + `WidgetDemoMenu.cs`），非 prefab 实例 |

§2.10 验收原文要求"**控件自带 prefab 模板** + 每件最小 Play 断言 + 一页 Demo 场景全展（灰盒）"——前两项是本规划的交付缺口。

## 2. 目标形态

**目录约定**：
```
Assets/LiteGame/UI/
├── Widgets/                    # 每控件一个模板 prefab
│   ├── StateButton.prefab
│   ├── Dialog.prefab
│   ├── VirtualList.prefab
│   └── ...（对齐 §2.10 清单）
├── Atlas/                      # 灰盒图集 + 九宫格切片（占位图）
└── Demo/
    └── WidgetsDemo.prefab      # Demo 页（全展各控件，灰盒）
```

**每模板的结构与标记**（绑定双路径 A 的落点）：
- 根节点：挂 `UIWidget` 子类 + `BindRoot`（类名 / 命名空间 / 输出目录）
- 需要暴露的子控件：挂 `BindNode` 标记（名字 = 绑定名，PascalCase；类型 = 子控件组件类型）→ 索引与绑定类**一键生成**（BindCodeGen §4.6，零手工查找）

**层级与命名约定**：
- 结构：`Root(UIWidget) → Bg / Content / Interaction`；三层缺省，特殊控件可加层
- 节点名 = 绑定名（PascalCase，如 `IconImage` / `CountText`）；非暴露节点前缀 `_`（如 `_Decor`）
- 锚点：模板按"内容自适应 + 锚点居中"设计，实例化方设尺寸；`SafeAreaReceiver` 纳入适配层统一维护

**灰盒美术标准**：
- 统一色板（背景/主色/警告/禁用四色）、九宫格边界切片、单一占位字体
- **禁止业务文案与图标**：文案走表/Lua（`Bridge.ui` 受控 API），占位图仅形态示意

**变体策略**：同族用 prefab variant 派生（如 `Dialog` 的确认/警告/输入三 variant；`StateButton` 的多态视觉用子资源状态图，不做多 prefab）

## 3. 落地步骤（六批）

| 批 | 内容 | 验收 |
| --- | --- | --- |
| ① 约定定案 | 目录、命名、层级、灰盒色板与图集、标记规范写入本文档附录 | 约定评审通过（本文件补充） |
| ② 样板 4 件 | `StateButton` / `Dialog` / `VirtualList` / `HpBar`——覆盖交互/弹窗/列表/展示四类 | 四件模板 + 标记完整；索引生成成功；Play 断言通过 |
| ③ 余件补齐 | 对齐 §2.10 清单（TabGroup / BottomNav / Toast / Bubble / ProgressBar 环形直线 / StarRating / CountText / Countdown / AnimatedImage / AvatarFrame / InputField / Slider / Toggle / Dropdown / Stepper / RedDot / FlyText / 引导高亮位 / PageView / SafeAreaReceiver） | 清单对账无缺项 |
| ④ 断言迁移 | 现有 UIDemoPage 断言 → **prefab 实例化断言**（加载模板 → 驱动 → 断言），每件至少一条 | 断言全绿（Play） |
| ⑤ Demo 页改造 | `UIDemoPage.cs` 代码构建 → `WidgetsDemo.prefab` 实例 + 断言；场景/prefab 入库 | Demo 页全展为 prefab 实例，非代码构建 |
| ⑥ 用法表 | 控件 → Lua 受控 API 对照表（内容作者可见） | 表与代码一致（抽查） |

## 4. 与编辑器/绑定工具的关系

- **绑定工具（已有）**：模板的 `BindRoot`/`BindNode` 标记 → 索引/绑定类生成——本规划是它的第一批真实消费者
- **UI 编辑器阶段一（标记工具）**：为模板提供可视化编辑与批量校验（见《编辑器设计规划》§2.1）——模板落地过程即是标记工具的验收场景
- **UI 编辑器阶段二（编排器）**：以本模板库为拖拽素材（见《编辑器设计规划》§2.2）

## 5. 纪律

- 模板是**壳机制层的资产**：Lua 不碰内部实现，只经受控 API 驱动（§1.3 归属纪律）
- 模板**不引业务依赖**（不挂业务脚本、不含业务资源引用——地址由表/代码给）
- 模板**不含逻辑**：交互行为由控件 C# 类实现，模板只提供结构、样式与标记
- prefab 变更后**索引/绑定类必须重新生成**（标记工具批量校验纳入 CI 前置/自检清单）

## 6. 验收（本规划完成判定）

1. §2.10 清单控件**每件一个 prefab 模板**，标记完整、索引可一键生成
2. 每件至少一条 **Play 断言**（prefab 实例化路径）全绿
3. Demo 页全展（**prefab 实例**，不再代码构建）
4. 无业务文案/图标硬编码在模板内
5. 控件 → Lua 受控 API 对照表交付

## 7. 工作量与排期

- **工作量**：手工搭建 20 余件模板（主要成本，灰盒）+ 断言/Demo 改造约 300 行代码
- **排期**：随 M4 遗留清偿 + UI 编辑器阶段一（在 M5 纵向切片与 M11 demo 之间择机；联机 UI 清单（《联机Demo设计》§4）是本规划的消费者之一）
- **依赖**：不阻塞 M5-M10；但 M11 demo 的 UI 验收要求模板就绪
