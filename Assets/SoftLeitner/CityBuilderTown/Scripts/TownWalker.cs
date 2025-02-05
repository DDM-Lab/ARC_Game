using CityBuilderCore;
using System;
using System.Linq;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// town walkers are the only kind of walker in the town demo and are managed by the <see cref="TownManager"/><br/>
    /// they have some inherent actions like eat or chill that they take on their own<br/>
    /// when none of those is currently needed they check if theres a <see cref="TownTask"/> available for them and perform that
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_walker.html")]
    public class TownWalker : Walker, IItemOwner
    {
        private const float MIN_FOOD = 0.4f;//cancels the walkers current task
        private const float MID_FOOD = 0.6f;//walker goes home when not occupied
        private const float MAX_FOOD = 0.6f;//above max walker does not eat
        private const float MIN_WARMTH = 0.4f;
        private const float MID_WARMTH = 0.6f;
        private const float MAX_WARMTH = 0.8f;

        public const string HOME = "HOME";
        public const string CHILL = "CHILL";
        public const string WANDER = "WANDER";
        public const string DELIVER = "DELIVER";
        public const string PROVISION = "PROVISION";

        [Tooltip("stores the walkers items, can be left as StorageMode.Free since the tasks set their own item limits")]
        public ItemStorage Storage;
        [Tooltip("when the walker has a job the jobs hat gets instantiated here")]
        public Transform HatParent;
        [Tooltip("when the walker has a task its tool visual gets instantiated here")]
        public Transform ToolParent;

        public override ItemStorage ItemStorage => Storage;
        public IItemContainer ItemContainer => Storage;

        public TownJob Job { get; private set; }
        public float Age { get; private set; }
        public float Energy { get; private set; }
        public float Food { get; private set; }
        public float Warmth { get; private set; }
        public TownTask CurrentTask { get; private set; }
        public GameObject CurrentTaskTool { get; set; }
        public TownIdentity Identity { get; private set; }

        public bool HasHome => Home?.HasInstance ?? false;
        public int VisualAge => 20 + Mathf.FloorToInt(Age / 60f);

        private GameObject _currentHat;
        private GameObject _currentTool;

        private void Update()
        {
            Food -= Time.deltaTime * Identity.FoodLoss * TownManager.Instance.FoodModifier;
            if (Food <= 0f)
            {
                Dependencies.GetOptional<INotificationManager>()?.Notify(new NotificationRequest(
                    $"{Identity.FullName} has starved!",
                    (Home != null && Home.HasInstance) ? Home.Instance.WorldCenter : transform.position));

                Finish();
            }

            Warmth -= TownManager.Instance.Coldness * Time.deltaTime * Identity.WarmthLoss * TownManager.Instance.WarmModifier;
            if (Warmth <= 0f)
            {
                Dependencies.GetOptional<INotificationManager>()?.Notify(new NotificationRequest(
                    $"{Identity.FullName} has frozen!",
                    (Home != null && Home.HasInstance) ? Home.Instance.WorldCenter : transform.position));

                Finish();
            }

            Age += Time.deltaTime * TownManager.Instance.AgeModifier;
            if (Age > Identity.Lifespan)
            {
                Dependencies.GetOptional<INotificationManager>()?.Notify(new NotificationRequest(
                    $"{Identity.FullName} has died of old age!",
                    (Home != null && Home.HasInstance) ? Home.Instance.WorldCenter : transform.position));

                Finish();
            }
        }

        public void SetJob(TownJob job)
        {
            Job = job;

            applyJob();

            if (CurrentTask?.Job != null && CurrentTask.Job != Job)
            {
                CancelProcess();
            }
        }

        public void Work(float effort = 1f)
        {
            Energy -= effort * Time.deltaTime;

            if (Energy <= 0f)
            {
                CancelProcess();
            }
        }
        public bool Recover()
        {
            Energy += Identity.EnergyRecovery * Time.deltaTime;
            return Energy < Identity.EnergyCapacity;
        }

        public override void Initialize(BuildingReference home, Vector2Int start)
        {
            base.Initialize(home, start);

            this.StartChecker(check);

            if (HasHome)
                Home.Instance.GetBuildingComponents<TownHomeComponent>().Single().AddInhabitant(this);
        }

        public override void Spawned()
        {
            base.Spawned();

            Identity = TownIdentity.Generate();
            Energy = Identity.EnergyCapacity;
            Food = Identity.FoodCapacity;
            Warmth = Identity.WarmthCapacity;

            applyIdentity();

            StartProcess(new WalkerAction[] { new WaitAction(1f) }, "SPWN");
        }

        protected override void onFinished()
        {
            base.onFinished();

            if (CurrentProcess != null)
            {
                CancelProcess();

                if (CurrentTask != null)
                {
                    CurrentTask.FinishTask(this, CurrentProcess);
                    CurrentTask = null;
                }
            }

            if (HasHome)
                Home.Instance.GetBuildingComponents<TownHomeComponent>().Single().RemoveInhabitant(this);
        }

        protected override void onProcessFinished(ProcessState process)
        {
            if (CurrentTask != null)
            {
                CurrentTask.FinishTask(this, process);
                CurrentTask = null;

                applyTask();
            }

            if (_isFinished)
                return;

            if (!process.IsCanceled && (process.Key == HOME || process.Key == PROVISION) && HasHome)
            {
                //ARRIVED HOME
                var home = Home.Instance.GetBuildingComponent<TownHomeComponent>();

                bool hasFood = true;
                bool hasWood = true;

                if (Food < Identity.FoodCapacity * MAX_FOOD)
                {
                    hasFood = home.Eat();

                    if (hasFood)
                        Food = Identity.FoodCapacity;
                }

                if (Warmth < Identity.WarmthCapacity * MAX_WARMTH)
                {
                    hasWood = home.Warm();

                    if (hasWood)
                        Warmth = Identity.WarmthCapacity;

                }

                if (!hasFood)
                {
                    if (provision(home, TownManager.Instance.Food))//TRY TO GET SOME FOOD
                        return;
                }

                if (!hasWood)
                {
                    if (provision(home, TownManager.Instance.Wood))//TRY TO GET SOME WOOD
                        return;
                }

                think(true);//dont go home when you're already there, avoids getting stuck at home if there is no food/wood
            }
            else
            {
                think();
            }
        }

        public override string GetDebugText()
        {
            return
                "PRC:" + (CurrentProcess?.Key ?? string.Empty) + Environment.NewLine +
                "JOB:" + Job + Environment.NewLine +
                "AGE:" + Age + Environment.NewLine +
                "ENG:" + Energy + Environment.NewLine +
                "FOD:" + Food + Environment.NewLine +
                "WAM:" + Warmth;
        }

        public override void OnMessage(string e)
        {
            base.OnMessage(e);

            if (CurrentTask)
                CurrentTask.OnMessage(e);
        }

        public string GetActivityText()
        {
            if (CurrentTask == null)
            {
                if (CurrentProcess?.Key == null)
                {
                    return string.Empty;
                }
                else
                {
                    switch (CurrentProcess.Key)
                    {
                        case HOME:
                            return "going home to recharge";
                        case CHILL:
                            return "taking a break from work";
                        case WANDER:
                            return "nothing to do, just wandering";
                        case DELIVER:
                            var itemQuantity = Storage.GetItemQuantities().FirstOrDefault();
                            if (itemQuantity == null)
                                return string.Empty;
                            return $"putting away {itemQuantity.Quantity} {itemQuantity.Item.Name}";
                        case PROVISION:
                            return "refilling my home";
                        default:
                            return string.Empty;
                    }
                }
            }
            else
            {
                return CurrentTask.GetDescription();
            }
        }

        private void check()
        {
            if (!HasHome)
            {
                Home = TownManager.Instance.GetHome(this);
                if (HasHome)
                    Home.Instance.GetBuildingComponents<TownHomeComponent>().Single().AddInhabitant(this);
            }
        }

        private void think(bool isHome = false)
        {
            //ITEMS >> DELIVER
            if (ItemStorage.HasItems() && deliver())
                return;

            if (!isHome)
            {
                //REALLY HUNGRY >> EAT
                if (Food < Identity.FoodCapacity * MIN_FOOD && eat())
                    return;
                //REALLY COLD >> WARM UP
                if (Warmth < Identity.WarmthCapacity * MIN_WARMTH && warm())
                    return;
            }

            //NO ENERGY >> CHILL
            if (Energy <= 0f && chill())
                return;

            //WOOD IS GETTING LOW > REFILL
            if (fillWood())
                return;

            //NO PRESSING ISSUES >> CHECK FOR WORK
            if (work())
                return;

            if (!isHome)
            {
                //NO WORK BUT KINDA HUNGRY >> EAT
                if (Food < Identity.FoodCapacity * MID_FOOD && eat())
                    return;
                //KINDA COLD >> WARM UP
                if (Warmth < Identity.WarmthCapacity * MID_WARMTH && warm())
                    return;
            }

            //NOT FULL ENERGY >> CHILL
            if (Energy < Identity.EnergyCapacity && chill())
                return;

            //NOTHING TO DO AT ALL >> WANDER AROUND
            wander();
        }

        private bool work()
        {
            var task = TownManager.Instance.GetTask(this);

            if (task == null)
                return false;

            CurrentTask = task;
            applyTask();
            StartProcess(CurrentTask.StartTask(this), CurrentTask.Key);
            return true;
        }

        private bool chill()
        {
            StartProcess(new WalkerAction[]
            {
                new WalkPointAction(){ _point= PathHelper.FindRandomPoint(CurrentPoint,2,5,PathType,PathTag)},
                new TownRecoveryAction()
            }, CHILL);
            return true;
        }

        private bool fillWood()
        {
            if (TownManager.Instance.ColdModifier == 0f)
                return false;
            if (!HasHome)
                return false;

            var home = Home.Instance.GetBuildingComponent<TownHomeComponent>();

            if (home.ItemStorage.GetItemQuantity(TownManager.Instance.Wood) + home.ItemStorage.GetReservedCapacity(TownManager.Instance.Wood) < 5 && provision(home, TownManager.Instance.Wood, false))
                return true;
            return false;
        }

        private bool eat() => goHome();
        private bool warm()
        {
            if (!HasHome)
                return false;

            var home = Home.Instance.GetBuildingComponent<TownHomeComponent>();

            if (home.ItemStorage.GetItemQuantity(TownManager.Instance.Wood) + home.ItemStorage.GetReservedCapacity(TownManager.Instance.Wood) == 0 && provision(home, TownManager.Instance.Wood, false))
                return true;

            return goHome();
        }
        private bool goHome()
        {
            if (!HasHome)
                return false;

            var path = PathHelper.FindPath(CurrentPoint, Home.Instance, PathType, PathTag);
            if (path == null)
                return false;

            StartProcess(new WalkerAction[]
            {
                new WalkPathAction(path),
                new WaitAction(1f)
            }, HOME);

            return true;
        }

        private void wander()
        {
            Vector2Int point;
            if (HasHome)
                point = PathHelper.FindRandomPoint(Home.Instance.Point, 1, 10, PathType, PathTag);
            else
                point = PathHelper.FindRandomPoint(CurrentPoint, 1, 10, PathType, PathTag);

            StartProcess(new WalkerAction[] {
                new WalkPointAction(point),
                new WaitAction(1f)
            }, WANDER);
        }

        private bool deliver()
        {
            foreach (var itemQuantity in ItemStorage.GetItemQuantities())
            {
                var path = Dependencies.Get<IReceiverPathfinder>().GetReceiverPath(Home?.Instance, CurrentPoint, itemQuantity, 1000, PathType, PathTag);
                if (path == null)
                    continue;

                StartProcess(new WalkerAction[] { new ItemsReceiverAction(itemQuantity, path.Component.Instance, path.Path, true) }, DELIVER);

                return true;
            }

            return false;
        }

        private bool provision(TownHomeComponent home, ItemCategory itemCategory, bool isHome = true)
        {
            foreach (var item in itemCategory.Items)
            {
                if (provision(home, item, isHome))
                    return true;
            }
            return false;
        }
        private bool provision(TownHomeComponent home, Item item, bool isHome = true)
        {
            if (home.ItemStorage.GetItemCapacityRemaining(item) <= 0)
                return false;

            var path = Dependencies.Get<IGiverPathfinder>().GetGiverPath(null, CurrentPoint, new ItemQuantity(item, 1), 1000, PathType, PathTag);
            if (path == null)
                return false;

            StartProcess(new WalkerAction[] { new TownProvisionAction(this, home, path.Component.Instance, path.Path, item, isHome) }, PROVISION);

            return true;
        }

        private void applyIdentity()
        {
            Pivot.localScale = new Vector3(Identity.Width, Identity.Heigth, Identity.Width);
        }
        private void applyJob()
        {
            if (_currentHat)
                Destroy(_currentHat);

            if (Job && Job.Hat)
                _currentHat = Instantiate(Job.Hat, HatParent);
        }
        private void applyTask()
        {
            if (_currentTool)
                Destroy(_currentTool);

            if (CurrentTask && CurrentTask.Tool)
                _currentTool = Instantiate(CurrentTask.Tool, ToolParent);
        }

        #region Saving
        [Serializable]
        public class WorkerWalkerData
        {
            public WalkerData WalkerData;
            public ItemStorage.ItemStorageData Storage;
            public string Job;
            public float Age;
            public float Energy;
            public float Food;
            public float Warmth;
            public TownIdentity Identity;
            public string TaskId;
        }

        public override string SaveData()
        {
            return JsonUtility.ToJson(new WorkerWalkerData()
            {
                WalkerData = savewalkerData(),
                Storage = Storage.SaveData(),
                Job = Job?.Key,
                Age = Age,
                Energy = Energy,
                Food = Food,
                Warmth = Warmth,
                Identity = Identity,
                TaskId = CurrentTask?.Id.ToString() ?? string.Empty
            });
        }
        public override void LoadData(string json)
        {
            var data = JsonUtility.FromJson<WorkerWalkerData>(json);

            loadWalkerData(data.WalkerData);

            Storage.LoadData(data.Storage);

            Job = TownManager.Instance.TownJobs.GetObject(data.Job);
            Age = data.Age;
            Energy = data.Energy;
            Food = data.Food;
            Warmth = data.Warmth;
            Identity = data.Identity;

            applyJob();
            applyIdentity();

            this.StartChecker(check);

            if (HasHome)
                Home.Instance.GetBuildingComponents<TownHomeComponent>().Single().AddInhabitant(this);

            if (!string.IsNullOrWhiteSpace(data.TaskId))
            {
                CurrentTask = TownManager.Instance.GetTask(new Guid(data.TaskId));
                CurrentTask?.ContinueTask(this);

                applyTask();
            }

            if (CurrentProcess?.Actions != null)
                continueProcess();
            else
                think();
        }
        #endregion
    }

    /// <summary>
    /// concrete implementation for serialization, not needed starting unity 2020.1
    /// </summary>
    [Serializable]
    public class ManualTownWalkerSpawner : ManualWalkerSpawner<TownWalker> { }
}
