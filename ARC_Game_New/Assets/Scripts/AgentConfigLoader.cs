using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Reads agents_config.json and exposes agent definitions to other Unity components.
/// Attach to any persistent GameObject (e.g. the WebSocketManager GameObject).
///
/// Set ConfigFilePath in the Inspector to the absolute or project-relative path
/// of agents_config.json (e.g. "../config/agents_config.json").
/// </summary>
public class AgentConfigLoader : MonoBehaviour
{
    [Header("Config")]
    [Tooltip("Absolute or project-relative path to agents_config.json")]
    public string ConfigFilePath = "../config/agents_config.json";

    [Header("Status (Read-Only)")]
    public bool IsLoaded = false;
    public string LoadError = "";

    public AgentsConfig Config { get; private set; }

    void Awake()
    {
        LoadConfig();
    }

    public void LoadConfig()
    {
        string path = Path.IsPathRooted(ConfigFilePath)
            ? ConfigFilePath
            : Path.GetFullPath(Path.Combine(Application.dataPath, "..", ConfigFilePath));

        if (!File.Exists(path))
        {
            LoadError = $"agents_config.json not found at: {path}";
            Debug.LogError($"[AgentConfigLoader] {LoadError}");
            IsLoaded = false;
            return;
        }

        try
        {
            string json = File.ReadAllText(path);
            Config = JsonUtility.FromJson<AgentsConfig>(json);
            IsLoaded = true;
            LoadError = "";
            int agentCount = Config?.agents != null ? Config.agents.Length : 0;
            Debug.Log($"[AgentConfigLoader] Loaded {agentCount} agents. "
                     + $"Order rule: {Config?.agent_order_rule}");

            // Warn if config changes on disk after load (hot-reload not supported in v1)
            try
            {
                string dir = Path.GetDirectoryName(path);
                string file = Path.GetFileName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    var watcher = new FileSystemWatcher(dir, file);
                    watcher.Changed += (s, e) => Debug.LogWarning(
                        "[AgentConfigLoader] agents_config.json changed on disk. "
                        + "Restart required to apply changes.");
                    watcher.EnableRaisingEvents = true;
                }
            }
            catch (Exception watchEx)
            {
                // FileSystemWatcher may not work on all platforms — non-fatal
                Debug.LogWarning($"[AgentConfigLoader] Could not set up file watcher: {watchEx.Message}");
            }
        }
        catch (Exception ex)
        {
            LoadError = $"Failed to parse agents_config.json: {ex.Message}";
            Debug.LogError($"[AgentConfigLoader] {LoadError}");
            IsLoaded = false;
        }
    }

    /// <summary>Return the agent config for the given agent name, or null.</summary>
    public AgentDefinition GetAgent(string agentName)
    {
        if (Config?.agents == null) return null;
        foreach (var agent in Config.agents)
            if (agent.subagent_name == agentName) return agent;
        return null;
    }

    /// <summary>Return the director agent definition, or null.</summary>
    public AgentDefinition GetDirector()
    {
        if (Config?.agents == null) return null;
        foreach (var agent in Config.agents)
            if (agent.role == "director") return agent;
        return null;
    }

    /// <summary>Return all subagent definitions (role != "director").</summary>
    public AgentDefinition[] GetSubagents()
    {
        if (Config?.agents == null) return new AgentDefinition[0];
        var result = new System.Collections.Generic.List<AgentDefinition>();
        foreach (var agent in Config.agents)
            if (agent.role == "subagent") result.Add(agent);
        return result.ToArray();
    }
}

// ── Serializable config mirror (matches agents_config.json schema) ──

[Serializable]
public class AgentsConfig
{
    public string agent_order_rule;
    public AgentDefinition[] agents;
}

[Serializable]
public class AgentDefinition
{
    public string subagent_name;
    public string role;               // "subagent" | "director"
    public string actor_type;         // "auto" | "choices" | "manual" | "llm"
    public int num_choices;
    public int max_actions_per_package;
    public string talkinghead_endpoint;   // TaskOfficer enum name
    public SubActionEntry[] subaction_space;
    public string[] subobservation_space;
    public string llm_model;
    public string llm_endpoint;
    public int llm_port;
    public int turn_token_budget;
    public string system_prompt;
    public string[] can_address;
}

[Serializable]
public class SubActionEntry
{
    public string category;
}
