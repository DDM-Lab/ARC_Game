using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// displays a tooltip in the UI<br/>
    /// manages its position and display of name and description
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_tooltip_panel.html")]
    public class TooltipPanel : MonoBehaviour
    {
        [Tooltip("is moved to the position of the tooltip and de/activated accordingly")]
        public RectTransform Root;
        [Tooltip("has its pivot changed to align the tooltip when the pointer is too close to the right or bottom")]
        public RectTransform Content;
        [Tooltip("displays the main text of the tooltip")]
        public TMPro.TMP_Text NameText;
        [Tooltip("displays the secondary, potentially smaller and longer, text of the tooltip")]
        public TMPro.TMP_Text DescriptionText;
        [Tooltip("offset that avoids having the tooltip directly under the pointer, only applied when the tooltip is aligned bottom right")]
        public Vector2 PointerOffset;

        public bool IsVisible => Root.gameObject.activeSelf;

        private Canvas _canvas;
        private float _nameHeight, _descriptionHeight;

        private void Awake()
        {
            _canvas = GetComponentInParent<Canvas>();

            if (NameText)
                _nameHeight += NameText.rectTransform.sizeDelta.y;
            if (DescriptionText)
                _descriptionHeight += DescriptionText.rectTransform.sizeDelta.y;
        }

        public void Show(Vector2 mousePosition, ITooltipOwner owner)
        {
            var position = mousePosition / _canvas.scaleFactor;

            var width = 0f;
            var height = 0f;

            if (NameText)
            {
                if (string.IsNullOrWhiteSpace(owner.TooltipName))
                {
                    NameText.gameObject.SetActive(false);
                }
                else
                {
                    NameText.gameObject.SetActive(true);
                    NameText.text = owner.TooltipName;
                    width = NameText.GetPreferredValues(owner.TooltipName).x;
                    height += _nameHeight;
                }
            }

            if (DescriptionText)
            {
                if (string.IsNullOrWhiteSpace(owner.TooltipDescription))
                {
                    DescriptionText.gameObject.SetActive(false);
                }
                else
                {
                    DescriptionText.gameObject.SetActive(true);
                    DescriptionText.text = owner.TooltipDescription;
                    width = Mathf.Max(width, DescriptionText.GetPreferredValues(owner.TooltipDescription).x);
                    height += _descriptionHeight;
                }
            }

            var pivot = Vector2.zero;

            if (position.y - height - PointerOffset.y <= 0f)
            {
                pivot.y = 0;
            }
            else
            {
                pivot.y = 1;
            }

            if (position.x + width + PointerOffset.x > Screen.width)
            {
                pivot.x = 1;

                if (NameText)
                    NameText.horizontalAlignment = TMPro.HorizontalAlignmentOptions.Right;
                if (DescriptionText)
                    DescriptionText.horizontalAlignment = TMPro.HorizontalAlignmentOptions.Right;
            }
            else
            {
                pivot.x = 0;

                if (NameText)
                    NameText.horizontalAlignment = TMPro.HorizontalAlignmentOptions.Left;
                if (DescriptionText)
                    DescriptionText.horizontalAlignment = TMPro.HorizontalAlignmentOptions.Left;
            }

            if (pivot == Vector2.up)
                position += new Vector2(PointerOffset.x, -PointerOffset.y);

            Root.gameObject.SetActive(true);
            Root.anchoredPosition = position;
            Content.pivot = pivot;
        }
        public void Hide()
        {
            Root.gameObject.SetActive(false);
        }
    }
}
