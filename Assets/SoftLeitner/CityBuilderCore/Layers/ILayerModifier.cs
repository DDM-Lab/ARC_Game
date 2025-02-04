using System;

namespace CityBuilderCore
{
    /// <summary>
    /// a global modifier affecting an entire layer regardless of position, is registered with <see cref="ILayerManager"/><br/>
    /// for example a heat wave or simply the season may raise a heat layer that makes fires more likely
    /// </summary>
    public interface ILayerModifier
    {
        /// <summary>
        /// the layer for which this modifier changes values
        /// </summary>
        Layer Layer { get; }
        /// <summary>
        /// name of the modifier that will be displayed in the UI
        /// </summary>
        string Name { get; }

        /// <summary>
        /// event the modifier has to fire when it changes how it modifies the layer
        /// </summary>
        event Action<ILayerModifier> Changed;

        /// <summary>
        /// takes the unmodified value and returns the modified one
        /// </summary>
        /// <param name="value">the layer value before modification</param>
        /// <returns>the modified value</returns>
        int Modify(int value);
    }
}