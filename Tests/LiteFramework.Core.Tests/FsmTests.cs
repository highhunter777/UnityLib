using System;
using Xunit;

namespace LiteFramework.Tests
{
    public sealed class FsmTests   // 实例状态机，无静态状态
    {
        private sealed class Owner { }

        private sealed class StateA : FsmState<Owner>
        {
            public int Enter, Leave, Update;
            public Fsm<Owner> Fsm;                        // 供回调里发起 ChangeState
            public Action OnUpdateHook;
            public Action OnLeaveHook;
            public override void OnEnter(Fsm<Owner> fsm) => Enter++;
            public override void OnUpdate(Fsm<Owner> fsm, float s) { Update++; OnUpdateHook?.Invoke(); }
            public override void OnLeave(Fsm<Owner> fsm) { Leave++; OnLeaveHook?.Invoke(); }
        }

        private sealed class StateB : FsmState<Owner>
        {
            public int Enter, Leave, Update;
            public Fsm<Owner> Fsm;
            public Action OnEnterHook;
            public override void OnEnter(Fsm<Owner> fsm) { Enter++; OnEnterHook?.Invoke(); }
            public override void OnUpdate(Fsm<Owner> fsm, float s) => Update++;
            public override void OnLeave(Fsm<Owner> fsm) => Leave++;
        }

        [Fact]
        public void Fsm_Start_OnEnter触发_未Start时Tick静默()
        {
            var a = new StateA();
            var fsm = new Fsm<Owner>("t", new Owner(), a);
            fsm.Tick(0.016f);                             // 未启动：静默跳过
            Assert.Equal(0, a.Update);

            fsm.Start<StateA>();
            Assert.Equal(1, a.Enter);
        }

        [Fact]
        public void Fsm_ChangeState_帧末切换_状态时间归零()
        {
            var a = new StateA(); var b = new StateB();
            var fsm = new Fsm<Owner>("t", new Owner(), a, b);
            fsm.Start<StateA>();

            fsm.Tick(0.5f);
            Assert.Equal(1, a.Update);
            Assert.Equal(0.5f, fsm.CurrentStateTime, 5);

            a.Fsm = fsm;
            a.OnUpdateHook = () => fsm.ChangeState<StateB>();
            fsm.Tick(0.25f);
            Assert.Equal(2, a.Update);                    // 切换在 OnUpdate 之后
            Assert.Equal(1, a.Leave);
            Assert.Equal(1, b.Enter);
            Assert.Same(b, fsm.CurrentState);
            Assert.Equal(0f, fsm.CurrentStateTime, 5);    // 归零
        }

        [Fact]
        public void Fsm_同帧多次请求_last_wins()
        {
            var a = new StateA(); var b = new StateB();
            var c = new StateC();
            var fsm = new Fsm<Owner>("t", new Owner(), a, b, c);
            fsm.Start<StateA>();
            a.Fsm = fsm;
            a.OnUpdateHook = () => { fsm.ChangeState<StateB>(); fsm.ChangeState<StateC>(); };

            fsm.Tick(0.016f);
            Assert.Same(c, fsm.CurrentState);             // last-wins：直接到 C
            Assert.Equal(1, a.Leave);
            Assert.Equal(0, b.Enter);                     // B 只是被跳过目标，未进
        }

        private sealed class StateC : FsmState<Owner>
        {
            public int Enter;
            public override void OnEnter(Fsm<Owner> fsm) => Enter++;
        }

        [Fact]
        public void Fsm_OnEnter内请求_挂下帧末_期间收到一次OnUpdate()
        {
            var a = new StateA(); var b = new StateB();
            var fsm = new Fsm<Owner>("t", new Owner(), a, b);
            fsm.Start<StateA>();
            a.Fsm = fsm;
            a.OnUpdateHook = () => fsm.ChangeState<StateB>();
            b.Fsm = fsm;
            b.OnEnterHook = () => fsm.ChangeState<StateA>();   // OnEnter 内改道

            fsm.Tick(0.016f);                             // A → B（B.OnEnter 挂起 A 请求）
            Assert.Same(b, fsm.CurrentState);
            fsm.Tick(0.016f);                             // B 先收到一次 OnUpdate，帧末才切
            Assert.Equal(1, b.Update);
            Assert.Same(a, fsm.CurrentState);
        }

        [Fact]
        public void Fsm_重入_抛()
        {
            var a = new StateA();
            var fsm = new Fsm<Owner>("t", new Owner(), a);
            fsm.Start<StateA>();
            Assert.Throws<InvalidOperationException>(() => fsm.ChangeState<StateA>());
        }

        [Fact]
        public void Fsm_OnLeave内改道_抛()
        {
            var a = new StateA(); var b = new StateB();
            var fsm = new Fsm<Owner>("t", new Owner(), a, b);
            fsm.Start<StateA>();
            a.Fsm = fsm;
            a.OnUpdateHook = () => fsm.ChangeState<StateB>();
            a.OnLeaveHook = () => fsm.ChangeState<StateA>();   // 离场中改道

            Assert.Throws<InvalidOperationException>(() => fsm.Tick(0.016f));
        }

        [Fact]
        public void Fsm_未注册状态_调用时当场抛()
        {
            var a = new StateA();
            var fsm = new Fsm<Owner>("t", new Owner(), a);
            fsm.Start<StateA>();
            Assert.Throws<InvalidOperationException>(() => fsm.ChangeState<StateB>());
        }
    }
}
