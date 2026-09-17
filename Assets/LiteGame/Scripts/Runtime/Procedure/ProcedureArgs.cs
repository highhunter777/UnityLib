using System;

namespace LiteGame
{
    /// <summary>
    /// 流程迁移 payload（通用状态机 `StageMachine&lt;ProcedureId, ProcedureArgs&gt;` 的 `TReq`，2026-09-17）：
    /// **每次迁移随参数带**，编译期强类型 —— 取代已退休的 `ProcedureOwner`（那套"owner 上挂字段、可能为 null"
    /// 的弱约束）。所有字段只读；每次 `Request` 覆盖上一次（last-wins 与迁移目标同源）。
    /// </summary>
    public readonly struct ProcedureArgs
    {
        /// <summary>失败原因（`ProcedureId.Error` 阶段读；其余阶段为 null）。</summary>
        public readonly Exception Error;

        public ProcedureArgs(Exception error = null)
        {
            Error = error;
        }

        // ---- 联机线扩展位（M10 四批 / M11 填，见《UI扩展能力设计》§9.1）----
        // roomId / frameNo / BattleContext：流程间传参加在这里（只读字段 + 构造入参），
        // 不新增 owner 载体、不用字符串键字典。
    }
}
