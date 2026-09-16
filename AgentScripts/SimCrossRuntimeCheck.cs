using System;
using System.Globalization;
using System.IO;
using System.Text;
using LiteSim;
using UnityEngine;

// M8 跨运行时双跑对账（v3 定位：预测质量保障，非阻断）：
// 在 Unity（Mono/2022.3）内**原样重放** dotnet 侧 SimChecksumBaselineSpec 的 3000 帧场景
// （同种子/同布点/同输入脚本——逻辑逐句对齐 Tests/LiteSim.Core.Tests/SimChecksumBaselineSpec.cs），
// 逐帧 checksum 与 .NET 记录的 Baselines/SimChecksum.txt 比对。
// 产出：AgentScripts/crossruntime_report.txt（摘要 + 前 5 个分叉）+ crossruntime_unity_checksums.txt（全序列）。
// 返回：0 = 3000/3000 逐帧一致；1 = 有分叉；2 = 基线文件缺失/损坏。
//
// 辅助入口 DumpFirstFrames：前 3 帧全状态位级转储到 %TEMP%/unity_dump.txt——
// 与 dotnet 侧临时用例 TempStateDump 对拍，用于定位跨运行时首个分叉字段。
public static class SimCrossRuntimeCheck
{
    private const int FrameCount = 3000;
    private const int PlayerCount = 2;
    private const int TargetCount = 5;
    private const ulong WorldSeed = 0x5EEDBEEF12345678UL;
    private const ulong InputSeed = 0x0F0F0F0F0F0F0F0FUL;
    private const string BaselinePath = "Tests/LiteSim.Core.Tests/Baselines/SimChecksum.txt";
    private const string ReportPath = "AgentScripts/crossruntime_report.txt";
    private const string UnitySeqPath = "AgentScripts/crossruntime_unity_checksums.txt";

    // ---- 与 SimChecksumBaselineSpec 逐句对齐的共享装配 ----

