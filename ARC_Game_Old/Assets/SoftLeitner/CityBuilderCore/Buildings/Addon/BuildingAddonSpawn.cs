using System.Collections;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// addon that visualy spawns in the building by scaling it up and rotating it using a coroutine(no animation needed)
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/buildings">https://citybuilder.softleitner.com/manual/buildings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_building_addon_spawn.html")]
    public class BuildingAddonSpawn : BuildingAddon
    {
        [Tooltip("how long to spend rotating and scaling up to full size")]
        public float Duration = 0.2f;

        private Vector3 _pivotScale;
        private Quaternion _pivotRotation;

        public override void InitializeAddon()
        {
            base.InitializeAddon();

            _pivotScale = Building.Pivot.localScale;
            _pivotRotation = Building.Pivot.localRotation;

            StartCoroutine(spawn());
        }

        public override void TerminateAddon()
        {
            base.TerminateAddon();

            Building.Pivot.localScale = _pivotScale;
            Building.Pivot.localRotation = _pivotRotation;
        }

        private IEnumerator spawn()
        {
            var isXY = Dependencies.GetOptional<IMap>()?.IsXY ?? false;

            var t = 0f;
            while (t < Duration)
            {
                Building.Pivot.localScale = Vector3.Lerp(Vector3.zero, _pivotScale, t / Duration);
                Building.Pivot.localEulerAngles = isXY ? new Vector3(0, 0, 180f + 180f * t / Duration) : new Vector3(0, 180f + 180f * t / Duration, 0);
                t += Time.deltaTime;
                yield return null;
            }

            Building.Pivot.localScale = _pivotScale;
            Building.Pivot.localRotation = _pivotRotation;

            Remove();
        }
    }
}