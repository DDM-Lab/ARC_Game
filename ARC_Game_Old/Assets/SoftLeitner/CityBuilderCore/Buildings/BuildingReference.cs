using System;
using UnityEngine.Events;

namespace CityBuilderCore
{
    /// <summary>
    /// reference to a building that is reset when a building gets replaced<br/>
    /// never assign buildings directly when there is any chance they might be replaced
    /// </summary>
    public class BuildingReference
    {
        /// <summary>
        /// the currently referenced building
        /// </summary>
        public IBuilding Instance { get; private set; }
        /// <summary>
        /// check if the building still exists or if it has been terminated
        /// </summary>
        public bool HasInstance => Instance != null && Instance as UnityEngine.Object;

        /// <summary>
        /// fired during building replacement, both original and replacement exist at that point
        /// </summary>
        public event Action<IBuilding, IBuilding> Replacing;

        public Guid Id => HasInstance ? Instance.Id : Guid.Empty;

        public BuildingReference(IBuilding building)
        {
            Instance = building;
        }

        internal void Replace(IBuilding replacement)
        {
            Replacing?.Invoke(Instance, replacement);

            Instance = replacement;
            replacement.BuildingReference = this;
        }
    }

    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class BuildingEvent : UnityEvent<BuildingReference> { }
}