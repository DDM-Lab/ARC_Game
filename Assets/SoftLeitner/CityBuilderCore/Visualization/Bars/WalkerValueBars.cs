using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// helper for <see cref="DefaultBuildingManager"/> that manages building bars
    /// </summary>
    public class WalkerValueBars
    {
        public ViewWalkerBarBase View { get; private set; }

        private Transform _globalRoot;
        private List<WalkerValueBar> _bars;

        public WalkerValueBars(ViewWalkerBarBase view, Transform globalRoot)
        {
            View = view;
            _globalRoot = globalRoot;
            _bars = new List<WalkerValueBar>();
        }

        public void Update()
        {
            _bars.ForEach(b => b.gameObject.SetActive(b.HasValue()));
        }

        public void Add(Walker walker)
        {
            if (View.WalkerValue != null && !View.WalkerValue.HasValue(walker))
                return;

            var bar = Object.Instantiate(View.Bar, View.Bar.IsGlobal ? _globalRoot : walker.Pivot);
            bar.Initialize(walker, View.WalkerValue);

            _bars.Add(bar);
        }

        public void Remove(Walker walker)
        {
            foreach (var bar in _bars.Where(b => b.Walker == walker))
            {
                Object.Destroy(bar.gameObject);
            }

            _bars.RemoveAll(b => b.Walker == walker);
        }

        public void Clear()
        {
            _bars.ForEach(b => Object.Destroy(b.gameObject));
            _bars.Clear();
        }
    }
}