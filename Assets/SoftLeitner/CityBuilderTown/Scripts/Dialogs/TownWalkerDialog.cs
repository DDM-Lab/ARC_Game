using CityBuilderCore;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace CityBuilderTown
{
    /// <summary>
    /// visualizes the state of a town walker in the UI
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_walker_dialog.html")]
    public class TownWalkerDialog : MonoBehaviour
    {
        private class WalkerEnergy : IWalkerValue
        {
            public bool HasValue(Walker walker) => walker is TownWalker;
            public float GetMaximum(Walker walker) => 100;
            public float GetValue(Walker walker) => ((TownWalker)walker).Energy / ((TownWalker)walker).Identity.EnergyCapacity * 100f;
            public Vector3 GetPosition(Walker walker) => walker.Pivot.position;
        }
        private class WalkerFood : IWalkerValue
        {
            public bool HasValue(Walker walker) => walker is TownWalker;
            public float GetMaximum(Walker walker) => 100;
            public float GetValue(Walker walker) => ((TownWalker)walker).Food / ((TownWalker)walker).Identity.FoodCapacity * 100f;
            public Vector3 GetPosition(Walker walker) => walker.Pivot.position;
        }
        private class WalkerWarmth : IWalkerValue
        {
            public bool HasValue(Walker walker) => walker is TownWalker;
            public float GetMaximum(Walker walker) => 100;
            public float GetValue(Walker walker)
            {
                var townWalker = (TownWalker)walker;
                if (townWalker.Identity.WarmthCapacity == 0f)
                    return 0;

                return townWalker.Warmth / townWalker.Identity.WarmthCapacity * 100f;
            }
            public Vector3 GetPosition(Walker walker) => walker.Pivot.position;
        }

        private CoroutineToken _followToken;
        private TownWalker _currentWalker;

        [Tooltip("used to select the walkers home when the button is clicked")]
        public TownBuildingDialog BuildingDialog;
        [Tooltip("displays the walkers name")]
        public TMPro.TMP_Text Name;
        [Tooltip("displays the walkers age")]
        public TMPro.TMP_Text Age;
        [Tooltip("displays the walkers job")]
        public TMPro.TMP_Text Job;
        [Tooltip("area that shows the walkers home")]
        public GameObject Home;
        [Tooltip("displays the walkers current activity")]
        public TMPro.TMP_Text Activity;
        [Tooltip("displays the walkers energy")]
        public WalkerRectBar EnergyBar;
        [Tooltip("displays the walkers food")]
        public WalkerRectBar FoodBar;
        [Tooltip("displays the walkers warmth")]
        public WalkerRectBar WarmthBar;
        [Tooltip("displays the walkers items")]
        public ItemPanel ItemPanel;
        [Tooltip("addon added to selected walker to visualize the selection")]
        public WalkerAddon SelectionAddon;
        [Tooltip("fired whenever the current walker of the dialog is changed")]
        public UnityEvent<TownWalker> WalkerChanged;

        private void Start()
        {
            Hide();
        }

        private void Update()
        {
            if (_followToken?.IsActive == false)
            {
                Hide();
                _followToken = null;
                return;
            }

            if (_currentWalker == null)
                return;

            Age.text = _currentWalker.VisualAge.ToString();
            Job.text = _currentWalker.Job == null ? "-" : _currentWalker.Job.Name;
            Home.SetActive(_currentWalker.HasHome);
            Activity.text = _currentWalker.GetActivityText();

            ItemPanel.SetItem(_currentWalker.Storage.GetItemQuantities().FirstOrDefault());
        }

        public void Show(object target) => Show(target as TownWalker);
        public void Show(Walker walker) => Show((TownWalker)walker);
        public void Show(TownWalker walker)
        {
            gameObject.SetActive(true);

            if (SelectionAddon && _currentWalker)
                _currentWalker.RemoveAddon(SelectionAddon.Key);
            _currentWalker = walker;
            if (SelectionAddon)
                _currentWalker.AddAddon(SelectionAddon);

            Name.text = _currentWalker.Identity.FullName;

            EnergyBar.Initialize(walker, new WalkerEnergy());
            FoodBar.Initialize(walker, new WalkerFood());
            WarmthBar.Initialize(walker, new WalkerWarmth());

            _followToken = Dependencies.GetOptional<IMainCamera>()?.Follow(walker.Pivot);

            WalkerChanged?.Invoke(_currentWalker);
        }
        public void Show(TownJob job)
        {
            if (_currentWalker == null || _currentWalker.Job != job)
            {
                var walker = Dependencies.Get<IWalkerManager>().GetWalkers().OfType<TownWalker>().Where(w => w.Job == job).FirstOrDefault();
                if (walker)
                    Show(walker);
            }
            else
            {
                var walkers = Dependencies.Get<IWalkerManager>().GetWalkers().OfType<TownWalker>().Where(w => w.Job == job).Cast<object>().ToList();
                if (walkers.Count > 1)
                {
                    var index = walkers.IndexOf(_currentWalker) + 1;
                    if (index >= walkers.Count)
                        index = 0;
                    else if (index < 0)
                        index = walkers.Count - 1;
                    Show(walkers[index]);
                }
            }
        }

        public void Hide()
        {
            if (SelectionAddon && _currentWalker)
                _currentWalker.RemoveAddon(SelectionAddon.Key);

            _currentWalker = null;

            _followToken?.Stop();
            _followToken = null;

            gameObject.SetActive(false);

            WalkerChanged?.Invoke(_currentWalker);
        }

        public void SelectHome()
        {
            var home = _currentWalker?.Home;

            Hide();
            BuildingDialog.Show(home);
        }
    }
}
