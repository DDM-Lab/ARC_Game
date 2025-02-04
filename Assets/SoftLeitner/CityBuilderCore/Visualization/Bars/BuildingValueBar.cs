using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// base class for in game visuals of building values
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_building_value_bar.html")]
    public abstract class BuildingValueBar : MonoBehaviour
    {
        public IBuilding Building => _building;
        public virtual bool IsGlobal => false;

        protected IBuilding _building;
        protected IBuildingValue _value;

        public virtual void Initialize(IBuilding building, IBuildingValue value)
        {
            _building = building;
            _value = value;
        }

        public bool HasValue() => _value.HasValue(_building);
        public float GetMaximum() => _value.GetMaximum(_building);
        public float GetValue() => _value.GetValue(_building);
        public Vector3 GetPosition() => _value.GetPosition(_building);
        public float GetRatio() => GetValue() / GetMaximum();
    }
}