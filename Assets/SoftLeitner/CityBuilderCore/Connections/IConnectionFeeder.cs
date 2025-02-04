using System;

namespace CityBuilderCore
{
    /// <summary>
    /// an object that, in addition to passing it, also feeds value into a connection<br/>
    /// defines some parameters for how that value perpetuates, how exactly those are calculated is defiend in the manager
    /// </summary>
    public interface IConnectionFeeder : IConnectionPasser
    {
        /// <summary>
        /// value at the point of the feeder
        /// </summary>
        int Value { get; }
        /// <summary>
        /// how far the value of the feeder carries without falling off
        /// </summary>
        int Range { get; }
        /// <summary>
        /// value subtracted for every step outside the range
        /// </summary>
        int Falloff { get; }

        /// <summary>
        /// event that has to be fired when the feeder changes its output
        /// </summary>
        event Action<IConnectionFeeder> FeederValueChanged;
    }
}
