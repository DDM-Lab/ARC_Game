using UnityEngine;

namespace CityBuilderCore
{
    public class ScriptableHelper : ScriptableObject
    {
        public void PlayPooled(string key)
        {
            Dependencies.Get<AudioPool>().Play(key);
        }

        public void PlayPitched05(AudioSource audioSource)
        {
            audioSource.pitch = Random.Range(0.95f, 1.05f);
            audioSource.Play();
        }
        public void PlayPitched10(AudioSource audioSource)
        {
            audioSource.pitch = Random.Range(0.90f, 1.10f);
            audioSource.Play();
        }
        public void PlayPitched20(AudioSource audioSource)
        {
            audioSource.pitch = Random.Range(0.80f, 1.20f);
            audioSource.Play();
        }
    }
}
