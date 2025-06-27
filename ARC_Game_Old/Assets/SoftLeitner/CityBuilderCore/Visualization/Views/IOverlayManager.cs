namespace CityBuilderCore
{
    /// <summary>
    /// overlays the tilemap with coloured tiles that visualize <see cref="Layer"/>, <see cref="Connection"/> or Efficiency values<br/>
    /// eg desirable areas are green, undesirable areas are red
    /// </summary>
    public interface IOverlayManager
    {
        /// <summary>
        /// activates a layer view that displays a gradient for layer values
        /// </summary>
        /// <param name="layerView">the view to activate</param>
        void ActivateOverlay(ViewLayer layerView);
        /// <summary>
        /// activates a connection view that displays a gradient for connection values
        /// </summary>
        /// <param name="connectionView">the view to activate</param>
        void ActivateOverlay(ViewConnection connectionView);
        /// <summary>
        /// activates a layer view that displays a gradient for building efficiency
        /// </summary>
        /// <param name="efficiencyView">the view to activate</param>
        void ActivateOverlay(ViewEfficiency efficiencyView);
        /// <summary>
        /// clears out any previously activated view
        /// </summary>
        void ClearOverlay();
    }
}