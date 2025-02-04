using UnityEngine.UIElements;

namespace CityBuilderCore.Editor
{
    public class BuildingCategoryElement : VisualElement
    {
        private BuildingCategory _category;

        public BuildingCategoryElement(BuildingCategory category) : base()
        {
            BuildingsWindow.BuildingCategoryTemplate.CloneTree(this);

            _category = category;

            this.Q<Label>("CategoryNameLabel").text = category.NameSingular;

            var filterButton = this.Q<Button>();
            filterButton.style.backgroundImage = BuildingsWindow.FilterIcon;
            filterButton.clicked += () => BuildingsWindow.Instance.Filter(_category);
        }
    }
}
