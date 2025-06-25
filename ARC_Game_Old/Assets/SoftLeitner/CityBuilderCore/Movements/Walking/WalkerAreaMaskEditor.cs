#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace CityBuilderCore
{
    [CustomEditor(typeof(WalkerAreaMask))]
    public class WalkerAreaMaskEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            var areaMaskProperty = serializedObject.FindProperty("AreaMask");

            //Initially needed data
#if UNITY_6000_0_OR_NEWER
            var areaNames = UnityEngine.AI.NavMesh.GetAreaNames();
#else
            var areaNames = GameObjectUtility.GetNavMeshAreaNames();
#endif
            var currentMask = areaMaskProperty.longValue;
            var compressedMask = 0;

            if (currentMask == 0xffffffff)
            {
                compressedMask = ~0;
            }
            else
            {
                //Need to find the index as the list of names will compress out empty areas
                for (var i = 0; i < areaNames.Length; i++)
                {
#if UNITY_6000_0_OR_NEWER
                    var areaIndex = UnityEngine.AI.NavMesh.GetAreaFromName(areaNames[i]);
#else
                    var areaIndex = GameObjectUtility.GetNavMeshAreaFromName(areaNames[i]);
#endif
                    if (((1 << areaIndex) & currentMask) != 0)
                        compressedMask = compressedMask | (1 << i);
                }
            }

            var position = EditorGUILayout.GetControlRect();
            EditorGUI.BeginProperty(position, GUIContent.none, areaMaskProperty);

            EditorGUI.BeginChangeCheck();
            EditorGUI.showMixedValue = areaMaskProperty.hasMultipleDifferentValues;
            var areaMask = EditorGUI.MaskField(position, new GUIContent("Area Mask"), compressedMask, areaNames, EditorStyles.layerMaskField);
            EditorGUI.showMixedValue = false;

            if (EditorGUI.EndChangeCheck())
            {
                if (areaMask == ~0)
                {
                    areaMaskProperty.longValue = 0xffffffff;
                }
                else
                {
                    uint newMask = 0;
                    for (var i = 0; i < areaNames.Length; i++)
                    {
                        //If the bit has been set in the compacted mask
                        if (((areaMask >> i) & 1) != 0)
                        {
                            //Find out the 'real' layer from the name, then set it in the new mask
                            newMask = newMask | (uint)(1 << UnityEngine.AI.NavMesh.GetAreaFromName(areaNames[i]));
                        }
                    }
                    areaMaskProperty.longValue = newMask;
                }
            }
            EditorGUI.EndProperty();

            serializedObject.ApplyModifiedProperties();

        }
    }
}
#endif