using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// determines how people die in a building afflicted with the <see cref="DiseaseAddon"/>
    /// </summary>
    public enum DiseaseMortality
    {
        [Tooltip("population is not reduced by the disease")]
        None = 0,
        [Tooltip("when disease hits a ratio of the housing population is immediately killed")]
        Initial = 10,
        [Tooltip("a ratio of the housing population is killed of when the duration of the disease is over")]
        Final = 11,
        [Tooltip("housing population is continuously killed at a certain rate/sec")]
        Continuous = 20
    }
}