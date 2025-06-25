using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// moves population quantities into and out of the map depending on the current sentiment
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/people">https://citybuilder.softleitner.com/manual/people</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_migration.html")]
    public class Migration : MonoBehaviour
    {
        [Tooltip("walkers that move population into housing, leave prefab empty to move population in without walker")]
        public ManualImmigrationWalkerSpawner ImmigrationWalkers;
        [Tooltip("walkers that move population out of housing, leave prefab empty to move population out without walker")]
        public ManualEmigrationWalkerSpawner EmigrationWalkers;
        [Tooltip("walkers for population that got thrown out, leave prefab empty to not spawn")]
        public ManualHomelessWalkerSpawner HomelessWalkers;

        [Tooltip("start point for immigration walkers and end point for emigration walkers")]
        public Transform Entry;
        [Tooltip("additional entry points that are checked when no valid path is found(when using map pathing AllowInvalidPath needs to be set to false on DefaultStructureManager)")]
        public Transform[] EntryAlternatives;
        [Tooltip("type of population migrating, add one migration per population")]
        public Population Population;
        [Tooltip("check interval for immigration and emigration, combined with sentiment controls the rate at which people can move into or out of your city")]
        public float Interval = 1f;
        [Tooltip("positive sentiment makes people move to your city while negative sentiment makes them leave, higher numbers increase the rate(sentiment*interval)")]
        public float Sentiment = 1f;
        [Tooltip("optional, set to pull sentiment from a score(-100 to 100) instead of using the manual sentiment value above")]
        public Score SentimentScore;
        [Tooltip("Minumum amount of people that always immigrates before the sentiment value counts")]
        public int Minimum = 0;

        public IEnumerable<Vector2Int> EntryPositions
        {
            get
            {
                yield return Dependencies.Get<IGridPositions>().GetGridPoint(Entry.position);
                if (EntryAlternatives != null)
                {
                    foreach (var entry in EntryAlternatives)
                    {
                        yield return Dependencies.Get<IGridPositions>().GetGridPoint(entry.position);
                    }
                }
            }
        }

        private IPopulationManager _populationManager;

        private void Awake()
        {
            var assembly = typeof(Walker).Assembly;
            var check = assembly.GetTypes().SelectMany(t => t.GetFields()).Where(f => f.FieldType.Assembly == assembly && f.FieldType.IsGenericType).Select(f => f.FieldType + "-" + f.Name).ToList();

            ImmigrationWalkers.Initialize(transform);
            EmigrationWalkers.Initialize(transform);
            HomelessWalkers.Initialize(transform);
        }

        private void Start()
        {
            _populationManager = Dependencies.Get<IPopulationManager>();

            StartCoroutine(checkMigration());
        }

        private IEnumerator checkMigration()
        {
            while (true)
            {
                var sentiment = Sentiment;
                if (SentimentScore)
                    sentiment = Dependencies.Get<IScoresCalculator>().GetValue(SentimentScore) / 100f;

                if (sentiment == 0)
                    yield return new WaitForSeconds(Interval);
                else
                    yield return new WaitForSeconds(Interval / Mathf.Abs(sentiment));

                foreach (var homeless in HomelessWalkers.CurrentWalkers)//check for homeless housing
                {
                    if (homeless.IsAssigned)
                        continue;
                    var housing = _populationManager.GetHousings().OrderByDescending(h => h.GetRemainingCapacity(Population)).FirstOrDefault();
                    if (housing == null)
                        break;

                    homeless.AssignHousing(housing.Reference);
                }

                IHousing emptiestHousing = null;
                var emptiesHousingCapacity = 0;
                var totalCapacity = 0;
                IHousing fullestHousing = null;
                var fullestHousingQuantity = 0;
                var totalQuantity = 0;

                foreach (var h in _populationManager.GetHousings())
                {
                    var c = h.GetRemainingCapacity(Population);
                    if (c > emptiesHousingCapacity)
                    {
                        emptiesHousingCapacity = c;
                        emptiestHousing = h;
                    }

                    var q = h.GetQuantity(Population);
                    if (q > fullestHousingQuantity)
                    {
                        fullestHousingQuantity = q;
                        fullestHousing = h;
                    }

                    totalCapacity += c;
                    totalQuantity += h.GetQuantity(Population,true);
                }

                if (sentiment > 0f || (Minimum > 0 && totalQuantity < Minimum))//immigrate
                {
                    if (emptiestHousing == null)
                        continue;

                    if (ImmigrationWalkers.Prefab)
                    {
                        if (ImmigrationWalkers.HasWalker)
                        {
                            foreach (var entryPosition in EntryPositions)
                            {
                                var path = PathHelper.FindPath(entryPosition, emptiestHousing.Building, ImmigrationWalkers.Prefab.PathType, ImmigrationWalkers.Prefab.PathTag);
                                if (path == null)
                                    continue;

                                ImmigrationWalkers.Spawn(w => w.StartImmigrating(emptiestHousing.Reference, path, Population), entryPosition);
                                break;
                            }
                        }
                    }
                    else
                    {
                        emptiestHousing.Inhabit(Population, emptiesHousingCapacity);
                    }
                }
                else if (sentiment < 0f)//emigrate
                {
                    if (fullestHousing == null)
                        continue;

                    if (EmigrationWalkers.Prefab)
                    {
                        if (!EmigrationWalkers.HasWalker)
                            continue;

                        fullestHousing.Abandon(Population, EmigrationWalkers.Prefab.Capacity);

                        foreach (var entryPosition in EntryPositions)
                        {
                            var path = PathHelper.FindPath(fullestHousing.Building, entryPosition, EmigrationWalkers.Prefab.PathType, EmigrationWalkers.Prefab.PathTag);
                            if (path == null)
                                continue;

                            EmigrationWalkers.Spawn(w => w.StartEmigrating(path), entryPosition);
                            break;
                        }
                    }
                    else
                    {
                        fullestHousing.Abandon(Population, 1);
                    }
                }
            }
        }

        public void AddHomeless(IHousing housing, int quantity)
        {
            if (!HomelessWalkers.Prefab)
                return;

            var accessPoint = housing.Building.GetAccessPoint(HomelessWalkers.Prefab.PathType, HomelessWalkers.Prefab.PathTag);
            if (!accessPoint.HasValue)
                return;

            while (quantity > 0)
            {
                int walkerQuantity = Mathf.Min(HomelessWalkers.Prefab.Capacity, quantity);
                HomelessWalkers.Spawn(w => w.StartHomelessing(walkerQuantity, Population), accessPoint.Value);
                quantity -= walkerQuantity;
            }
        }

        #region Saving
        [Serializable]
        public class MigrationData
        {
            public string Population;
            public ManualWalkerSpawnerData ImmigrationWalkers;
            public ManualWalkerSpawnerData EmigrationWalkers;
            public ManualWalkerSpawnerData HomelessWalkers;
        }

        public MigrationData SaveData() => new MigrationData()
        {
            Population = Population.Key,
            ImmigrationWalkers = ImmigrationWalkers.SaveData(),
            EmigrationWalkers = EmigrationWalkers.SaveData(),
            HomelessWalkers = HomelessWalkers.SaveData()
        };
        public void LoadData(MigrationData data)
        {
            ImmigrationWalkers.LoadData(data.ImmigrationWalkers);
            EmigrationWalkers.LoadData(data.EmigrationWalkers);
            HomelessWalkers.LoadData(data.HomelessWalkers);
        }
        #endregion
    }
}