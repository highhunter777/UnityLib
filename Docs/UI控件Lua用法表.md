# UI 控件 → Lua 用法表（内容作者向）

> 生成日期：2026-09-14（《UI控件库Prefab落地规划》批⑥）。**本表与代码逐条对齐**——方法名来自 `LuaBehaviourAdapter.UiApiShim/Dispatch`，控件字段来自 `WidgetPrefabBuilder`（模板构建器）。
> 改动任一侧（适配器方法 / 模板字段）时两处同改；发现不一致以代码为准并修本表。

---

## 0. 前置：Lua 界面逻辑怎么拿到 `self.ui`

| 环节 | 说明 |
| --- | --- |
| 逻辑表位置 | `Assets/LiteGame/Lua/UI/<界面名>.lua`（须在注册表/`LuaKeys` 有登记路径） |
| 七回调 | `OnInit(self, data)` / `OnShow(self, data)` / `OnUpdate(self, dt)` / `OnPause` / `OnCover` / `OnReveal` / `OnHide`（缺哪个跳哪个，不必全写） |
| `self.ui` 何时可用 | **`OnInit` 内由壳挂好**（`LuaBehaviourAdapter.OnInit` → 建绑定索引 + 挂 `self.ui`）——OnShow/OnUpdate 里直接可用 |
| 控件名从哪来 | **界面 prefab 上的 `BindNode.BindName`**（路径 A）。模板本身**不带 BindName**（避免多实例重名炸索引）——实例化后由界面作者命名 |
| 未命中/类型不符 | **当场抛**（`UIBindIndex.Get`：未命中 `KeyNotFoundException`，类型不符 `InvalidOperationException`）——写错名字会立刻看到，不会静默 |
| 所有权互斥 | 同一控件只能被"数据绑定"或"命令式"之一驱动；`Debug` 三宏下违例当场抛（`MarkDriver`）。**一个控件一旦 `SetText` 过，就不能再 `BindText`**，反之亦然 |
| 界面隐藏 | `OnHide` 时壳自动 `UnbindAll()` 解绑全部按钮监听（池化复用安全垫）——**Lua 不需要手动注销按钮**（但自己 `events.on` 的订阅仍要按事件桥规则注销） |

### 0.1 全局表（`LuaComponent.BindBridge` 绑定，与代码 1:1）

| 全局 | 方法 | 语义 |
| --- | --- | --- |
| `log` | `info/warning/error(msg)` | 日志收口进 `LiteFramework.Log`（tag `Lua`） |
| `events` | `on(name, fn)` 等 | C# 事件 → Lua（逐个 pcall 隔离；`on` 返回注销委托） |
| `Bridge.data` | `GetItem(id)` / `GetUIForm(id)` | 查表 → LuaTable（**按行缓存**，同 id 同实例） |
| `Bridge.ui` | `Show(id, data)` / `Close(id)` / `IsOpen(id)` / `GetLogic(id)` | 界面开/关/查（**以 tbuiform 行 id 驱动**；`Show` 是 fire-and-forget，错误进日志） |
| `Bridge.content` | `GetProcessor(id)` | 内容条目 → 处理器表（解释器消费） |

> **开界面写 `Bridge.ui:Show(id, data)`，不是 `self.ui`**——`self.ui` 只管当前界面的控件（见 §1）；`data` 传 Lua 表，界面侧在 `OnShow(self, data)` 收到同一张表。

---

## 1. 现有 `self.ui` 全集（**16 个方法**，与代码 1:1；批⑦ 已补 G1/G3/G7/G10，批⑧ 已补 G20 动效口）

