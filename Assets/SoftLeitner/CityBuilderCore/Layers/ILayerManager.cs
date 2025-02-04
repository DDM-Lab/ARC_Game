using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// handles layer values, may have some built in way to establish base values<br/>
    /// <see cref="ILayerAffector"/> that only affect some points and <see cref="ILayerModifier"/> that affect a layer globally are registered with it<br/>
    /// combining base values, affectors and modifiers it can provide the layer value for every point on the map<br/>
    /// the value can be manually requested or automatically delivered when structures or building parts are marked as <see cref="ILayerDependency"/>
    /// </summary>
    public interface ILayerManager
    {
        /// <summary>
        /// fired whenever an affector is added or removed
        /// </summary>
        event Action<Layer> Changed;

        void Register(ILayerAffector affector);
        void Deregister(ILayerAffector affector);

        void Register(ILayerModifier modifier);
        void Deregister(ILayerModifier modifier);


        /// <summary>
        /// checks if the points in the specified box, overall, satisfy the requirements
        /// </summary>
        /// <param name="origin"></param>
        /// <param name="size"></param>
        /// <param name="requirement"></param>
        /// <returns></returns>
        bool CheckRequirement(Vector2Int origin, Vector2Int size, LayerRequirement requirement);
        /// <summary>
        /// returns all layer dependencies<br/>
        /// dependencies might be evolutions, roads, ...
        /// </summary>
        /// <param name="position"></param>
        /// <returns></returns>
        IEnumerable<ILayerDependency> GetDependencies();
        /// <summary>
        /// returns a layer dependency if one exists at that point<br/>
        /// dependencies might be evolutions, roads, ...
        /// </summary>
        /// <param name="point"></param>
        /// <returns></returns>
        IEnumerable<ILayerDependency> GetDependencies(Vector2Int point);

        /// <summary>
        /// returns an explanation of the layer value at a certain point(basevalue+affectors)
        /// </summary>
        /// <param name="layer"></param>
        /// <param name="point"></param>
        /// <returns></returns>
        LayerKey GetKey(Layer layer, Vector2Int point);
        /// <summary>
        /// returns the computed total value of a layer at a point
        /// </summary>
        /// <param name="point"></param>
        /// <param name="layer"></param>
        /// <returns></returns>
        int GetValue(Vector2Int point, Layer layer);
        /// <summary>
        /// returns all positions and values of a layer where the value differs from 0
        /// </summary>
        /// <param name="layer"></param>
        /// <returns></returns>
        IEnumerable<Tuple<Vector2Int, int>> GetValues(Layer layer);
    }
}