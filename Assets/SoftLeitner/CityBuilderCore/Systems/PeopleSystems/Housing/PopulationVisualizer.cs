
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// visualizes employment as text in unity ui<br/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/people">https://citybuilder.softleitner.com/manual/people</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_population_visualizer.html")]
    public class PopulationVisualizer : MonoBehaviour
    {
        [Tooltip("population that gets visualized on the TMPro text component")]
        public Population Population;
        [Tooltip("text component for the population in the form of '<name>: <quantity> / <capacity>'")]
        public TMPro.TMP_Text Text;

        private IPopulationManager _populationManager;

        private void Start()
        {
            _populationManager = Dependencies.Get<IPopulationManager>();
        }

        private void Update()
        {
            Text.text = $"{Population.Name}: {_populationManager.GetQuantity(Population)} / {_populationManager.GetCapacity(Population)}";
        }
    }
}