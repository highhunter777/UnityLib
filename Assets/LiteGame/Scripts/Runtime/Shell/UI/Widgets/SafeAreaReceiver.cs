using UnityEngine;

namespace LiteGame
{
    /// <summary>SafeArea 策略（M4 §2.6，设计方案 §1.3 刘海屏红线）：解析避让矩形。默认 = Screen.safeArea 原样
    /// （桌面/编辑器 = 全屏；异形屏 = 真实安全区；横竖屏 / 分屏自动跟随）。</summary>
    public interface ISafeAreaStrategy
    {
        Rect Resolve();
    }

    public sealed class DefaultSafeAreaStrategy : ISafeAreaStrategy
    {
        public Rect Resolve() => Screen.safeArea;
    }

    /// <summary>
    /// SafeArea 接收器（M4 §2.6）：把自身 RectTransform 贴进安全区（归一化锚点法，任意分辨率 / DPI 通吃）。
    /// 挂在界面根 Canvas 下的"需要避让的容器"上——**不能挂 Canvas 根**（其 RectTransform 由 Canvas 驱动）。
    /// 无变化不写（防多余布局重建）；低频自检 + 尺寸变化回调双保险。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class SafeAreaReceiver : MonoBehaviour
    {
        private ISafeAreaStrategy _strategy = new DefaultSafeAreaStrategy();
        private RectTransform _rt;
        private float _nextCheck;
        private Vector2 _lastMin, _lastMax;
        private bool _dirty = true;

        /// <summary>替换策略（异形屏特殊规则由策略实现，本接收器只管应用）。</summary>
        public void SetStrategy(ISafeAreaStrategy strategy)
        {
            _strategy = strategy ?? new DefaultSafeAreaStrategy();
            _dirty = true;
            Apply();
        }

        private void OnEnable()
        {
            _rt = (RectTransform)transform;
            _dirty = true;
            Apply();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 0.5f;
            Apply();
        }

        private void OnRectTransformDimensionsChange() => _dirty = true;

        private void Apply()
        {
            if (_rt == null || !_dirty) return;
            var sa = _strategy.Resolve();
            if (Screen.width <= 0 || Screen.height <= 0) return;
            var min = new Vector2(sa.x / Screen.width, sa.y / Screen.height);
            var max = new Vector2((sa.x + sa.width) / Screen.width, (sa.y + sa.height) / Screen.height);
            if (min == _lastMin && max == _lastMax) { _dirty = false; return; }
            _lastMin = min;
            _lastMax = max;
            _rt.anchorMin = min;
            _rt.anchorMax = max;
            _rt.offsetMin = Vector2.zero;
            _rt.offsetMax = Vector2.zero;
            _dirty = false;
        }
    }
}
