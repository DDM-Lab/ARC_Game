using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// the component that tracks all the buildings in the game
    /// </summary>
    public interface IBuildingManager
    {
        /// <summary>
        /// fired after a new building has been instantiated and initialized
        /// </summary>
        event Action<IBuilding> Added;
        /// <summary>
        /// fired when a new building is registered(happens during initializion)
        /// </summary>
        event Action<IBuilding> Registered;
        /// <summary>
        /// fired when a building gets deregistered(happens during termination)
        /// </summary>
        event Action<IBuilding> Deregistered;

        /// <summary>
        /// creates a new building
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="origin"></param>
        /// <param name="rotation"></param>
        /// <param name="prefab"></param>
        /// <returns></returns>
        T Add<T>(Vector3 position, Quaternion rotation, T prefab, Action<T> initializer = null) where T : Building;

        /// <summary>
        /// registers a building into the managers responsibility, called by building on initialization
        /// </summary>
        /// <param name="building"></param>
        void RegisterBuilding(IBuilding building);
        /// <summary>
        /// deregisters a building from the managers responsibility, called by building on termination
        /// </summary>
        /// <param name="building"></param>
        void DeregisterBuilding(IBuilding building);

        /// <summary>
        /// registers a trait with the manager so the trait can be easily retrieved from outside
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="component"></param>
        /// <returns></returns>
        BuildingComponentReference<T> RegisterBuildingTrait<T>(T component) where T : IBuildingTrait<T>;
        /// <summary>
        /// replace a trait in its reference or remove it if there is no replacement, called by the component on replacement
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="component"></param>
        /// <param name="replacement"></param>
        void ReplaceBuildingTrait<T>(T component, T replacement) where T : IBuildingTrait<T>;
        /// <summary>
        /// deregister a trait from the manager, called by the component on termination
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="component"></param>
        void DeregisterBuildingTrait<T>(T component) where T : IBuildingTrait<T>;

        int Count(BuildingInfo info);
        int Count(BuildingCategory buildingCategory);

        /// <summary>
        /// returns a building at the point, can be any point in the building not just the origin
        /// </summary>
        /// <param name="point"></param>
        /// <returns></returns>
        IEnumerable<IBuilding> GetBuilding(Vector2Int point);
        /// <summary>
        /// retrieve building by its id, may be used to get building references when loading
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        BuildingReference GetBuildingReference(Guid id);
        IEnumerable<IBuilding> GetBuildings();
        IEnumerable<IBuilding> GetBuildings(BuildingCategory category);
        IEnumerable<IBuilding> GetBuildings(BuildingInfo info);
        
        IEnumerable<T> GetBuildingTraits<T>() where T : IBuildingTrait<T>;

        bool HasEvolutionDelay(bool direction);
        float GetEvolutionDelay(bool direction);
        string AddEvolutionAddon(IBuilding building, bool direction);

        string AddSuspensionAddon(IBuilding building);

        string SaveData();
        void LoadData(string json);
    }
}