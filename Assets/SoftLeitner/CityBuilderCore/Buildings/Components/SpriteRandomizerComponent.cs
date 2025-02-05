using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// building component selects a random sprite for a renderer<br/>
    /// it also saves which sprite was selected so it stays consistent across saves<br/>
    /// used in the urban demo to get some visual variety in houses that are otherwise identical
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/buildings">https://citybuilder.softleitner.com/manual/buildings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_sprite_randomizer_component.html")]
    public class SpriteRandomizerComponent : BuildingComponent
    {
        public override string Key => "SRA";

        [Tooltip("the renderer that the selected sprite is assigned to")]
        public SpriteRenderer Renderer;
        [Tooltip("one of these will be randomly selected")]
        public Sprite[] Sprites;

        private int _index;

        public override void InitializeComponent()
        {
            base.InitializeComponent();

            _index = Random.Range(0, Sprites.Length);
            Renderer.sprite = Sprites[_index];
        }
        public override void OnReplacing(IBuilding replacement)
        {
            base.OnReplacing(replacement);

            var replacementComponent = replacement.GetBuildingComponent<SpriteRandomizerComponent>();

            if (replacementComponent != null && replacementComponent.Sprites.Length == Sprites.Length)
            {
                replacementComponent._index = _index;
                replacementComponent.Renderer.sprite = replacementComponent.Sprites[_index];
            }
        }

        #region Saving
        public override string SaveData()
        {
            return _index.ToString();
        }
        public override void LoadData(string json)
        {
            _index = int.Parse(json);
            Renderer.sprite = Sprites[_index];
        }
        #endregion
    }
}
