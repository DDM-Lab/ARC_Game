using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// <para>buildings are special structures that occupy points starting from an origin extending by a size which can be accessed at ceratin points<br/>
    /// the behaviour of a building is achieved by adding different components and addons</para>
    /// <para>default implementation is <see cref="Building"/> which is sometimes used due to Unity restrictions on interfaces<br/>
    /// always consider overriding <see cref="Building"/> before reimplementing IBuilding</para>
    /// </summary>
    public interface IBuilding : IStructure, ISaveData
    {
        /// <summary>
        /// reference to the building that keeps working even if the building is replaced
        /// </summary>
        BuildingReference BuildingReference { get; set; }

        /// <summary>
        /// unique id for the building, used for saving
        /// is not carried over when replacing
        /// </summary>
        Guid Id { get; set; }
        /// <summary>
        /// building is temporarly disabled, can be used to stop work in a building without having to destroy and rebuild
        /// </summary>
        bool IsSuspended { get; }
        /// <summary>
        /// index of the prefab used, only relevant if the building has <see cref="BuildingInfo.PrefabAlternatives"/>
        /// </summary>
        int Index { get; set; }

        /// <summary>
        /// meta info for data that does not change between instances of a building<br/>
        /// can be used to compare types of buildings
        /// </summary>
        BuildingInfo Info { get; }
        /// <summary>
        /// root transform used as a parent for things that belong to the building but should not be rotated in the pivot(walkers, bars)
        /// </summary>
        Transform Root { get; }
        /// <summary>
        /// base transform for building visuals, used in animation
        /// </summary>
        Transform Pivot { get; }

        /// <summary>
        /// origin(bottomLeft) point on the grid, stays constant when rotated
        /// </summary>
        Vector2Int Point { get; }
        /// <summary>
        /// size of the building without rotation
        /// </summary>
        Vector2Int RawSize { get; }
        /// <summary>
        /// size on the grid, always positive
        /// </summary>
        Vector2Int Size { get; }
        /// <summary>
        /// center position of the building in world space
        /// </summary>
        Vector3 WorldCenter { get; }
        /// <summary>
        /// rotation of the building on the grid, just a int from 0 to 3
        /// </summary>
        BuildingRotation Rotation { get; }

        /// <summary>
        /// how efficient the building currently is, influenced by all parts implementing <see cref="IEfficiencyFactor"/><br/>
        /// ranges from 0-1, a building with half its employees would have 0.5 efficiency
        /// </summary>
        float Efficiency { get; }
        /// <summary>
        /// whether a buildings efficiency is indisturbed<br/>
        /// for example a farm on semi ideal land might not have full efficiency while still working
        /// </summary>
        bool IsWorking { get; }

        /// <summary>
        /// display description
        /// </summary>
        /// <returns></returns>
        string GetDescription();

        /// <summary>
        /// initialization for buildings when placing and loading<br/>
        /// is not called when a building is replaced
        /// </summary>
        void Initialize();
        /// <summary>
        /// only called when the building is first placed
        /// </summary>
        void Setup();
        /// <summary>
        /// replaces the building and all of its parts with another one<br/>
        /// runtime values are transferred and references are reset to the new instances
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="prefab"></param>
        /// <returns></returns>
        T Replace<T>(T prefab) where T : MonoBehaviour, IBuilding;
        /// <summary>
        /// moves the building to a different position
        /// </summary>
        void Move(Vector3 position, Quaternion rotation);
        /// <summary>
        /// de-initializes the buildings and destroys it
        /// </summary>
        void Terminate();

        /// <summary>
        /// temporarily stops the building from working
        /// </summary>
        void Suspend();
        /// <summary>
        /// resumes work in the building after <see cref="Suspend"/> has been called
        /// </summary>
        void Resume();

        /// <summary>
        /// checks if a type of part(components, addons) exists
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        bool HasBuildingPart<T>();
        /// <summary>
        /// returns all parts composing the building(components, addons)
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        IEnumerable<T> GetBuildingParts<T>();

        /// <summary>
        /// checks if a type of component exists
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        bool HasBuildingComponent<T>() where T : IBuildingComponent;
        /// <summary>
        /// get first component of a certain type or null
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        T GetBuildingComponent<T>() where T : class, IBuildingComponent;
        /// <summary>
        /// get all components of a certain type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        IEnumerable<T> GetBuildingComponents<T>() where T : IBuildingComponent;

        /// <summary>
        /// checks if a type of addon exists
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        bool HasBuildingAddon<T>() where T : BuildingAddon;
        /// <summary>
        /// get first addon of a type or null
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        T GetBuildingAddon<T>() where T : BuildingAddon;
        /// <summary>
        /// get all addons of a certain type
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        IEnumerable<T> GetBuildingAddons<T>() where T : BuildingAddon;

        /// <summary>
        /// adds and initializes an addon onto the building from a prefab
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="prefab"></param>
        /// <returns>the created addon instance</returns>
        T AddAddon<T>(T prefab) where T : BuildingAddon;
        /// <summary>
        /// get the first addon with the specified key
        /// </summary>
        /// <typeparam name="T">type of addon to look for(BuildingAddon for any type)</typeparam>
        /// <param name="key">key of the addon to look for</param>
        /// <returns>a fitting addon or null if none were found</returns>
        T GetAddon<T>(string key) where T : BuildingAddon;
        /// <summary>
        /// terminates and removes the addon from the building
        /// </summary>
        /// <param name="addon">the addon instance to remove</param>
        void RemoveAddon(BuildingAddon addon);
        /// <summary>
        /// terminates and removes an addon from the building with the given key or returns false if none were found
        /// </summary>
        /// <param name="key"></param>
        /// <returns>true if an addon was found and removed</returns>
        bool RemoveAddon(string key);

        /// <summary>
        /// returns any access points that is accessible with the given pathtype
        /// </summary>
        /// <param name="type"></param>
        /// <param name="tag"></param>
        /// <returns></returns>
        IEnumerable<Vector2Int> GetAccessPoints(PathType type, object tag = null);
        /// <summary>
        /// returns the first access point that is accessible with the given pathtype
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        Vector2Int? GetAccessPoint(PathType type, object tag = null);
        /// <summary>
        /// checks if the building can currently be accessed using the given pathtype
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        bool HasAccessPoint(PathType type, object tag = null);
    }
}