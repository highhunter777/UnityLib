using System;
using Xunit;

namespace LiteFramework.Tests
{
    /// <summary>
    /// 通用流程状态机 `StageMachine&lt;TId, TReq&gt;`（2026-09-17 A 路线重构，取代 `Fsm&lt;TOwner&gt;`）。
    /// 前 7 条 = 旧 `FsmTests` 语义**逐条移植**（迁移红线：语义不得回退）；
    /// 后 4 条 = 通用化新增（payload 传递 / 两段式 / payload last-wins / 未 Start 时 Request 抛）。
    /// </summary>
    public sealed class StageMachineTests   // 实例状态机，无静态状态
    {
        private enum Id { A, B, C, Unregistered }

        private readonly struct Req
        {
            public readonly int Value;
            public Req(int value) => Value = value;
        }

        private sealed class StageA : IStage<Id, Req>
        {
            public int Enter, Leave, Update, LastReq;
            public Action OnUpdateHook, OnLeaveHook;
            public void OnInit(IStageHost<Id, Req> m) { }
            public void OnEnter(IStageHost<Id, Req> m, in Req req) { Enter++; LastReq = req.Value; }
            public void OnUpdate(IStageHost<Id, Req> m, float s) { Update++; OnUpdateHook?.Invoke(); }
            public void OnLeave(IStageHost<Id, Req> m) { Leave++; OnLeaveHook?.Invoke(); }
        }

        private sealed class StageB : IStage<Id, Req>
        {
            public int Enter, Leave, Update, LastReq;
            public Action OnEnterHook;
            public void OnInit(IStageHost<Id, Req> m) { }
            public void OnEnter(IStageHost<Id, Req> m, in Req req) { Enter++; LastReq = req.Value; OnEnterHook?.Invoke(); }
            public void OnUpdate(IStageHost<Id, Req> m, float s) => Update++;
            public void OnLeave(IStageHost<Id, Req> m) => Leave++;
        }

        private sealed class StageC : IStage<Id, Req>
        {
            public int Enter, LastReq;
            public void OnInit(IStageHost<Id, Req> m) { }
            public void OnEnter(IStageHost<Id, Req> m, in Req req) { Enter++; LastReq = req.Value; }
            public void OnUpdate(IStageHost<Id, Req> m, float s) { }
            public void OnLeave(IStageHost<Id, Req> m) { }
        }

        private static StageMachine<Id, Req> Make(StageA a, StageB b, StageC c)
            => new StageMachine<Id, Req>("t", (Id.A, a), (Id.B, b), (Id.C, c));

        // ---- 旧 FsmTests 语义（逐条移植）----

        [Fact]
        public void StageMachine_Start_OnEnter触发_未Start时Tick静默()
        {
            var a = new StageA();
            var m = new StageMachine<Id, Req>("t", (Id.A, a));
            m.Tick(0.016f);                                  // 未启动：静默跳过
            Assert.Equal(0, a.Update);
            Assert.False(m.Started);

            m.Start(Id.A);
            Assert.Equal(1, a.Enter);
            Assert.True(m.Started);
            Assert.Equal(Id.A, m.Current);
        }

        [Fact]
        public void StageMachine_帧末切换_阶段时间归零_切换在OnUpdate之后()
        {
            var a = new StageA(); var b = new StageB(); var c = new StageC();
            var m = Make(a, b, c);
            m.Start(Id.A);

            m.Tick(0.5f);
            Assert.Equal(1, a.Update);
            Assert.Equal(0.5f, m.StageTime, 5);

            a.OnUpdateHook = () => m.Request(Id.B);
            m.Tick(0.25f);
            Assert.Equal(2, a.Update);                       // 切换发生在 OnUpdate 之后
            Assert.Equal(1, a.Leave);
            Assert.Equal(1, b.Enter);
            Assert.Equal(Id.B, m.Current);
            Assert.Equal(0f, m.StageTime, 5);                // 归零
        }

        [Fact]
        public void StageMachine_同帧多次请求_last_wins()
        {
            var a = new StageA(); var b = new StageB(); var c = new StageC();
            var m = Make(a, b, c);
            m.Start(Id.A);
            a.OnUpdateHook = () => { m.Request(Id.B); m.Request(Id.C); };

            m.Tick(0.016f);
            Assert.Equal(Id.C, m.Current);                   // last-wins：直接到 C
            Assert.Equal(1, a.Leave);
            Assert.Equal(0, b.Enter);                        // B 只是被跳过目标，未进
        }

