using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// specialized version of <see cref="OffGridLink"/> for expandable buildings<br/>
    /// adjusts cost and distance according to the linear expansion of the building
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/buildings">https://citybuilder.softleitner.com/manual/buildings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_expandable_off_grid_link.html")]
    public class ExpandableOffGridLink : OffGridLink
    {
        [Tooltip("the linear(x) expansion of this building is used to multiply cost and distance")]
        public ExpandableBuilding ExpandableBuilding;
        [Tooltip("is multiplied with linear building expansion and added to regular cost")]
        public int MultipliedCost;
        [Tooltip("is multiplied with linear building expansion and added to regular distance")]
        public float MultipliedDistance;

        protected override int getCost() => Cost + ExpandableBuilding.Expansion.x;
        protected override float getDistance() => Distance + ExpandableBuilding.Expansion.x;

        protected override void Start()
        {
            base.Start();

            ExpandableBuilding.ExpansionChanged += _ => initialize();
        }

        public override void Walk(Walker walker, float moved, Vector2Int start)
        {
            base.Walk(walker, moved, start);
        }
    }
}
