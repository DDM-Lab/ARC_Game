#if UNITY_EDITOR
using UnityEditor;
using UnityEngine.UIElements;

namespace CityBuilderCore
{
    public class SceneExplainerWindow : EditorWindow
    {
        private Label _label;

        public void Initialize(string text)
        {
            _label.text = text;
        }

        public void CreateGUI()
        {
            rootVisualElement.style.paddingBottom = 5;
            rootVisualElement.style.paddingLeft = 5;
            rootVisualElement.style.paddingRight = 5;
            rootVisualElement.style.paddingTop = 5;

            _label = new Label();
            _label.style.whiteSpace = WhiteSpace.Normal;

            var scrollView = new ScrollView();
            scrollView.style.flexGrow = 1;
            scrollView.Add(_label);

            rootVisualElement.Add(scrollView);
        }

        private void okClicked()
        {
            Close();
        }
    }
}
#endif