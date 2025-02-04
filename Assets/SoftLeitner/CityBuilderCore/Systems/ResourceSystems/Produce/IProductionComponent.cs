using System.Collections.Generic;

namespace CityBuilderCore
{
    /// <summary>
    /// building component that produces items from other items
    /// </summary>
    public interface IProductionComponent : IProgressComponent, IItemReceiver, IItemOwner
    {
        /// <summary>
        /// exposes the items the production component needs to work
        /// </summary>
        /// <returns></returns>
        IEnumerable<ItemLevel> GetNeededItems();
        /// <summary>
        /// exposes the items the component can produce
        /// </summary>
        /// <returns></returns>
        IEnumerable<ItemLevel> GetProducedItems();
    }
}