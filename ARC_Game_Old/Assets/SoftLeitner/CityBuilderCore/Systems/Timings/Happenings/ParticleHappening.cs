using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// an event that, during its activation, plays particles
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/timings">https://citybuilder.softleitner.com/manual/timings</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_particle_happening.html")]
    [CreateAssetMenu(menuName = "CityBuilder/Happenings/" + nameof(ParticleHappening))]
    public class ParticleHappening : TimingHappening
    {
        [Tooltip("name of the gameobject that has a particle system that will play while the happening is active")]
        public string ObjectName;

        public override void Activate()
        {
            base.Activate();

            GameObject.Find(ObjectName).GetComponent<ParticleSystem>().Play();
        }

        public override void Deactivate()
        {
            base.Deactivate();

            GameObject.Find(ObjectName).GetComponent<ParticleSystem>().Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }
}
