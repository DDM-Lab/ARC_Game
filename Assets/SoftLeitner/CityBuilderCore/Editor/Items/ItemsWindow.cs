using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityBuilderCore.Editor
{
    public class ItemsWindow : EditorWindow
    {
        [MenuItem("Window/CityBuilder/Items")]
        public static void ShowWindow()
        {
            GetWindow<ItemsWindow>().Show();
        }

        public static ItemsWindow Instance { get; private set; }

        public static VisualTreeAsset ItemTemplate { get; private set; }

        public static Texture2D CopyIcon { get; private set; }
        public static Texture2D DeleteIcon { get; private set; }
        public static Texture2D FilterIcon { get; private set; }
        public static Texture2D RefreshIcon { get; private set; }

        private ItemSet _set;

        private void OnEnable()
        {
            Instance = this;

            titleContent = new GUIContent("CCBK Items");

            var scriptPath = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(this));

            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(Path.ChangeExtension(scriptPath, "uss"));
            var layout = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(Path.ChangeExtension(scriptPath, "uxml"));

            ItemTemplate = (VisualTreeAsset)EditorGUIUtility.Load(scriptPath.Replace("ItemsWindow.cs", "ItemElement.uxml"));

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

        public void Copy(Item item)
        {
            var result = EditorDialog.Show("New Item", "Key", "Name", null, item.Key, item.Name, null);
            if (result == null)
                return;

            var newItem = EditorHelper.DuplicateAsset(item, result.Item1, result.Item2);

            _set.Add(newItem);
            EditorUtility.SetDirty(_set);

            if (item.Icon)
                newItem.Icon = EditorHelper.DuplicateAsset(item.Icon, result.Item1, result.Item2 + "Icon");

            if (item.Visuals != null)
            {
                for (int i = 0; i < item.Visuals.Length; i++)
                {
                    newItem.Visuals[i] = EditorHelper.DuplicateAsset(item.Visuals[i], result.Item1, result.Item2 + "Visual" + i.ToString());
                }
            }

            AssetDatabase.SaveAssets();
            initialize();
        }

        public void Delete(Item item)
        {
            _set.Remove(item);
            EditorUtility.SetDirty(_set);

            if (item.Icon)
                AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(item.Icon));

            if (item.Visuals != null)
            {
                foreach (var visual in item.Visuals)
                {
                    AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(visual));
                }
            }

            AssetDatabase.DeleteAsset(AssetDatabase.GetAssetPath(item));

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
            var sets = AssetDatabase.FindAssets("t:" + typeof(ItemSet).FullName).Select(id => AssetDatabase.LoadAssetAtPath<ItemSet>(AssetDatabase.GUIDToAssetPath(id))).OrderBy(s => s.name).ToList();
            if (sets.Count == 0)
                return false;

            if (!sets.Contains(_set))
                _set = sets[0];

            var setsElement = rootVisualElement.Q("SetsElement");
            var setsField = new PopupField<ItemSet>(sets, _set, s => s.name, s => s.name);
            setsField.RegisterCallback<ChangeEvent<ItemSet>>(e =>
            {
                _set = e.newValue;
                initializeItems();
            });

            setsElement.Clear();
            setsElement.Add(setsField);

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

            var objects = _set.Objects.Where(o => o).OrderBy(o => o.Name).ToList();

            list.itemsSource = new List<Item>();
            list.makeItem = () => new ItemElement();
            list.bindItem = (e, i) => ((ItemElement)e).SetItem(objects.ElementAtOrDefault(i));
            list.itemsSource = objects;
            list.selectionType = SelectionType.None;
#if UNITY_2021_3_OR_NEWER
            list.fixedItemHeight = 50;
#else
            list.itemHeight = 50;
#endif
        }
    }
}