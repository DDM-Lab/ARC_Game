using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CityBuilderCore
{
    /// <summary>
    /// ui behaviour that allows cycling through selected buildings or walkers of the same type
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_selection_switcher.html")]
    public class SelectionSwitcher : MonoBehaviour
    {
        [Tooltip("switches to the previous walker/building of the same type")]
        public Button PreviousButton;
        [Tooltip("displays the number of the currently selected walker/building in the format '(current/total)'")]
        public TMP_Text Text;
        [Tooltip("switches to the next walker/building of the same type")]
        public Button NextButton;
        [Tooltip("fired whenever one of the buttons is pressed, sends the new target so it can be plugged into SelectionDialog.Activate")]
        public UnityEvent<object> Switched;

        protected object _currentTarget;

        private void Start()
        {
            PreviousButton.onClick.AddListener(new UnityAction(() => switchTarget(-1)));
            NextButton.onClick.AddListener(new UnityAction(() => switchTarget(1)));
        }

        public void SetTarget(object target)
        {
            _currentTarget = target;

            var candidates = getCandidates();
            if (candidates == null || candidates.Count <= 1)
            {
                PreviousButton.interactable = false;
                NextButton.interactable = false;
                Text.text = "(1/1)";
            }
            else
            {
                var candidate = _currentTarget;
                if (candidate is BuildingReference buildingReference)
                    candidate = buildingReference.Instance;

                PreviousButton.interactable = true;
                NextButton.interactable = true;
                Text.text = $"({candidates.IndexOf(candidate) + 1}/{candidates.Count})";
            }
        }

        private void switchTarget(int direction)
        {
            var candidates = getCandidates();

            if (candidates == null)
                return;

            var candidate = _currentTarget;
            if (candidate is BuildingReference buildingReference)
                candidate = buildingReference.Instance;

            var index = candidates.IndexOf(candidate) + direction;
            if (index >= candidates.Count)
                index = 0;
            else if (index < 0)
                index = candidates.Count - 1;

            var switched=candidates[index];
            if (switched is IBuilding b)
                switched = b.BuildingReference;
            
            Switched?.Invoke(switched);
        }

        protected virtual List<object> getCandidates()
        {
            if (_currentTarget is Walker walker)
                return Dependencies.Get<IWalkerManager>().GetWalkers().Where(w => w.Info == walker.Info).Cast<object>().ToList();
            else if (_currentTarget is Building building)
                return Dependencies.Get<IBuildingManager>().GetBuildings(building.Info).Cast<object>().ToList();
            else if (_currentTarget is BuildingReference buildingReference)
                return Dependencies.Get<IBuildingManager>().GetBuildings(buildingReference.Instance.Info).Cast<object>().ToList();
            else
                return null;
        }
    }
}
