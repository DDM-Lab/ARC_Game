using System;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// addon that simulates a fire<br/>
    /// <para>
    /// burns for some time, then replaces the building with a defined structure(eg Rubble)<br/>
    /// also completely disrupts building efficiency for obvious reasons
    /// </para>
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/risks">https://citybuilder.softleitner.com/manual/risks</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_fire_addon.html")]
    public class FireAddon : BuildingAddon, IEfficiencyFactor
    {
        [Tooltip("risk that triggered this addon, will be reset(-100) if the fire is extinguised")]
        public Risk Risk;
        [Tooltip("key of the structure collection the building is replaced after the fire burns out")]
        public string StructureCollectionKey;
        [Tooltip("how long the fire takes to burn out")]
        public float Duration;
        [Tooltip("the amount required to extinguish the fire with the building intact")]
        public float ExtinguishAmount;

        public bool IsWorking => false;
        public float Factor => 0f;

        public bool IsExtinguished => _extinguished > ExtinguishAmount;

        private float _progress;
        private float _extinguished;

        public override void Update()
        {
            base.Update();

            _progress += Time.deltaTime / Duration;
            if (_progress >= 1f)
            {
                var collection = Dependencies.Get<IStructureManager>().GetStructure(StructureCollectionKey) as StructureCollection;
                var positions = PositionHelper.GetBoxPositions(Building.Point, Building.Point + Building.Size - Vector2Int.one, collection.ObjectSize);

                Building.Terminate();
                collection.Add(positions);
            }
        }

        public void Extinguish(float amount)
        {
            _extinguished += amount;
            if (IsExtinguished)
            {
                Building.GetBuildingComponent<IRiskRecipient>()?.ModifyRisk(Risk, -100f);
                Building.RemoveAddon(this);
            }
        }

        #region Saving
        [Serializable]
        public class FireData
        {
            public float Progress;
            public float Extinguished;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new FireData()
            {
                Progress = _progress,
                Extinguished = _extinguished
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<FireData>(json);

            _progress = data.Progress;
            _extinguished = data.Extinguished;
        }
        #endregion
    }
}