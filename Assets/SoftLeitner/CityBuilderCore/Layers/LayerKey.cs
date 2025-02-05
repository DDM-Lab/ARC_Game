using System;
using System.Collections.Generic;

namespace CityBuilderCore
{
    /// <summary>
    /// explanation of the computed layer value at a point (Base+Affectors=Total)<br/>
    /// can be obtained for any point on the map by calling <see cref="ILayerManager.GetKey(Layer, UnityEngine.Vector2Int)"/>
    /// </summary>
    public class LayerKey
    {
        public int BaseValue { get; private set; }
        public int TotalValue { get; private set; }
        public IReadOnlyCollection<Tuple<int, ILayerAffector>> Affectors => _affectors;
        public IReadOnlyCollection<Tuple<int, ILayerModifier>> Modifiers => _modifiers;

        private List<Tuple<int, ILayerAffector>> _affectors;
        private List<Tuple<int, ILayerModifier>> _modifiers;

        public LayerKey(int baseValue)
        {
            BaseValue = baseValue;
            TotalValue = baseValue;
            _affectors = new List<Tuple<int, ILayerAffector>>();
            _modifiers = new List<Tuple<int, ILayerModifier>>();
        }

        public void AddAffector(ILayerAffector affector, int value)
        {
            _affectors.Add(Tuple.Create(value, affector));

            if (affector.Layer.IsCumulative)
                TotalValue += value;
            else if (Math.Abs(TotalValue) < Math.Abs(value))
                TotalValue = value;
        }

        public void AddModifier(ILayerModifier modifier)
        {
            var newTotal = modifier.Modify(TotalValue);

            _modifiers.Add(Tuple.Create(newTotal - TotalValue, modifier));

            TotalValue = newTotal;
        }
    }
}