| 方法 | 签名 | 语义 | 备注 |
| --- | --- | --- | --- |
| `OnButton` | `ui:OnButton(name, fn)` | 给名为 `name` 的控件绑点击（**替换式**：重绑先移除旧监听） | 目标必须带 `Button`；回调异常由 `SafeCall` 隔离 |
| `OffButton` | `ui:OffButton(name)` | 移除该控件全部点击监听 | |
| `SetText` | `ui:SetText(name, text)` | 写文本（**TMP 优先，回退 UGUI Text**） | 目标无 `Text/TMP_Text` → 抛 |
| `SetVisible` | `ui:SetVisible(name, visible)` | `SetActive` 显隐 | 名字指向任意节点即可 |
| `SetInteractable` | `ui:SetInteractable(name, on)` | 可交互开关 | **G1 已修**：`Selectable` 优先 → 回退 `UIWidget.Interactable`（`StateButton` 现可用） |
| `SetProgress` | `ui:SetProgress(name, value01)` | 进度条归一化值（0~1） | 目标 `ProgressBar` |
| `SetProgressRange` | `ui:SetProgressRange(name, cur, max)` | 进度条当前/上限（带数值文本） | 目标 `ProgressBar` |
| `SetHp` | `ui:SetHp(name, cur, max)` | 血条（前条瞬时 + 后条延迟滑落） | 目标 `HpBar` |
| `StartCountdown` | `ui:StartCountdown(name, seconds)` | 启动倒计时（mm:ss 自渲染） | 目标 `Countdown` |
| `StopCountdown` | `ui:StopCountdown(name)` | 停止倒计时（**不触发 OnDone**） | 目标 `Countdown` |
| `ShowToast` | `ui:ShowToast(text)` | 轻提示（走场景内 `Toast` 单例） | **无 ToastHost 时记日志不抛**（提示非关键路径） |
| `ShowBubble` | `ui:ShowBubble(name, text, duration)` | 气泡（`duration` 省略默认 1.5s） | 目标 `UIBubble`；重复调用取消上一次 |
| `ShowFlyText` | `ui:ShowFlyText(name, text)` | 飘字（位置取控件自身 anchoredPosition） | 目标 `FlyTextPool` |
| `Pulse` | `ui:Pulse(name, strength, duration)` | 脉冲：透明度呼吸两次 | **G20**（`strength` 默认 1.2 / `duration` 0.16）；目标须能解析出 `Graphic` |
| `Flash` | `ui:Flash(name, duration)` | 闪烁：一次性高亮回落 | **G20**（`duration` 默认 0.3） |
| `Slide` | `ui:Slide(name, ox, oy, duration)` | 位移入场：从相对偏移滑回原位 | **G20**（默认 0.25s）；目标是节点自身 `transform` |

> 上面各条走的是 **whitelist 派发通道**（`Action<string, LuaTable>`，payload 表协议）——新增方法不必新增 xLua 生成，同款手法扩展即可。
>
> **动效口（G20）解析规则（实现细节，写 Lua 时只需知道结论）**：索引里存的是 `BindNode` 自动检测到的组件——
> 带 `Button` 的节点存的是 `Button`，故 `Pulse/Flash` 解析 `Graphic` 时三级回退（自身 `Graphic` → `Button.targetGraphic` → 子级 `Graphic`）；
> `Slide` 取 `transform as RectTransform`（**不用** `Get<RectTransform>`——`BindNode` 不产 RectTransform）。
> 未命中 / 拿不到 `Graphic` = **抛**（与其余 13 个方法同语义，fail-fast）。

---

## 2. 控件 × 用法对照（25 件模板）

图例：✅ 现有 API 可直接驱动　🟡 部分可用（需配合命名/组合）　❌ 需新增受控方法（缺口编号见 §3）

