using System;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using LiteFramework;
using UnityEngine;

namespace LiteGame
{
    /// <summary>层级策略（M4 §2.2）：组内 sortingOrder 分配规则。默认 = 组基序 + 递增槽位。</summary>
    public interface ILayerStrategy
    {
        int ResolveSortingOrder(UILayerGroup group, int slot);
    }

    /// <summary>转场策略（M4 §2.2，动效设计方案 附 A.2）：入场/离场动效的表现位。
    /// 实现纪律（附 A 原语库）：SetUpdate(true) 走 UIClock 轨；SetLink(KillOnDisable) 防泄漏；
    /// 动效永不携带判定——播完与否不影响七态迁移（UIService 侧容错等待）。</summary>
    public interface ITransitionStrategy
    {
        UniTask PlayShow(UIForm form);
        UniTask PlayClose(UIForm form);
    }

    /// <summary>出栈拦截（M4 §2.2）：Close 的统一闸口——返回键/程序关闭都经此处，可否决。</summary>
    public interface IPopInterceptor
    {
        bool CanClose(UIForm form);
    }

    /// <summary>默认层级策略：组基序 + 槽位（与 2.1 灰盒行为一致）。</summary>
    public sealed class DefaultLayerStrategy : ILayerStrategy
    {
        public int ResolveSortingOrder(UILayerGroup group, int slot) => group.BaseDepth + slot;
    }

    /// <summary>
    /// 默认转场：淡入淡出 + 轻位移（灰盒版，动效方案附 A.2 形态）。
    /// 本工程 DOTween 为核心 DLL 导入（无 UI Modules）——CanvasGroup 透明度/锚点位移走 DOTween.To 泛型。
    /// 离场先关交互（blocksRaycasts=false）防连点；tween 随界面禁用自动销毁（KillOnDisable）。
    /// </summary>
    public sealed class FadeSlideTransition : ITransitionStrategy
    {
        public UniTask PlayShow(UIForm form)
        {
            var cg = form.CanvasGroup;
            cg.blocksRaycasts = false;
            var seq = BuildBase(form);
            seq.Join(DOTween.To(() => cg.alpha, v => cg.alpha = v, 1f, 0.25f).From(0f));
            var rt = form.Root.transform as RectTransform;
            if (rt != null)
            {
                var end = rt.anchoredPosition;
                rt.anchoredPosition = end + new Vector2(0f, 40f);
                seq.Join(DOTween.To(() => rt.anchoredPosition, v => rt.anchoredPosition = v, end, 0.25f)
                    .SetEase(Ease.OutQuad));
            }
            seq.OnComplete(() => cg.blocksRaycasts = true);
            return ToTask(seq);
        }

        public UniTask PlayClose(UIForm form)
        {
            var cg = form.CanvasGroup;
            cg.blocksRaycasts = false;
            var seq = BuildBase(form);
            seq.Join(DOTween.To(() => cg.alpha, v => cg.alpha = v, 0f, 0.2f).SetEase(Ease.InQuad));
            return ToTask(seq);
        }

        private static Sequence BuildBase(UIForm form)
        {
            var seq = DOTween.Sequence();
            seq.SetUpdate(true);                                            // UIClock 轨：时停不停
            seq.SetLink(form.Root, LinkBehaviour.KillOnDisable);            // 池化回收/隐藏即杀，防泄漏
            return seq;
        }

        /// <summary>序列完成/被杀都放行（不依赖 DOTween UniTask 模块，UniTaskCompletionSource 直连）。</summary>
        private static UniTask ToTask(Sequence seq)
        {
            var tcs = new UniTaskCompletionSource();
            seq.OnComplete(() => tcs.TrySetResult());
            seq.OnKill(() => tcs.TrySetResult());
            return tcs.Task;
        }
    }

    /// <summary>默认出栈拦截：全放行（灰盒；具体界面的"未保存拦截"由业务替换策略实现）。</summary>
    public sealed class DefaultPopInterceptor : IPopInterceptor
    {
        public bool CanClose(UIForm form) => true;
    }
}
