# 玩法数值解耦审查与 Luban 表设计（2026-09-19）

> 触发：用户审查指出"伤害直接写代码里，没解耦"。追溯确认：《M8实施指导》§2.1 原文登记"SimConfig 增 M8 玩法占位常量——**测试版数值，待 Luban 数值表（tb_skill_num 数据链路）经参数面接入后替换**"——欠账至今，本文补齐：①全量解耦审查 ②玩法数值需求清单 ③Luban 表设计 ④接入方案。

---

## 1. 解耦审查：数值分五类，归属各不同

对 Sim/LiteNet/RoomServer/LiteGame 的**全部数值字面量**盘点后，按"谁消费、何时变"分五类——**只有 C 类需要进数值表**：

| 类 | 内容 | 归属 | 依据 |
| --- | --- | --- | --- |
| **A. 协议/架构常量** | TickRate=60、InputDelay=1、MaxCatchUp=5、MaxRollbackFrames=8、InterpFrames=4、LagCompHistory=16、MaxInputHistory=32、MaxRollbacksPerFrame=2、CustomBytesPerEntity=32、GlobalsBytes=256、SnapshotHz=30 | `SimConfig` const **保持** | 协议与架构约束——改动=改协议，编译期锁死是特性（buildHash 覆盖 ✓） |
| **B. 调试/展示常量** | SimSandbox 的 Gizmos 存留 0.4s、TargetCount=5 等 | 就地 const（harness 件，M11 删） | §2.7 一次性调试件 |
| **C. 玩法数值（9 项，本文主角）** | 见 §2 清单 | **迁 Luban 表**（本次设计） | 调数值 ≠ 改代码重编译重部署 |
| **D. 地图数据** | GroundY=0、HalfWidth/Depth=50、16 出生点网格 | `SimMapData` 构造注入 **保持**（已是数据形态） | 归地图定义（M11 正式地图装配时若需编辑器化再议） |
| **E. 实体出生属性** | Hp=100（Spawn 硬编码 ×2 处：Room.Start/SimSandbox） | **实体定义**——M11 若加职业/兵种则建 `tb_entity`；MVP 先并入 C 类表（`entity_hp` 字段） | 单一职责：出生属性归"实体是什么"，不归"战斗怎么算" |

**UI 色板（12 token）已走 `UiStyle`（仅编辑器语义）✓ 不属玩法数值。**

---

## 2. 玩法数值需求全清单（C 类 9+2 项）

| # | 数值 | 现值 | 消费点 | 语义 |
| --- | --- | --- | --- | --- |
| 1 | `move_speed` | 5f | `InputSystem`（Vel = MoveX × speed）| 玩家移动速度 m/s |
| 2 | `gravity` | -20f | `MovementSystem`（y 轴积分）| 重力加速度 m/s² |
| 3 | `hitscan_range` | 100f | `ShootingSystem`（射线长度上限）| 射程 m |
| 4 | `hitscan_radius` | 0.5f | `ShootingSystem`（圆柱求交）| 命中圆柱半径 m |
| 5 | `hitscan_height` | 2f | `ShootingSystem`（y 区间）+ 回溯态判定 | 命中圆柱高度 m |
| 6 | `base_damage` | 25 | `ShootingSystem`（伤害浮动 ±1 的基值）| 基础伤害 |
| 7 | `damage_spread` | 1（代码里 `+ rng.NextRange(0,3) - 1`）| `ShootingSystem` | 伤害浮动幅度 ±spread（**现值内嵌表达式，需提取**）|
| 8 | `entity_hp` | 100（`Room.Start`/`SimSandbox`/`BaselineSpec.BuildWorld` 三处 Spawn 硬编码）| 出生属性 | 实体初始 HP |
| 9 | `fire_window`（隐含）| `(NextUInt32() & 3) == 0`（1/4 概率）| 测试脚本开火概率 | **测试参数不进表**（归 harness 脚本）|

**新增发现（第 7 项）**：伤害浮动 `NextRange(0, 3) - 1` 的"±1"散在 `ShootingSystem` 表达式里——Luban 化时拆成 `base_damage` + `damage_spread` 两个字段，表达式收敛为 `base + rng.NextRange(-spread, spread+1)`。

**边界澄清**：`TickRate/Dt/InputDelay/MaxCatchUp` 等虽在 SimConfig，但属 A 类协议常量——**数值表管"手感参数"，协议常量管"确定性架构"**，不混。

---

## 3. Luban 表设计（对齐既有链路形态：xlsx 单源 + gen.bat 双 pass + cs-bin/lua 双产物）

### 3.1 表结构（单行全局表，域分组注释）

