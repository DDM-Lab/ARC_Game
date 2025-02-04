using System;

namespace CityBuilderCore
{
    /// <summary>
    /// building component that has come kind of progress<br/>
    /// common interface so different progressors can use the same visualizers
    /// </summary>
    public interface IProgressComponent : IBuildingComponent
    {
        /// <summary>
        /// progress from 0 to 1
        /// </summary>
        float Progress { get; }
        /// <summary>
        /// progress has finished and is reset to 0
        /// </summary>
        event Action ProgressReset;
        /// <summary>
        /// progress has changed, carries new progress
        /// </summary>
        event Action<float> ProgressChanged;
    }
}