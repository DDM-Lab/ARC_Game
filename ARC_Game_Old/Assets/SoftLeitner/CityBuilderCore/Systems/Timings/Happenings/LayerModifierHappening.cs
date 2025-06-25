using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// an event that, during its activity, globally modifies a layers values<br/>
    /// might be used for weather or seasons(heat, fertility, ...)
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/timings">https://citybuilder.softleitner.com/manual/timings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_layer_modifier_happening.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Happenings/" + nameof(LayerModifierHappening))]
    public class LayerModifierHappening : TimingHappening, ILayerModifier
    {
        [Tooltip("the layer that will be modified during the happenings activity")]
        public Layer Layer;
        [Tooltip("layer value will first be multiplied with this value")]
        public float Multiplier = 1f;
        [Tooltip("after multiplication this value is added to the layer value")]
        public int Addend;

        Layer ILayerModifier.Layer => Layer;
        string ILayerModifier.Name => Name;

#pragma warning disable 0067
        public event Action<ILayerModifier> Changed;
#pragma warning restore 0067

        public override void Activate()
        {
            base.Activate();

            Dependencies.Get<ILayerManager>().Register(this);
        }

        public override void Deactivate()
        {
            base.Deactivate();

            Dependencies.Get<ILayerManager>().Deregister(this);
        }

        public int Modify(int value)
        {
            return Mathf.RoundToInt(value * Multiplier) + Addend;
        }
    }
}
