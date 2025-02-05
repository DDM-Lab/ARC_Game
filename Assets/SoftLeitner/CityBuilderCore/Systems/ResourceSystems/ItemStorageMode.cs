namespace CityBuilderCore
{
    /// <summary>
    /// defines how a storage handles items and how capacity is calculated
    /// </summary>
    public enum ItemStorageMode
    {
        /// <summary>
        /// storage consists of several sub stacks
        /// </summary>
        Stacked,
        /// <summary>
        /// stores anything without limitations
        /// </summary>
        Free,
        /// <summary>
        /// stores anything up to a quantity per item
        /// </summary>
        ItemCapped,
        /// <summary>
        /// stores anything up to a unit amount per item
        /// </summary>
        UnitCapped,
        /// <summary>
        /// storage acts as a proxy for <see cref="IGlobalStorage"/>
        /// </summary>
        Global,
        /// <summary>
        /// stores item quantities as specified per item
        /// </summary>
        ItemSpecific,
        /// <summary>
        /// stores anything up to a total quantity of items
        /// </summary>
        TotalItemCapped,
        /// <summary>
        /// stores anything up to a total unit amount
        /// </summary>
        TotalUnitCapped,
        /// <summary>
        /// storage acts as a proxy for an <see cref="ItemStore"/> 
        /// </summary>
        Store
    }
}