        [Fact]
        public void StageMachine_OnEnter内请求_挂下个Advance_期间收到一次OnUpdate()
        {
            var a = new StageA(); var b = new StageB(); var c = new StageC();
            var m = Make(a, b, c);
            m.Start(Id.A);
            a.OnUpdateHook = () => m.Request(Id.B);
            b.OnEnterHook = () => m.Request(Id.A);           // OnEnter 内改道 → 挂起

            m.Tick(0.016f);                                  // A → B（B.OnEnter 挂起回 A）
            Assert.Equal(Id.B, m.Current);
            Assert.True(m.HasPending);

            m.Tick(0.016f);                                  // B 先收到一次 OnUpdate，帧末才切
            Assert.Equal(1, b.Update);
            Assert.Equal(Id.A, m.Current);
        }

        [Fact]
        public void StageMachine_重入_抛()
        {
            var a = new StageA();
            var m = new StageMachine<Id, Req>("t", (Id.A, a));
            m.Start(Id.A);
            Assert.Throws<InvalidOperationException>(() => m.Request(Id.A));
        }

        [Fact]
        public void StageMachine_OnLeave内改道_抛()
        {
            var a = new StageA(); var b = new StageB(); var c = new StageC();
            var m = Make(a, b, c);
            m.Start(Id.A);
            a.OnUpdateHook = () => m.Request(Id.B);
            a.OnLeaveHook = () => m.Request(Id.A);           // 离场中改道

            Assert.Throws<InvalidOperationException>(() => m.Tick(0.016f));
        }

        [Fact]
        public void StageMachine_未注册阶段_调用时当场抛()
        {
            var a = new StageA();
            var m = new StageMachine<Id, Req>("t", (Id.A, a));
            m.Start(Id.A);
            Assert.Throws<InvalidOperationException>(() => m.Request(Id.Unregistered));
        }

        // ---- 通用化新增 ----

        [Fact]
        public void StageMachine_未Start时_Request抛()
        {
            var a = new StageA();
            var m = new StageMachine<Id, Req>("t", (Id.A, a));
            Assert.Throws<InvalidOperationException>(() => m.Request(Id.A));
        }

        [Fact]
        public void StageMachine_payload随迁移传递()
        {
            var a = new StageA(); var b = new StageB(); var c = new StageC();
            var m = Make(a, b, c);
            m.Start(Id.A);                                   // Start 用 default(payload)

            m.Request(Id.B, new Req(42));
            m.Advance();
            Assert.Equal(Id.B, m.Current);
            Assert.Equal(42, b.LastReq);                     // OnEnter 收到 Request 时给的 payload
        }

        [Fact]
        public void StageMachine_两段式_Request不迁移_Advance才迁移()
        {
            var a = new StageA(); var b = new StageB(); var c = new StageC();
            var m = Make(a, b, c);
            m.Start(Id.A);

            m.Request(Id.B, new Req(7));
            Assert.Equal(Id.A, m.Current);                   // Request 只入队
            Assert.True(m.HasPending);
            Assert.Equal(Id.B, m.PendingId);
            Assert.Equal(0, b.Enter);                        // 未迁移

            m.Advance();                                     // 信号驱动入口
            Assert.Equal(Id.B, m.Current);
            Assert.False(m.HasPending);
            Assert.Equal(7, b.LastReq);
        }

        [Fact]
        public void StageMachine_payload也last_wins()
        {
            var a = new StageA(); var b = new StageB(); var c = new StageC();
            var m = Make(a, b, c);
            m.Start(Id.A);
            a.OnUpdateHook = () =>
            {
                m.Request(Id.B, new Req(1));
                m.Request(Id.C, new Req(2));                 // 同帧覆盖目标，payload 同步覆盖
            };

            m.Tick(0.016f);
            Assert.Equal(Id.C, m.Current);
            Assert.Equal(2, c.LastReq);
            Assert.Equal(0, b.Enter);
        }
    }
}
