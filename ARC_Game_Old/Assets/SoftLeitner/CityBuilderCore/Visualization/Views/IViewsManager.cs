namespace CityBuilderCore
{
    /// <summary>
    /// manages the active <see cref="View"/>
    /// </summary>
    public interface IViewsManager
    {
        /// <summary>
        /// the currently active view or null when null
        /// </summary>
        View ActiveView { get; }
        /// <summary>
        /// whether there is any view active currently
        /// </summary>
        bool HasActiveView { get; }

        /// <summary>
        /// deactivates the currently active view if there is one and activates the new one
        /// </summary>
        /// <param name="view">the view to activate</param>
        void ActivateView(View view);
        /// <summary>
        /// deactivates the view if it is the currently active one
        /// </summary>
        /// <param name="view">the view to deactivate</param>
        void DeactivateView(View view);
    }
}