using System;
using System.Linq;

namespace CityBuilderCore
{
    /// <summary>
    /// reference to a building component that will keep working even when the building gets replaced<br/>
    /// it can also be saved to keep building component referenes alive across save/load<br/>
    /// should always be used when a reference to a building component is stored in some way
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class BuildingComponentReference<T>
    where T : IBuildingComponent
    {
        public T Instance { get; set; }
        /// <summary>
        /// the component referenced is still alive
        /// </summary>
        public bool HasInstance => Instance as UnityEngine.Object;

        public BuildingComponentReference(T component)
        {
            Instance = component;
        }

        #region Saving
        public BuildingComponentReferenceData GetData() => HasInstance ? new BuildingComponentReferenceData() { BuildingId = Instance.Building.Id.ToString(), ComponentKey = Instance.Key } : null;
    }
    [Serializable]
    public class BuildingComponentReferenceData
    {
        public string BuildingId;
        public string ComponentKey;

        public BuildingComponentReference<T> GetReference<T>() where T : IBuildingTrait<T>
        {
            if (string.IsNullOrWhiteSpace(BuildingId))
                return null;
            Guid id = new Guid(BuildingId);
            return Dependencies.Get<IBuildingManager>().GetBuildingTraits<T>().FirstOrDefault(r => r.Building.Id == id && r.Key == ComponentKey)?.Reference;
        }
    }
    #endregion
}