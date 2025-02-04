using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CityBuilderCore.Editor
{
    [CustomEditor(typeof(StructureCollection), true)]
    [CanEditMultipleObjects]
    public class StructureCollectionEditor : UnityEditor.Editor
    {
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
            var structureCollection = (StructureCollection)target;
            var point = _gridPositions.GetGridPoint(_position);

            if (Application.isPlaying)
            {
                if (structureCollection.HasPoint(point))
                    structureCollection.Remove(point);
                else
                    structureCollection.Add(point);
            }
            else
            {
                foreach (Transform child in structureCollection.transform)
                {
                    if (_gridPositions.GetGridPoint(child.position) == point)
                    {
                        Undo.DestroyObjectImmediate(child.gameObject);
                        return;
                    }
                }

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(structureCollection.Prefab, structureCollection.transform);
                if (structureCollection.AddInCenter)
                    instance.transform.position = EditorHelper.ApplyEditorHeight(_map, _gridHeights, _gridPositions.GetWorldCenterPosition(_position));
                else
                    instance.transform.position = EditorHelper.ApplyEditorHeightCenter(_map, _gridHeights, _gridPositions, _gridPositions.GetWorldPosition(_position));
                Undo.RegisterCreatedObjectUndo(instance, "Place " + structureCollection.GetName());
            }
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