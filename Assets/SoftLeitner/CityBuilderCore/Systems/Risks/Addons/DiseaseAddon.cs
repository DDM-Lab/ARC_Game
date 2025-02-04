using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// addon that simulates a disease<br/>
    /// <para>
    /// for the duration of its existance the addon can spawn walkers that further spread the disease<br/>
    /// kills its housings population either continuously, at the start or end of the duration<br/>
    /// after the duration the disease is fully healed<br/>
    /// disease also fully disrupts efficiency which eg keeps the population from working
    /// </para>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/risks">https://citybuilder.softleitner.com/manual/risks</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_disease_addon.html")]
    public class DiseaseAddon : BuildingAddon, IEfficiencyFactor
    {
        [Tooltip("risk that triggered this addon, will be reset(-100) when the addon finishes")]
        public Risk Risk;
        [Tooltip("can be used to send out walkers that spread the disease")]
        public CyclicRiskWalkerSpawner Spawner;
        [Tooltip("how long the disease stays active")]
        public float Duration = 30f;
        [Tooltip(@"when do the deaths of the disease occur
None		no deaths
Initial		ratio killed at start
Final		ratio killed after duration
Continuous	kills certain amount every s")]
        public DiseaseMortality Mortality = DiseaseMortality.Final;
        [Tooltip("dead/sec for continuous, dead ratio for initial and final mortality")]
        public float MortalityRate = 100f;

        public bool IsWorking => false;
        public float Factor => 0f;

        private float _progress;
        private float _interval;

        public override void InitializeAddon()
        {
            base.InitializeAddon();

            if (Mortality == DiseaseMortality.Initial)
            {
                var housing = Building.GetBuildingComponent<IHousing>();
                if (housing != null)
                {
                    housing.Kill(MortalityRate);
                }
            }

            if (Spawner.Prefab && Spawner.Count > 0)
                Spawner.Initialize(Building);
        }
        public override void TerminateAddon()
        {
            base.TerminateAddon();

            Building.GetBuildingComponent<IRiskRecipient>().ModifyRisk(Risk, -100f);
        }
        public override void OnReplacing(Transform parent, IBuilding replacement)
        {
            base.OnReplacing(parent, replacement);

            if (Spawner.Prefab && Spawner.Count > 0)
                Spawner.Initialize(Building);
        }

        public override void Update()
        {
            base.Update();

            if (Spawner.Count > 0)
            {
                Spawner.Update();
            }

            if (Mortality == DiseaseMortality.Continuous)
            {
                _interval += Time.deltaTime;
                if (_interval >= 1f)
                {
                    _interval = 0;

                    var housing = Building.GetBuildingComponent<IHousing>();
                    if (housing != null)
                    {
                        housing.Kill(MortalityRate);
                    }
                }
            }

            _progress += Time.deltaTime / Duration;
            if (_progress >= 1f)
            {
                if (Mortality == DiseaseMortality.Final)
                {
                    var housing = Building.GetBuildingComponent<IHousing>();
                    if (housing != null)
                    {
                        housing.Kill(MortalityRate);
                    }
                }

                Building.RemoveAddon(this);
            }
        }

        #region Saving
        [Serializable]
        public class DiseaseData
        {
            public float Progress;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new DiseaseData()
            {
                Progress = _progress
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<DiseaseData>(json);

            _progress = data.Progress;
        }
        #endregion
    }
}