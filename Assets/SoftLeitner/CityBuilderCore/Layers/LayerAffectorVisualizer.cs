using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// used by <see cref="LayerKeyVisualizer"/> to display the value for an affector
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/layers">https://citybuilder.softleitner.com/manual/layers</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_layer_affector_visualizer.html")]
    public class LayerAffectorVisualizer : MonoBehaviour
    {
        [Tooltip("name of the affector will be displayed here")]
        public TMPro.TMP_Text NameText;
        [Tooltip("the value of the affector will be displayed here")]
        public TMPro.TMP_Text ValueText;
    }
}
