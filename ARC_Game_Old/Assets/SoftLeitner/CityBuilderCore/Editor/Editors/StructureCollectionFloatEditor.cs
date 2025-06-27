using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CityBuilderCore.Editor
{
    [CustomEditor(typeof(StructureCollectionFloat), true)]
    [CanEditMultipleObjects]
    public class StructureCollectionFloatEditor : UnityEditor.Editor
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
            var structureCollection = (StructureCollectionFloat)target;
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

                StructureCollectionFloat.Variant variant;
                switch (_prefabSelection)
                {
                    case PrefabSelection.Random:
                        variant = structureCollection.Variants.Random();
                        break;
                    case PrefabSelection.Alternating:
                        variant = structureCollection.Variants.ElementAtOrDefault(_prefabIndex);
                        _prefabIndex++;
                        if (_prefabIndex >= structureCollection.Variants.Length - 1)
                            _prefabIndex = 0;
                        break;
                    default:
                    case PrefabSelection.First:
                        variant = structureCollection.Variants.ElementAtOrDefault(0);
                        break;
                    case PrefabSelection.Second:
                        variant = structureCollection.Variants.ElementAtOrDefault(1);
                        break;
                    case PrefabSelection.Third:
                        variant = structureCollection.Variants.ElementAtOrDefault(2);
                        break;
                    case PrefabSelection.Fourth:
                        variant = structureCollection.Variants.ElementAtOrDefault(3);
                        break;
                    case PrefabSelection.Fifth:
                        variant = structureCollection.Variants.ElementAtOrDefault(4);
                        break;
                }

                if (variant == null)
                    return;

                var instance = (GameObject)PrefabUtility.InstantiatePrefab(variant.Prefab, structureCollection.transform);
                variant.Adjust(instance, point, _map, _gridPositions, _gridRotations); 
                instance.transform.position = EditorHelper.ApplyEditorHeight(_map, _gridHeights, instance.transform.position);
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