**定案：一张 `tb_combat_num` 单行表**（而非按域拆多张）——9 项同属"一局战斗的手感参数"，生命周期一致（同表同改同 buildHash），拆表只增加加载与维护面。字段分组即单一职责的体现：

| 字段 | 类型 | 默认 | 组 | 说明 |
| --- | --- | --- | --- | --- |
| `move_speed` | float | 5 | 移动 | m/s |
| `gravity` | float | -20 | 移动 | m/s²（负值向下）|
| `hitscan_range` | float | 100 | 射击 | m |
| `hitscan_radius` | float | 0.5 | 射击 | m |
| `hitscan_height` | float | 2 | 射击 | m |
| `base_damage` | int | 25 | 伤害 | 命中基础值 |
| `damage_spread` | int | 1 | 伤害 | ± 浮动幅度（0=无浮动）|
| `entity_hp` | int | 100 | 出生 | 实体初始 HP |

**携带方式**：Luban `bean`（`CombatNum`）+ 单行表 `tb_combat_num`；xlsx 新建 `#combatnum.xlsx`（数据一行）+ `__beans__.xlsx` 增 bean + `__tables__.xlsx` 增表定义——**全部对齐既有三件套约定**（gen.bat 的 pass1/pass2 自动拾取，无需改脚本）。

### 3.2 接入形态：`SimConfig` 玩法段 const → static readonly（运行期从 Tables 读）

```csharp
// 玩法数值（Luban tb_combat_num 单行表；Tables 加载后由 ConfigBoot 回填——改表重跑 gen.bat 即生效，零重编译）
public static float MoveSpeed { get; private set; } = 5f;      // 表缺失时的编译期兜底（与表默认值一致）
public static int BaseDamage { get; private set; } = 25;
...（6 项同款）
/// <summary>启动期回填（Tables 加载后调用一次；缺表/加载失败 = 兜底值 + Ops 告警）。</summary>
public static void LoadFrom(LiteSimConfigRow row) { ... }
```

- **消费点零改动**：`InputSystem`/`MovementSystem`/`ShootingSystem` 引用 `SimConfig.MoveSpeed` 等名字不变（const → static readonly 属性，编译器透明）；
- **确定性**：两端读同一份 bin 数据 → 位级一致（Aim 改造已验证的 Luban 双端链路复用）；
- **零分配纪律**：静态回填一次，帧内无查找。

### 3.3 buildHash 闭包（数据一致性红线）

`scripts/gen-build-hash.py` 的 Targets 增 `Assets/LiteGame/RawFile/Config`（战斗数值 bin 的落盘位置）——**表数据变更 → buildHash 变 → 旧客户端拒绝进房**（数值两端不一致 = 行为分叉 = 和解风暴，必须挡在门外）。

### 3.4 E1 红线顺带核验

`ShootingSystem` 的 clamp（移动向量长度 ≤1）已在 Sim 内 ✓（§4.5-6 第二层）——数值表化后 clamp 逻辑不变（表给的是参数上限，输入合法性仍 Sim 内钳制）。

---

## 4. 实施批次（M11 前置，估 ~200 行 + xlsx）

| 批 | 内容 | 验收 |
| --- | --- | --- |
| ① | `#combatnum.xlsx` + bean/表定义 + gen.bat 生成（Tables 增 `Tbcombatnum`） | Tables 加载链出数 |
| ② | `SimConfig` 玩法段 static readonly 化 + `LoadFrom` 回填 + `Tables` 加载处调用 | L1 全绿（消费点零改动） |
| ③ | buildHash Targets 增 RawFile/Config | 守卫用例复算含数据文件 |
| ④ | `BaselineSpec`/`SimSandbox`/`Room.Start` 的 Hp=100 改读表值 | 三处硬编码清零 |

**依赖**：`Luban/Data` 表源在本机为空壳——xlsx 定义需按 Luban 格式新建（既有 demo 表可参照）；gen.bat 双 pass 原样可用。

---

## 5. 落地记录（2026-09-19，四批全做完）

