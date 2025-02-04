using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CityBuilderCore
{
    /// <summary>
    /// default implementation of <see cref="IOverlayManager"/><br/>
    /// shows layer overlay using the color in <see cref="ViewLayer.Gradient"/>for tiles in a tilemap<br/>
    /// can also display a explanation for the values using <see cref="LayerKeyVisualizer"/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/views">https://citybuilder.softleitner.com/manual/views</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_default_overlay_manager.html")]
    [RequireComponent(typeof(Tilemap))]
    public class DefaultOverlayManager : MonoBehaviour, IOverlayManager
    {
        [Tooltip("neutral tile that gets coloured")]
        public TileBase Tile;
        [Tooltip("optional visualizer that explains how the layer value on a point are calculated when a LayerView is active")]
        public LayerKeyVisualizer LayerKeyVisualizer;
        [Tooltip("optional visualizer that shows the numerical connection value of the map point under the mouse when a ConnectionView is active")]
        public ConnectionValueVisualizer ConnectionValueVisualizer;

        private Tilemap _tilemap;
        private ViewEfficiency _currentEfficiencyView;
        private ViewLayer _currentLayerView;
        private ViewConnection _currentConnectionView;

        private ILayerManager _layerManager;
        private IConnectionManager _connectionManager;

        protected virtual void Awake()
        {
            Dependencies.Register<IOverlayManager>(this);
        }

        protected virtual void Start()
        {
            _tilemap = GetComponent<Tilemap>();

            _layerManager = Dependencies.GetOptional<ILayerManager>();
            if (_layerManager != null)
                _layerManager.Changed += layerChanged;

            _connectionManager = Dependencies.GetOptional<IConnectionManager>();
            if (_connectionManager != null)
                _connectionManager.Changed += connectionChanged;
        }

        public void ActivateOverlay(ViewLayer view)
        {
            var range = view.Maximum - view.Minimum;
            var bottom = -view.Minimum;

            foreach (var value in Dependencies.Get<ILayerManager>().GetValues(view.Layer))
            {
                setTile((Vector3Int)value.Item1, view.Gradient.Evaluate((float)(value.Item2 + bottom) / range));
            }

            _currentLayerView = view;

            if (LayerKeyVisualizer)
                LayerKeyVisualizer.Activate(view.Layer);
        }

        public void ActivateOverlay(ViewConnection view)
        {
            var range = view.Maximum - view.Minimum;
            var bottom = -view.Minimum;

            foreach (var value in view.GetValues())
            {
                setTile((Vector3Int)value.Key, view.Gradient.Evaluate((float)(value.Value + bottom) / range));
            }

            _currentConnectionView = view;

            if (ConnectionValueVisualizer)
                ConnectionValueVisualizer.Activate(view.Connection);
        }

        public void ActivateOverlay(ViewEfficiency view)
        {
            _currentEfficiencyView = view;

            refreshEfficiencyOverlay();
            this.StartChecker(refreshEfficiencyOverlay);
        }

        public void ClearOverlay()
        {
            StopAllCoroutines();

            _tilemap.ClearAllTiles();

            _currentLayerView = null;
            _currentConnectionView = null;
            _currentEfficiencyView = null;

            if (LayerKeyVisualizer)
                LayerKeyVisualizer.Deactivate();

            if (ConnectionValueVisualizer)
                ConnectionValueVisualizer.Deactivate();
        }

        private void layerChanged(Layer layer)
        {
            if (_currentLayerView && _currentLayerView.Layer == layer)
                refreshLayerOverlay();
        }
        private void refreshLayerOverlay()
        {
            var current = _currentLayerView;
            ClearOverlay();
            _currentLayerView = current;
            ActivateOverlay(_currentLayerView);
        }

        private void connectionChanged(Connection connection)
        {
            if (_currentConnectionView && _currentConnectionView.Connection == connection)
                refreshConnectionOverlay();
        }
        private void refreshConnectionOverlay()
        {
            var current = _currentConnectionView;
            ClearOverlay();
            _currentConnectionView = current;
            ActivateOverlay(current);
        }

        private void refreshEfficiencyOverlay()
        {
            _tilemap.ClearAllTiles();

            var buildingManager = Dependencies.Get<IBuildingManager>();
            foreach (var building in buildingManager.GetBuildings().Where(b => b.HasBuildingPart<IEfficiencyFactor>()))
            {
                var efficiency = building.Efficiency;
                foreach (var point in PositionHelper.GetStructurePositions(building.Point, building.Size))
                {
                    setTile((Vector3Int)point, _currentEfficiencyView.Gradient.Evaluate(efficiency));
                }
            }
        }

        private void setTile(Vector3Int point, Color color)
        {
            _tilemap.SetTile(point, Tile);
            _tilemap.SetTileFlags(point, TileFlags.None);
            _tilemap.SetColor(point, color);
        }
    }
}