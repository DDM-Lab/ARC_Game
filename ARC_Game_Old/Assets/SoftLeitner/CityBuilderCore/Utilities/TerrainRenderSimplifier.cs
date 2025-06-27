using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// hides trees and details from a terrain for the camera it sits on<br/>
    /// used for the minimap in the town demo
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_terrain_render_simplifier.html")]
    [RequireComponent(typeof(Camera))]
    public class TerrainRenderSimplifier : MonoBehaviour
    {
        [Tooltip("trees and details on this terrain are hidden while the camera this sits on renders(for minimap for example)")]
        public Terrain Terrain;

        private float _defaultBillboardDistance;
        private float _defaultDetailDistance;

        private void Awake()
        {
            _defaultBillboardDistance = Terrain.treeBillboardDistance;
            _defaultDetailDistance = Terrain.detailObjectDistance;
        }

        private void OnPreCull()
        {
            Terrain.treeBillboardDistance = 0f;
            Terrain.detailObjectDistance = 0f;
        }
        void OnPostRender()
        {
            Terrain.treeBillboardDistance = _defaultBillboardDistance;
            Terrain.detailObjectDistance = _defaultDetailDistance;
        }
    }
}
