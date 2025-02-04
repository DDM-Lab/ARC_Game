using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// visualizes a building value by scaling a transform along Y<br/>
    /// used for the risk and service bars in THREE
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_building_scaled_bar.html")]
    public class BuildingScaledBar : BuildingValueBar
    {
        [Tooltip("the transform that wil have its Y-Axis scaled between its original height and 0")]
        public Transform ScaleTransform;
        public MeshRenderer BarRenderer;

        public Material BarMaterial
        {
            get
            {
                return BarRenderer.material;
            }
            set
            {
                BarRenderer.material = value;
            }
        }

        private float _fullHeight;
        private Vector3 _scale;

        private void Start()
        {
            _fullHeight = ScaleTransform.localScale.y;
            _scale = ScaleTransform.localScale;

            setBar();
        }

        private void Update()
        {
            setBar();
        }

        private void setBar()
        {
            _scale.y = GetRatio() * _fullHeight;
            ScaleTransform.localScale = _scale;
        }
    }
}