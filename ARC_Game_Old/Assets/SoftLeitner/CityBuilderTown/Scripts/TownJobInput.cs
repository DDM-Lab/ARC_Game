using CityBuilderCore;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace CityBuilderTown
{
    /// <summary>
    /// ui behaviour that lets players set number of walkers for the specified job
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_job_input.html")]
    public class TownJobInput : TooltipOwnerBase, IPointerClickHandler
    {
        [Tooltip("numeric input field for number of walkers")]
        public TMP_InputField Input;
        [Tooltip("the job which specified number of walkers should have")]
        public TownJob Job;
        [Tooltip("fired when the background is clicked")]
        public UnityEvent<TownJob> Clicked;

        public override string TooltipName => Job.Name;
        public override string TooltipDescription => Job.Description;

        private void Start()
        {
            if (Input.text != "0")
                textChanged(Input.text);
            Input.onValueChanged.AddListener(new UnityEngine.Events.UnityAction<string>(textChanged));
        }

        public void SetText()
        {
            Input.SetTextWithoutNotify(TownManager.Instance.GetJobCount(Job).ToString());
        }

        public void Change(int delta)
        {
            if (!int.TryParse(Input.text, out int num))
                return;
            set(num + delta);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            Clicked?.Invoke(Job);
        }

        private void textChanged(string text)
        {
            if (!int.TryParse(text, out int num))
                return;
            set(num);
        }

        private void set(int num)
        {
            var neutralCount = TownManager.Instance.GetJobCount(null);
            var currentCount = TownManager.Instance.GetJobCount(Job);

            num = Mathf.Min(num, currentCount + neutralCount);
            num = Mathf.Max(num, 0);

            TownManager.Instance.SetJobCount(Job, num);

            Input.SetTextWithoutNotify(num.ToString());
        }
    }
}
