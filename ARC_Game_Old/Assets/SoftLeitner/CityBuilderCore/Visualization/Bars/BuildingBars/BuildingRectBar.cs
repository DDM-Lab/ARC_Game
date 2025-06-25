using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// visualizes a building value by sizing a rect to its ratio and writing the value in a text field<br/>
    /// used for the building health bar in the Defense demo
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_building_rect_bar.html")]
    public class BuildingRectBar : BuildingValueBar
    {
        [Tooltip("displays the value in text form, optional")]
        public TMPro.TMP_Text Text;
        [Tooltip("optional format used to display the text, for example ###.##")]
        public string Format;
        [Tooltip("displays the value in bar form, make sure it is full in the prefab, optional")]
        public RectTransform Rect;

        public override bool IsGlobal => true;

        private Vector2 _sizeFull;
        private IMainCamera _mainCamera;

        public override void Initialize(IBuilding building, IBuildingValue value)
        {
            base.Initialize(building, value);

            _mainCamera = Dependencies.Get<IMainCamera>();

            if (Rect)
                _sizeFull = Rect.sizeDelta;

            updateVisuals();
        }

        private void LateUpdate()
        {
            updateVisuals();
        }

        private void updateVisuals()
        {
            if (_building == null || _mainCamera == null)
                return;

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