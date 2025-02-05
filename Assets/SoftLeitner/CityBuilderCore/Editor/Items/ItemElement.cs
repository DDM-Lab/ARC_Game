using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace CityBuilderCore.Editor
{
    public class ItemElement : VisualElement
    {
        public Item Item { get; private set; }

        private Label _nameLabel;
        private Image _previewImage;

        public ItemElement() : base()
        {
            ItemsWindow.ItemTemplate.CloneTree(this);

            _nameLabel = this.Q<Label>("ItemNameLabel");
            _previewImage = this.Q<Image>("PreviewImage");

            RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.clickCount == 2)
                    Selection.activeObject = Item;
            });

            _previewImage.RegisterCallback<MouseDownEvent>(e =>
            {
                if (e.clickCount == 2)
                {
                    if (Item.Visuals.Length > 0)
                        AssetDatabase.OpenAsset(Item.Visuals[0]);
                }
            });

            var copyButton = this.Q<Button>("CopyButton");
            copyButton.style.backgroundImage = ItemsWindow.CopyIcon;
            copyButton.clicked += () => ItemsWindow.Instance.Copy(Item);

            var deleteButton = this.Q<Button>("DeleteButton");
            deleteButton.style.backgroundImage = ItemsWindow.DeleteIcon;
            deleteButton.clicked += () => ItemsWindow.Instance.Delete(Item);
        }

        public void SetItem(Item item)
        {
            Item = item;

            _nameLabel.text = Item.Name;

            if (item.Icon)
            {
                _previewImage.image = item.Icon.texture;
                if (item.Icon.rect == Rect.zero)
                    _previewImage.sourceRect = Rect.zero;
                else
                    _previewImage.sourceRect = new Rect(item.Icon.rect.x, item.Icon.texture.height - item.Icon.rect.y - item.Icon.rect.height, item.Icon.rect.width, item.Icon.rect.height);
            }
        }
    }
}
