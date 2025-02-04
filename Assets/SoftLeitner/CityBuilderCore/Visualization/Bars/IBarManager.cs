namespace CityBuilderCore
{
    /// <summary>
    /// responsible for adding adding and removing visuals for building and walker values
    /// </summary>
    public interface IBarManager
    {
        /// <summary>
        /// activates a building bar and creates building value bars on every building that has the value
        /// </summary>
        /// <param name="view">the view that will be activated</param>
        void ActivateBar(ViewBuildingBarBase view);
        /// <summary>
        /// deactivates a building bar and destroys all the bars that were created
        /// </summary>
        /// <param name="view">the view that will be deactivated</param>
        void DeactivateBar(ViewBuildingBarBase view);
        /// <summary>
        /// activates a walker bar and creates building value bars on every walker that has the value
        /// </summary>
        /// <param name="view">the view that will be activated</param>
        void ActivateBar(ViewWalkerBarBase view);
        /// <summary>
        /// deactivates a walker bar and destroys all the bars that were created
        /// </summary>
        /// <param name="view">the view that will be deactivated</param>
        void DeactivateBar(ViewWalkerBarBase view);
    }
}