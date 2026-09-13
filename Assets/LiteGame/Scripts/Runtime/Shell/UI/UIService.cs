using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>
    /// UI 壳服务（M4 §2.1/§2.2/§2.3，手册步骤 1/2/3）：栈 / 层级组 / 七态机 / 实例化管线
    /// + 策略三件（构造注入，默认实现壳内自带——壳零策略硬编码）
    /// + 逻辑解析器（LuaBehaviourAdapter 接线；解析失败降级 NullLogic）。
    /// 薄壳 = DI 注册的普通服务（设计方案 §1.3），ProcedureLaunch 注册、装配点构造。
    /// 驱动：ITickable（GameEntry 统一喂）——仅 Active 态转发 OnUpdate；
    /// 遮盖语义（组级批量暂停的灰盒形态）：全屏界面激活 → 更低层级组的 Active 界面批量 Covered，
    /// 关闭后按"仍开着的最高全屏"重算（多全屏叠开不误恢复）。
    /// </summary>
    public sealed class UIService : ITickable, IModuleStats
    {
        public static readonly string[] GroupNames = { "Bottom", "Window", "Top" };

        private readonly UIFormCatalog _catalog;
        private readonly Dictionary<int, UIForm> _forms = new Dictionary<int, UIForm>(16);   // id → 实例（含池中）
        private readonly HashSet<int> _loading = new HashSet<int>();                         // 加载在途（幂等守卫：入字典前的窗口期）
        private readonly HashSet<int> _closing = new HashSet<int>();                         // 关闭在途（转场动画期间防并发 Close 重入）
        private readonly UILayerGroup[] _groups;
        private readonly Transform _root;
        private readonly ITransitionStrategy _transition;
        private readonly IPopInterceptor _pop;
        private readonly Func<UIFormInfo, IUIFormLogic> _logicResolver;

        public UIService(UIFormCatalog catalog,
            ILayerStrategy layerStrategy = null,
            ITransitionStrategy transitionStrategy = null,
            IPopInterceptor popInterceptor = null,
            Func<UIFormInfo, IUIFormLogic> logicResolver = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _transition = transitionStrategy ?? new FadeSlideTransition();
            _pop = popInterceptor ?? new DefaultPopInterceptor();
            _logicResolver = logicResolver;

            _root = new GameObject("[UIRoot]").transform;
            UnityEngine.Object.DontDestroyOnLoad(_root.gameObject);
            _groups = new UILayerGroup[GroupNames.Length];
            for (int i = 0; i < GroupNames.Length; i++)
            {
                var node = new GameObject(GroupNames[i]).transform;
                node.SetParent(_root, false);
                _groups[i] = new UILayerGroup(GroupNames[i], (i + 1) * UILayerGroup.DepthStride, node,
                    layerStrategy ?? new DefaultLayerStrategy());
            }
            Log.Info("UI 壳就绪:层级组 Bottom/Window/Top（策略三件默认实现）", "UI");
        }

        /// <summary>打开界面（幂等：已 Active 直接返回；池中复用不重跑 OnInit）。全屏界面激活后重算遮盖。
        /// 数据传参收口 <see cref="IUIData"/>（禁 object，2026-09-13 修订）。</summary>
        public async UniTask<UIForm> ShowAsync(int formId, IUIData data = null, CancellationToken ct = default)
        {
            var info = _catalog.Get(formId);
            var group = GetGroup(info.Layer);

            if (_forms.TryGetValue(formId, out var existing))
            {
                switch (existing.State)
                {
                    case UIFormState.Recycled:
                        Reuse(existing, group, data);
                        return existing;
                    case UIFormState.Active:
                        Log.Warning($"UIForm[{formId}] 已打开——ShowAsync 幂等返回", "UI");
                        return existing;
                    default:
                        throw new InvalidOperationException(
                            $"UIForm[{formId}] 状态 {existing.State} 不允许 Show（§3.4 fail-fast）");
                }
            }
            if (_loading.Contains(formId))
            {
                Log.Warning($"UIForm[{formId}] 加载中——并发 Show 已忽略（否则双实例：字典覆盖 + 首实例泄漏）", "UI");
                return null;
            }
            _loading.Add(formId);
            try
            {
                // 实例化管线：加载 → 实例化挂组 → 画布就位 → 入栈 → OnInit/OnShow → 转场表现
                var prefab = await AssetService.LoadAssetAsync<GameObject>(info.Location, ct);
                var root = UnityEngine.Object.Instantiate(prefab, group.Root);
                var form = new UIForm(info, root);
                group.AssignDepth(form);
                form.Logic = ResolveLogic(info);
                _forms[formId] = form;
                group.Stack.Push(form);
                form.EnterActiveFromLoading(data);
                if (info.FullScreen) RecomputeCovering();
                await PlayTransitionShow(form, ct);
                Log.Info($"UIForm[{formId}] 打开（{group.Name}@{form.Canvas.sortingOrder}）", "UI");
                return form;
            }
            finally
            {
                _loading.Remove(formId);
            }
        }

        /// <summary>关闭界面（仅 Active；出栈拦截 → 离场转场 → OnHide → 落池）。全屏关闭后重算遮盖。
        /// 并发安全：转场动画期间二次 Close 直接忽略（_closing 在途集——重入会在 Recycled 态撞迁移守卫）。</summary>
        public async UniTask CloseAsync(int formId)
        {
            if (!_forms.TryGetValue(formId, out var form))
                throw new KeyNotFoundException($"UIForm 表存在但未打开:{formId}（§3.4 fail-fast）");
            if (form.State != UIFormState.Active)
                throw new InvalidOperationException($"UIForm[{formId}] 状态 {form.State} 不允许 Close（仅 Active）");
            if (!_pop.CanClose(form))
            {
                Log.Info($"UIForm[{formId}] 关闭被出栈拦截", "UI");
                return;
            }
            if (!_closing.Add(formId))
            {
                Log.Warning($"UIForm[{formId}] 关闭中——并发 Close 已忽略", "UI");
                return;
            }

            try
            {
                await PlayTransitionClose(form, ct: default);
                if (form.State != UIFormState.Active)
                {
                    Log.Info($"UIForm[{formId}] 关闭中止（转场期间状态已变为 {form.State}）", "UI");
                    return;
                }
                form.EnterClosing();
                GetGroup(form.Info.Layer).Stack.Remove(form);
                form.Recycle();
                if (form.Info.FullScreen) RecomputeCovering();
                Log.Info($"UIForm[{formId}] 关闭", "UI");
            }
            finally
            {
                _closing.Remove(formId);
            }
        }

        /// <summary>DevReload 编排用（§2.3 定案"重载后全关"）：env 重建前关掉所有打开界面——
        /// 旧 LuaTable 经适配器持有时全部失效，留旧界面 = 静默用死对象。逐界面容错（单界面失败不阻断重载）。</summary>
        public async UniTask CloseAllOpen()
        {
            var ids = new List<int>();
            foreach (var kv in _forms)
                if (kv.Value.State == UIFormState.Active) ids.Add(kv.Key);
            foreach (var id in ids)
            {
                try { await CloseAsync(id); }
                catch (Exception ex) { Log.Error($"CloseAllOpen[{id}]:{ex.Message}", "UI"); }
            }
        }

        /// <summary>手动暂停（Active → Paused；恢复走 <see cref="Resume"/>）。</summary>
        public void Pause(int formId)
        {
            var form = RequireOpen(formId);
            form.EnterPaused();
        }

        public void Resume(int formId)
        {
            var form = RequireOpen(formId);
            form.EnterActiveFromPaused();
        }

        /// <summary>查询打开状态（IsOpen = Active/Covered/Paused——对 Lua 语义"界面上没关"）。</summary>
        public bool IsOpen(int formId)
            => _forms.TryGetValue(formId, out var f)
               && f.State is UIFormState.Active or UIFormState.Covered or UIFormState.Paused;

        // ---- 内部 ----

        private UILayerGroup GetGroup(int layer)
        {
            if (layer < 0 || layer >= _groups.Length)
                throw new ArgumentOutOfRangeException(nameof(layer),
                    $"层级越界:{layer}（灰盒三层 0..{_groups.Length - 1}，核对 tbuiform.layer 列）");
            return _groups[layer];
        }

        private UIForm RequireOpen(int formId)
        {
            if (!_forms.TryGetValue(formId, out var form) || !IsOpen(formId))
                throw new InvalidOperationException($"UIForm[{formId}] 未打开（当前:{(form?.State.ToString() ?? "无")})");
            return form;
        }

        private void Reuse(UIForm form, UILayerGroup group, IUIData data)
        {
            form.EnterActiveFromRecycled(data);
            group.Stack.Push(form);
            if (form.Info.FullScreen) RecomputeCovering();
            Log.Info($"UIForm[{form.Id}] 复用打开（{group.Name}@{form.Canvas.sortingOrder}）", "UI");
        }

        /// <summary>遮盖重算（幂等）：以"仍开着的最高全屏界面"所在组为界，**严格更低**的组全遮、其余照常；
        /// 逐界面按目标态做最小迁移（Active↔Covered 配对回调不重复触发）。全屏界面不得遮盖自身所在组。</summary>
        private void RecomputeCovering()
        {
            int coverThreshold = -1;                    // 严格低于该深度的组全遮
            foreach (var g in _groups)
                foreach (var f in g.Stack.Open)
                    if (f.State == UIFormState.Active && f.Info.FullScreen)
                        coverThreshold = Math.Max(coverThreshold, g.BaseDepth);

            for (int i = _groups.Length - 1; i >= 0; i--)
            {
                var g = _groups[i];
                bool shouldCover = g.BaseDepth < coverThreshold;
                foreach (var f in g.Stack.Open)
                {
                    if (shouldCover && f.State == UIFormState.Active) f.EnterCovered();
                    else if (!shouldCover && f.State == UIFormState.Covered) f.EnterActiveFromCovered();
                }
            }
        }

        private IUIFormLogic ResolveLogic(UIFormInfo info)
        {
            // 逻辑解析：resolver（GameEntry 装配 LuaBehaviourAdapter ← UI 注册表）→ 失败降级 NullLogic
            // （错误语义"注册失败"，§3.4——启动期已知键由 RegistryFiller 校验兜底，此处为运行期降级）
            if (_logicResolver == null) return NullUIFormLogic.Instance;
            try
            {
                var logic = _logicResolver(info);
                return logic ?? NullUIFormLogic.Instance;
            }
            catch (Exception ex)
            {
                Log.Error($"界面逻辑解析失败[{info.LuaPath}]:{ex.Message}", "UI");
                return NullUIFormLogic.Instance;
            }
        }

        /// <summary>转场容错等待：转策略抛异常只记日志（表现层故障不阻塞七态迁移——原则"动效不携带判定"）。</summary>
        private async UniTask PlayTransitionShow(UIForm form, CancellationToken ct)
        {
            try { await _transition.PlayShow(form); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Error($"入场转场失败[{form.Id}]:{ex.Message}", "UI");
            }
        }

        private async UniTask PlayTransitionClose(UIForm form, CancellationToken ct)
        {
            try { await _transition.PlayClose(form); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Log.Error($"离场转场失败[{form.Id}]:{ex.Message}", "UI");
            }
        }

        // ---- ITickable / IModuleStats ----

        public void Tick(float deltaTime)
        {
            foreach (var form in _forms.Values)
                if (form.State == UIFormState.Active) form.RaiseUpdate(deltaTime);
        }

        public string StatsName => "UI";

        public void Snapshot(Dictionary<string, string> into)
        {
            int active = 0, covered = 0, paused = 0, recycled = 0, loading = 0;
            foreach (var form in _forms.Values)
            {
                switch (form.State)
                {
                    case UIFormState.Active: active++; break;
                    case UIFormState.Covered: covered++; break;
                    case UIFormState.Paused: paused++; break;
                    case UIFormState.Recycled: recycled++; break;
                    case UIFormState.Loading: loading++; break;
                }
            }
            into["打开"] = active.ToString();
            into["遮盖"] = covered.ToString();
            into["暂停"] = paused.ToString();
            into["池中"] = recycled.ToString();
            into["加载中"] = loading.ToString();
        }
    }
}
