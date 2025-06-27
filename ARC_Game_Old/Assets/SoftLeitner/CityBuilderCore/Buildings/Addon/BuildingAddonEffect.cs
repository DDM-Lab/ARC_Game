using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// blank building addon that can be used to attach particle effects for example<br/>
    /// removal is either done when the building is replaced(Evolution went through) or from the outside(evolution canceled)
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/buildings">https://citybuilder.softleitner.com/manual/buildings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_building_addon_effect.html")]
    public class BuildingAddonEffect : BuildingAddon
    {
        [Tooltip("addon will not carry over when its building is replaced")]
        public bool RemoveOnReplace;
        [Tooltip("can be used to let the addon rotate")]
        public Vector3 Rotation;

        public override void Update()
        {
            base.Update();

            if (Rotation != Vector3.zero)
                transform.Rotate(Rotation * Time.unscaledDeltaTime);
        }

        public override void OnReplacing(Transform parent, IBuilding replacement)
        {
            if (!RemoveOnReplace)
                base.OnReplacing(parent, replacement);
        }
    }
}