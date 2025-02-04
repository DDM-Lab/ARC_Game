using CityBuilderCore;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// variant of the <see cref="SelectionSwitcher"/> that also cycles walkers by the job they have in the town demo
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_selection_switcher.html")]
    public class TownSelectionSwitcher : SelectionSwitcher
    {
        protected override List<object> getCandidates()
        {
            if (_currentTarget is TownWalker walker)
                return Dependencies.Get<IWalkerManager>().GetWalkers().OfType<TownWalker>().Where(w => w.Job == walker.Job).Cast<object>().ToList();

            return base.getCandidates();
        }
    }
}
