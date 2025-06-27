using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityBuilderCore.Editor
{
    public class BuildingsWindow : EditorWindow
    {
        [MenuItem("Window/CityBuilder/Buildings")]
        public static void ShowWindow()
        {
            GetWindow<BuildingsWindow>().Show();
        }

        public static BuildingsWindow Instance { get; private set; }

        public static VisualTreeAsset BuildingTemplate { get; private set; }
        public static VisualTreeAsset BuildingComponentTemplate { get; private set; }
        public static VisualTreeAsset BuildingCategoryTemplate { get; private set; }

        public static Texture2D CopyIcon { get; private set; }
        public static Texture2D DeleteIcon { get; private set; }
        public static Texture2D FilterIcon { get; private set; }
        public static Texture2D RefreshIcon { get; private set; }

        private BuildingCategory[] _categories;
        private BuildingInfoSet _set;

        private IBuildingComponent _filterComponent;
        private BuildingCategory _filterCategory;

        private void OnEnable()
        {
            Instance = this;

            titleContent = new GUIContent("CCBK Buildings");

            var scriptPath = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));

            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(Path.ChangeExtension(scriptPath, "uss"));
            var layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Path.ChangeExtension(scriptPath, "uxml"));

            BuildingTemplate = (VisualTreeAsset)EditorGUIUtility.Load(scriptPath.Replace("BuildingsWindow.cs", "BuildingElement.uxml"));
            BuildingComponentTemplate = (VisualTreeAsset)EditorGUIUtility.Load(scriptPath.Replace("BuildingsWindow.cs", "BuildingComponentElement.uxml"));
            BuildingCategoryTemplate = (VisualTreeAsset)EditorGUIUtility.Load(scriptPath.Replace("BuildingsWindow.cs", "BuildingCategoryElement.uxml"));

            CopyIcon = Resources.Load<Texture2D>("ccbk_copy");
            DeleteIcon = Resources.Load<Texture2D>("ccbk_delete");
            FilterIcon = Resources.Load<Texture2D>("ccbk_filter");
            RefreshIcon = Resources.Load<Texture2D>("ccbk_refresh");

            rootVisualElement.styleSheets.Add(style);

            layout.CloneTree(rootVisualElement);

            initialize();
        }

        private void OnDisable()
        {
            Instance = null;
        }

        public IEnumerable<BuildingCategory> GetCategories(BuildingInfo info)
        {
            return _categories.Where(c => c.Contains(info));
        }

        public void Filter(IBuildingComponent component)
        {
            _filterComponent = component;
            _filterCategory = null;

            initialize();
        }

        public void Filter(BuildingCategory category)
        {
            _filterComponent = null;
            _filterCategory = category;

            initialize();
        }

        public void ClearFilter()
        {
            _filterComponent = null;
            _filterCategory = null;

            initialize();
        }

        public void Copy(BuildingInfo info)
        {
            var result = EditorDialog.Show("New Building", "Key", "Name", null, info.Key, info.Name, null);
            if (result == null)
                return;

            var newInfo = EditorHelper.DuplicateAsset(info, result.Item1, result.Item2 + "Info");

            _set.Add(newInfo);
            EditorUtility.SetDirty(_set);

            if (info.Prefab)
            {
                newInfo.Prefab = EditorHelper.DuplicateAsset(info.Prefab, result.Item1, result.Item2);
                newInfo.Prefab.Info = newInfo;
            }

            if (info.Ghost)
            {
                newInfo.Ghost = EditorHelper.DuplicateAsset(info.Ghost, result.Item1 + "Ghost", result.Item2);
            }

            EditorUtility.SetDirty(newInfo);

            AssetDatabase.SaveAssets();
            initialize();
        }

        public void Delete(BuildingInfo info)
        {
            _set.Remove(info);
            EditorUtility.SetDirty(_set);

            if (info.Prefab)
                AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(info.Prefab));
            if (info.Ghost)
                AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(info.Ghost));
            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(info));

            AssetDatabase.SaveAssets();
            initialize();
        }

        private void initialize()
        {
            if (initializeHeader())
                initializeItems();
        }

        private bool initializeHeader()
        {
            _categories = AssetDatabase.FindAssets("t:" + typeof(BuildingCategory).FullName).Select(id => AssetDatabase.LoadAssetAtPath<BuildingCategory>(AssetDatabase.GUIDToAssetPath(id))).OrderBy(s => s.NameSingular).ToArray();

            var sets = AssetDatabase.FindAssets("t:" + typeof(BuildingInfoSet).FullName).Select(id => AssetDatabase.LoadAssetAtPath<BuildingInfoSet>(AssetDatabase.GUIDToAssetPath(id))).OrderBy(s => s.name).ToList();
            if (sets.Count == 0)
                return false;

            if (!sets.Contains(_set))
                _set = sets[0];

            var setsElement = rootVisualElement.Q("SetsElement");
            var setsField = new PopupField<BuildingInfoSet>(sets, _set, s => s.name, s => s.name);
            setsField.RegisterCallback<ChangeEvent<BuildingInfoSet>>(e =>
            {
                _set = e.newValue;
                initializeItems();
            });

            setsElement.Clear();
            setsElement.Add(setsField);

            var filterElement = rootVisualElement.Q("FilterElement");
            var filterLabel = rootVisualElement.Q<Label>("FilterLabel");
            var filterButton = rootVisualElement.Q<Button>("FilterButton");

            filterElement.visible = _filterComponent != null || _filterCategory != null;
            filterButton.style.backgroundImage = DeleteIcon;
            filterButton.clicked -= ClearFilter;
            filterButton.clicked += ClearFilter;

            if (_filterComponent != null)
                filterLabel.text = _filterComponent.GetType().Name.Replace("Component", string.Empty);
            else if (_filterCategory != null)
                filterLabel.text = _filterCategory.NameSingular;

            var reloadButton = rootVisualElement.Q<Button>("ReloadButton");
            reloadButton.style.backgroundImage = RefreshIcon;
            reloadButton.clicked -= initialize;
            reloadButton.clicked += initialize;

            return true;
        }

        private void initializeItems()
        {
            var list = rootVisualElement.Q<ListView>();
            if (list == null)
                return;

            var objects = _set.Objects.Where(o => o && o.Prefab).OrderBy(o => o.Name).ToList();

            if (_filterComponent != null)
                objects = objects.Where(o => o.Prefab.GetComponent(_filterComponent.GetType())).ToList();
            if (_filterCategory != null)
                objects = objects.Where(o => _filterCategory.Contains(o)).ToList();

            list.itemsSource = new List<BuildingInfo>();
            list.makeItem = () => new BuildingElement();
            list.bindItem = (e, i) => ((BuildingElement)e).SetInfo(objects.ElementAtOrDefault(i));
            list.itemsSource = objects;
            list.selectionType = SelectionType.None;
#if UNITY_2021_3_OR_NEWER
            list.fixedItemHeight = 70;
#else
            list.itemHeight = 70;
#endif
        }
    }
}