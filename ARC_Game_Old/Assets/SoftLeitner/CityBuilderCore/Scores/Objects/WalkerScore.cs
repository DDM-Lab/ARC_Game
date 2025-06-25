using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// counts how many walkers currently exist on the map
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/scores">https://citybuilder.softleitner.com/manual/scores</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_walker_score.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Scores/" + nameof(WalkerScore))]
    public class WalkerScore : Score
    {
        [Tooltip("the walker to count")]
        public WalkerInfo Walker;
        [Tooltip("the walker category to count(dont set walker)")]
        public WalkerCategory WalkerCategory;

        public override int Calculate()
        {
            if (Walker)
                return Dependencies.Get<IWalkerManager>().GetWalkers().Where(w => w.Info == Walker).Count();
            else if (WalkerCategory)
                return Dependencies.Get<IWalkerManager>().GetWalkers().Where(w => WalkerCategory.Contains(w.Info)).Count();
            else
                return 0;
        }
    }
}