| 模板 | 现有可用 | 缺口 / 需要的受控方法 |
| --- | --- | --- |
| **StateButton** | ✅ `OnButton`（点击）<br>✅ **`SetInteractable`（G1 已修）**<br>🟡 `SetText`（指向其 `Label` 子节点） | — |
| **Dialog**(UIDialog) | ✅ `OnButton`（`OkButton`/`CancelButton` 是 Button）<br>🟡 `SetText`（`Title`/`Message`）<br>✅ 开关由 `Bridge.ui:Show(id, data)` / `Bridge.ui:Close(id)` 驱动（id 来自 `tbuiform` 行） | — |
| **Toast** | ✅ **`ShowToast(text)`**（G3 已实现） | — |
| **Bubble** | ✅ **`ShowBubble(name, text, duration)`**（G3 已实现） | — |
| **FlyText** | ✅ **`ShowFlyText(name, text)`**（G3 已实现） | — |
| **RedDot** | ❌ | **❌ G4** `BindRedDot(name, key)` / 计数由 C# 侧 `RedDotTree` 推（Lua 一般不直接改计数） |
| **TabGroup** | ✅ `OnButton`（各页签按钮） | ❌ G5 `SelectTab(name, index)`（程序化选中） |
| **BottomNav** | ✅ `OnButton`（各入口按钮） | ❌ G5 同上 |
| **VirtualList** | ❌ | **❌ G6** 列表数据源（Lua 回调协议或 C# 侧数据源）——设计上"数据源经适配器"，见 §4 |
| **SimpleList** | ❌ | ❌ G6 同 VirtualList（非虚拟化版） |
| **ProgressBar** | ✅ **`SetProgress` / `SetProgressRange`**（G7 已实现）<br>🟡 `SetVisible` | — |
| **HpBar** | ✅ **`SetHp`**（G7 已实现）<br>🟡 `SetVisible` | — |
| **StarRating** | ❌ | **❌ G8** `SetStars(name, n)` |
| **CountText** | 🟡 `SetText`（静态文本） | **❌ G9** `RollCount(name, to)`（滚动动画） |
| **Countdown** | ✅ **`StartCountdown` / `StopCountdown`**（G10 已实现） | — |
| **AnimatedImage** | ❌ | ❌ G11 `PlayAnim(name)` / `StopAnim(name)` |
| **AvatarFrame** | 🟡 `SetText`（`LevelBadge`） | **❌ G12** `SetAvatar(name, address)`（贴图经资源地址加载） |
| **Stepper** | 🟡 `SetText`（`Value`） | **❌ G13** `SetStepper(name, value)`；步进变化回调用事件桥或 `OnStepperChanged(name, fn)` |
| **InputField** | ✅ `SetInteractable`<br>🟡 `SetText`（写文本） | **❌ G14** `GetInput(name)` 取值 + 提交回调 `OnInputSubmit(name, fn)` |
| **Slider** | ✅ `SetInteractable` | **❌ G15** `SetSlider(name, value01)` + `OnSliderChanged(name, fn)` |
| **Toggle** | ✅ `SetInteractable` | **❌ G16** `SetToggle(name, on)` + `OnToggleChanged(name, fn)` |
| **Dropdown** | ✅ `SetInteractable` | **❌ G17** `SetDropdown(name, index)` + 选项数据源 + `OnDropdownChanged(name, fn)` |
| **EventRelay** | ❌ | **❌ G18** `OnClickArea(name, fn)`（透明点击区；等价于给它绑 `UIEventRelay.OnClicked`） |
| **GuideHighlight** | ✅ `SetVisible` | **❌ G19** `GuideTo(name, targetName)`（对齐目标控件） |
| **SafeArea** | — | 无需 Lua 驱动（壳自动应用） |

**现状小结**：25 件里**只有按钮类（6 件）能被现有 API 直接驱动**；展示类/输入类/容器类共 **19 件需要新增受控方法**（G3-G19）——这就是批⑥ 暴露出的真实工作量，不是文档问题。

---

## 3. 缺口清单与建议形态（按优先级）

**实现方式统一约定**：全部走**同一个 whitelist 派发通道**（`Action<string, LuaTable>` + shim 表），**不暴露 `GetControl`**——`HIDE_REFLECTION` 下裸调 C# 类型不可行，且违反"受控面收窄"（设计方案 §4.3）。每条新方法必须：
1. 经 `UIBindIndex` 收口（`MarkDriver(name, Command)` 所有权登记）
2. 失败语义与现有一致（未命中/类型不符 = 抛）
3. 交互类回调经 `SafeCall` 隔离（同 `OnButton`）

