using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// blank walker addon that can be used to attach particle effects for example
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/walkers">https://citybuilder.softleitner.com/manual/walkers</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_walker_addon_effect.html")]
    public class WalkerAddonEffect : WalkerAddon
    {
        [Tooltip("can be used to let the addon rotate")]
        public Vector3 Rotation;

        public override void Update()
        {
            base.Update();

            if (Rotation != Vector3.zero)
                transform.Rotate(Rotation * Time.unscaledDeltaTime);
        }
    }
}