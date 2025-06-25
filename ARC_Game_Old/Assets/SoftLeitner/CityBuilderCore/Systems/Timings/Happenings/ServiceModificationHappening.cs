using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// an event that, on activation, modifies the service values of a set amount of buildings<br/>
    /// increase > blessings, self sufficiency ...<br/>
    /// decrease > water leak, thief,  ...
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/timings">https://citybuilder.softleitner.com/manual/timings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_service_modification_happening.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Happenings/" + nameof(ServiceModificationHappening))]
    public class ServiceModificationHappening : TimingHappening
    {
        [Tooltip("the service that will be modified when the happening starts")]
        public Service Service;
        [Tooltip("amount that the risk will be modified by(positive to increase, negative to reduce and maybe lose service on some buildings)")]
        public float Amount;
        [Tooltip("how many randomly selected building will be affected, 0 or less for all")]
        public int Count;

        public override void Start()
        {
            base.Start();

            foreach (var building in Dependencies.Get<IBuildingManager>().GetRandom(Count, b => Service.HasValue(b)))
            {
                Service.ModifyValue(building, Amount);
            }
        }
    }
}
