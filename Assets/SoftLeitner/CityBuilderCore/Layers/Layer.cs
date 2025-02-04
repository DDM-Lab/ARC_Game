using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// layers are arrays of numbers underlying the map points(desirability, fertility, resources)
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/layers">https://citybuilder.softleitner.com/manual/layers</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_layer.html")]
    [CreateAssetMenu(menuName = "CityBuilder/" + nameof(Layer))]
    public class Layer : ScriptableObject, IBuildingValue, IWalkerValue
    {
        [Tooltip("display name")]
        public string Name;
        [Tooltip("when two affectors affect the same position cumulative layers sum them while others use the bigger value")]
        public bool IsCumulative = true;

        public bool HasValue(IBuilding building) => true;
        public float GetMaximum(IBuilding building) => 100;
        public float GetValue(IBuilding building) => Dependencies.Get<ILayerManager>().GetValue(building.Point, this);
        public Vector3 GetPosition(IBuilding building) => building.WorldCenter;

        public bool HasValue(Walker walker) => true;
        public float GetMaximum(Walker walker) => 100;
        public float GetValue(Walker walker) => Dependencies.Get<ILayerManager>().GetValue(walker.GridPoint, this);
        public Vector3 GetPosition(Walker walker) => walker.Pivot.position;
    }
}