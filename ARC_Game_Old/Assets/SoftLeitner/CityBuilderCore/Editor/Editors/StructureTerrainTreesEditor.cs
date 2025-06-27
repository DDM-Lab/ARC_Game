using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CityBuilderCore.Editor
{
    [CustomEditor(typeof(StructureTerrainTrees))]
    public class StructureTerrainTreesEditor : UnityEditor.Editor
    {
        private int _num;

        private IMap _map;
        private IGridPositions _gridPositions;
        private IGridHeights _gridHeights;

        private bool _isPlacing;
        private bool _isValid;
        private Vector3 _position;

        private void OnEnable()
        {
            _map = this.FindObjects<MonoBehaviour>().OfType<IMap>().FirstOrDefault();
            _gridPositions = this.FindObjects<MonoBehaviour>().OfType<IGridPositions>().FirstOrDefault();
            _gridHeights = this.FindObjects<MonoBehaviour>().OfType<IGridHeights>().FirstOrDefault();
        }

        private void OnDisable()
        {
            if (_isPlacing)
                stopPlacing();
        }

        private void OnSceneGUI()
        {
            if (_map == null || _gridPositions == null || !_isPlacing)
                return;

            _isValid = false;
            if (_map != null && _gridPositions != null)
            {
                if (EditorHelper.GetWorldPosition(out _position, _map))
                {
                    _isValid = true;
                    Handles.DrawWireDisc(EditorHelper.ApplyEditorHeight(_map, _gridHeights, _gridPositions.GetWorldCenterPosition(_position)), Vector3.up, _map.CellOffset.x / 2f);
                }
            }

            SceneView.RepaintAll();
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            EditorGUILayout.Space();

            if (_isPlacing)
            {
                if (GUILayout.Button(new GUIContent("Stop Placing", "click in scene view to place, needs gizmos visible")))
                    stopPlacing();
            }
            else
            {
                if (GUILayout.Button(new GUIContent("Start Placing", "click in scene view to place, needs gizmos visible")))
                    startPlacing();
            }

            EditorGUILayout.BeginHorizontal();
            _num = EditorGUILayout.IntField(_num);
            if (GUILayout.Button("Add Random"))
            {
                var trees = (StructureTerrainTrees)target;
                var terrain = trees.TerrainModifier.GetComponent<Terrain>();

                var treeInstances = terrain.terrainData.treeInstances.ToList();

                for (int i = 0; i < _num; i++)
                {
                    var size = Random.Range(trees.MinHeight, trees.MaxHeight);
                    var color = 1f - Random.Range(0, trees.ColorVariation);

                    treeInstances.Add(new TreeInstance()
                    {
                        prototypeIndex = trees.Index,
                        position = new Vector3(Random.Range(0, 1f), 0f, Random.Range(0, 1f)),
                        heightScale = size,
                        widthScale = size,
                        color = new Color(color, color, color),
                        lightmapColor = Color.white
                    });
                }

                terrain.terrainData.SetTreeInstances(treeInstances.ToArray(), true);
            }
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Clear"))
            {
                var trees = (StructureTerrainTrees)target;
                var terrain = trees.TerrainModifier.GetComponent<Terrain>();

                terrain.terrainData.SetTreeInstances(terrain.terrainData.treeInstances.Where(i => i.prototypeIndex != trees.Index).ToArray(), true);
            }
        }

        private void startPlacing()
        {
            SceneView.beforeSceneGui += beforeSceneGui;
            _isPlacing = true;
        }
        private void stopPlacing()
        {
            SceneView.beforeSceneGui -= beforeSceneGui;
            _isPlacing = false;
        }

        private void place()
        {
            var structure = (StructureTerrainTrees)target;
            var point = _gridPositions.GetGridPoint(_position);

            if (Application.isPlaying)
            {
                if (structure.HasPoint(point))
                    structure.Remove(point);
                else
                    structure.Add(point);
            }
            else
            {
                var terrain = structure.TerrainModifier.GetComponent<Terrain>();
                Undo.RegisterCompleteObjectUndo(terrain.terrainData, "Place Tree");

                for (int i = 0; i < terrain.terrainData.treeInstances.Length; i++)
                {
                    if (getTreePoint(terrain, terrain.terrainData.treeInstances[i].position) == point)
                    {
                        var trees = terrain.terrainData.treeInstances.ToList();
                        trees.RemoveAt(i);
                        terrain.terrainData.SetTreeInstances(trees.ToArray(), false);
                        return;
                    }
                }

                var position = _gridPositions.GetWorldCenterPosition(point);
                position.y = terrain.SampleHeight(position);
                position = new Vector3(position.x / terrain.terrainData.size.x, position.y / terrain.terrainData.size.y, position.z / terrain.terrainData.size.z);

                var size = Random.Range(structure.MinHeight, structure.MaxHeight);
                var color = 1f - Random.Range(0, structure.ColorVariation);

                terrain.AddTreeInstance(new TreeInstance()
                {
                    prototypeIndex = structure.Index,
                    position = position,
                    heightScale = size,
                    widthScale = size,
                    color = new Color(color, color, color),
                    lightmapColor = Color.white
                });
            }
        }

        private Vector2Int getTreePoint(Terrain terrain, Vector3 position)
        {
            return _gridPositions.GetGridPoint(Vector3.Scale(position, terrain.terrainData.size));
        }

        private void beforeSceneGui(SceneView sceneView)
        {
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                Event.current.Use();
            }

            if (Event.current.type == EventType.MouseUp && Event.current.button == 0)
            {
                Event.current.Use();
                if (_isValid)
                    place();
            }
        }
    }
}