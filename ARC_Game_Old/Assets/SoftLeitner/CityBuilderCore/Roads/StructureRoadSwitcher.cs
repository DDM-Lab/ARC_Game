using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// registers road switches at every point of the structure<br/>
    /// demonstrated in the SwitchRoadDebugging test scene
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/walkers">https://citybuilder.softleitner.com/manual/walkers</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_structure_road_switcher.html")]
    public class StructureRoadSwitcher : MonoBehaviour
    {
        public IStructure Structure { get; private set; }

        [Tooltip("one of the two roads that the switch will connect")]
        public Road RoadA;
        [Tooltip("one of the two roads that the switch will connect")]
        public Road RoadB;

        private void Start()
        {
            Structure = GetComponent<IStructure>() ?? GetComponentInParent<IStructure>();

            var roadManager = Dependencies.Get<IRoadManager>();

            foreach (var point in Structure.GetPoints())
            {
                roadManager.RegisterSwitch(point, RoadA, RoadB);
            }

            Structure.PointsChanged += structurePointsChanged;
        }

        private void structurePointsChanged(PointsChanged<IStructure> change)
        {
            var roadManager = Dependencies.Get<IRoadManager>();

            roadManager.Deregister(change.RemovedPoints, RoadA);
            roadManager.Deregister(change.RemovedPoints, RoadB);

            foreach (var point in change.AddedPoints)
            {
                roadManager.RegisterSwitch(point, RoadA, RoadB);
            }
        }

        private void OnDestroy()
        {
            if (gameObject.scene.isLoaded)
            {
                var roadManager = Dependencies.Get<IRoadManager>();
                var points = Structure.GetPoints();

                roadManager.Deregister(points, RoadA);
                roadManager.Deregister(points, RoadB);
            }
        }
    }
}
