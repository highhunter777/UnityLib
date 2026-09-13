using System;
using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>星级评分（M4c）：N 颗星（Image 子节点，顺序即星级），整星粒度；onChange 回调。</summary>
    public class StarRating : MonoBehaviour
    {
        public Image[] Stars;
        public Color OnColor = new Color(1f, 0.8f, 0.2f);
        public Color OffColor = new Color(0.3f, 0.3f, 0.3f);

        private int _value;
        public int Value => _value;
        public event Action<int> OnChanged;

        /// <summary>设置星级（0~Stars.Length，超界钳制；静默同值）。</summary>
        public void Set(int value)
        {
            value = Mathf.Clamp(value, 0, Stars.Length);
            if (_value == value) return;
            _value = value;
            Apply();
            OnChanged?.Invoke(value);
        }

        private void Apply()
        {
            for (int i = 0; i < Stars.Length; i++)
                if (Stars[i] != null) Stars[i].color = i < _value ? OnColor : OffColor;
        }

        private void Awake() => Apply();
    }

    /// <summary>数值滚动文本（M4c）：CountUp 原语驱动，format 自定义（金币/伤害数字）。</summary>
    public class CountText : MonoBehaviour
    {
        public Text Label;
        public float Duration = 0.5f;

        /// <summary>从当前值滚动到目标值（首调从 0 起）。</summary>
        public void Roll(float target, string format = "N0")
        {
            if (Label == null) return;
            float from = float.TryParse(Label.text, out var cur) ? cur : 0f;
            UiFx.CountUp(Label, from, target, Duration, v => v.ToString(format));
        }

        /// <summary>直接定格（不走滚动）。</summary>
        public void Set(float value, string format = "N0") { if (Label != null) Label.text = value.ToString(format); }
    }

    /// <summary>倒计时（M4c）：unscaled 时钟驱动（UI 时层），OnDone 一次性回调。格式默认 mm:ss。
    /// Awake 惰性初始化 OnDone——运行时 AddComponent 不走反序列化，UnityEvent 字段为 null。</summary>
    public class Countdown : MonoBehaviour
    {
        public Text Label;
        public UnityEngine.Events.UnityEvent OnDone;

        private float _remaining;
        private bool _running;
        public bool Running => _running;

        protected virtual void Awake()
        {
            if (OnDone == null) OnDone = new UnityEngine.Events.UnityEvent();
        }

        public void StartCountdown(float seconds)
        {
            _remaining = seconds;
            _running = seconds > 0f;
            Render();
        }

        public void Stop() => _running = false;

        /// <summary>推进（Update 每帧喂 unscaled dt；公开供自测确定性推进）。</summary>
        public void Tick(float deltaTime)
        {
            if (!_running) return;
            _remaining -= deltaTime;
            if (_remaining <= 0f)
            {
                _remaining = 0f;
                _running = false;
                Render();
                OnDone?.Invoke();
                return;
            }
            Render();
        }

        private void Update() => Tick(Time.unscaledDeltaTime);

        private void Render()
        {
            if (Label == null) return;
            var t = TimeSpan.FromSeconds(Math.Max(0, _remaining));
            Label.text = $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";
        }
    }

    /// <summary>帧序列动画（M4c）：Sprite 数组按 fps 循环/单次播放（灰盒动图——序列帧 UI 元素）。</summary>
    public class AnimatedImage : MonoBehaviour
    {
        public Sprite[] Frames;
        public float Fps = 10f;
        public bool Loop = true;
        public bool PlayOnEnable = true;

        private Image _image;
        private int _frame;
        private float _timer;
        private bool _playing;

        public void Play() { _frame = 0; _timer = 0f; _playing = true; }
        public void Stop() => _playing = false;

        private void OnEnable() { _image = GetComponent<Image>(); if (PlayOnEnable) Play(); }

        private void Update()
        {
            if (!_playing || _image == null || Frames == null || Frames.Length == 0) return;
            _timer += Time.unscaledDeltaTime;
            float step = 1f / Mathf.Max(1f, Fps);
            while (_timer >= step)
            {
                _timer -= step;
                _frame++;
                if (_frame >= Frames.Length)
                {
                    if (!Loop) { _frame = Frames.Length - 1; _playing = false; break; }
                    _frame = 0;
                }
            }
            _image.sprite = Frames[_frame];
        }
    }

    /// <summary>头像框（M4c）：头像 + 框 + 等级角标三件套的组装件（Set 一口喂）。</summary>
    public class AvatarFrame : MonoBehaviour
    {
        public Image Avatar;
        public Image Frame;
        public Text LevelBadge;

        public void Set(Sprite avatar, Sprite frame, string level)
        {
            if (Avatar != null) Avatar.sprite = avatar;
            if (Frame != null) Frame.sprite = frame;
            if (LevelBadge != null) LevelBadge.text = level ?? string.Empty;
        }
    }
}
