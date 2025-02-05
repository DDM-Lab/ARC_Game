namespace CityBuilderCore
{
    /// <summary>
    /// manages the population and housing system<br/>
    /// <para>
    /// keeps track of different population migrations, housings and statistics
    /// </para>
    /// </summary>
    public interface IPopulationManager
    {
        /// <summary>
        /// calculates the total quantity of a population currently being housed in the city
        /// </summary>
        /// <param name="population">the type of population(plebs, aristocrats, ...)</param>
        /// <param name="includeReserved">whether migrants that have not arrived at their housing should be counted</param>
        /// <returns>total population count</returns>
        int GetQuantity(Population population, bool includeReserved = false);
        /// <summary>
        /// calculates the total quantity of a population that can be housed in the city
        /// </summary>
        /// <param name="population">the type of population(plebs, aristocrats, ...)</param>
        /// <returns>total capacit</returns>
        int GetCapacity(Population population);
        /// <summary>
        /// calculated how many more of a population fits into the city
        /// </summary>
        /// <param name="population">the type of population(plebs, aristocrats, ...)</param>
        /// <returns>remaining capacity in the city</returns>
        int GetRemainingCapacity(Population population);

        /// <summary>
        /// spawns homeless walkers that carry the population quantity and try to find new housing<br/>
        /// used when housing downgrades and can no longer hold the quantity it did before
        /// </summary>
        /// <param name="population">the type of population(plebs, aristocrats, ...)</param>
        /// <param name="housing">the housing that downgraded</param>
        /// <param name="quantity">quantity that no longer fits into the housing</param>
        void AddHomeless(Population population, IHousing housing, int quantity);

        string SaveData();
        void LoadData(string json);
    }
}