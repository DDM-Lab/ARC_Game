using UnityEngine;
using UnityEngine.Events;

namespace CityBuilderCore
{
    /// <summary>
    /// colors sprites in a sprite renderer based on a connection passer<br/>
    /// in urban this is used to visualize whether buildings receive power
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/connections">https://citybuilder.softleitner.com/manual/connections</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_connection_sprite_gradient.html")]
    public class ConnectionSpriteGradient : MonoBehaviour
    {
        [Tooltip("the passer whose points will be used")]
        public ConnectionPasserBase ConnectionPasser;
        [Tooltip("the renderer whose color will be set")]
        public SpriteRenderer SpriteRenderer;
        [Tooltip("gradient of color that will be applied to the renderer")]
        public Gradient Gradient;
        [Tooltip("maximum value the gradiant is scaled to")]
        public int Maximum;

        private void Awake()
        {
            if (ConnectionPasser)
                ConnectionPasser.PointValueChanged.AddListener(new UnityAction<Vector2Int, int>(Apply));
        }

        public void Apply(Vector2Int point, int value)
        {
            SpriteRenderer.color = Gradient.Evaluate(value / (float)Maximum);
        }
    }
}
