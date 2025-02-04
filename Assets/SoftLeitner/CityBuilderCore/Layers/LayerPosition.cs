using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// helper for <see cref="LayerValues"/> that holds the current value of a point and all the affectors that affect it
    /// </summary>
    public class LayerPosition
    {
        public int Value { get; private set; }
        public List<ILayerAffector> Affectors { get; private set; }

        private LayerValues _owner;

        public LayerPosition(LayerValues owner)
        {
            Value = 0;
            Affectors = new List<ILayerAffector>();

            _owner = owner;
        }

        public void AddAffector(Vector2Int point, int value, ILayerAffector affector)
        {
            if (affector.Layer.IsCumulative)
                Value += value;
            else if (Mathf.Abs(value) > Mathf.Abs(Value))
                Value = value;

            Affectors.Add(affector);
        }
        public void RemoveAffector(Vector2Int point, int value, ILayerAffector affector)
        {
            if (affector.Layer.IsCumulative)
                Value -= value;
            else if (Mathf.Abs(Value) <= Mathf.Abs(value))
                reevalueteValue(point);

            Affectors.Remove(affector);
        }

        private void reevalueteValue(Vector2Int point)
        {
            if (Affectors.Count == 0)
            {
                Value = 0;
            }
            else if (Affectors[0].Layer.IsCumulative)
            {
                Value = Affectors.Sum(a => _owner.GetAffectorValue(point, a));
            }
            else
            {
                foreach (var affector in Affectors)
                {
                    var value = _owner.GetAffectorValue(point, affector);
                    if (Mathf.Abs(value) > Mathf.Abs(Value))
                        Value = value;
                }
            }
        }
    }
}