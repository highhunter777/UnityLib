using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LiteGame.UI
{
    /// <summary>输入框封装（M4 §2.5/控件库）：UGUI InputField + 校验 hook（返回 false = 拒绝本次提交）。</summary>
    public class UIInputFieldWrap : MonoBehaviour
    {
        public InputField Field;
        [Tooltip("提交校验 hook；false = 拒绝并还原")]
        public Func<string, bool> Validator;
        public event Action<string> OnSubmitted;
        public event Action<string> OnValueChanged;

        private string _lastValid;

        private void Awake()
        {
            if (Field == null) Field = GetComponent<InputField>();
            if (Field == null) return;
            Field.onValueChanged.AddListener(v => OnValueChanged?.Invoke(v));
            Field.onEndEdit.AddListener(v =>
            {
                if (Validator != null && !Validator(v))
                {
                    Field.text = _lastValid ?? string.Empty;     // 拒绝并还原
                    return;
                }
                _lastValid = v;
                OnSubmitted?.Invoke(v);
            });
        }

        public string Value => Field != null ? Field.text : string.Empty;

        public void Set(string value, bool notify = false)
        {
            if (Field == null) return;
            _lastValid = value;
            if (notify) Field.text = value;
            else { Field.text = value; OnValueChanged?.Invoke(value); }
        }
    }

    /// <summary>滑条封装：Set（免回调）与 onChanged 分离。</summary>
    public class UISliderWrap : MonoBehaviour
    {
        public Slider Slider;
        public event Action<float> OnChanged;

        private void Awake()
        {
            if (Slider == null) Slider = GetComponent<Slider>();
            if (Slider != null) Slider.onValueChanged.AddListener(v => OnChanged?.Invoke(v));
        }

        public void Set(float value) { if (Slider != null) Slider.SetValueWithoutNotify(value); }
        public float Value => Slider != null ? Slider.value : 0f;
    }

    /// <summary>开关封装。</summary>
    public class UIToggleWrap : MonoBehaviour
    {
        public Toggle Toggle;
        public event Action<bool> OnChanged;

        private void Awake()
        {
            if (Toggle == null) Toggle = GetComponent<Toggle>();
            if (Toggle != null) Toggle.onValueChanged.AddListener(v => OnChanged?.Invoke(v));
        }

        public void Set(bool value) { if (Toggle != null) Toggle.SetIsOnWithoutNotify(value); }
        public bool Value => Toggle != null ? Toggle.isOn : false;
    }

    /// <summary>下拉选择封装：SetOptions 重建选项；onIndexChanged 出索引。</summary>
    public class UIDropdownWrap : MonoBehaviour
    {
        public Dropdown Dropdown;
        public event Action<int> OnIndexChanged;

        private void Awake()
        {
            if (Dropdown == null) Dropdown = GetComponent<Dropdown>();
            if (Dropdown != null) Dropdown.onValueChanged.AddListener(i => OnIndexChanged?.Invoke(i));
        }

        public void SetOptions(IReadOnlyList<string> options, int selected = 0)
        {
            if (Dropdown == null) return;
            Dropdown.ClearOptions();
            Dropdown.AddOptions(new List<string>(options));
            Dropdown.SetValueWithoutNotify(Mathf.Clamp(selected, 0, options.Count - 1));
        }

        public int Selected => Dropdown != null ? Dropdown.value : 0;
    }

    /// <summary>步进器（M4c）：-/值/+，min/max/step 钳制；onChanged 出整数值。</summary>
    public class Stepper : MonoBehaviour
    {
        public Button Minus;
        public Button Plus;
        public Text ValueLabel;
        public int Min;
        public int Max = 10;
        public int Step = 1;

        private int _value;
        public int Value => _value;
        public event Action<int> OnChanged;

        private void Awake()
        {
            if (Minus != null) Minus.onClick.AddListener(() => Set(_value - Step));
            if (Plus != null) Plus.onClick.AddListener(() => Set(_value + Step));
            Render();
        }

        public void Set(int value)
        {
            value = Mathf.Clamp(value, Min, Max);
            if (_value == value) return;
            _value = value;
            Render();
            OnChanged?.Invoke(value);
        }

        private void Render()
        {
            if (ValueLabel != null) ValueLabel.text = _value.ToString();
            if (Minus != null) Minus.interactable = _value > Min;
            if (Plus != null) Plus.interactable = _value < Max;
        }
    }

    /// <summary>数值事件转发辅助（灰盒：UIEventTrigger 位——Pointer 点击转发为 UnityEvent）。</summary>
    public class UIEventRelay : MonoBehaviour, IPointerClickHandler
    {
        public UnityEvent OnClicked;
        public void OnPointerClick(PointerEventData e) => OnClicked?.Invoke();
    }
}
