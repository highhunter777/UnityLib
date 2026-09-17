using LiteSim;

namespace LiteNet.Protocol
{
    /// <summary>
    /// **快照载波**（《M10实施指导》决策 7 的解耦点，2026-09-17 前置）：权威循环只管"取一份待广播的快照"，
    /// **不关心它是全量还是增量**。
    ///
    /// 为什么先抽这个口：第二批（RoomServer 权威循环）要验收"服务器能独立跑 60Hz 权威局"，而增量差分
    /// `SnapshotDiffer` 属第三批 → 第二批用 <see cref="FullSnapshotSource"/> 全量占位即可跑通并验收；
    /// 第三批把实现换成 SnapshotDiffer（增量 + 全量兜底 + ack 触发），**循环零改动**。
    /// </summary>
    public interface ISnapshotSource
    {
        /// <summary>
        /// 产出第 <paramref name="frame"/> 帧待广播的快照（含 `frameNo` / `is_full` / `checksum` / `ackInput`）。
        /// </summary>
        Proto.StateSnapshot Build(int frame, SimWorldState state, int ackInput);
    }

    /// <summary>
    /// 全量占位实现（每帧全量）：第二批权威循环用。
    /// 第三批换增量后本类**保留**——它是"定期全量兜底 / ack 落后兜底"的现成复用件（决策 7）。
    /// 无状态 → 可被多房间共享同一实例。
    /// </summary>
    public sealed class FullSnapshotSource : ISnapshotSource
    {
        public Proto.StateSnapshot Build(int frame, SimWorldState state, int ackInput)
            => SnapshotCodec.PackFull(frame, state, ackInput);
    }
}