    private static SimWorldState BuildWorld(out long p0, out long p1)
    {
        var s = new SimWorldState { RngState = WorldSeed };
        s.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(0f, 0f, 0f), Yaw = 0f }, out int _);
        s.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(10f, 0f, 10f), Yaw = SimTrig.Pi }, out int _);
        for (int i = 0; i < TargetCount; i++)
        {
            float z = -10f + 5f * i;
            s.Spawn(new EntitySlot { Hp = 100, Pos = new SimVector3(20f, 0f, z) }, out int _);
        }
        p0 = s.Entities[0].Id;
        p1 = s.Entities[1].Id;
        return s;
    }

    private static SimMapData BuildMap()
    {
        var map = new SimMapData { GroundY = 0f, HalfWidth = 50f, HalfDepth = 50f };
        map.SpawnPoints[0] = new SimVector3(0f, 0f, 0f);
        map.SpawnPoints[1] = new SimVector3(10f, 0f, 10f);
        map.SpawnPointCount = 2;
        return map;
    }

    private static void MakeInputs(ref SimRng rng, long p0, long p1, SimInputFrame[] inputs)
    {
        for (int i = 0; i < inputs.Length; i++)
        {
            inputs[i].EntityId = i == 0 ? p0 : p1;
            inputs[i].MoveX = rng.NextFloat01() * 2f - 1f;
            inputs[i].MoveZ = rng.NextFloat01() * 2f - 1f;
            inputs[i].Yaw = rng.NextFloat01() * SimTrig.TwoPi;
            inputs[i].Buttons = (rng.NextUInt32() & 3u) == 0u ? SimInputFrame.ButtonFire : 0u;
        }
    }

    private static string Bits(float v)
    {
        return BitConverter.SingleToInt32Bits(v).ToString("X8", CultureInfo.InvariantCulture);
    }

    public static int Run()
    {
        string full = Path.GetFullPath(BaselinePath);
        if (!File.Exists(full))
        {
            Debug.LogError("[SimCrossRuntimeCheck] 基线文件缺失: " + full);
            return 2;
        }

        var values = new System.Collections.Generic.List<uint>(FrameCount);
        foreach (var raw in File.ReadAllLines(full))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;
            if (!uint.TryParse(line, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint v))
            {
                Debug.LogError("[SimCrossRuntimeCheck] 基线行解析失败: " + line);
                return 2;
            }
            values.Add(v);
        }

        if (values.Count != FrameCount)
        {
            Debug.LogError($"[SimCrossRuntimeCheck] 基线帧数 {values.Count} != {FrameCount}");
            return 2;
        }

        SimMapData map = BuildMap();
        var s = BuildWorld(out long p0, out long p1);
        var inputRng = new SimRng(InputSeed);
        var inputs = new SimInputFrame[PlayerCount];

        int firstMismatch = -1;
        int mismatchCount = 0;
        int loggedDiffs = 0;
        var report = new StringBuilder();
        var unitySeq = new StringBuilder(FrameCount * 11);
        for (int f = 0; f < FrameCount; f++)
        {
            MakeInputs(ref inputRng, p0, p1, inputs);
            SimStep.Step(s, map, inputs);
            uint sum = SimChecksum.ComputeChecksum(s);
            unitySeq.Append(sum.ToString(CultureInfo.InvariantCulture)).Append('\n');

            if (sum != values[f])
            {
                mismatchCount++;
                if (firstMismatch < 0) firstMismatch = f;
                if (loggedDiffs < 5)
                {
                    report.AppendLine($"帧 {f}: Unity={sum} .NET={values[f]}");
                    loggedDiffs++;
                }
            }
        }
        File.WriteAllText(UnitySeqPath, unitySeq.ToString());

        string summary = $"frames={FrameCount} mismatches={mismatchCount} firstMismatchFrame={(firstMismatch < 0 ? "none" : firstMismatch.ToString(CultureInfo.InvariantCulture))}";
        File.WriteAllText(ReportPath, summary + "\n" + report);

        if (mismatchCount == 0)
        {
            Debug.Log("[SimCrossRuntimeCheck] ✅ " + summary);
            return 0;
        }

        Debug.LogError("[SimCrossRuntimeCheck] ❌ " + summary + "（v3 定位下不阻断发版，记录为预测质量议题）");
        return 1;
    }

    /// <summary>分叉定位：前 3 帧全状态位级转储到 %TEMP%/unity_dump.txt（与 dotnet 侧 TempStateDump 对拍）。</summary>
    public static int DumpFirstFrames()
    {
        SimMapData map = BuildMap();
        var s = BuildWorld(out long p0, out long p1);
        var inputRng = new SimRng(InputSeed);
        var inputs = new SimInputFrame[PlayerCount];

        var sb = new StringBuilder();
        for (int f = 0; f < 8; f++)
        {
            MakeInputs(ref inputRng, p0, p1, inputs);
            SimStep.Step(s, map, inputs);

            sb.Append("frame ").Append(f).Append(": checksum=").Append(SimChecksum.ComputeChecksum(s)).Append('\n');
            sb.Append("  rng=").Append(s.RngState.ToString("X16", CultureInfo.InvariantCulture)).Append('\n');
            for (int i = 0; i < SimConfig.MaxEntities; i++)
            {
                if (!s.IsAlive(i)) continue;
                ref EntitySlot e = ref s.Entities[i];
                sb.Append("  slot ").Append(i)
                  .Append(": id=").Append(e.Id.ToString("X16", CultureInfo.InvariantCulture))
                  .Append(" pos=(").Append(Bits(e.Pos.X)).Append(',').Append(Bits(e.Pos.Y)).Append(',').Append(Bits(e.Pos.Z)).Append(')')
                  .Append(" vel=(").Append(Bits(e.Vel.X)).Append(',').Append(Bits(e.Vel.Y)).Append(',').Append(Bits(e.Vel.Z)).Append(')')
                  .Append(" yaw=").Append(Bits(e.Yaw))
                  .Append(" hp=").Append(e.Hp)
                  .Append(" flags=").Append(e.Flags.ToString("X8", CultureInfo.InvariantCulture))
                  .Append('\n');
            }
        }

        string path = Path.Combine(Path.GetTempPath(), "unity_dump.txt");
        File.WriteAllText(path, sb.ToString());
        Debug.Log("[SimCrossRuntimeCheck] 转储完成: " + path);
        return 0;
    }
}
