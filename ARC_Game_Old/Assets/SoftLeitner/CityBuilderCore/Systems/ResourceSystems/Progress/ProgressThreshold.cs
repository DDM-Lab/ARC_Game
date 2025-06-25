using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// helper for <see cref="ProgressThresholdVisualizer"/> that combines a threshold value with the gameobject that threshold will activate
    /// </summary>
    [Serializable]
    public class ProgressThreshold
    {
        [Tooltip("minimum value for this threshold to be active")]
        public float Value;
        [Tooltip("the object that will activated when this threshold is the highest one met")]
        public GameObject GameObject;
    }
}
