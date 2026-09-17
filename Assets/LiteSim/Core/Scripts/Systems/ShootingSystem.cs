namespace LiteSim
{
    /// <summary>
    /// 射击判定系统（§3.3 顺序第 3 位，§3.4 hitscan + M8 决策 #10/#12）：
    /// 对活体做圆柱求交（半径 + y 区间），按距离取最近（并列取低槽位——遍历顺序恒定）；
    /// 命中 → Cmds.Write(Damage)；开火/命中 → Events.Write(Fire/Hit)。
    /// 本系统是 M8 唯一消费 RngState 的系统（#10：确定性审计写在签名上——伤害浮动 ±1）。
    /// </summary>
    public static class ShootingSystem
    {
        public static void Run(SimWorldState s, SimInputFrame[] inputs)
        {
            for (int i = 0; i < inputs.Length; i++)
            {
                if ((inputs[i].Buttons & SimInputFrame.ButtonFire) == 0u) continue;
                if (!s.TryResolve(inputs[i].EntityId, out int shooterSlot)) continue;

                ref EntitySlot shooter = ref s.Entities[shooterSlot];

                // 射线方向 = 输入瞄准向量本身（2026-09-17：Aim 即事实，省一次三角函数往返；
                // 零向量不会命中任何目标——采集侧契约要求非零）
                float dx = inputs[i].AimX;
                float dz = inputs[i].AimZ;
                float originY = shooter.Pos.Y + SimConfig.HitscanHeight * 0.5f;

                int hitSlot = -1;
                float hitT = SimConfig.HitscanRange;
                for (int j = 0; j < SimConfig.MaxEntities; j++)
                {
                    if (j == shooterSlot) continue; // lint-allow R3（整型等值，非浮点精度比较）
                    if ((s.AliveBitmap[j >> 5] & (1u << (j & 31))) == 0u) continue;

                    ref EntitySlot tgt = ref s.Entities[j];

                    // 圆柱 y 区间：射线在 [tgt.Pos.Y, tgt.Pos.Y + Height] 内才算
                    if (originY < tgt.Pos.Y) continue;
                    if (originY > tgt.Pos.Y + SimConfig.HitscanHeight) continue;

                    // XZ 平面射线-圆求交：m = C-O；b = m·D（前向投影）；c2 = |m|² - b²（垂距平方）
                    float mx = tgt.Pos.X - shooter.Pos.X;
                    float mz = tgt.Pos.Z - shooter.Pos.Z;
                    float b = mx * dx + mz * dz;
                    if (b < 0f) continue; // 目标在身后

                    float r2 = SimConfig.HitscanRadius * SimConfig.HitscanRadius;
                    float c2 = mx * mx + mz * mz - b * b;
                    if (c2 > r2) continue; // 垂距超出圆柱半径

                    float t = b - SimMath.Sqrt(r2 - c2);
                    if (t < 0f) t = 0f; // 起点已在圆柱内

                    if (t < hitT)
                    {
                        hitT = t;
                        hitSlot = j;
                    }
                }

                s.Events.Write(FrameEventKind.Fire, shooter.Id, 0L, 0, shooter.Pos);

                if (hitSlot >= 0)
                {
                    ref EntitySlot hit = ref s.Entities[hitSlot];

                    // 伤害浮动 ±1（消费 RngState——局部副本推进后写回，SimRng 使用约定）
                    var rng = new SimRng(s.RngState);
                    int dmg = SimConfig.BaseDamage + rng.NextRange(0, 3) - 1;
                    s.RngState = rng.State;

                    var hitPos = new SimVector3(
                        shooter.Pos.X + dx * hitT,
                        originY,
                        shooter.Pos.Z + dz * hitT);

                    s.Cmds.Write(SimCommandKind.Damage, hit.Id, shooter.Id, dmg);
                    s.Events.Write(FrameEventKind.Hit, hit.Id, shooter.Id, dmg, hitPos);
                }
            }
        }
    }
}
