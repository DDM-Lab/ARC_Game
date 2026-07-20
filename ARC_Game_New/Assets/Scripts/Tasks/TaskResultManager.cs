using UnityEngine;

public class TaskResultManager : MonoBehaviour
{
    [Header("Popup Settings")]
    public GameObject taskResultPopupPrefab;
    public Transform popupContainer;

    public static TaskResultManager Instance { get; private set; }

    private int activePopupCount = 0;
    private float savedTimeScale = 1f;
    private bool pausedByPopup = false;

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

        if (activePopupCount == 0 && Time.timeScale > 0f)
        {
            savedTimeScale = Time.timeScale;
            Time.timeScale = 0f;
            pausedByPopup = true;
        }
        activePopupCount++;

        GameObject popupObj = Instantiate(taskResultPopupPrefab, popupContainer);
        TaskResultPopup popup = popupObj.GetComponent<TaskResultPopup>();

        if (popup != null)
        {
            popup.Initialize(task, reason);
        }
    }

    public void OnPopupClosed()
    {
        activePopupCount = Mathf.Max(0, activePopupCount - 1);

        if (activePopupCount == 0 && pausedByPopup)
        {
            Time.timeScale = savedTimeScale;
            pausedByPopup = false;
        }
    }
}