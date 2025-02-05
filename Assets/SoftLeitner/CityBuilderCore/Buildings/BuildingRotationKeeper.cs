using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// optional component that keeps rotation between different builders
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/buildings">https://citybuilder.softleitner.com/manual/buildings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_building_rotation_keeper.html")]
    public class BuildingRotationKeeper : MonoBehaviour
    {
        [Tooltip("initial rotation used to create a building roation that is then shared between builders")]
        public int InitialRotation;

        public BuildingRotation Rotation { get; set; }

        private void Awake()
        {
            Dependencies.Register(this);
        }

        private void Start()
        {
            Rotation = BuildingRotation.Create(InitialRotation);
        }
    }
}
