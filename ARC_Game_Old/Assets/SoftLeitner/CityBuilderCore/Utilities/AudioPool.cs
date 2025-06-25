using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Random = UnityEngine.Random;

namespace CityBuilderCore
{
    /// <summary>
    /// manages multiple audio sources for easy access by key
    /// </summary>
    /// <remarks><see href="https://citybuilder.softleitner.com/manual">https://citybuilder.softleitner.com/manual</see></remarks>
    [HelpURL("https://citybuilderapi.softleitner.com/class_city_builder_core_1_1_audio_pool.html")]
    public class AudioPool : MonoBehaviour
    {
        public enum AudioPoolValueMode
        {
            None,
            Constant,
            Range
        }

        [Serializable]
        public struct AudioPoolValue
        {
            public AudioPoolValueMode Mode;
            public float Value;
            public float Maximum;
        }

        [Serializable]
        public class AudioPoolEntry
        {
            public string Key;
            public AudioSource Source;
            public AudioPoolValue Pitch;
            public AudioPoolValue Volume;

            public void Play()
            {
                switch (Pitch.Mode)
                {
                    case AudioPoolValueMode.Constant:
                        Source.pitch = Pitch.Value;
                        break;
                    case AudioPoolValueMode.Range:
                        Source.pitch = Random.Range(Pitch.Value, Pitch.Maximum);
                        break;
                }

                switch (Volume.Mode)
                {
                    case AudioPoolValueMode.Constant:
                        Source.volume = Volume.Value;
                        break;
                    case AudioPoolValueMode.Range:
                        Source.volume = Random.Range(Volume.Value, Volume.Maximum);
                        break;
                }

                Source.Play();
            }
        }

        public AudioPoolEntry[] Entries;

        private Dictionary<string, AudioPoolEntry> _entries;

        private void Awake()
        {
            Dependencies.Register(this);

            _entries = new Dictionary<string, AudioPoolEntry>(Entries.ToDictionary(e => e.Key));
        }

        public void Play(string key)
        {
            if (_entries.TryGetValue(key, out AudioPoolEntry entry))
                entry.Play();
            else
                Debug.LogWarning($"Audio Pool does not contain {key}!", this);
        }
    }
}
