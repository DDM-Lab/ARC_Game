using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityBuilderCore.Editor
{
    public class BuildingElement : VisualElement
    {
        public BuildingInfo Info { get; private set; }

        private Label _nameLabel;
        private Image _previewImage;
        private VisualElement _componentsContainer;
        private VisualElement _categoriesContainer;
        private Texture2D _texture;

        public BuildingElement() : base()
        {
            BuildingsWindow.BuildingTemplate.CloneTree(this);

            _nameLabel = this.Q<Label>("BuildingNameLabel");
            _previewImage = this.Q<Image>("PreviewImage");
            _componentsContainer = this.Q("ComponentsContainer");
            _categoriesContainer = this.Q("CategoriesContainer");
            _texture = new Texture2D(128, 128, TextureFormat.ARGB32, false);
            _previewImage.image = _texture;

            RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.isPropagationStopped)
                    return;

                if (e.clickCount == 2)
                    Selection.activeObject = Info;
            });

            _previewImage.RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.clickCount == 1)
                {
                    if (e.button == 0)
                        Selection.activeObject = Info.Prefab;
                    else
                        Selection.activeObject = Info.Ghost;
                }
                else if (e.clickCount == 2)
                {
                    if (e.button == 0)
                        AssetDatabase.OpenAsset(Info.Prefab);
                    else
                        AssetDatabase.OpenAsset(Info.Ghost);
                }

                e.StopPropagation();
            });

            var copyButton = this.Q<Button>("CopyButton");
            copyButton.style.backgroundImage = BuildingsWindow.CopyIcon;
            copyButton.clicked += () => BuildingsWindow.Instance.Copy(Info);

            var deleteButton = this.Q<Button>("DeleteButton");
            deleteButton.style.backgroundImage = BuildingsWindow.DeleteIcon;
            deleteButton.clicked += () => BuildingsWindow.Instance.Delete(Info);
        }

        public async void SetInfo(BuildingInfo info)
        {
            Info = info;

            _nameLabel.text = Info.Name;

            _componentsContainer.Clear();
            foreach (var component in info.Prefab.GetComponents<IBuildingComponent>().Select(c => Tuple.Create(c, c.GetType().Name)).OrderBy(n => n.Item2))
            {
                _componentsContainer.Add(new BuildingComponentElement(component.Item1));
            }

            _categoriesContainer.Clear();
            foreach (var category in BuildingsWindow.Instance.GetCategories(info))
            {
                _categoriesContainer.Add(new BuildingCategoryElement(category));
            }

            var previewImage = AssetPreview.GetAssetPreview(info.Prefab.gameObject);
            if (previewImage == null)
            {
                await System.Threading.Tasks.Task.Delay(500);
                if (Info != info)
                    return;
                previewImage = AssetPreview.GetAssetPreview(info.Prefab.gameObject);
                if (previewImage == null)
                {
                    await System.Threading.Tasks.Task.Delay(500);
                    if (Info == info)
                        previewImage = AssetPreview.GetAssetPreview(info.Prefab.gameObject);
                }
            }

            if (previewImage != null && _texture != null)
            {
                Graphics.CopyTexture(previewImage, _texture);
            }
        }
    }
}
