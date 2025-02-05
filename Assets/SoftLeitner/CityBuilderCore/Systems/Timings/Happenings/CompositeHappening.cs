using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// an event that, on activation, modifies the risk values of a set amount of buildings<br/>
    /// increase > arsonist, disease outbreak, natural disaster(volcano, earthquake) ...<br/>
    /// decrease > blessings, ...
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/timings">https://citybuilder.softleitner.com/manual/timings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_composite_happening.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Happenings/" + nameof(CompositeHappening))]
    public class CompositeHappening : TimingHappening
    {
        [Tooltip("happenings that will be activated with this happening")]
        public TimingHappening[] Happenings;

        public override void Start()
        {
            base.Start();

            foreach (var happening in Happenings)
            {
                happening.Start();
            }
        }

        public override void End()
        {
            base.End();

            foreach (var happening in Happenings)
            {
                happening.End();
            }
        }

        public override void Activate()
        {
            base.Activate();

            foreach (var happening in Happenings)
            {
                happening.Activate();
            }
        }

        public override void Deactivate()
        {
            base.Deactivate();

            foreach (var happening in Happenings)
            {
                happening.Deactivate();
            }
        }
    }
}
