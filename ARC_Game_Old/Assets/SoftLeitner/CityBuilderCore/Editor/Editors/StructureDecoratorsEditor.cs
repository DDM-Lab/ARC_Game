using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CityBuilderCore.Editor
{
    [CustomEditor(typeof(StructureDecorators), true)]
    [CanEditMultipleObjects]
    public class StructureDecoratorsEditor : UnityEditor.Editor
    {
        public enum PrefabSelection { Random, Alternating, First, Second, Third, Fourth, Fifth }

        private IMap _map;
        private IGridPositions _gridPositions;
        private IGridRotations _gridRotations;
        private IGridHeights _gridHeights;

        private PrefabSelection _prefabSelection;

        private int _prefabIndex;
        private bool _isPlacing;
        private bool _isValid;
        private Vector3 _position;

        private void OnEnable()
        {
            _map = this.FindObjects<MonoBehaviour>().OfType<IMap>().FirstOrDefault();
            _gridPositions = this.FindObjects<MonoBehaviour>().OfType<IGridPositions>().FirstOrDefault();
            _gridRotations = this.FindObjects<MonoBehaviour>().OfType<IGridRotations>().FirstOrDefault();
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

            _prefabSelection = (PrefabSelection)EditorGUILayout.EnumPopup("PrefabSelection", _prefabSelection);
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
            var structureDecorators = (StructureDecorators)target;
            var point = _gridPositions.GetGridPoint(_position);

            if (Application.isPlaying)
            {
                if (structureDecorators.HasPoint(point))
                    structureDecorators.Remove(point);
                else
                    structureDecorators.Add(point);
            }
            else
            {
                foreach (Transform child in structureDecorators.transform)
                {
                    if (_gridPositions.GetGridPoint(child.position) == point)
                    {
                        Undo.DestroyObjectImmediate(child.gameObject);
                        return;
                    }
                }

                GameObject prefab;
                switch (_prefabSelection)
                {
                    case PrefabSelection.Random:
                        prefab = structureDecorators.Prefabs.Random();
                        break;
                    case PrefabSelection.Alternating:
                        prefab = structureDecorators.Prefabs.ElementAtOrDefault(_prefabIndex);
                        _prefabIndex++;
                        if (_prefabIndex >= structureDecorators.Prefabs.Length - 1)
                            _prefabIndex = 0;
                        break;
                    default:
                    case PrefabSelection.First:
                        prefab = structureDecorators.Prefabs.ElementAtOrDefault(0);
                        break;
                    case PrefabSelection.Second:
                        prefab = structureDecorators.Prefabs.ElementAtOrDefault(1);
                        break;
                    case PrefabSelection.Third:
                        prefab = structureDecorators.Prefabs.ElementAtOrDefault(2);
                        break;
                    case PrefabSelection.Fourth:
                        prefab = structureDecorators.Prefabs.ElementAtOrDefault(3);
                        break;
                    case PrefabSelection.Fifth:
                        prefab = structureDecorators.Prefabs.ElementAtOrDefault(4);
                        break;
                }

                if (prefab == null)
                    return;

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, structureDecorators.transform);
                instance.transform.position = EditorHelper.ApplyEditorHeight(_map, _gridHeights, _gridPositions.GetWorldCenterPosition(_position));

                switch (structureDecorators.RotationMode)
                {
                    case StructureRotationMode.Stepped:
                        _gridRotations?.SetRotation(instance.transform, Random.Range(0, 3) * 90);
                        break;
                    case StructureRotationMode.Full:
                        _gridRotations?.SetRotation(instance.transform, Random.Range(0f, 360f));
                        break;
                }

                if (structureDecorators.ScaleMinimum != 1f && structureDecorators.ScaleMaximum != 1f)
                    instance.transform.localScale *= Random.Range(structureDecorators.ScaleMinimum, structureDecorators.ScaleMaximum);

                Undo.RegisterCreatedObjectUndo(instance, "Place " + structureDecorators.GetName());
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