using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityBuilderCore
{
    public static class SaveHelper
    {
        [Serializable]
        public class ContinueData
        {
            public string Mission;
            public string Difficulty;
            public string Name;

            public Mission GetMission()
            {
                return IKeyedSet<Mission>.GetKeyedObject(Mission);
            }
            public Difficulty GetDifficulty()
            {
                return IKeyedSet<Difficulty>.GetKeyedObject(Difficulty);
            }
            public bool CheckSave()
            {
                var mission = GetMission();
                if (mission == null)
                    return false;

                var difficulty = GetDifficulty();
                if (!string.IsNullOrWhiteSpace(Difficulty) && difficulty == null)
                    return false;

                return HasSave(GetKey(mission, difficulty), Name);
            }
        }

        [Serializable]
        private class StageData
        {
            public string Version;
            public bool IsFinished;
            public List<string> Saves;

            public bool AddSave(string name)
            {
                if (string.IsNullOrWhiteSpace(name))
                    name = "";//QuickSave

                if (Saves == null)
                    Saves = new List<string>();
                if (Saves.Contains(name))
                    return false;
                Saves.Add(name);
                return true;
            }
            public bool RemoveSave(string name)
            {
                if (string.IsNullOrWhiteSpace(name))
                    name = "";//QuickSave

                if (Saves == null)
                    return false;
                if (!Saves.Contains(name))
                    return false;
                Saves.Remove(name);
                return true;
            }
        }

        public static void SetContinue(string mission, string difficulty, string name, bool save = false)
        {
            PlayerPrefs.SetString("CTU", JsonUtility.ToJson(new ContinueData() { Mission = mission, Difficulty = difficulty, Name = name ?? string.Empty }));
            if (save)
                PlayerPrefs.Save();
        }
        public static bool HasContinue()
        {
            return PlayerPrefs.HasKey("CTU");
        }
        public static ContinueData GetContinue()
        {
            if (!HasContinue())
                return null;
            return JsonUtility.FromJson<ContinueData>(PlayerPrefs.GetString("CTU"));
        }

        public static bool HasSave(string key, string name)
        {
            return PlayerPrefs.HasKey(getSaveDataName(key, name));
        }
        public static void Save(string key, string name, string data, bool save = true)
        {
            var stage = getStageData(key);
            if (stage.AddSave(name))
                setStageData(key, stage);

            PlayerPrefs.SetString(getSaveDataName(key, name), data);
            if (save)
                PlayerPrefs.Save();
        }
        public static string Load(string key, string name)
        {
            return PlayerPrefs.GetString(getSaveDataName(key, name));
        }
        public static void Delete(string key, string name)
        {
            var stage = getStageData(key);
            if (!stage.RemoveSave(name))
                return;

            setStageData(key, stage);
            DeleteExtra(key, name, "META");
            PlayerPrefs.DeleteKey(getSaveDataName(key, name));
            PlayerPrefs.Save();
        }

        public static void SetExtra(string key, string name, string extraName, string data, bool save = true)
        {
            PlayerPrefs.SetString($"{getSaveDataName(key, name)}_{extraName}", data);
            if (save)
                PlayerPrefs.Save();
        }
        public static string GetExtra(string key, string name, string extraName)
        {
            return PlayerPrefs.GetString($"{getSaveDataName(key, name)}_{extraName}");
        }
        public static void DeleteExtra(string key, string name, string extraName)
        {
            PlayerPrefs.DeleteKey($"{getSaveDataName(key, name)}_{extraName}");
        }

        public static bool GetFinished(string key)
        {
            return getStageData(key)?.IsFinished ?? false;
        }
        public static void Finish(string key)
        {
            if (key == "DEBUG")
                return;

            var stage = getStageData(key);
            if (stage.IsFinished)
                return;
            stage.IsFinished = true;
            setStageData(key, stage);
            PlayerPrefs.Save();
        }

        public static List<string> GetSaves(string key) => getStageData(key)?.Saves ?? new List<string>();

        public static string GetKey(Mission mission, Difficulty difficulty)
        {
            if (mission == null && difficulty == null)
                return "MAIN";

            if (mission == null)
                return difficulty.Key;
            if (difficulty == null)
                return mission.Key;

            return mission.Key + difficulty.Key;
        }

        private static string getStageDataName(string key) => $"SAVE_{key}";
        private static string getSaveDataName(string key, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return $"SAVE_{key}_QUICK";
            else
                return $"SAVE_{key}_{name}";

        }

        private static StageData getStageData(string key)
        {
            string json = PlayerPrefs.GetString(getStageDataName(key));
            if (string.IsNullOrWhiteSpace(json))
                return new StageData() { Version = Application.version };
            return JsonUtility.FromJson<StageData>(json);
        }
        private static void setStageData(string key, StageData data)
        {
            PlayerPrefs.SetString(getStageDataName(key), JsonUtility.ToJson(data));
        }

    }
}