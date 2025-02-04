using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// items dispenser that reloads its charges over time
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/resources">https://citybuilder.softleitner.com/manual/resources</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_reloading_items_dispenser.html")]
    public class ReloadingItemsDispenser : MonoBehaviour, IItemsDispenser, ISaveData
    {
        [Tooltip("dispenser key, used in retrievers")]
        public string Key;
        [Tooltip("items returned on dispense")]
        public ItemQuantity Items;
        [Tooltip("maximum charges that are loaded up")]
        public int TotalCharges;
        [Tooltip("time to reload one charge")]
        public float ReloadTime;
        [Tooltip("visuals used for different charge states, only the one visual at charge index is displayed(first visual is for no charges, second for one,...)")]
        public GameObject[] Visuals;

        [Tooltip("fired when the the dispenser is used, contains remaining charges")]
        public IntEvent Dispensed;

        public Vector3 Position => transform.position;
        string IItemsDispenser.Key => Key;

        private int _currentCharges;
        private float _currentReloadTime;

        private void Start()
        {
            _currentCharges = TotalCharges;
            setVisuals();

            Dependencies.Get<IItemsDispenserManager>().Add(this);
        }

        private void Update()
        {
            if (_currentCharges == TotalCharges)
                return;

            _currentReloadTime += Time.deltaTime;
            if (_currentReloadTime >= ReloadTime)
            {
                _currentReloadTime = 0f;
                _currentCharges++;
                setVisuals();

                if (_currentCharges == 1)
                    Dependencies.Get<IItemsDispenserManager>().Add(this);
            }
        }

        private void OnDestroy()
        {
            Dependencies.Get<IItemsDispenserManager>().Remove(this);
        }

        private void setVisuals()
        {
            for (int i = 0; i < Visuals.Length; i++)
            {
                if (Visuals[i])
                    Visuals[i].SetActive(i == _currentCharges);
            }
        }

        public ItemQuantity Dispense()
        {
            _currentCharges--;
            setVisuals();

            Dispensed?.Invoke(_currentCharges);

            if (_currentCharges == 0)
                Dependencies.Get<IItemsDispenserManager>().Remove(this);

            return Items;
        }

        #region Saving
        [Serializable]
        public class ReloadingData
        {
            public int Charges;
            public float ReloadTime;
        }

        public string SaveData()
        {
            return JsonUtility.ToJson(new ReloadingData()
            {
                Charges = _currentCharges,
                ReloadTime = _currentReloadTime
            });
        }

        public void LoadData(string json)
        {
            var data = JsonUtility.FromJson<ReloadingData>(json);

            _currentCharges = data.Charges;
            _currentReloadTime = data.ReloadTime;

            setVisuals();
        }
        #endregion
    }
}