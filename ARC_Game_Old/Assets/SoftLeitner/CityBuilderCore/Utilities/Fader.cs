using System;
using System.Collections;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// simple helper that fades a canvas group in or out over a short period
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_fader.html")]
    public class Fader : MonoBehaviour
    {
        [Tooltip("canvas group that is faded in or out(probably a fully black image on top)")]
        public CanvasGroup CanvasGroup;
        [Tooltip("how long the fading takes in seconds")]
        public float Duration;
        [Tooltip("whether interactable should be set according to visibility")]
        public bool SetInteractable;
        [Tooltip("whether the audio listener volume should also be faded")]
        public bool SetVolume;

        public void Load() => LoadNamed(null);
        public void LoadNamed(string name)
        {
            gameObject.SetActive(true);

            StopAllCoroutines();
            StartCoroutine(load(name));
        }
        private IEnumerator load(string name)
        {
            gameObject.SetActive(true);

            yield return fadeOut();
            Dependencies.Get<IGameSaver>().LoadNamed(name);
            yield return delayedFadeIn(5);
        }

        public void SetVisibility(bool value)
        {
            if (value)
                Show();
            else
                Hide();
        }

        public IEnumerator WaitForShow() => WaitForFadeOut();
        public void Show() => FadeOut();
        public void ShowNow()
        {
            gameObject.SetActive(true);
            setFading(1f);
            if (SetInteractable)
                CanvasGroup.interactable = true;
        }
        public void ShowDelayed(int frames) => DelayedFadeOut(frames);
        public void ShowDelayed(float seconds) => DelayedFadeOut(seconds);

        public IEnumerator WaitForHide() => WaitForFadeIn();
        public void Hide() => FadeIn();
        public void HideNow()
        {
            gameObject.SetActive(false);
            setFading(0f);
            if (SetInteractable)
                CanvasGroup.interactable = false;
        }
        public void HideDelayed(int frames) => DelayedFadeIn(frames);
        public void HideDelayed(float seconds) => DelayedFadeIn(seconds);

        public IEnumerator WaitForFadeIn()
        {
            gameObject.SetActive(true);

            StopAllCoroutines();
            yield return fadeIn();
        }
        public void FadeIn()
        {
            gameObject.SetActive(true);

            StopAllCoroutines();
            StartCoroutine(fadeIn());
        }
        public void FadeIn(Action callback)
        {
            gameObject.SetActive(true);

            StopAllCoroutines();
            StartCoroutine(fadeIn(callback));
        }
        public IEnumerator WaitForFadeOut()
        {
            gameObject.SetActive(true);

            StopAllCoroutines();
            yield return fadeOut();
        }
        public void FadeOut()
        {
            gameObject.SetActive(true);

            StopAllCoroutines();
            StartCoroutine(fadeOut());
        }
        public void FadeOut(Action callback)
        {
            gameObject.SetActive(true);

            StopAllCoroutines();
            StartCoroutine(fadeOut(callback));
        }

        public void DelayedFadeIn(int frames = 5)
        {
            gameObject.SetActive(true);

            StopAllCoroutines();
            StartCoroutine(delayedFadeIn(frames));
        }
        public void DelayedFadeOut(int frames = 5)
        {
            gameObject.SetActive(true);

            StopAllCoroutines();
            StartCoroutine(delayedFadeOut(frames));
        }

        public void DelayedFadeIn(float seconds)
        {
            gameObject.SetActive(true);

            StopAllCoroutines();
            StartCoroutine(delayedFadeIn(seconds));
        }
        public void DelayedFadeOut(float seconds)
        {
            gameObject.SetActive(true);

            StopAllCoroutines();
            StartCoroutine(delayedFadeOut(seconds));
        }

        private IEnumerator delayedFadeIn(int frames)
        {
            setFading(1f);
            if (SetInteractable)
                CanvasGroup.interactable = true;

            for (int i = 0; i < frames; i++)
            {
                yield return null;
            }

            yield return fadeIn();
        }
        private IEnumerator delayedFadeOut(int frames)
        {
            setFading(0f);
            if (SetInteractable)
                CanvasGroup.interactable = false;

            for (int i = 0; i < frames; i++)
            {
                yield return null;
            }

            yield return fadeOut();
        }

        private IEnumerator delayedFadeIn(float seconds)
        {
            setFading(1f);
            if (SetInteractable)
                CanvasGroup.interactable = true;
            yield return new WaitForSeconds(seconds);
            yield return fadeIn();
        }
        private IEnumerator delayedFadeOut(float seconds)
        {
            setFading(0f);
            if (SetInteractable)
                CanvasGroup.interactable = false;
            yield return new WaitForSeconds(seconds);
            yield return fadeOut();
        }

        private IEnumerator fadeIn(Action callback = null)
        {
            setFading(1f);
            if (SetInteractable)
                CanvasGroup.interactable = false;

            float _time = 0f;
            while (_time < Duration)
            {
                yield return null;
                _time += Time.unscaledDeltaTime;
                setFading(1f - _time / Duration);
            }

            gameObject.SetActive(false);
            setFading(0f);

            callback?.Invoke();
        }
        private IEnumerator fadeOut(Action callback = null)
        {
            setFading(0f);
            if (SetInteractable)
                CanvasGroup.interactable = false;

            float _time = 0f;
            while (_time < Duration)
            {
                yield return null;
                _time += Time.unscaledDeltaTime;
                setFading(_time / Duration);
            }

            setFading(1f);
            CanvasGroup.interactable = true;

            callback?.Invoke();
        }

        protected virtual void setFading(float value)
        {
            if (CanvasGroup)
                CanvasGroup.alpha = value;
            if (SetVolume)
                AudioListener.volume = 1f - value;
        }

        public static void TryFadeOut(Fader fader, Action callback)
        {
            if (fader)
                fader.FadeOut(callback);
            else
                callback?.Invoke();
        }
    }
}
