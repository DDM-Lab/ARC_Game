using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// an event that, on activation, modifies the risk values of a set amount of buildings<br/>
    /// increase > arsonist, disease outbreak, natural disaster(volcano, earthquake) ...<br/>
    /// decrease > blessings, ...
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/timings">https://citybuilder.softleitner.com/manual/timings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_risk_modification_happening.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Happenings/" + nameof(RiskModificationHappening))]
    public class RiskModificationHappening : TimingHappening
    {
        [Tooltip("the risk that will be modified when the happening starts")]
        public Risk Risk;
        [Tooltip("amount that the risk will be modified by(positive to increase and maybe trigger risks, negative to reduce)")]
        public float Amount;
        [Tooltip("how many randomly selected building will be affected, 0 or less for all")]
        public int Count;

        public override void Start()
        {
            base.Start();

            if (Count > 0)
            {
                foreach (var building in Dependencies.Get<IBuildingManager>().GetRandom(Count, b => Risk.HasValue(b)).ToArray())
                {
                    Risk.ModifyValue(building, Amount);
                }
            }
            else
            {
                foreach (var building in Dependencies.Get<IBuildingManager>().GetBuildings().Where(b => Risk.HasValue(b)).ToArray())
                {
                    Risk.ModifyValue(building, Amount);
                }
            }
        }
    }
}
