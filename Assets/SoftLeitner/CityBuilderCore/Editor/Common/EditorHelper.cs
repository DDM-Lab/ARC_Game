using System.IO;
using UnityEditor;
using UnityEngine;

namespace CityBuilderCore.Editor
{
    public static class EditorHelper
    {
        public static T DuplicateAsset<T>(T asset) where T : Object
        {
            var path = AssetDatabase.GetAssetPath(asset);
            var extension = Path.GetExtension(path);

            var counter = 0;
            string newPath;

            do
            {
                counter++;
                newPath = path.Replace(extension, string.Empty) + counter + extension;
            }
            while (AssetDatabase.LoadAssetAtPath<Object>(newPath));

            AssetDatabase.CopyAsset(path, newPath);

            var newAsset = AssetDatabase.LoadAssetAtPath<T>(newPath);

            if (newAsset is BuildingInfo building)
            {
                building.Key += counter;
                building.Name += counter;
            }
            else if (newAsset is WalkerInfo walker)
            {
                walker.Name += counter;
            }
            else if (newAsset is Item item)
            {
                item.Name += counter;
            }

            EditorUtility.SetDirty(newAsset);

            return newAsset;
        }

        public static T DuplicateAsset<T>(T asset, string key, string name) where T : Object
        {
            var path = AssetDatabase.GetAssetPath(asset);
            var filename = Path.GetFileName(path);
            var extension = Path.GetExtension(path);

            var counter = -1;
            string newPath;

            do
            {
                counter++;
                newPath = path.Replace(filename, name) + (counter == 0 ? string.Empty : counter.ToString()) + extension;
            }
            while (AssetDatabase.LoadAssetAtPath<Object>(newPath));

            AssetDatabase.CopyAsset(path, newPath);

            var newAsset = AssetDatabase.LoadAssetAtPath<T>(newPath);

            string postfix = counter == 0 ? string.Empty : counter.ToString();

            if (newAsset is BuildingInfo building)
            {
                building.Key = key + postfix;
                building.Name = name.Replace("Info", string.Empty) + postfix;
            }
            else if (newAsset is WalkerInfo walker)
            {
                walker.Key = key + postfix;
                walker.Name = name.Replace("Info", string.Empty) + postfix;
            }
            else if (newAsset is Item item)
            {
                item.Key = key + postfix;
                item.Name = name.Replace("Info", string.Empty) + postfix;
            }

            EditorUtility.SetDirty(newAsset);

            return newAsset;
        }

        public static bool GetWorldPosition(out Vector3 position, IMap map)
        {
            if (HandleUtility.PlaceObject(Event.current.mousePosition, out position, out var _))
                return true;

            Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            Plane plane;
            if (map.IsXY)
                plane = new Plane(new Vector3(0, 0, 1), 0f);
            else
                plane = new Plane(new Vector3(0, 1, 0), 0f);

            if (plane.Raycast(ray, out var enter))
            {
                position = ray.GetPoint(enter);
                return true;//no point found
            }

            return false;
        }

        public static Vector3 ApplyEditorHeight(IMap map, IGridHeights gridHeights, Vector3 position) => ApplyEditorHeight(map, gridHeights, position, position);
        public static Vector3 ApplyEditorHeightCenter(IMap map, IGridHeights gridHeights, IGridPositions gridPositions, Vector3 position) => ApplyEditorHeight(map, gridHeights, position, gridPositions.GetCenterFromPosition(position));
        public static Vector3 ApplyEditorHeight(IMap map, IGridHeights gridHeights, Vector3 position, Vector3 heightPosition)
        {
            if (gridHeights == null)
                return position;

            var height = gridHeights.GetHeight(heightPosition);
            if (height == 0f)
                return position;

            if (map.IsXY)
                return new Vector3(position.x, position.y, height);
            else
                return new Vector3(position.x, height, position.z);
        }
    }
}
