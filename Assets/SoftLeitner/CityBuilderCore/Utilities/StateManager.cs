using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

namespace CityBuilderCore
{
    public class StateManager : MonoBehaviour
    {
        [Serializable]
        public class StateEntry
        {
            public string Key;
            public UnityEvent Started;
            public UnityEvent Entered;
            public UnityEvent Exited;

            public void Start() => Started?.Invoke();
            public void Enter() => Entered?.Invoke();
            public void Exit() => Exited?.Invoke();
        }
        [Serializable]
        public class StateOverride
        {
            public int Priority;
            public string State;
        }
        [Serializable]
        public class TransitionEntry
        {
            public string From;
            public string To;
            public UnityEvent Triggered;
        }

        public static StateManager Main;
        public static Dictionary<string, StateManager> Managers = new Dictionary<string, StateManager>();
        public static StateManager GetManager(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
                return Main;
            else
                return Managers.GetValueOrDefault(key);
        }

        [Header("Configuration")]
        [Tooltip("all the possible states the manager can be set to, the state key is used for identification and the events are where their effects are defined")]
        public StateEntry[] States;
        [Tooltip("can be used to define additional events between two specific states")]
        public TransitionEntry[] Transitions;
        [Header("Runtime")]
        [Tooltip("key of the current state")]
        public string State;
        [Tooltip("overrides can be used to temporarily jump to a different state and then back to the original one when the override is removed")]
        public StateOverride[] Overrides;

        [Tooltip("fires when the current state changes, parameter is the current state key")]
        public UnityEvent<string> StateChanged;

        public bool IsInitializing { get; private set; }
        public StateEntry CurrentState { get; private set; }

        private bool _isInitialized;

        private void Start()
        {
            IsInitializing = true;
            checkState(true);
            IsInitializing = false;
            _isInitialized = true;
        }

        private void Update()
        {
            checkState(false);
        }

        public void StartState(string state)
        {
            State = state;
            if (_isInitialized)
                checkState(true);
        }
        public void StartOverride(string state) => StartOverride(state, 100);
        public void StartOverride(string state, int priority)
        {
            addOverride(state, priority);
            if (_isInitialized)
                checkState(true);
        }

        public void SetState(string state)
        {
            State = state;
            checkState(false);
        }
        public void SetOverride(string state) => SetOverride(state, 100);
        public void SetOverride(string state, int priority)
        {
            addOverride(state, priority);
            checkState(false);
        }
        public void ResetOverride() => ResetOverride(100);
        public void ResetOverride(int priority)
        {
            var o = Overrides.FirstOrDefault(o => o.Priority == priority);
            if (o == null)
                return;

            var ods = Overrides.ToList();
            ods.Remove(o);
            Overrides = ods.ToArray();

            checkState(false);
        }

        public StateEntry GetStateEntry(string key) => States.FirstOrDefault(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        public TransitionEntry GetTransitionEntry(string from, string to) => Transitions.FirstOrDefault(t => t.From.Equals(from, StringComparison.OrdinalIgnoreCase) && t.To.Equals(to, StringComparison.OrdinalIgnoreCase));

        public void Toggle()
        {
            if (States.Length < 2)
                return;

            if (CurrentState == States[0])
                SetState(States[1].Key);
            else
                SetState(States[0].Key);
        }

        private void addOverride(string state, int priority)
        {
            var o = Overrides.FirstOrDefault(o => o.Priority == priority);
            if (o == null)
            {
                o = new StateOverride() { Priority = priority, State = state };
                var ods = Overrides.ToList();
                int i = 0;
                while (Overrides.Length < i && Overrides[i].Priority > priority)
                    i++;
                ods.Insert(i, o);
                Overrides = ods.ToArray();
            }
            else
            {
                o.State = state;
            }
        }

        private void checkState(bool isInitializing)
        {
            var newStateKey = string.Empty;
            if (Overrides == null || Overrides.Length == 0)
                newStateKey = State;
            else
                newStateKey = Overrides.OrderBy(o => o.Priority).Last().State;

            var newState = States.FirstOrDefault(s => s.Key.Equals(newStateKey, StringComparison.OrdinalIgnoreCase));

            if (CurrentState == newState)
                return;

            var transitions = Transitions.Where(t => t.From.Equals(CurrentState.Key, StringComparison.OrdinalIgnoreCase) && t.To.Equals(newState.Key, StringComparison.OrdinalIgnoreCase)).ToList();

            CurrentState?.Exit();
            CurrentState = newState;
            if (isInitializing)
                CurrentState?.Start();
            else
                CurrentState?.Enter();

            transitions.ForEach(t => t.Triggered?.Invoke());

            StateChanged?.Invoke(State);
        }
    }
}
