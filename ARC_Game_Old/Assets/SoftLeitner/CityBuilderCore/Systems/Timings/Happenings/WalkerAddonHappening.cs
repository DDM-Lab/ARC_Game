using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// an event that, on activation, modifies the risk values of a set amount of buildings<br/>
    /// increase > arsonist, disease outbreak, natural disaster(volcano, earthquake) ...<br/>
    /// decrease > blessings, ...
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/timings">https://citybuilder.softleitner.com/manual/timings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_walker_addon_happening.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Happenings/" + nameof(WalkerAddonHappening))]
    public class WalkerAddonHappening : TimingHappening
    {
        [Tooltip("the addon that is added to walkers when the happening starts")]
        public WalkerAddon Addon;
        [Tooltip("the type of walker affected, empty for all")]
        public WalkerInfo Walker;
        [Tooltip("how many randomly selected walkers will be affected, 0 or less for all")]
        public int Count;
        [Tooltip("whether addons will be removed when the happening ends")]
        public bool Remove;

        public override void Start()
        {
            base.Start();

            foreach (var walker in Dependencies.Get<IWalkerManager>().GetRandom(Count, w =>
            {
                if (w.HasAddon(Addon))
                    return false;
                if (Walker == null || w.Info == Walker)
                    return true;
                return false;
            }))
            {
                walker.AddAddon(Addon);
            }
        }

        public override void End()
        {
            base.End();

            if (Remove)
            {
                foreach (var walker in Dependencies.Get<IWalkerManager>().GetRandom(Count, w => w.HasAddon(Addon)))
                {
                    walker.RemoveAddon(Addon);
                }
            }
        }
    }
}
