using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// sets a random color for some sprite renderers, can be used to set some random color for a little visual variation
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_sprite_color_randomizer.html")]
    public class SpriteColorRandomizer : MonoBehaviour
    {
        [Tooltip("get a random color set, can be used for a little visual variance, not persisted")]
        public SpriteRenderer[] SpriteRenderers;
        [Tooltip("one of these colors is randomly selected")]
        public Color[] Colors;

        private void Start()
        {
            var color = Colors.Random();
            SpriteRenderers.ForEach(r => r.color = color);
        }
    }
}
