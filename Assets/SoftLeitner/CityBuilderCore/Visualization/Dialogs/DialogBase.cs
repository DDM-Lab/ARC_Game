using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CityBuilderCore
{
    /// <summary>
    /// simple base class for UI dialogs that mostly just provides a way to show and hide it
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_dialog_base.html")]
    public class DialogBase : MonoBehaviour
    {
        private static List<DialogBase> _dialogStack = new List<DialogBase>();

        [Tooltip("whether the game should be paused when the dialog is opened")]
        public bool PauseGame;
        [Tooltip("optional fitter that wil have its layout forcibly rebuilt")]
        public ContentSizeFitter Fitter;

        public bool IsDialogActive { get; private set; }

        protected virtual void Awake()
        {

        }

        protected virtual void Start()
        {
            if (!IsDialogActive)
                Deactivate();
        }

        protected virtual void Update()
        {
            if (!PauseGame && IsDialogActive)
            {
                updateContent(false);
                updateLayout();
            }
        }

        protected virtual void OnDestroy()
        {
            _dialogStack.Remove(this);
        }

        public void Toggle()
        {
            if (IsDialogActive)
                Deactivate();
            else
                Activate();
        }

        public void ToggleGlobal()
        {
            if (_dialogStack.Count > 0)
                _dialogStack.ToArray().ForEach(d => d.Deactivate());
            else
                Activate();
        }

        public void ActivateGlobal()
        {
            _dialogStack.ToArray().ForEach(d => d.Deactivate());

            Activate();
        }

        public virtual void Activate()
        {
            _dialogStack.Add(this);

            IsDialogActive = true;
            gameObject.SetActive(true);
            updateContent(true);
            updateLayout();

            if (PauseGame)
                Dependencies.Get<IGameSpeed>().Pause();
        }

        public virtual void Deactivate()
        {
            _dialogStack.Remove(this);

            IsDialogActive = false;
            gameObject.SetActive(false);

            if (PauseGame)
                Dependencies.Get<IGameSpeed>().Resume();
        }

        protected virtual void updateContent(bool initiate)
        {

        }

        protected virtual void updateLayout()
        {
            if (Fitter)
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)Fitter.transform);
        }
    }
}