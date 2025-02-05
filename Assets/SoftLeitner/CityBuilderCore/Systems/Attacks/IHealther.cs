using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// any object that has health wich should be displayed
    /// </summary>
    public interface IHealther
    {
        /// <summary>
        /// maximum health the entity can have
        /// </summary>
        float TotalHealth { get; }
        /// <summary>
        /// current health the entity has
        /// </summary>
        float CurrentHealth { get; }
        /// <summary>
        /// the absolute world position that the health bar should be displayed at
        /// </summary>
        Vector3 HealthPosition { get; }
    }
}