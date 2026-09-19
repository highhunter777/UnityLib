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
