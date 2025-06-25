using CityBuilderCore;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// view used for bars that take care of retrieving values on their own instead of using an <see cref="IWalkerValue"/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/views">https://citybuilder.softleitner.com/manual/views</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_view_walker_bar_generic.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Views/" + nameof(ViewWalkerBarGeneric))]
    public class ViewWalkerBarGeneric : ViewWalkerBarBase
    {
        public override IWalkerValue WalkerValue => null;
    }
}