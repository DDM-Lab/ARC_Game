namespace CityBuilderCore
{
    /// <summary>
    /// displays grid lines overlaying the map
    /// </summary>
    public interface IGridOverlay
    {
        /// <summary>
        /// shows grid lines on the map
        /// </summary>
        void Show();
        /// <summary>
        /// hides grid lines on the map
        /// </summary>
        void Hide();
    }
}