using CityBuilderCore;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// view that visualizes a walkers current energy in percent
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_view_walker_energy_bar.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Town/" + nameof(TownViewWalkerEnergyBar))]
    public class TownViewWalkerEnergyBar : ViewWalkerBarBase, IWalkerValue
    {
        public override IWalkerValue WalkerValue => this;

        public bool HasValue(Walker walker) => walker is TownWalker;
        public float GetMaximum(Walker walker) => 100;//((TownWalker)walker).Identity.EnergyCapacity;
        public float GetValue(Walker walker) => ((TownWalker)walker).Energy / ((TownWalker)walker).Identity.EnergyCapacity * 100f;//((TownWalker)walker).Energy;
        public Vector3 GetPosition(Walker walker) => walker.Pivot.position;
    }
}