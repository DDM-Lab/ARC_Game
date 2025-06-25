using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    public class LayerValues
    {
        private Dictionary<Vector2Int, LayerPosition> _affectedPositions;
        private Dictionary<ILayerAffector, Dictionary<Vector2Int, int>> _affectorValues;

        private List<ILayerModifier> _modifiers;

        private int[,] _baseValues;

        public LayerValues(Vector2Int size)
        {
            _affectedPositions = new Dictionary<Vector2Int, LayerPosition>();
            _affectorValues = new Dictionary<ILayerAffector, Dictionary<Vector2Int, int>>();
            _modifiers = new List<ILayerModifier>();
            _baseValues = new int[size.x, size.y];
        }

        public void AddTiles(LayerAffectorTile affectingTile)
        {
            for (int x = 0; x < _baseValues.GetLength(0); x++)
            {
                for (int y = 0; y < _baseValues.GetLength(1); y++)
                {
                    var value = affectingTile.GetValue(new Vector2Int(x, y));
                    if (Math.Abs(value) == 0)
                        continue;

                    var currentValue = _baseValues[x, y];
                    if (affectingTile.Layer.IsCumulative)
                        _baseValues[x, y] = currentValue + value;
                    else if (Math.Abs(currentValue) < Math.Abs(value))
                        _baseValues[x, y] = value;
                }
            }
        }

        public IEnumerable<Tuple<Vector2Int, int>> GetValues(Layer layer)
        {
            for (int x = 0; x < _baseValues.GetLength(0); x++)
            {
                for (int y = 0; y < _baseValues.GetLength(1); y++)
                {
                    var position = new Vector2Int(x, y);
                    var value = GetValue(position, layer);
                    if (value != 0 || _affectedPositions.ContainsKey(position))
                        yield return Tuple.Create(position, value);
                }
            }
        }
        public int GetValue(Vector2Int point, Layer layer)
        {
            if (point.x < 0 || point.y < 0)
                return 0;
            if (point.x >= _baseValues.GetLength(0) || point.y >= _baseValues.GetLength(1))
                return 0;

            var baseValue = _baseValues[point.x, point.y];

            if (!_affectedPositions.ContainsKey(point))
                return modify(baseValue);

            var affectedValue = _affectedPositions[point].Value;

            if (layer.IsCumulative)
                return modify(baseValue + affectedValue);
            else if (Math.Abs(baseValue) > Math.Abs(affectedValue))
                return modify(baseValue);
            else
                return modify(affectedValue);
        }
        public int GetAffectorValue(Vector2Int point, ILayerAffector affector)
        {
            if (!_affectorValues.ContainsKey(affector))
                return 0;
            if (!_affectorValues[affector].ContainsKey(point))
                return 0;
            return _affectorValues[affector][point];
        }
        public LayerKey GetKey(Vector2Int point)
        {
            LayerKey key = null;

            var baseValue = _baseValues[point.x, point.y];
            if (baseValue != 0)
                key = new LayerKey(baseValue);

            var positions = _affectedPositions;
            if (positions.ContainsKey(point))
            {
                if (key == null)
                    key = new LayerKey(0);

                foreach (var affector in positions[point].Affectors)
                {
                    key.AddAffector(affector, GetAffectorValue(point, affector));
                }
            }

            if (key != null)
            {
                foreach (var modifier in _modifiers)
                {
                    key.AddModifier(modifier);
                }
            }

            return key;
        }

        public void Register(ILayerAffector affector)
        {
            Dictionary<ILayerDependency, List<Vector2Int>> dependencies = new Dictionary<ILayerDependency, List<Vector2Int>>();

            var layerManager = Dependencies.Get<ILayerManager>();
            var values = affector.GetValues();

            _affectorValues.Add(affector, values);

            foreach (var pointValue in values)
            {
                var point = pointValue.Key;
                var value = pointValue.Value;
                if (!_affectedPositions.ContainsKey(point))
                    _affectedPositions.Add(point, new LayerPosition(this));
                _affectedPositions[point].AddAffector(point, value, affector);

                foreach (var dependency in layerManager.GetDependencies(point))
                {
                    if (!dependencies.ContainsKey(dependency))
                        dependencies.Add(dependency, new List<Vector2Int>());
                    dependencies[dependency].Add(point);
                }
            }

            if (Dependencies.GetOptional<IGameSaver>()?.IsLoading != true)
            {
                foreach (var dependency in dependencies)
                {
                    dependency.Key.CheckLayers(dependency.Value);
                }
            }
        }
        public void Deregister(ILayerAffector affector)
        {
            Dictionary<ILayerDependency, List<Vector2Int>> dependencies = new Dictionary<ILayerDependency, List<Vector2Int>>();

            var layerManager = Dependencies.Get<ILayerManager>();

            if (!_affectorValues.ContainsKey(affector))
                return;

            foreach (var positionValue in _affectorValues[affector])
            {
                var position = positionValue.Key;
                var value = positionValue.Value;
                if (!_affectedPositions.ContainsKey(position))
                    continue;
                var layerPosition = _affectedPositions[position];
                layerPosition.RemoveAffector(position, value, affector);
                if (layerPosition.Affectors.Count == 0)
                    _affectedPositions.Remove(position);

                foreach (var dependency in layerManager.GetDependencies(position))
                {
                    if (!dependencies.ContainsKey(dependency))
                        dependencies.Add(dependency, new List<Vector2Int>());
                    dependencies[dependency].Add(position);
                }
            }

            _affectorValues.Remove(affector);

            if (!Dependencies.Get<IGameSaver>().IsLoading)
            {
                foreach (var dependency in dependencies)
                {
                    dependency.Key.CheckLayers(dependency.Value);
                }
            }
        }

        public void Register(ILayerModifier modifier)
        {
            _modifiers.Add(modifier);

            if (!Dependencies.Get<IGameSaver>().IsLoading)
            {
                foreach (var dependency in Dependencies.Get<ILayerManager>().GetDependencies())
                {
                    dependency.CheckLayers(null);
                }
            }
        }
        public void Deregister(ILayerModifier modifier)
        {
            _modifiers.Remove(modifier);

            if (!Dependencies.Get<IGameSaver>().IsLoading)
            {
                foreach (var dependency in Dependencies.Get<ILayerManager>().GetDependencies())
                {
                    dependency.CheckLayers(null);
                }
            }
        }

        private int modify(int value)
        {
            foreach (var modifier in _modifiers)
            {
                value = modifier.Modify(value);
            }

            return value;
        }
    }
}