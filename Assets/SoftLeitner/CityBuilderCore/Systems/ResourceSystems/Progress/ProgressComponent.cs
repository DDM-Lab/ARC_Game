using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// base class for building components implementing <see cref="IProgressComponent"/><br/>
    /// basically any component that progresses over time, does something and resets
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/resources">https://citybuilder.softleitner.com/manual/resources</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_progress_component.html")]
    public abstract class ProgressComponent : BuildingComponent, IProgressComponent
    {
        [Tooltip("time it takes to progress to completion when working with full efficiency")]
        public float ProgressInterval;

        [Tooltip("fired whenever the component starts or stops progressing")]
        public BoolEvent IsProgressing;

        public event Action ProgressReset;
        public event Action<float> ProgressChanged;

        public float Progress => _progressTime / ProgressInterval;

        protected float _progressTime;

        public override string GetDebugText() => (Progress * 100f).ToString("F0") + "%";

        public override void InitializeComponent()
        {
            base.InitializeComponent();

            IsProgressing?.Invoke(false);
        }

        protected bool addProgress(float multiplier)
        {
            _progressTime = Mathf.Min(ProgressInterval, _progressTime + Time.deltaTime * multiplier);
            ProgressChanged?.Invoke(Progress);
            return _progressTime >= ProgressInterval;
        }

        protected void resetProgress()
        {
            _progressTime = 0f;
            ProgressReset?.Invoke();
            ProgressChanged?.Invoke(Progress);
        }
    }
}