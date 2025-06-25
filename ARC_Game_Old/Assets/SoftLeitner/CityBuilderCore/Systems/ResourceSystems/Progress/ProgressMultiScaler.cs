using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// visualizes the progress of a <see cref="ProgressComponent"/> by scaling multiple transforms<br/>
    /// scales are all increased in parallel, can be made more visually interesting using <see cref="Variance"/><br/>
    /// (eg the plants at a farm)
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/resources">https://citybuilder.softleitner.com/manual/resources</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_progress_multi_scaler.html")]
    public class ProgressMultiScaler : MonoBehaviour
    {
        [Tooltip("the building component that provides the progress ratio")]
        public ProgressComponent Component;
        [Tooltip("the transforms that will be scaled depending on the components progress")]
        public Transform[] Transforms;
        [Tooltip("adds some random visual variety(V=1 means a random multiplier between 0.5 and 1.5)")]
        public float Variance = 0f;
        [Tooltip("scale when progress is 0")]
        public Vector3 From = Vector3.zero;
        [Tooltip("scale when progress is full")]
        public Vector3 To = Vector3.one;

        private float[] _variances;

        private void Start()
        {
            Component.ProgressReset += setVariances;
            Component.ProgressChanged += updateProgress;

            setVariances();
            updateProgress(Component.Progress);
        }

        private void setVariances()
        {
            _variances = new float[Transforms.Length];
            for (int i = 0; i < Transforms.Length; i++)
            {
                _variances[i] = Random.Range(1 - Variance / 2, 1 + Variance / 2);
            }
            updateProgress(0f);
        }

        private void updateProgress(float progress)
        {
            for (int i = 0; i < Transforms.Length; i++)
            {
                Transforms[i].localScale = Vector3.Lerp(From, To, progress) * _variances[i];
            }
        }
    }
}