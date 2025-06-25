using System;
using UnityEngine.Audio;
using UnityEngine;
using UnityEngine.UI;

namespace CityBuilderCore
{
    /// <summary>
    /// allows changing a value of an audiomixer using a slider in the UI, value can also be persisted into player prefs
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_audio_slider.html")]
    public class AudioSlider : MonoBehaviour
    {
        [Tooltip("the slider that is used to set the audio value")]
        public Slider Slider;
        [Tooltip("the mixer the value is set on")]
        public AudioMixer Mixer;
        [Tooltip("name of the value to set on the mixer(exposed volume values)")]
        public string AudioKey;
        [Tooltip("key used to persist the value in PlayerPrefs(optional)")]
        public string PlayerPrefKey;
        [Tooltip("minimum decibel value of the slider")]
        public float MinimumDB = -80;
        [Tooltip("maximum decibel value of the slider")]
        public float MaximumDB = 3;
        [Tooltip("additionally scale the slider along a nonlinear curve to give more precision in the middle to top range.")]
        [Range(0.1f, 1f)]
        public float Linearity = 0.5f;

        private void OnEnable()
        {
            Slider.onValueChanged.AddListener(OnValueChanged);
        }

        private void OnDisable()
        {
            Slider.onValueChanged.RemoveListener(OnValueChanged);
        }

        private void Start()
        {
            float value;
            if (string.IsNullOrEmpty(PlayerPrefKey) || !PlayerPrefs.HasKey(PlayerPrefKey))
                value = ReadInitialValueFromMixer();
            else
                value = PlayerPrefs.GetFloat(PlayerPrefKey);

            OnValueChanged(value);
            Slider.value = value;
        }

        private float ReadInitialValueFromMixer()
        {
            Mixer.GetFloat(AudioKey, out var decibels);
            var normalized = Mathf.Clamp01(remap(MinimumDB, MaximumDB, 0, 1, decibels));
            var nonlinear = Mathf.Pow(normalized, 1f / Linearity);
            var exponential = Mathf.Pow(10f, nonlinear);
            var remapped = remap(1, 10, 0, 1, exponential);
            return Mathf.Clamp01(remapped);
        }

        private void OnValueChanged(float sliderValue)
        {
            var remapped = remap(0, 1, 1, 10, Slider.value);
            var logarithmic = Mathf.Clamp01(Mathf.Log10(remapped));
            var nonlinear = Mathf.Pow(logarithmic, Linearity);
            var decibels = remap(0f, 1f, MinimumDB, MaximumDB, nonlinear);
            Mixer.SetFloat(AudioKey, decibels);

            if (!string.IsNullOrWhiteSpace(PlayerPrefKey))
                PlayerPrefs.SetFloat(PlayerPrefKey, Slider.value);
        }

        private void OnValidate()
        {
            if (Slider)
            {
                Slider.minValue = 0;
                Slider.maxValue = 1;
            }
        }

        private float remap(float srcStart, float srcEnd, float dstStart, float dstEnd, float x) => Mathf.Lerp(dstStart, dstEnd, unlerp(srcStart, srcEnd, x));
        public static float unlerp(float start, float end, float x) { return (x - start) / (end - start); }
    }
}
