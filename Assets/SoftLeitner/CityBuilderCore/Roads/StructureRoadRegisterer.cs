using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// registers roads at every point of the structure on the same or the parent gameobject<br/>
    /// could be used to built roads out of some 3d strctures in addition or instead of tiles
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/walkers">https://citybuilder.softleitner.com/manual/walkers</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_structure_road_registerer.html")]
    public class StructureRoadRegisterer : MonoBehaviour
    {
        public IStructure Structure { get; private set; }

        [Tooltip("the road that will be registered at every point of the structure")]
        public Road Road;

        private void Start()
        {
            Structure = GetComponent<IStructure>() ?? GetComponentInParent<IStructure>();

            Dependencies.Get<IRoadManager>().Register(Structure.GetPoints(), Road);

            Structure.PointsChanged += structurePointsChanged;
        }

        private void structurePointsChanged(PointsChanged<IStructure> change)
        {
            var roadManager = Dependencies.Get<IRoadManager>();

            roadManager.Deregister(change.RemovedPoints, Road);
            roadManager.Register(change.AddedPoints, Road);
        }

        private void OnDestroy()
        {
            if (!gameObject.scene.isLoaded)
                return;
            if (Dependencies.GetOptional<IGameSaver>()?.IsLoading == true)
                return;

            Dependencies.Get<IRoadManager>().Deregister(Structure.GetPoints(), Road);
        }
    }
}
