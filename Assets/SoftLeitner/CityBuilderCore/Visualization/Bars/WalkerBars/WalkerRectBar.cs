using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// visualizes a walker value by sizing a rect to its ratio and writing the value in a text field<br/>
    /// used for the walker health bars in the Defense demo
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_walker_rect_bar.html")]
    public class WalkerRectBar : WalkerValueBar
    {
        [Tooltip("displays the value in text form, optional")]
        public TMPro.TMP_Text Text;
        [Tooltip("optional format used to display the text, for example ###.##")]
        public string Format;
        [Tooltip("displays the value in bar form, make sure it is full in the prefab, optional")]
        public RectTransform Rect;
        [Tooltip("keeps the bar from being moved around with the walker")]
        public bool IsStatic;

        public override bool IsGlobal => true;

        private Vector2 _sizeFull;
        private IMainCamera _mainCamera;

        public override void Initialize(Walker walker, IWalkerValue value)
        {
            base.Initialize(walker, value);

            if (_mainCamera == null)
            {
                _mainCamera = Dependencies.Get<IMainCamera>();

                if (Rect)
                    _sizeFull = Rect.sizeDelta;
            }

            updateVisuals();
        }

        private void LateUpdate()
        {
            updateVisuals();
        }

        private void updateVisuals()
        {
            if (!Walker || _mainCamera == null)
                return;

            if (!IsStatic)
                transform.position = RectTransformUtility.WorldToScreenPoint(_mainCamera.Camera, GetPosition());

            if (Text)
            {
                if (string.IsNullOrWhiteSpace(Format))
                    Text.text = GetValue().ToString();
                else
                    Text.text = GetValue().ToString(Format);
            }

            if (Rect)
            {
                Rect.sizeDelta = Vector2.Lerp(Vector2.zero, _sizeFull, GetRatio());
            }
        }

    }
}