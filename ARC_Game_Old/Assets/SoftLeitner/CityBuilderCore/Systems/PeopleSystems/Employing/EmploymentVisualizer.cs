using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// visualizes employment as text in unity ui<br/>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/people">https://citybuilder.softleitner.com/manual/people</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_employment_visualizer.html")]
    public class EmploymentVisualizer : MonoBehaviour
    {
        [Tooltip("the population that will be visualized(plebs, aristocrats, ...)")]
        public Population Population;
        [Tooltip("the text field that will be set(Pop: Available / Needed >> 'Plebs: 100 / 150')")]
        public TMPro.TMP_Text Text;

        private IEmploymentManager _employmentManager;

        private void Start()
        {
            _employmentManager = Dependencies.Get<IEmploymentManager>();
        }

        private void Update()
        {
            Text.text = $"{Population.Name}: {_employmentManager.GetAvailable(Population)} / {_employmentManager.GetNeeded(Population)}";
        }
    }
}