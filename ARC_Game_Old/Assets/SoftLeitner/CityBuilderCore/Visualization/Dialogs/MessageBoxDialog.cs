using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace CityBuilderCore
{
    public class MessageBoxDialog : DialogBase
    {
        public enum MessageBoxButtons
        {
            Ok,
            OkCancel,
            YesNo,
            YesNoCancel
        }
        public enum MessageBoxResult
        {
            Ok,
            Cancel,
            Yes,
            No
        }

        [Tooltip("can be used to set which buttons should be visible, usually set when activating the message box")]
        public MessageBoxButtons Buttons;
        [Header("Text")]
        [Tooltip("used to display the title text when it is passed in the Show method")]
        public TMPro.TMP_Text Title;
        [Tooltip("used to display the message text when it is passed in the Show method")]
        public TMPro.TMP_Text Message;
        [Header("Buttons")]
        [Tooltip("activated when required by the buttons, dialog subscribes to click event to set result")]
        public Button Ok;
        [Tooltip("activated when required by the buttons, dialog subscribes to click event to set result")]
        public Button Cancel;
        [Tooltip("activated when required by the buttons, dialog subscribes to click event to set result")]
        public Button Yes;
        [Tooltip("activated when required by the buttons, dialog subscribes to click event to set result")]
        public Button No;
        [Header("Events")]
        [Tooltip("fired when the dialogs result is set and the dialog closes")]
        public UnityEvent<MessageBoxResult> Clicked;
        [Tooltip("fired when the dialogs is closed by clicking OK")]
        public UnityEvent OkClicked;
        [Tooltip("fired when the dialogs is closed by clicking Cancel")]
        public UnityEvent CancelClicked;
        [Tooltip("fired when the dialogs is closed by clicking Yes")]
        public UnityEvent YesClicked;
        [Tooltip("fired when the dialogs is closed by clicking No")]
        public UnityEvent NoClicked;

        private Action<MessageBoxResult> _callback;

        protected override void Awake()
        {
            base.Awake();

            if (Ok)
                Ok.onClick.AddListener(new UnityAction(() => SetDialogResult(MessageBoxResult.Ok)));
            if (Cancel)
                Cancel.onClick.AddListener(new UnityAction(() => SetDialogResult(MessageBoxResult.Cancel)));
            if (Yes)
                Yes.onClick.AddListener(new UnityAction(() => SetDialogResult(MessageBoxResult.Yes)));
            if (No)
                No.onClick.AddListener(new UnityAction(() => SetDialogResult(MessageBoxResult.No)));
        }

        public override void Activate()
        {
            base.Activate();

            if (Ok)
                Ok.gameObject.SetActive(Buttons == MessageBoxButtons.Ok || Buttons == MessageBoxButtons.OkCancel);
            if (Cancel)
                Cancel.gameObject.SetActive(Buttons == MessageBoxButtons.OkCancel || Buttons == MessageBoxButtons.YesNoCancel);
            if (Yes)
                Yes.gameObject.SetActive(Buttons == MessageBoxButtons.YesNo || Buttons == MessageBoxButtons.YesNoCancel);
            if (No)
                No.gameObject.SetActive(Buttons == MessageBoxButtons.YesNo || Buttons == MessageBoxButtons.YesNoCancel);
        }

        public override void Deactivate()
        {
            base.Deactivate();

            _callback = null;
        }

        public void Show()
        {
            Activate();
        }
        public void Show(string title, string message)
        {
            if (Title)
                Title.text = title;
            if (Message)
                Message.text = message;
            Activate();
        }
        public void Show(string title, string message, MessageBoxButtons buttons, Action<MessageBoxResult> callback)
        {
            if (Title)
                Title.text = title;
            if (Message)
                Message.text = message;
            Buttons = buttons;
            _callback = callback;
            Activate();
        }

        public void Check(string title, string message, Action ok)
        {
            Show(title, message, MessageBoxButtons.OkCancel, r =>
            {
                if (r == MessageBoxResult.Ok)
                    ok();
            });
        }
        public void CheckYes(string title, string message, Action yes)
        {
            Show(title, message, MessageBoxButtons.YesNo, r =>
            {
                if (r == MessageBoxResult.Yes)
                    yes();
            });
        }

        public void SetDialogResult(MessageBoxResult result)
        {
            _callback?.Invoke(result);
            Deactivate();
        }

        public static void TryCheck(MessageBoxDialog messageBox, string title, string message, Action ok)
        {
            if (messageBox)
                messageBox.Check(title, message, ok);
            else
                ok?.Invoke();
        }
        public static void TryCheckYes(MessageBoxDialog messageBox, string title, string message, Action yes)
        {
            if (messageBox)
                messageBox.CheckYes(title, message, yes);
            else
                yes?.Invoke();
        }
    }
}
