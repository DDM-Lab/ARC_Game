namespace CityBuilderCore
{
    /// <summary>
    /// a building component that houses populations(a hut that provides housing for 20 plebs, a villa that provides housing for 5 snobs)
    /// </summary>
    public interface IHousing : IBuildingTrait<IHousing>
    {
        /// <summary>
        /// checks how many people of a population are housed in the component
        /// </summary>
        /// <param name="population">the population to check</param>
        /// <param name="includeReserved">whether inhabitants that have not arrived at the housing should be counted</param>
        /// <returns>population count in this housíng</returns>
        int GetQuantity(Population population, bool includeReserved = false);
        /// <summary>
        /// checks how many people of a population can be housed in the component
        /// </summary>
        /// <param name="population">the population to check</param>
        /// <returns>total quantity that will fit</returns>
        int GetCapacity(Population population);
        /// <summary>
        /// checks how many more people of the population would fit in the housing
        /// </summary>
        /// <param name="population">the relevant population</param>
        /// <returns>quantity that will still fit(capacity-quantity-reserved)</returns>
        int GetRemainingCapacity(Population population);
        /// <summary>
        /// reserves space in the housing for inhabitants that are walking to it<br/>
        /// this is done to make sure there is still place when they arrive<br/>
        /// otherwise all immigrants in THREE would walk to the same housing until the first one arrives
        /// </summary>
        /// <param name="population">the population to reserve</param>
        /// <param name="quantity">how many to reserve</param>
        /// <returns>remaining quantity that would have exceeded capacity</returns>
        int Reserve(Population population, int quantity);
        /// <summary>
        /// removes reserved quantity and adds it to the housing
        /// </summary>
        /// <param name="population">population to add</param>
        /// <param name="quantity">quantity to add</param>
        /// <returns>remaining quantity that would have exceeded capacity</returns>
        int Inhabit(Population population, int quantity);
        /// <summary>
        /// removes a quantity of population from the housing
        /// </summary>
        /// <param name="population">population to remove</param>
        /// <param name="quantity">quantity to remove</param>
        /// <returns>remaining quantity that was not there in the first place</returns>
        int Abandon(Population population, int quantity);
        /// <summary>
        /// removes a portion of the housed quantities across populations
        /// </summary>
        /// <param name="ratio">0-1</param>
        void Kill(float ratio);
    }
}