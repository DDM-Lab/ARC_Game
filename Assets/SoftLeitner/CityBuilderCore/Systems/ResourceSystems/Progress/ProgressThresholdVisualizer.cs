using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// visualizes a <see cref="ProgressComponent"/> by checking against thresholds and showing and hiding the linked objects
    /// for example in the historic demo farms use this to show progressively more grown fields
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/resources">https://citybuilder.softleitner.com/manual/resources</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_progress_threshold_visualizer.html")]
    public class ProgressThresholdVisualizer : MonoBehaviour
    {
        [Tooltip("the building component that provides the progress ratio")]
        public ProgressComponent Component;
        [Tooltip("true > show only highest cleared threshold | false > show all cleared thresholds")]
        public bool Swap;
        [Tooltip("the tresholds(for example 0.2 > sappling 0.5 > small tree 0.8 > tree 1 > tree with apples)")]
        public ProgressThreshold[] ProgressThresholds;

        private void Start()
        {
            Component.ProgressChanged += updateProgress;

            updateProgress(Component.Progress);
        }

        private void updateProgress(float progress)
        {
            if (Swap)
            {
                ProgressThresholds.ForEach(t => t.GameObject.SetActive(false));
                var activeThreshold = ProgressThresholds.Where(p => progress > p.Value).LastOrDefault();
                if (activeThreshold != null)
                    activeThreshold.GameObject.SetActive(true);
            }
            else
            {
                foreach (var threshold in ProgressThresholds)
                {
                    threshold.GameObject.SetActive(progress > threshold.Value);
                }
            }
        }
    }
}
