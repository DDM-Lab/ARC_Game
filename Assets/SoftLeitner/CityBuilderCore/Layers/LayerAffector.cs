using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// affector that uses the points of an <see cref="IStructure"/> on the same or the parent component<br/>
    /// for example in THREE trees raise desirability needed by housing and ore structures affect the iron layer which is needed to place mines
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/layers">https://citybuilder.softleitner.com/manual/layers</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_layer_affector.html")]
    public class LayerAffector : MonoBehaviour, ILayerAffector
    {
        public Layer Layer;
        [Tooltip("value inside the affector")]
        public int Value;
        [Tooltip("range of points outside the affector")]
        public int Range;
        [Tooltip("value subtracted for every step outside the affector")]
        public int Falloff;

        public virtual bool IsAffecting => true;
        public virtual string Name => _structure?.GetName() ?? string.Empty;

        Layer ILayerAffector.Layer => Layer;

        public event Action<ILayerAffector> Changed;

        private IStructure _structure;
        private bool _isAffecting;

        protected virtual void Start()
        {
            _structure = GetComponent<IStructure>() ?? GetComponentInParent<IStructure>();
            _structure.PointsChanged += structurePointsChanged;

            checkAffector();
        }

        private void structurePointsChanged(PointsChanged<IStructure> change)
        {
            Changed?.Invoke(this);
        }

        private void OnDestroy()
        {
            if (gameObject.scene.isLoaded)
                Dependencies.Get<ILayerManager>().Deregister(this);
        }

        protected void checkAffector()
        {
            if (_isAffecting == IsAffecting)
                return;

            _isAffecting = IsAffecting;

            if (IsAffecting)
                Dependencies.Get<ILayerManager>().Register(this);
            else
                Dependencies.Get<ILayerManager>().Deregister(this);
        }

        public Dictionary<Vector2Int, int> GetValues()
        {
            var values = new Dictionary<Vector2Int, int>();

            foreach (var item in _structure.GetPoints())
            {
                var value = Value;

                for (int i = 0; i <= Range; i++)
                {
                    foreach (var point in PositionHelper.GetAdjacent(item, Vector2Int.one, true, i - 1))
                    {
                        if (values.ContainsKey(point))
                        {
                            if (Mathf.Abs(values[point]) < Mathf.Abs(value))
                                values[point] = value;
                        }
                        else
                        {
                            values.Add(point, value);
                        }
                    }

                    value -= Falloff;
                }
            }

            return values;
        }

        public int GetValue(Vector2Int point)
        {
            var distance = _structure.GetPoints().Min(p => p.GetMaxAxisDistance(point));
            if (distance > Range)
                return 0;
            else
                return Value - distance * Falloff;
        }
    }
}