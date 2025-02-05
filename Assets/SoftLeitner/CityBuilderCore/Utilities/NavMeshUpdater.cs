using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace CityBuilderCore
{
    /// <summary>
    /// can be used to update the navmesh at runtime using known components<br/>
    /// source creation taken from https://github.com/Unity-Technologies/NavMeshComponents
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_nav_mesh_updater.html")]
    public class NavMeshUpdater : MonoBehaviour
    {
        [Tooltip("Bounding box relative to position and rotation which describes the volume where the NavMesh should be built.")]
        public Bounds Bounds;
        [Header("Sources")]
        [Tooltip("these terrains are passed to nav mesh as sources")]
        public Terrain[] Terrains;
        [Tooltip("meshes passed to nav mesh as sources with area 0")]
        public MeshFilter[] Meshes;
        [Tooltip("meshes passed to nav mesh as sources with area 1")]
        public MeshFilter[] MeshesNotWalkable;
        [Header("Agent")]
        [Tooltip("The radius of the agent for baking in world units.")]
        public float AgentRadius = 0.5f;
        [Tooltip("The height of the agent for baking in world units.")]
        public float AgentHeight = 2f;
        [Tooltip("The maximum slope angle which is walkable (angle in degrees).")]
        public float MaxSlope = 45;
        [Tooltip("The maximum vertical step size an agent can take.")]
        public float StepHeight = 0.5f;

        private NavMeshData _navMeshData;
        private NavMeshDataInstance _navMeshInstance;
        private List<NavMeshBuildSource> _sources;

        public void UpdateNavMesh()
        {
            if (_sources == null)
            {
                _navMeshData = new NavMeshData();
                _navMeshInstance = NavMesh.AddNavMeshData(_navMeshData);

                _sources = new List<NavMeshBuildSource>();
            }
            else
            {
                _sources.Clear();
            }

            for (var i = 0; i < Meshes.Length; ++i)
            {
                var mf = Meshes[i];
                if (mf == null) continue;

                var m = mf.sharedMesh;
                if (m == null) continue;

                var s = new NavMeshBuildSource();
                s.shape = NavMeshBuildSourceShape.Mesh;
                s.sourceObject = m;
                s.transform = mf.transform.localToWorldMatrix;
                s.area = 0;
                _sources.Add(s);
            }

            for (var i = 0; i < MeshesNotWalkable.Length; ++i)
            {
                var mf = MeshesNotWalkable[i];
                if (mf == null) continue;

                var m = mf.sharedMesh;
                if (m == null) continue;

                var s = new NavMeshBuildSource();
                s.shape = NavMeshBuildSourceShape.Mesh;
                s.sourceObject = m;
                s.transform = mf.transform.localToWorldMatrix;
                s.area = 1;
                _sources.Add(s);
            }

            for (var i = 0; i < Terrains.Length; ++i)
            {
                var t = Terrains[i];
                if (t == null) continue;

                var s = new NavMeshBuildSource();
                s.shape = NavMeshBuildSourceShape.Terrain;
                s.sourceObject = t.terrainData;
                // Terrain system only supports translation - so we pass translation only to back-end
                s.transform = Matrix4x4.TRS(t.transform.position, Quaternion.identity, Vector3.one);
                s.area = 0;
                _sources.Add(s);
            }

            var settings = new NavMeshBuildSettings()
            {
                agentRadius = AgentRadius,
                agentHeight = AgentHeight,
                agentSlope = MaxSlope,
                agentClimb = StepHeight,
                minRegionArea = 2,
                tileSize = 256,
                voxelSize = 0.166667f
            };

            NavMeshBuilder.UpdateNavMeshData(_navMeshData, settings, _sources, Bounds);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(Bounds.center, Bounds.size);
        }
    }
}
