using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// derives its building efficiency from the layer value at building origin(for example to influence farming speed from fertility layer)<br/>
    /// only changes the efficiency factor and does not completely disrupt it
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/layers">https://citybuilder.softleitner.com/manual/layers</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_layer_efficiency_component.html")]
    public class LayerEfficiencyComponent : BuildingComponent, IEfficiencyFactor
    {
        public override string Key => "LEF";

        [Tooltip("the layer that affects the buildings efficiency")]
        public Layer Layer;
        [Tooltip("the minimum efficiency returned so the building does not stall even in a bad position")]
        public float MinValue = 0;
        [Tooltip("the layer value needed to reach max efficiency")]
        public int MaxLayerValue = 10;
        [Tooltip("whether the efficiency gets clamped to 1")]
        public bool MaxClamped = false;

        public bool IsWorking => true;
        public float Factor
        {
            get
            {
                var val = Dependencies.Get<ILayerManager>().GetValue(Building.Point, Layer) / (float)MaxLayerValue;

                if (MaxClamped)
                    val = Mathf.Min(1f, val);

                val = Mathf.Max(MinValue, val);

                return val;
            }
        }
    }
}