| 编号 | 方法（建议） | 目标控件 | 优先级 | 状态 |
| --- | --- | --- | --- | --- |
| **G1** | `SetInteractable` 扩展：Selectable 优先 → 回退 `UIWidget.Interactable` | StateButton/RedDot 等 | P0 | ✅ **已实现（2026-09-14 批⑦）** |
| **G7** | `SetProgress` / `SetProgressRange` / `SetHp` | ProgressBar / HpBar | P0 | ✅ **已实现** |
| **G3** | `ShowToast` / `ShowBubble` / `ShowFlyText` | Toast / Bubble / FlyText | P0 | ✅ **已实现** |
| **G10** | `StartCountdown` / `StopCountdown` | Countdown | P0 | ✅ **已实现** |
| **G9** | `RollCount` + Lua 侧绑定区（G21） | CountText | P1 | ⏳ |
| **G8** | `SetStars` | StarRating | P1 | ⏳ |
| **G13** | `SetStepper` + 变化回调 | Stepper | P1 | ⏳ |
| **G15/G16/G17** | `SetSlider` / `SetToggle` / `SetDropdown` + 变化回调 | 输入三件 | P1 | ⏳ |
| **G14** | `GetInput` + `OnInputSubmit` | InputField | P1 | ⏳ |
| **G4** | `BindRedDot(name, key)` | RedDot | P1 | ⏳ |
| **G6** | 列表数据源协议 | VirtualList / SimpleList | **P1（需协议设计）** | ⏳（方案见 §4） |
| **G5** | `SelectTab` | TabGroup / BottomNav | P2 | ⏳ |
| **G11/G12** | `PlayAnim` / `SetAvatar` | AnimatedImage / AvatarFrame | P2 | ⏳ |
| **G18** | `OnClickArea` | EventRelay | P2 | ⏳ |
| **G19** | `GuideTo` | GuideHighlight | P2 | ⏳ |
| **G20** | **动效口** `Pulse` / `Flash` / `Slide` | 任意 Graphic/RectTransform | P1 | ✅ **已实现（批⑧，2026-09-19）**（《动效设计方案》§A.3 定集） |

---

## 4. 列表数据源协议（G6 待设计，单独标出）

`VirtualList.SetSource(IVirtualListSource)` 需要 C# 侧对象；Lua 侧的正确形态有两条候选：

| 方案 | 形态 | 取舍 |
| --- | --- | --- |
| **A. 行数据驱动**（推荐） | `ui:SetList(name, rows)` —— Lua 传一个行数组（LuaTable 数组），适配器内部实现 `IVirtualListSource`，`Bind(index,item)` 时按行数据填字段（字段名→子控件映射走命名约定） | 无跨语言每帧回调；适合"数据整体刷新"（房间列表/排行榜） |
| **B. 回调驱动** | `ui:OnListBind(name, fn)` —— 每个 item 绑定回调一次 Lua | 灵活但跨语言回调频繁；需要 SafeCall + 生命周期注销纪律 |

**首版建议 A**（与 §4.3 热路径纪律一致：跨语言调用要少、要粗粒度）。

---

## 5. 样例：一个界面逻辑表（现有 API 能写的部分 + 缺口处标注）

