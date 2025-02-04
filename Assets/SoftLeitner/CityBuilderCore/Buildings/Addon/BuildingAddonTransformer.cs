using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// addon that transfers its scale and rotation to the attached building<br/>
    /// this can be used to attach animations to any building by animating the addon<br/><br/>
    /// in THREE this is used to make buildings pop up with an animation when they are built<br/>
    /// to make this happen a BuildingAddonTransform with an animation is assigned to <see cref="DefaultBuildingManager.AddingAddon"/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/buildings">https://citybuilder.softleitner.com/manual/buildings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_building_addon_transformer.html")]
    public class BuildingAddonTransformer : BuildingAddon
    {
        private Vector3 _pivotScale;
        private Quaternion _pivotRotation;

        public override void InitializeAddon()
        {
            base.InitializeAddon();

            _pivotScale = Building.Pivot.localScale;
            _pivotRotation = Building.Pivot.localRotation;
        }

        public override void TerminateAddon()
        {
            base.TerminateAddon();

            Building.Pivot.localScale = _pivotScale;
            Building.Pivot.localRotation = _pivotRotation;
        }

        public override void Update()
        {
            base.Update();

            if (_isTerminated)
                return;

            Building.Pivot.localScale = Vector3.Scale(_pivotScale, transform.localScale);
            Building.Pivot.localRotation = _pivotRotation * transform.localRotation;
        }
    }
}