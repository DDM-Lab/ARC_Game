using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// an event that, on activation, modifies the risk values of a set amount of buildings<br/>
    /// increase > arsonist, disease outbreak, natural disaster(volcano, earthquake) ...<br/>
    /// decrease > blessings, ...
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/timings">https://citybuilder.softleitner.com/manual/timings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_animation_happening.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Happenings/" + nameof(AnimationHappening))]
    public class AnimationHappening : TimingHappening
    {
        [Tooltip("name of the gameobject whose animator will be set")]
        public string ObjectName;
        [Tooltip("name of the bool parameter that will be set to true while the happening is active")]
        public string ParameterName;

        public override void Activate()
        {
            base.Activate();

            GameObject.Find(ObjectName).GetComponent<Animator>().SetBool(ParameterName, true);
        }

        public override void Deactivate()
        {
            base.Deactivate();

            GameObject.Find(ObjectName).GetComponent<Animator>().SetBool(ParameterName, false);
        }
    }
}
