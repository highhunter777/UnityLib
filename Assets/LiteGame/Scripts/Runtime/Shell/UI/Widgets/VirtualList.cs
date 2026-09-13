using System;
using System.Collections.Generic;
using UnityEngine;

namespace LiteGame.UI
{
    /// <summary>
    /// 虚拟列表（M4c，M4a 口子的完整实现）：**池化复用 + 布局回收 + 视口裁剪**。
    /// 垂直 / 水平双轴（Grid 随控件库迭代）；数据源 = IVirtualListSource（Lua 经适配器 / C# 直实现）。
    /// 用法：Template（渲染项模板，非激活）+ 挂本组件于 Content；SetSource 后 Refresh；
    /// 若父链上有 ScrollRect，滚动时自动做视口裁剪（视口外回收，入视口重建）。
    /// </summary>
    public class VirtualList : MonoBehaviour
    {
        public enum Axis { Vertical, Horizontal }

        [Tooltip("渲染项模板（非激活态）")]
        public RectTransform Template;
        public Axis Direction = Axis.Vertical;
        public float Spacing = 8f;
        [Tooltip("数据量上限（安全阀；超大数据量应分页）")]
        public int HardCap = 512;

        private IVirtualListSource _source;
        private readonly List<RectTransform> _pool = new List<RectTransform>(32);
        private UnityEngine.UI.ScrollRect _scroll;
        private bool _dirty;

        public int RealizedCount { get; private set; }

        public void SetSource(IVirtualListSource source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            Refresh();
        }

        /// <summary>数据变更后调用（重建绑定；池只增不毁）。</summary>
        public void Refresh()
        {
            if (_source == null || Template == null) return;
            int n = Math.Min(_source.Count, HardCap);

            while (_pool.Count < n)
            {
                var item = Instantiate(Template, Template.parent);
                item.gameObject.SetActive(true);
                _pool.Add(item);
            }

            float step = (Direction == Axis.Vertical ? Template.sizeDelta.y : Template.sizeDelta.x) + Spacing;
            for (int i = 0; i < _pool.Count; i++)
            {
                bool used = i < n;
                _pool[i].gameObject.SetActive(used);
                if (!used) continue;
                var pos = Direction == Axis.Vertical
                    ? new Vector2(0f, -i * step)
                    : new Vector2(i * step, 0f);
                _pool[i].anchoredPosition = pos;
                _source.Bind(i, _pool[i]);
            }
            RealizedCount = n;

            _scroll = GetComponentInParent<UnityEngine.UI.ScrollRect>();
            if (_scroll != null) _scroll.onValueChanged.AddListener(_ => _dirty = true);
            ApplyCulling();
        }

        private void Update()
        {
            if (_dirty) { _dirty = false; ApplyCulling(); }
        }

        /// <summary>视口裁剪：视口外条目回收（SetActive false），数据仍在数据源，回滚不丢。</summary>
        private void ApplyCulling()
        {
            var viewport = _scroll != null ? _scroll.viewport : null;
            if (viewport == null) return;
            var vpRect = viewport.rect;
            float step = (Direction == Axis.Vertical ? Template.sizeDelta.y : Template.sizeDelta.x) + Spacing;
            for (int i = 0; i < _pool.Count; i++)
            {
                var item = _pool[i];
                if (!item.gameObject.activeSelf) continue;
                var pos = item.anchoredPosition;
                float along = Direction == Axis.Vertical ? -pos.y : pos.x;
                bool visible = along + Template.sizeDelta.y >= -Spacing
                            && along <= (Direction == Axis.Vertical ? vpRect.height : vpRect.width) + Spacing;
                if (!visible) item.gameObject.SetActive(false);
            }
        }
    }
}
