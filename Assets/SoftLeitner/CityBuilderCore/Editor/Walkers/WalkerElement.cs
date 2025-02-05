using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityBuilderCore.Editor
{
    public class WalkerElement : VisualElement
    {
        public WalkerInfo Info { get; private set; }

        private Label _nameLabel;
        private Image _previewImage;
        private Label _componentNameLabel;
        private Button _filterButton;
        private Texture2D _texture;

        public WalkerElement() : base()
        {
            WalkersWindow.WalkerTemplate.CloneTree(this);

            _nameLabel = this.Q<Label>("WalkerNameLabel");
            _previewImage = this.Q<Image>("PreviewImage");
            _componentNameLabel = this.Q<Label>("ComponentNameLabel");
            _filterButton = this.Q<Button>("FilterButton");
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
                if (e.clickCount == 2)
                {
                    if (Info.Prefab)
                        AssetDatabase.OpenAsset(Info.Prefab);
                }

                e.StopPropagation();
            });

            _filterButton.style.backgroundImage = WalkersWindow.FilterIcon;
            _filterButton.clicked += () => WalkersWindow.Instance.Filter(Info.Prefab.GetComponent<Walker>());

            var copyButton = this.Q<Button>("CopyButton");
            copyButton.style.backgroundImage = WalkersWindow.CopyIcon;
            copyButton.clicked += () => WalkersWindow.Instance.Copy(Info);

            var deleteButton = this.Q<Button>("DeleteButton");
            deleteButton.style.backgroundImage = WalkersWindow.DeleteIcon;
            deleteButton.clicked += () => WalkersWindow.Instance.Delete(Info);
        }

        public async void SetInfo(WalkerInfo info)
        {
            Info = info;

            _nameLabel.text = Info.Name;

            if (Info.Prefab)
            {
                _componentNameLabel.visible = true;
                _filterButton.visible = true;

                _componentNameLabel.text = info.Prefab.GetComponent<Walker>().GetType().Name.Replace("Walker", string.Empty);

                var previewImage = AssetPreview.GetAssetPreview(info.Prefab);
                if (previewImage == null)
                {
                    await System.Threading.Tasks.Task.Delay(500);
                    if (Info != info)
                        return;
                    previewImage = AssetPreview.GetAssetPreview(info.Prefab); 
                    if (previewImage == null)
                    {
                        await System.Threading.Tasks.Task.Delay(500);
                        if (Info != info)
                            return;
                        previewImage = AssetPreview.GetAssetPreview(info.Prefab);
                    }
                }

                if (previewImage != null)
                {
                    Graphics.CopyTexture(previewImage, _texture);
                }
            }
            else
            {
                _componentNameLabel.visible = false;
                _filterButton.visible = false;
            }
        }
    }
}
