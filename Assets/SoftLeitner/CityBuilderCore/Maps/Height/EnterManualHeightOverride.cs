using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// sets or resets the height override of any <see cref="IOverrideHeight"/>(walkers) that enters its trigger<br/>
    /// does not do any thing on trigger exit, so setting and resetting has to be done on seperate triggers<br/>
    /// used in the urban tunnel demo to set cars heights when they enter and exit the underground(UrbanTunnelEntry/Exit)
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_enter_manual_height_override.html")]
    public class EnterManualHeightOverride : MonoBehaviour
    {
        [Tooltip("height override will be reset to null, walkers will go back to their regular height")]
        public bool Reset;
        [Tooltip("the height that is set when reset is false")]
        public float Height;

        private void OnTriggerEnter2D(Collider2D collider) => enter(collider);
        private void OnTriggerEnter(Collider collider) => enter(collider);
        private void enter(Component collider)
        {
            var overrideHeight = collider.GetComponent<IOverrideHeight>();
            if (overrideHeight != null)
            {
                if (Reset)
                    overrideHeight.HeightOverride = null;
                else
                    overrideHeight.HeightOverride = Height;
            }
        }
    }
}
