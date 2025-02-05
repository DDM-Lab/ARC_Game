using System.Collections;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// variant of demolish visual that scales down and rotates itself until it finally removes itself(quick and easy animated despawn without needing actual animation)
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_demolish_visual_despawn.html")]
    public class DemolishVisualDespawn : DemolishVisual
    {
        [Tooltip("how long to scale down and rotate before removing itself")]
        public float Duration = 0.2f;

        private void Start()
        {
            StartCoroutine(despawn());
        }

        private IEnumerator despawn()
        {
            var startScale = transform.localScale;
            var isXY = Dependencies.GetOptional<IMap>()?.IsXY ?? false;

            var t = 0f;
            while (t < Duration)
            {
                transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t / Duration);
                transform.localEulerAngles = isXY ? new Vector3(0, 0, -180f * t / Duration) : new Vector3(0, -180f * t / Duration, 0);
                t += Time.deltaTime;
                yield return null;
            }

            transform.localScale = Vector3.zero;

            Remove();
        }
    }
}