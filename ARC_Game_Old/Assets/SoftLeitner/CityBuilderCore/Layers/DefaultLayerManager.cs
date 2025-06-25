using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CityBuilderCore
{
    /// <summary>
    /// layer manager iomplementation that can use a tilemap to define the base values
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/layers">https://citybuilder.softleitner.com/manual/layers</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_default_layer_manager.html")]
    public class DefaultLayerManager : MonoBehaviour, ILayerManager
    {
        [Tooltip("tilemap containing the tiles determining the base values(optional)")]
        public Tilemap AffectingTilemap;
        [Tooltip("tiles that determine the layer base values, set at start so changes at runtime wont change base values")]
        public LayerAffectorTile[] AffectingTiles;

        public event Action<Layer> Changed;

        private readonly Dictionary<Layer, LayerValues> _layers = new Dictionary<Layer, LayerValues>();

        protected virtual void Awake()
        {
            Dependencies.Register<ILayerManager>(this);
        }

        protected virtual void Start()
        {
            if (AffectingTilemap)
            {
                //set base values from tiles

                var map = Dependencies.Get<IMap>();
                for (int x = 0; x < map.Size.x; x++)
                {
                    for (int y = 0; y < map.Size.y; y++)
                    {
                        var position = new Vector2Int(x, y);
                        var tile = AffectingTilemap.GetTile((Vector3Int)position);

                        foreach (var affectingTile in AffectingTiles.Where(t => t.Tile == tile))
                        {
                            affectingTile.AddPosition(position);
                        }
                    }
                }

                foreach (var affectingTile in AffectingTiles)
                {
                    getValues(affectingTile.Layer, true).AddTiles(affectingTile);
                }
            }
        }

        private LayerValues getValues(Layer layer, bool add)
        {
            if (_layers.ContainsKey(layer))
                return _layers[layer];

            if (add)
            {
                var values = new LayerValues(Dependencies.Get<IMap>().Size);
                _layers.Add(layer, values);
                return values;
            }
            else
            {
                return null;
            }
        }

        public IEnumerable<Tuple<Vector2Int, int>> GetValues(Layer layer)
        {
            return getValues(layer, false)?.GetValues(layer) ?? Enumerable.Empty<Tuple<Vector2Int, int>>();
        }
        public int GetValue(Vector2Int point, Layer layer)
        {
            return getValues(layer, false)?.GetValue(point, layer) ?? 0;
        }
        public LayerKey GetKey(Layer layer, Vector2Int point)
        {
            return getValues(layer, false)?.GetKey(point);
        }

        public bool CheckRequirement(Vector2Int origin, Vector2Int size, LayerRequirement requirement)
        {
            int maxValue = int.MinValue;
            int minValue = int.MaxValue;

            foreach (var position in PositionHelper.GetStructurePositions(origin, size))
            {
                int value = GetValue(position, requirement.Layer);

                maxValue = Math.Max(maxValue, value);
                minValue = Math.Min(minValue, value);
            }

            return maxValue >= requirement.MinValue && minValue <= requirement.MaxValue;
        }

        public void Register(ILayerAffector affector)
        {
            getValues(affector.Layer, true).Register(affector);

            affector.Changed += affectorChanged;

            Changed?.Invoke(affector.Layer);
        }
        public void Deregister(ILayerAffector affector)
        {
            getValues(affector.Layer, true).Deregister(affector);

            affector.Changed -= affectorChanged;

            Changed?.Invoke(affector.Layer);
        }
        private void affectorChanged(ILayerAffector affector)
        {
            getValues(affector.Layer, true).Deregister(affector);
            getValues(affector.Layer, true).Register(affector);

            Changed?.Invoke(affector.Layer);
        }

        public void Register(ILayerModifier modifier)
        {
            getValues(modifier.Layer, true).Register(modifier);

            modifier.Changed += modifierChanged;

            Changed?.Invoke(modifier.Layer);
        }
        public void Deregister(ILayerModifier modifier)
        {
            getValues(modifier.Layer, true).Deregister(modifier);

            modifier.Changed -= modifierChanged;

            Changed?.Invoke(modifier.Layer);
        }
        private void modifierChanged(ILayerModifier modifier)
        {
            Changed?.Invoke(modifier.Layer);
        }

        public virtual IEnumerable<ILayerDependency> GetDependencies()
        {
            var structureManager = Dependencies.GetOptional<IStructureManager>();
            if (structureManager == null)
                yield break;

            foreach (var structure in structureManager.GetStructures(0))
            {
                if (structure is ILayerDependency dependency)
                {
                    yield return dependency;
                }

                if(structure is IBuilding building)
                {
                    foreach (var part in building.GetBuildingParts<ILayerDependency>())
                    {
                        yield return part;
                    }
                }
            }
        }
        public virtual IEnumerable<ILayerDependency> GetDependencies(Vector2Int point)
        {
            var structureManager = Dependencies.GetOptional<IStructureManager>();
            if (structureManager == null)
                yield break;

            foreach (var structure in structureManager.GetStructures(point, 0).OfType<ILayerDependency>())
            {
                yield return structure;
            }

            foreach (var building in structureManager.GetStructures(point, 0).OfType<IBuilding>())
            {
                foreach (var part in building.GetBuildingParts<ILayerDependency>())
                {
                    yield return part;
                }
            }
        }
    }
}