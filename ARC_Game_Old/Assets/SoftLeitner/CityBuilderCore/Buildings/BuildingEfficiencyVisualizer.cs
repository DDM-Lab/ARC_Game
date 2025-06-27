using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// priovides an event that fires when a building starts or stops working<br/>
    /// can be used to turn visuals on and off that show whether the building is working<br/>
    /// for example a glowing forge or smoke for a smith
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/buildings">https://citybuilder.softleitner.com/manual/buildings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_building_efficiency_visualizer.html")]
    [RequireComponent(typeof(IBuilding))]
    public class BuildingEfficiencyVisualizer : MonoBehaviour
    {
        [Tooltip("fires when the buildings isworking changes")]
        public BoolEvent IsWorkingChanged;

        private IBuilding _building;
        private bool _isWorking;

        private void Awake()
        {
            _building = GetComponent<IBuilding>();
        }

        private void Start()
        {
            _isWorking = _building.IsWorking;
            IsWorkingChanged?.Invoke(_isWorking);
        }

        private void Update()
        {
            if (_building.IsWorking == _isWorking)
                return;

            _isWorking = _building.IsWorking;
            IsWorkingChanged?.Invoke(_isWorking);
        }
    }
}
