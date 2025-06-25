namespace CityBuilderCore
{
    /// <summary>
    /// a building component that generates items
    /// </summary>
    public interface IGenerationComponent : IProgressComponent, IItemOwner
    {
        /// <summary>
        /// exposes the different producers of the generator so they can be visualized
        /// </summary>
        ItemProducer[] ItemsProducers { get; }

        /// <summary>
        /// moves certain generated items from the component to the passed storage
        /// </summary>
        /// <param name="storage">storage that gets the items</param>
        /// <param name="items">the items that can be moved</param>
        void Collect(ItemStorage storage, Item[] items);
    }
}