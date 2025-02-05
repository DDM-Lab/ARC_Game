using UnityEngine.UIElements;

namespace CityBuilderCore.Editor
{
    public class BuildingComponentElement : VisualElement
    {
        private IBuildingComponent _component;

        public BuildingComponentElement(IBuildingComponent component) : base()
        {
            BuildingsWindow.BuildingComponentTemplate.CloneTree(this);

            _component = component;

            this.Q<Label>("ComponentNameLabel").text = component.GetType().Name.Replace("Component", string.Empty);

            var filterButton = this.Q<Button>();
            filterButton.style.backgroundImage = BuildingsWindow.FilterIcon;
            filterButton.clicked += () => BuildingsWindow.Instance.Filter(_component);
        }
    }
}
