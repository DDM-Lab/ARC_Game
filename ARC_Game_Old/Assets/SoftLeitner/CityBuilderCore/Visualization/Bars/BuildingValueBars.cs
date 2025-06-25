using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// helper for <see cref="DefaultBuildingManager"/> that manages building bars
    /// </summary>
    public class BuildingValueBars
    {
        public ViewBuildingBarBase View { get; private set; }

        private Transform _globalRoot;
        private List<BuildingReference> _buildingReferences;
        private List<BuildingValueBar> _bars;

        public BuildingValueBars(ViewBuildingBarBase view, Transform globalRoot)
        {
            View = view;
            _globalRoot = globalRoot;
            _buildingReferences = new List<BuildingReference>();
            _bars = new List<BuildingValueBar>();
        }

        public void Update()
        {
            _bars.ForEach(b => b.gameObject.SetActive(b.HasValue()));
        }

        public void Add(BuildingReference buildingReference)
        {
            buildingReference.Replacing += replacing;
            _buildingReferences.Add(buildingReference);

            if (View.BuildingValue.HasValue(buildingReference.Instance))
                add(buildingReference.Instance);

        }
        public void Remove(BuildingReference buildingReference)
        {
            buildingReference.Replacing -= replacing;
            _buildingReferences.Remove(buildingReference);

            foreach (var bar in _bars.Where(b => b.Building == buildingReference.Instance))
            {
                Object.Destroy(bar.gameObject);
            }

            _bars.RemoveAll(b => b.Building == buildingReference.Instance);
        }

        public void Clear()
        {
            _buildingReferences.ForEach(b => b.Replacing -= replacing);
            _buildingReferences.Clear();
            _bars.ForEach(b => Object.Destroy(b.gameObject));
            _bars.Clear();
        }

        private void add(IBuilding building)
        {
            BuildingValueBar bar;
            if(View.Bar.IsGlobal)
                bar = Object.Instantiate(View.Bar, building.WorldCenter, Quaternion.identity, _globalRoot);
            else
                bar = Object.Instantiate(View.Bar, building.Pivot);
            bar.Initialize(building, View.BuildingValue);
            bar.gameObject.SetActive(bar.HasValue());

            _bars.Add(bar);
        }

        private void replacing(IBuilding a, IBuilding b)
        {
            foreach (var bar in _bars.Where(bar => bar.Building == a).ToArray())
            {
                Object.Destroy(bar.gameObject);
                _bars.Remove(bar);
            }

            if (View.BuildingValue.HasValue(b))
                add(b);
        }
    }
}