| 批 | 状态 | 落地 |
| --- | --- | --- |
| ① 表源与生成 | ✅ | `Luban/Data/#combatnum.xlsx`（`id` + 8 字段，单行）+ `__tables__.xlsx` 登记 `TbCombatNum`（value_type `CombatNum`，`index=id`，**group=`c,s`**）→ `gen.bat` 双产物：客户端 bin `Assets/LiteGame/RawFile/Config/tbcombatnum.bytes` + 服务端 json `RoomServer/Data/tbcombatnum.json`；生成物 `Assets/GameData/Generated/{combatnum,Tbcombatnum}.cs` + Lua `cfg/tbcombatnum.lua` |
| ② 回填与消费 | ✅ | 客户端 `ConfigService`：预取清单增 `tbcombatnum` + 建表后 `ApplyCombatNumbers` 调 `CombatConfig.LoadFrom`；服务端 `RoomServer/CombatNumbers.LoadFromRepo()`（`Program` 启动即装载）；消费点（Input/Movement/Shooting）**零改动**（名字不变） |
| ③ buildHash 闭包 | ✅ | `gen-build-hash.py` 增 `DATA_TARGETS`（客户端 bin 目录 + 服务端 json 目录，扩展名白名单 `.bytes/.json`）→ **48 文件**（38 源 + 10 数据）；`BuildHashTests` 同步同规则复算 |
| ④ Hp 硬编码清零 | ✅ | `CombatConfig.SpawnHp`(const) → `EntityHp`(表字段 `entity_hp`)；全仓 `Hp = 100` **31 处清零**（Room.Start / SimSandbox / BaselineSpec / 各测试） |

### 5.1 落地中发现的约束与处置（重要）

**① 服务端拿不到 Luban 运行时** → 设计 §3.2 写"两端读同一份 bin"，但实测 `Luban.Runtime` 是**本机 `file:` 依赖**（《克隆后自备清单》§4：`Packages/manifest.json` 不入库），.NET 8 的 RoomServer **无法引用**。
**处置**：`gen.bat` 新增 **Pass 1b**——同一份表源额外产出 **json**（`RoomServer/Data/`），服务端用 `System.Text.Json` 解析（零新依赖、可移植）。两端数值仍**同源**（同一次 gen.bat、同一 xlsx），一致性由 buildHash 闭包 + `CombatNumbersTests`（表==代码默认值）双保险。
> 客户端仍走 bin（既有 YooAsset 链路），服务端走 json——**同源不同编码**，不是两份数据。

**② `mode=one` 未生效** → 单行表最初按 `mode=one` 登记，生成物却是"以首字段 `move_speed` 为键的 map"（`index` 空 → Luban 自动取首字段）。
**处置**：改为**显式 `id=1` + 常规 map 形态**（与其它表同构，最稳）；访问 `Tables.Tbcombatnum.Get(1)`。

**③ gen.bat 三个坑位（本次踩到并修复两个 + 新增自愈）**：
- **json pass 会清空 `outputDataDir`** → 首版把 json 输出到客户端 bin 目录，**把 4 张表的 `.bytes` 全删了**（git status 立现）。处置：json 输出改到 `RoomServer/Data/`（服务端目录），与客户端数据目录物理隔离。
- **cs-bin pass 删除 `Luban.Tables.asmdef`**（记录在案的老坑）：本次复现 → `cfg` 类型在 Unity 侧整体消失（`CS0246` 一大片）。**长期对策已落**：`gen.bat` 新增 **Pass 4** 自动 `git -C <root> checkout --` 恢复该 asmdef + meta（幂等自愈，跑完 gen 不再需要手工对账这一项）。
- **`gen_lua_keys.py` 输出路径过时**（Shell 分层重排后未同步）：写到 `Scripts/Runtime/Bridge/Generated/`（旧路径）→ 与 `Shell/Bridge/Generated/` 的**同名类重复定义**（`CS0101`）。**根因已修**：路径补 `Shell/`。

**④ L2 门禁两处噪点（顺带修）**：
- 新鲜度守卫的 `eval` 在"编辑器正忙（编译中）"时会失败 → 改为**失败只告警不中断**（由新鲜度断言与编译状态给结论）。
- 控制台缓冲里**上一次失败编译的 error** 会被判成本次失败（实测误报）→ 守卫前先 `clear_console`，再强制重编，再读控制台。

### 5.2 验收

| 项 | 结果 |
| --- | --- |
| L1 | **332 绿**（LiteFramework 203 / LiteSim 75 / **LiteNet 54**：含 6 条 `CombatNumbersTests`） |
| L2 | **退出码 0**：meta 10921 / 程序集新鲜 / 编译无失败 / 控制台 0 错 / **EditMode 6/6**（含 2 条 `CombatNumbersEditModeTests`：预取清单含表 + bin 可建表且与运行值一致） |
| 一致性守卫 | ① 表值与 `CombatConfig` 默认值一致（漂移即红）② 表数据进 buildHash（48 文件）→ 数值不同**直接拒进房** |
| 改表生效 | 改 xlsx → 跑 gen.bat → 重启（客户端 `ConfigService` / 服务端 `Program` 装载）即生效，**不改代码、不重编译** |
