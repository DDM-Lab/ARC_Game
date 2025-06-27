#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// custom editor for object sets that add a couple buttons for automatically filling out the objects
    /// </summary>
    [CustomEditor(typeof(ObjectSetBase), true)]
    public class ObjectSetEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            ObjectSetBase set = (ObjectSetBase)target;

            if (GUILayout.Button("Find All"))
            {
                set.SetObjects(Resources.FindObjectsOfTypeAll(set.GetObjectType()));
                EditorUtility.SetDirty(target);
            }

            if (GUILayout.Button("Find in Folder"))
            {
                var path = AssetDatabase.GetAssetPath(set);
                path = path.Substring(0, path.LastIndexOf('/'));

                List<object> objects = new List<object>();

                foreach (var guid in AssetDatabase.FindAssets(string.Empty, new string[] { path }))
                {
                    var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    var asset = AssetDatabase.LoadAssetAtPath(assetPath, set.GetObjectType());
                    if (asset != null)
                        objects.Add(asset);
                }

                set.SetObjects(objects.ToArray());
                EditorUtility.SetDirty(target);
            }
        }
    }
}
#endif