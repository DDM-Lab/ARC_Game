using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CityBuilderCore.Editor
{
    [CustomEditor(typeof(DefaultBuildingManager), true)]
    [CanEditMultipleObjects]
    public class DefaultBuildingManagerEditor : UnityEditor.Editor
    {
        private IMap _map;
        private IGridPositions _gridPositions;
        private IGridHeights _gridHeights;

        private BuildingInfo _buildingInfo;
        private int _index;
        private int _rotation;

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

            EditorGUILayout.BeginHorizontal();
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

            _buildingInfo = EditorGUILayout.ObjectField(_buildingInfo, typeof(BuildingInfo), false) as BuildingInfo;
            EditorGUILayout.EndHorizontal();

            _index = EditorGUILayout.IntField("Prefab Index", _index);
            _rotation = EditorGUILayout.IntSlider("Rotation", _rotation, 0, 3);
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
            var manager = (DefaultBuildingManager)target;
            var point = _gridPositions.GetGridPoint(_position);

            var buildingRotation = BuildingRotation.Create(_rotation, _map.IsHex);

            var position = _gridPositions.GetWorldPosition(buildingRotation.RotateOrigin(point, _buildingInfo.Size));
            var rotation = buildingRotation.GetRotation(_map.IsXY);

            var prefab = _buildingInfo.GetPrefab(_index);

            if (Application.isPlaying)
            {
                manager.Add(position, rotation, prefab);
            }
            else
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject, manager.transform);
                instance.transform.position = position;
                instance.transform.rotation = rotation;

                var pivot = instance.GetComponent<Building>().Pivot;
                if (pivot)
                    pivot.position = EditorHelper.ApplyEditorHeight(_map, _gridHeights, pivot.position);

                Undo.RegisterCreatedObjectUndo(instance, "Place " + prefab.GetName());
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