using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    public class DisplayOptions : MonoBehaviour
    {
        public TMPro.TMP_Dropdown FullscreenDropdown;
        public TMPro.TMP_Dropdown ResolutionDropdown;
        public TMPro.TMP_Dropdown QualityDropdown;

        private void Start()
        {
            FullscreenDropdown.ClearOptions();
            FullscreenDropdown.AddOptions(new List<string>() { "Fullscreen", "Borderless", "Maximized", "Windowed" });
            FullscreenDropdown.value = (int)Screen.fullScreenMode;

            ResolutionDropdown.ClearOptions();
            ResolutionDropdown.AddOptions(Screen.resolutions.Select(r => r.ToString()).ToList());
            ResolutionDropdown.value = Array.IndexOf(Screen.resolutions, Screen.currentResolution);

            QualityDropdown.value = QualitySettings.GetQualityLevel();

            FullscreenDropdown.onValueChanged.AddListener(v => Screen.fullScreenMode = (FullScreenMode)v);
#if UNITY_2022_2_OR_NEWER
            ResolutionDropdown.onValueChanged.AddListener(v => Screen.SetResolution(Screen.resolutions[v].width, Screen.resolutions[v].height, Screen.fullScreenMode, Screen.resolutions[v].refreshRateRatio));
#else
            ResolutionDropdown.onValueChanged.AddListener(v => Screen.SetResolution(Screen.resolutions[v].width, Screen.resolutions[v].height, Screen.fullScreenMode, Screen.resolutions[v].refreshRate));
#endif
            QualityDropdown.onValueChanged.AddListener(v => QualitySettings.SetQualityLevel(v));
        }
    }
}
