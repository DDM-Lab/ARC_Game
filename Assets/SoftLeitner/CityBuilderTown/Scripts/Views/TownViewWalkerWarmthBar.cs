using CityBuilderCore;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// view that visualizes a walkers current warmth in percent
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_view_walker_warmth_bar.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Town/" + nameof(TownViewWalkerWarmthBar))]
    public class TownViewWalkerWarmthBar : ViewWalkerBarBase, IWalkerValue
    {
        public override IWalkerValue WalkerValue => this;

        public bool HasValue(Walker walker) => walker is TownWalker;
        public float GetMaximum(Walker walker) => 100;// ((TownWalker)walker).Identity.WarmthCapacity;
        public float GetValue(Walker walker)
        {
            var townWalker = (TownWalker)walker;
            if (townWalker.Identity.WarmthCapacity == 0f)
                return 0;

            return townWalker.Warmth / townWalker.Identity.WarmthCapacity * 100f;//((TownWalker)walker).Warmth;
        }

        public Vector3 GetPosition(Walker walker) => walker.Pivot.position;
    }
}