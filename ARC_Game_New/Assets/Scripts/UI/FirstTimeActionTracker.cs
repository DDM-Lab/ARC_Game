using UnityEngine;

public class FirstTimeActionTracker : MonoBehaviour
{
    public static FirstTimeActionTracker Instance { get; private set; }
    
    // Action keys
    private const string EXECUTE_KEY = "FirstTime_Execute";
    private const string CONSTRUCT_KEY = "FirstTime_Construct";
    private const string TASK_CONFIRM_KEY = "FirstTime_TaskConfirm";
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    public bool IsFirstTime(string actionKey)
    {
        return PlayerPrefs.GetInt(actionKey, 1) == 1;
    }
    
    public void MarkAsCompleted(string actionKey)
    {
        PlayerPrefs.SetInt(actionKey, 0);
        PlayerPrefs.Save();
    }
    
    // Public methods for each action
    public bool IsFirstExecute() => IsFirstTime(EXECUTE_KEY);
    public void MarkExecuteCompleted() => MarkAsCompleted(EXECUTE_KEY);
    
    public bool IsFirstConstruct() => IsFirstTime(CONSTRUCT_KEY);
    public void MarkConstructCompleted() => MarkAsCompleted(CONSTRUCT_KEY);
    
    public bool IsFirstTaskConfirm() => IsFirstTime(TASK_CONFIRM_KEY);
    public void MarkTaskConfirmCompleted() => MarkAsCompleted(TASK_CONFIRM_KEY);
    
    [ContextMenu("Reset All First Time Flags")]
    public void ResetAllFlags()
    {
        PlayerPrefs.DeleteKey(EXECUTE_KEY);
        PlayerPrefs.DeleteKey(CONSTRUCT_KEY);
        PlayerPrefs.DeleteKey(TASK_CONFIRM_KEY);
        PlayerPrefs.Save();
        Debug.Log("All first-time flags reset");
    }
}