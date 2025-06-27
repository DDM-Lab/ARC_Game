using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// view that displays an overlay for a layer on a tilemap
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/views">https://citybuilder.softleitner.com/manual/views</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_view_layer.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Views/" + nameof(ViewConnection))]
    public class ViewConnection : View
    {
        [Tooltip("the connection to visualize with this view")]
        public Connection Connection;
        [Tooltip("gradient that determines the color for any connection value that will be displayed as an overlay")]
        public Gradient Gradient;
        [Tooltip("connection value for which the lowest gradient value will be used")]
        public int Minimum;
        [Tooltip("connection value at which the highest gradient value will be used")]
        public int Maximum;

        private bool _isActive;
        private List<IConnectionFeeder> _previewFeeders;
        private ConnectionGrid _previewGrid;

        public override void Activate()
        {
            _isActive = true;
            Dependencies.Get<IOverlayManager>().ActivateOverlay(this);
        }
        public override void Deactivate()
        {
            _isActive = false;
            Dependencies.Get<IOverlayManager>().ClearOverlay();
        }

        public Dictionary<Vector2Int, int> GetValues()
        {
            if (_previewGrid == null)
                return Dependencies.Get<IConnectionManager>().GetValues(Connection);
            else
                return _previewGrid.GetValues();
        }

        public void AddPreviewFeeder(IConnectionFeeder feeder)
        {
            if (_previewGrid == null)
                _previewGrid = Dependencies.Get<IConnectionManager>().GetGrid(Connection).CreatePreview();
            if (_previewFeeders == null)
                _previewFeeders = new List<IConnectionFeeder>();

            feeder.PointsChanged += feederPointsChanged;
            feeder.FeederValueChanged += feederFeederValueChanged;

            _previewFeeders.Add(feeder);
            _previewGrid.RegisterFeeder(feeder);
            
            refreshPreview();
        }

        public void RemovePreviewFeeder(IConnectionFeeder feeder)
        {
            if (_previewFeeders == null)
                _previewFeeders = new List<IConnectionFeeder>();

            feeder.PointsChanged -= feederPointsChanged;
            feeder.FeederValueChanged -= feederFeederValueChanged;

            _previewFeeders.Remove(feeder);
            _previewGrid?.DeregisterFeeder(feeder);

            if (_previewGrid != null && _previewFeeders.Count == 0)
                _previewGrid = null;

            refreshPreview();
        }

        private void feederPointsChanged(PointsChanged<IConnectionPasser> _) => refreshPreview();
        private void feederFeederValueChanged(IConnectionFeeder _) => refreshPreview();

        private void refreshPreview()
        {
            _previewGrid?.Check();

            if (_isActive)
            {
                Deactivate();
                Activate();
            }
        }
    }
}