```lua
-- Assets/LiteGame/Lua/UI/UIRoom.lua（示例：现有 16 个方法可覆盖 HUD/提示/计时/动效）
local M = {}

function M:OnInit(data)
    self.ui:SetText("TitleText", "房间列表")
    self.ui:OnButton("BtnCreate", function()
        log.info("创建房间")
        -- ⏳ 缺口 G14：需要 ui:GetInput("RoomCodeInput") 取输入
    end)
    self.ui:OnButton("BtnReady", function()
        self.ui:SetText("BtnReadyLabel", "已准备")     -- 🟡 按钮文案（指向 Label 子节点）
        self.ui:SetInteractable("BtnReady", false)     -- ✅ 按钮 / StateButton 均可
    end)
    self.ui:OnButton("Tab0", function()
        -- ⏳ 缺口 G5：需要 ui:SelectTab("MainTabs", 0)
    end)
end

function M:OnShow(data)
    self.ui:SetHp("PlayerHp", data.hp, data.maxHp)          -- ✅ G7
    self.ui:StartCountdown("RoundCountdown", 180)           -- ✅ G10
    self.ui:SetVisible("RedDot", data.hasNewMail)           -- ✅
    -- ⏳ 缺口 G6：需要 ui:SetList("RoomList", data.rooms)
end

function M:OnUpdate(dt)
    -- 命中反馈（事件桥回调里同样可用）
    -- self.ui:ShowFlyText("DamageFlyText", "-35")
    -- self.ui:ShowToast("回合开始")
end

function M:OnHide()
    -- 按钮监听由壳自动解绑；此处只需清理自己订阅的事件（见事件桥规则）
end

return M
```

> 受控方法在**任意时点**可用（OnInit/OnShow/OnUpdate/事件回调）——它们都是同一条派发通道，无生命周期限制；错名字会当场抛（便于发现拼写错误）。

---

## 6. 与既有设计的差异记录（如实）

1. ~~**《动效设计方案》§A.3** 定的受控 API 动效口（`Pulse`/`Flash`/`Slide`）**仍未实现** → G20（P1）~~
   → **已实现（批⑧，2026-09-19）**：`UIBindIndex` 加 `Pulse/Flash/Slide` + `ResolveGraphic/ResolveRect` 两个解析器；
   `LuaBehaviourAdapter` 的 shim/Dispatch 各加三条；模板自检 31→36（新增 G20a–e，只验解析层与错误语义，DOTween 行为留 Play）。
2. **《自研框架设计方案》§4.7** 的"Lua 侧绑定区（`Bind` 声明）"**仍未实现**（`UIBindIndex.BindText` 存在但未挂到 `self.ui`）→ G21（P1，与 G9 同批）
3. **批⑦ P0 已完成（2026-09-14）**：G1 修正 + G3/G7/G10 八条新方法；验证方式 = ①模板自检 31/31 PASS（含 6 条批⑦断言）②**Lua shim 实测**（Lua 原生值 → 5/5 正确）③**适配器 `Dispatch` 真实路径实测**（`setHp`/`setProgress`/`startCountdown`/`setInteractable` 4/4 ✓，验证 Lua number→float 与 bool 转换）
4. **批⑦ 顺带修掉一个真实缺陷**：`UIBubble` 在等待期间宿主被销毁会抛 `MissingReferenceException`（`UniTask.Delay` 默认非即时观测取消 + 销毁后仍 `SetActive`）→ 已改为 `cancelImmediately: true` + `this == null` 守卫
5. 剩余缺口 = **批⑧（P1/P2）**：G8/G9/G13/G14/G15/G16/G17/G4/G6/G5/G11/G12/G18/G19/G21
   （**G20 已于 2026-09-19 落地**，见差异记录 1）

---

## 7. 一致性维护

| 事项 | 落地方式 |
| --- | --- |
| 表 ↔ 代码 | 方法名以 `LuaBehaviourAdapter.UiApiShim`（Lua 侧）与 `Dispatch`（C# 侧）为准；控件字段以 `WidgetPrefabBuilder` 为准 |
| 新增受控方法 | ①`UiApiShim` 加一行 ②`Dispatch` 加 case ③`UIBindIndex` 加收口方法（含 `MarkDriver`）④本表 §1/§2 同步 |
| 抽查方式 | `WidgetPrefabCheck` 同款菜单化断言：对模板实例化后调受控方法，断言控件状态变化（建议批⑦ 一并加） |