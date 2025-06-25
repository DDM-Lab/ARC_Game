using CityBuilderCore;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderTown
{
    /// <summary>
    /// in spring the field can be tilled which makes a walker go to its point and work for a set time<br/>
    /// if the tilling was completed in spring the field will grow during summer</br>
    /// when autumn comes and the field has grown fully it can be harvested by a walker which places an item and resets the field
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual/town">https://citybuilder.softleitner.com/manual/town</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_town_1_1_town_field_task.html")]
    public class TownFieldTask : TownTask
    {
        public enum FieldState
        {
            None = 0,
            Till = 10,
            Grow = 20,
            Harvest = 30
        }
        
        [Tooltip("time it takes a walker to till the field in spring so it can grow in summer")]
        public float TillTime;
        [Tooltip("time it takes the field to grow in summer")]
        public float GrowTime;
        [Tooltip("time is takes a walker to harvest a fully grown field in autumn")]
        public float HarvestTime;
        [Tooltip("the item that is placed when a field is done harvesting")]
        public TownItemTask ItemTask;
        [Tooltip("will be raised to visualize tilling")]
        public Transform Tilled;
        [Tooltip("will be scaled to visualize growing")]
        public Transform[] Plants;

        public override IEnumerable<TownWalker> Walkers
        {
            get
            {
                if (_walker != null)
                    yield return _walker;
            }
        }

        private TownWalker _walker;
        private FieldState _state;
        private float _progress;

        private void Start()
        {
            Tilled.gameObject.SetActive(false);
            Plants.ForEach(p => p.gameObject.SetActive(false));
        }

        protected override void Update()
        {
            base.Update();

            switch (_state)
            {
                case FieldState.None:
                    if (TownManager.Instance.Season == TownSeason.Spring)
                        setState(FieldState.Till);
                    break;
                case FieldState.Till:
                    if (TownManager.Instance.Season == TownSeason.Summer)
                    {
                        if (_progress >= TillTime)
                            setState(FieldState.Grow);
                        else
                            setState(FieldState.None);
                    }
                    break;
                case FieldState.Grow:
                    if (_progress < GrowTime)
                    {
                        _progress = Mathf.Min(GrowTime, _progress + Time.deltaTime);
                    }

                    if (TownManager.Instance.Season == TownSeason.Autumn)
                    {
                        if (_progress >= GrowTime)
                            setState(FieldState.Harvest);
                        else
                            setState(FieldState.None);
                    }
                    break;
                case FieldState.Harvest:
                    if (TownManager.Instance.Season == TownSeason.Winter)
                        setState(FieldState.None);
                    break;
                default:
                    break;
            }

            updateVisual();
        }

        public override bool CanStartTask(TownWalker walker)
        {
            if (_walker == null)
            {
                switch (_state)
                {
                    case FieldState.Till:
                        return _progress < TillTime;
                    case FieldState.Harvest:
                        return _progress < HarvestTime;
                }
            }

            return false;
        }
        public override WalkerAction[] StartTask(TownWalker walker)
        {
            _walker = walker;

            return new WalkerAction[]
            {
                new WalkPointAction(){ _point = Point},
                new TownProgressAction(string.Empty,TownManager.WorkParameter)
            };
        }
        public override void ContinueTask(TownWalker walker)
        {
            _walker = walker;
        }
        public override void FinishTask(TownWalker walker, ProcessState process)
        {
            _walker = null;
        }

        public override bool ProgressTask(TownWalker walker, string key)
        {
            switch (_state)
            {
                case FieldState.Till:
                    walker.Work();
                    if (_progress < TillTime)
                        _progress = Mathf.Min(TillTime, _progress + Time.deltaTime);
                    return _progress < TillTime;
                case FieldState.Harvest:
                    walker.Work();
                    if (_progress < HarvestTime)
                        _progress = Mathf.Min(HarvestTime, _progress + Time.deltaTime);

                    if (_progress == HarvestTime)
                    {
                        TownManager.Instance.CreateTask(ItemTask, Point);
                        setState(FieldState.None);
                    }

                    return _progress < HarvestTime;
            }

            return false;
        }

        private void setState(FieldState state)
        {
            _progress = 0f;
            _state = state;

            updateVisual();
        }
        private void updateVisual()
        {
            switch (_state)
            {
                case FieldState.None:
                    Tilled.gameObject.SetActive(false);
                    Plants.ForEach(p => p.gameObject.SetActive(false));

                    if (Visual)
                        Visual.gameObject.SetActive(false);
                    break;
                case FieldState.Till:
                    Tilled.gameObject.SetActive(true);
                    Plants.ForEach(p => p.gameObject.SetActive(false));

                    Tilled.localPosition = Vector3.Lerp(new Vector3(0f, -0.1f, 0f), Vector3.zero, _progress / TillTime);

                    if (Visual)
                        Visual.gameObject.SetActive(!_isSuspended && _progress < TillTime);
                    break;
                case FieldState.Grow:
                    Tilled.gameObject.SetActive(true);
                    Plants.ForEach(p => p.gameObject.SetActive(true));

                    Tilled.localPosition = Vector3.zero;
                    Plants.ForEach(p => p.localScale = Vector3.Lerp(Vector3.zero, Vector3.one, _progress / GrowTime));

                    if (Visual)
                        Visual.gameObject.SetActive(!_isSuspended);
                    break;
                case FieldState.Harvest:
                    Tilled.gameObject.SetActive(true);
                    Plants.ForEach(p => p.gameObject.SetActive(true));

                    Tilled.localPosition = Vector3.zero;
                    Plants.ForEach(p => p.localScale = Vector3.one);

                    if (Visual)
                        Visual.gameObject.SetActive(!_isSuspended);
                    break;
            }
        }

        public override string GetDescription()
        {
            switch (_state)
            {
                case FieldState.Till:
                    return "tilling the fields";
                case FieldState.Harvest:
                    return "harvesting potatoes";
                default:
                    return string.Empty;
            }
        }
        public override string GetDebugText()
        {
            return _state.ToString() + ">" + _progress;
        }

        #region Saving
        [Serializable]
        public class TownFieldData
        {
            public int State;
            public float Progress;
        }

        protected override string saveExtras()
        {
            return JsonUtility.ToJson(new TownFieldData()
            {
                State = (int)_state,
                Progress = _progress,
            });
        }
        protected override void loadExtras(string json)
        {
            var data = JsonUtility.FromJson<TownFieldData>(json);

            _state = (FieldState)data.State;
            _progress = data.Progress;
        }
        #endregion
    }
}
