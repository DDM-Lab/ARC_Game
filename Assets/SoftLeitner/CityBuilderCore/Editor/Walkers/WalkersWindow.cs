using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityBuilderCore.Editor
{
    public class WalkersWindow : EditorWindow
    {
        [MenuItem("Window/CityBuilder/Walkers")]
        public static void ShowWindow()
        {
            GetWindow<WalkersWindow>().Show();
        }

        public static WalkersWindow Instance { get; private set; }

        public static VisualTreeAsset WalkerTemplate { get; private set; }

        public static Texture2D CopyIcon { get; private set; }
        public static Texture2D DeleteIcon { get; private set; }
        public static Texture2D FilterIcon { get; private set; }
        public static Texture2D RefreshIcon { get; private set; }

        private WalkerInfoSet _set;

        private Walker _filter;

        private void OnEnable()
        {
            Instance = this;

            titleContent = new GUIContent("CCBK Walkers");

            var scriptPath = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));

            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(Path.ChangeExtension(scriptPath, "uss"));
            var layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Path.ChangeExtension(scriptPath, "uxml"));

            WalkerTemplate = (VisualTreeAsset)EditorGUIUtility.Load(scriptPath.Replace("WalkersWindow.cs", "WalkerElement.uxml"));

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

        public void Filter(Walker walker)
        {
            _filter = walker;

            initialize();
        }

        public void ClearFilter()
        {
            _filter = null;

            initialize();
        }

        public void Copy(WalkerInfo info)
        {
            var result = EditorDialog.Show("New Walker", "Key", "Name", null, info.Key, info.Name, null);
            if (result == null)
                return;

            var newInfo = EditorHelper.DuplicateAsset(info, result.Item1, result.Item2 + "Info");

            _set.Add(newInfo);
            EditorUtility.SetDirty(_set);

            if (info.Prefab)
            {
                newInfo.Prefab = EditorHelper.DuplicateAsset(info.Prefab, result.Item1, result.Item2);
                newInfo.Prefab.GetComponent<Walker>().Info = newInfo;
            }

            EditorUtility.SetDirty(newInfo);

            AssetDatabase.SaveAssets();
            initialize();
        }

        public void Delete(WalkerInfo info)
        {
            _set.Remove(info);
            EditorUtility.SetDirty(_set);

            if (info.Prefab)
                AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(info.Prefab));
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
            var sets = AssetDatabase.FindAssets("t:" + typeof(WalkerInfoSet).FullName).Select(id => AssetDatabase.LoadAssetAtPath<WalkerInfoSet>(AssetDatabase.GUIDToAssetPath(id))).OrderBy(s => s.name).ToList();
            if (sets.Count == 0)
                return false;

            if (!sets.Contains(_set))
                _set = sets[0];

            var setsElement = rootVisualElement.Q("SetsElement");
            var setsField = new PopupField<WalkerInfoSet>(sets, _set, s => s.name, s => s.name);
            setsField.RegisterCallback<ChangeEvent<WalkerInfoSet>>(e =>
            {
                _set = e.newValue;
                initializeItems();
            });

            setsElement.Clear();
            setsElement.Add(setsField);

            var filterElement = rootVisualElement.Q("FilterElement");
            var filterLabel = rootVisualElement.Q<Label>("FilterLabel");
            var filterButton = rootVisualElement.Q<Button>("FilterButton");

            filterElement.visible = _filter != null;
            filterButton.style.backgroundImage = DeleteIcon;
            filterButton.clicked -= ClearFilter;
            filterButton.clicked += ClearFilter;

            if (_filter != null)
                filterLabel.text = _filter.GetType().Name.Replace("Walker", string.Empty);

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

            if (_filter != null)
                objects = objects.Where(o => _filter.GetType().IsAssignableFrom(o.Prefab.GetType())).ToList();

            list.itemsSource = new List<WalkerInfo>();
            list.makeItem = () => new WalkerElement();
            list.bindItem = (e, i) => ((WalkerElement)e).SetInfo(objects.ElementAtOrDefault(i));
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