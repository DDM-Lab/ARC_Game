namespace CityBuilderCore
{
    /// <summary>
    /// building component handling evolution(checking the current stage and replacing the building in case it has changed)
    /// </summary>
    public interface IEvolution : IBuildingComponent, IItemRecipient, IServiceRecipient, ILayerDependency
    {
        /// <summary>
        /// manually check if the evolution has changed in case any circumstances outside the components control have changed
        /// </summary>
        void CheckEvolution();
    }
}