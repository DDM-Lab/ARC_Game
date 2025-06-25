using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// utility components that provides a helper method that lets users switch between two materials
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_material_switcher.html")]
    public class MaterialSwitcher : MonoBehaviour
    {
        [Tooltip("material that is set when the SetMaterial helper method is called with TRUE")]
        public Material MaterialA;
        [Tooltip("material that is set when the SetMaterial helper method is called with FALSE")]
        public Material MaterialB;
        [Tooltip("renderer that gets its material set when the SetMaterial helper method is called")]
        public MeshRenderer Target;
        [Tooltip("index of the material in the renderer, leave at 0 when there is only one")]
        public int TargetIndex;

        public void SetMaterial(bool value)
        {
            var materials = Target.sharedMaterials;
            materials[TargetIndex]= value ? MaterialA : MaterialB;
            Target.sharedMaterials = materials;
        }
    }
}
