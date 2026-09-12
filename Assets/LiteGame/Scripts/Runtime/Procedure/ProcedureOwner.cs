using System;
using LiteFramework;

namespace LiteGame
{
    /// <summary>
    /// 流程宿主与**流程间传参的正身**（具名字段，编译期可查——替代已删的字符串键数据字典）。
    /// **业务件**：框架只认 <see cref="IProcedureOwner.LastError"/>，其余字段随业务需要加在这里，
    /// 不回填框架 Core（2026-09-10 泛型化重构：框架侧的 ProcedureOwner 已删，其零使用字段 RetryBoot 一并移除；
    /// 联机后的 roomId / frameNo 等即加在本类）。
    /// 流程自身的依赖不从 Owner 取（那是局部服务定位器）——依赖走流程构造函数注入。
    /// </summary>
    public sealed class ProcedureOwner : IProcedureOwner
    {
        /// <summary>最近一次流程失败的原因（ProcedureBase.Fail 写入；ProcedureError 读取后据此提示）。</summary>
        public Exception LastError { get; set; }
    }
}
