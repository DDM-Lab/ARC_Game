using UnityEngine;

public class TaskResultManager : MonoBehaviour
{
    [Header("Popup Settings")]
    public GameObject taskResultPopupPrefab;
    public Transform popupContainer;
    
    public static TaskResultManager Instance { get; private set; }
    
    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
        }
    }
    
    public void ShowTaskResult(GameTask task, string reason = "")
    {
        if (taskResultPopupPrefab == null || popupContainer == null)
        {
            Debug.LogWarning("TaskResultManager: Prefab or container not assigned!");
            return;
        }
        
        GameObject popupObj = Instantiate(taskResultPopupPrefab, popupContainer);
        TaskResultPopup popup = popupObj.GetComponent<TaskResultPopup>();
        
        if (popup != null)
        {
            popup.Initialize(task, reason);
        }
    }
}