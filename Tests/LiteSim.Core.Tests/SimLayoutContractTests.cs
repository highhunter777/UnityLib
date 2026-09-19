using System;
using System.Reflection;
using Xunit;

namespace LiteSim.Tests
{
    /// <summary>
    /// 第一批布局契约（《M8 实施指导》§2.1 验收 + §3"布局契约"组）：
    /// 用反射把"世界状态无引用类型字段"钉死——任何人往 SimWorldState 里加 List/class 成员，
    /// M9 的快照就会静默共享同一块内存（§7 风险 1），此测试当场暴露。
    /// 详尽的槽位/Id/确定性用例属第二批（§2.6），此处只放布局 + 常量对账 + 分配闭环冒烟。
    /// </summary>
    public class SimLayoutContractTests
    {
        private const BindingFlags AllInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        [Fact]
        public void 布局_SimWorldState_无引用类型字段()
        {
            FieldInfo[] fields = typeof(SimWorldState).GetFields(AllInstance);
            Assert.True(fields.Length > 0);

            for (int i = 0; i < fields.Length; i++)
            {
                Type ft = fields[i].FieldType;
                Assert.True(ft.IsArray || ft.IsValueType,
                    fields[i].Name + " : " + ft.Name + " 违反布局契约（进快照的字段只允许值类型/数组，#5）");

                if (ft.IsArray)
                {
                    Assert.True(ft.GetElementType().IsValueType,
                        fields[i].Name + " 的数组元素必须是值类型（引用元素会被浅拷共享）");
                }
            }
        }

        [Fact]
        public void 布局_EntitySlot_仅值类型字段()
        {
            FieldInfo[] fields = typeof(EntitySlot).GetFields(AllInstance);
            Assert.True(fields.Length > 0);

            for (int i = 0; i < fields.Length; i++)
            {
                Assert.True(fields[i].FieldType.IsValueType,
                    fields[i].Name + " : " + fields[i].FieldType.Name + " 必须是值类型（blittable，#3）");
            }
        }

        [Fact]
        public void 布局_SimConfig_常量与开工清单对账()
        {
            // 《状态同步实施方案》§11-3：60Hz / inputDelay 1 / MaxCatchUp 5 / MaxRollbackFrames 8 / MaxEntities 256
            Assert.Equal(60, SimConfig.TickRate);
            Assert.Equal(1, SimConfig.InputDelay);
            Assert.Equal(5, SimConfig.MaxCatchUp);
            Assert.Equal(8, SimConfig.MaxRollbackFrames);
            Assert.Equal(256, SimConfig.MaxEntities);
            Assert.Equal(SimMath.Dt, SimConfig.Dt);
            Assert.Equal(32, SimConfig.CustomBytesPerEntity);
            Assert.Equal(256, SimConfig.GlobalsBytes);
        }

        [Fact]
        public void 槽位_Spawn解析释放闭环_冒烟()
        {
            var s = new SimWorldState();

            var proto = new EntitySlot { Hp = CombatConfig.EntityHp, Pos = new SimVector3(1f, 0f, 2f) };
            long id = s.Spawn(in proto, out int slotIndex);
            Assert.True(id > 0L, "首个分配 version=1，Id 必为正");
            Assert.True(slotIndex >= 0);
            Assert.Equal(1, s.AliveCount());

            Assert.True(s.TryResolve(id, out int resolved));
            Assert.Equal(slotIndex, resolved);
            Assert.Equal(100, s.Entities[resolved].Hp);

            s.Despawn(id);
            Assert.False(s.TryResolve(id, out int gone));
            Assert.Equal(0, s.AliveCount());

            // Double-free 静默忽略（§3 用例）
            s.Despawn(id);
            Assert.Equal(0, s.AliveCount());
        }

        [Fact]
        public void 槽位_同槽复用_Id不复用_冒烟()
        {
            var s = new SimWorldState();

            // 灌满 256 槽 → 游标环形归零；世界满时分配失败（Id=0，slotIndex=-1）
            long[] ids = new long[SimConfig.MaxEntities];
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                ids[i] = s.Spawn(default(EntitySlot), out int _);
                Assert.True(ids[i] > 0L);
            }
            Assert.Equal(0L, s.Spawn(default(EntitySlot), out int full));
            Assert.Equal(-1, full);

            s.Despawn(ids[0]);                               // 回收槽 0
            long second = s.Spawn(default(EntitySlot), out int slotB);
            Assert.Equal(0, slotB);                          // 游标已绕回，回收到同一槽
            Assert.NotEqual(ids[0], second);                  // version 递增 → Id 必不同（#7）
            Assert.False(s.TryResolve(ids[0], out int stale)); // 旧 Id 失效（跨帧引用唯一入口）
        }

        [Fact]
        public void 快照_CopyTo逐数组深拷_改一处互不影响()
        {
            var src = new SimWorldState();
            var dst = new SimWorldState();

            long id = src.Spawn(new EntitySlot { Hp = 50, Pos = new SimVector3(3f, 0f, 4f) }, out int slot);
            src.Frame = 123;
            src.Globals[7] = 0xAB;
            src.CustomData[slot * SimConfig.CustomBytesPerEntity + 3] = 0xCD;

            src.CopyTo(dst);
            Assert.Equal(123, dst.Frame);
            Assert.Equal((byte)0xAB, dst.Globals[7]);
            Assert.Equal((byte)0xCD, dst.CustomData[slot * SimConfig.CustomBytesPerEntity + 3]);
            Assert.True(dst.TryResolve(id, out int resolved));
            Assert.Equal(50, dst.Entities[resolved].Hp);

            // 深拷独立性：改 dst 一字节，src 必须不受影响（§7 风险 1 对策）
            dst.Globals[7] = 0x11;
            dst.Entities[resolved].Hp = 99;
            Assert.Equal((byte)0xAB, src.Globals[7]);
            Assert.Equal(50, src.Entities[slot].Hp);

            // 瞬态缓冲不随快照走（§3.7/决策⑥）
            src.Cmds.Write(SimCommandKind.Damage, id, 0L, 10);
            Assert.Equal(1, src.Cmds.Count);
            src.CopyTo(dst);
            Assert.Equal(0, dst.Cmds.Count);
        }
    }
}
