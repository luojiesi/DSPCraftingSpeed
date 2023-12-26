using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine.UI;
using UnityEngine;

// Modified from https://github.com/soarqin/DSP_Mods/blob/master/UXAssist/UI/MySlider.cs

namespace CraftingSpeed
{
    public class MySlider : MonoBehaviour
    {
        public Slider slider;
        public RectTransform rectTrans;
        public Text labelText;
        public string labelFormat;
        public event Action OnValueChanged;
        private float _value;
        public float Value
        {
            get => _value;
            set
            {
                _value = value;
                OnValueSet();
            }
        }

        public static MySlider CreateSlider(RectTransform parent)
        {
            var optionWindow = UIRoot.instance.optionWindow;
            var src = optionWindow.audioVolumeComp;

            var go = Instantiate(src.gameObject);
            go.name = "assemblerSpeedMultiplierSlider";
            go.SetActive(true);
            var sl = go.AddComponent<MySlider>();
            sl._value = 100;
            RectTransform rect = (RectTransform)sl.transform;
            rect.SetParent(parent, false);
            rect.anchoredPosition = new Vector3(300, 120);

            rect.sizeDelta = new Vector2(150, 20);

            sl.slider = go.GetComponent<Slider>();
            sl.slider.minValue = 1;
            sl.slider.maxValue = 100;
            sl.slider.value = 100;
            sl.slider.wholeNumbers = true;
            sl.slider.onValueChanged.RemoveAllListeners();
            sl.slider.onValueChanged.AddListener(sl.SliderChanged);
            sl.labelText = sl.slider.handleRect.Find("Text")?.GetComponent<Text>();
            
            if (sl.labelText != null)
            {
                sl.labelText.fontSize = 14;
                if (sl.labelText.transform is RectTransform rectTrans)
                {
                    rectTrans.sizeDelta = new Vector2(22f, 22f);
                }
            }
            sl.labelFormat = "G";

            var bg = sl.slider.transform.Find("Background")?.GetComponent<Image>();
            if (bg != null)
            {
                bg.color = new Color(0.5f, 0.5f, 0.5f, 0.5f);
            }
            var fill = sl.slider.fillRect.GetComponent<Image>();
            if (fill != null)
            {
                fill.color = new Color(1f, 1f, 1f, 0.28f);
            }
            sl.OnValueSet();
            sl.UpdateLabel();

            return sl;
        }
        public void OnValueSet()
        {
            lock (this)
            {
                var sliderVal = _value;
                if (sliderVal.Equals(slider.value)) return;
                if (sliderVal > slider.maxValue)
                {
                    _value = sliderVal = slider.maxValue;
                }
                else if (sliderVal < slider.minValue)
                {
                    _value = sliderVal = slider.minValue;
                }

                slider.value = sliderVal;
                UpdateLabel();
            }
        }
        public void UpdateLabel()
        {
            if (labelText != null)
            {
                labelText.text = _value.ToString(labelFormat);
            }
        }

        public void SetLabelText(string text)
        {
            if (labelText != null)
            {
                labelText.text = text;
            }
        }

        public void SliderChanged(float val)
        {
            lock (this)
            {
                var newVal = Mathf.Round(slider.value);
                if (_value.Equals(newVal)) return;
                _value = newVal;
                UpdateLabel();
                OnValueChanged?.Invoke();
            }
        }
    }
}
