using UnityEngine;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>进度条（M4c）：直线（Filled Horizontal）与环形（Filled Radial）共用本件——
    /// 形态由 Image 的 fillMethod/精灵决定，组件只驱动 fillAmount 与可选数值文本。</summary>
    public class ProgressBar : MonoBehaviour
    {
        public Image Fill;                               // Image type=Filled
        public Text ValueText;                           // 可选
        [Tooltip("数值文本格式；{0}=当前 {1}=上限。留空不显示数值")]
        public string ValueFormat = "{0}/{1}";

        private float _current, _max = 1f;

        /// <summary>按归一化值设置（0~1）。</summary>
        public void Set(float value01)
        {
            _current = value01;
            _max = 1f;
            Apply();
        }

        /// <summary>按当前/上限设置（HpBar/经验条等）。</summary>
        public void Set(float current, float max)
        {
            _current = current;
            _max = max > 0f ? max : 1f;
            Apply();
        }

        private void Apply()
        {
            float v = _current / _max;
            if (Fill != null) Fill.fillAmount = Mathf.Clamp01(v);
            if (ValueText != null && !string.IsNullOrEmpty(ValueFormat))
                ValueText.text = string.Format(ValueFormat, Mathf.RoundToInt(_current), Mathf.RoundToInt(_max));
        }
    }

    /// <summary>血条（M4c）：双条血格——前条瞬时响应，后条延迟滑落（打击感标准做法）。底层即两条 ProgressBar。</summary>
    public class HpBar : MonoBehaviour
    {
        public Image FrontFill;                          // 前条（瞬时）
        public Image BackFill;                           // 后条（延迟滑落）
        public Text ValueText;
        public float BackLerpSpeed = 2f;

        private float _current, _max = 1f;

        /// <summary>设置血量（0~max）。前条立即到位，后条逐帧追赶。</summary>
        public void Set(float current, float max)
        {
            _current = current;
            _max = max > 0f ? max : 1f;
            Apply();
        }

        private void Apply()
        {
            float v = Mathf.Clamp01(_current / _max);
            if (FrontFill != null) FrontFill.fillAmount = v;
            if (ValueText != null) ValueText.text = $"{Mathf.RoundToInt(_current)}/{Mathf.RoundToInt(_max)}";
        }

        private void Update()
        {
            float target = Mathf.Clamp01(_current / _max);
            if (BackFill != null && BackFill.fillAmount > target)
                BackFill.fillAmount = Mathf.Lerp(BackFill.fillAmount, target, Time.deltaTime * BackLerpSpeed);
        }
    }
}
