using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum ToastType
{
    Success,
    Warning,
    Info
}

public class ToastManager : MonoBehaviour
{
    public static ToastManager Instance { get; private set; }

    [Header("Toast Settings")]
    public float displayDuration = 3f;
    public int maxToastCount = 3;
    public GameObject toastPrefab;
    public Transform toastParent;

    [Header("Audio")]
    public AudioClip[] soundEffects; // Index: 0=Success, 1=Warning, 2=Info
    public AudioSource audioSource;

    [Header("Spacing")]
    public float toastSpacing = 80f;

    private Queue<ToastData> toastQueue = new Queue<ToastData>();
    private List<GameObject> activeToasts = new List<GameObject>();

    private struct ToastData
    {
        public string message;
        public ToastType type;
        public bool playSound;

        public ToastData(string message, ToastType type, bool playSound = false)
        {
            this.message = message;
            this.type = type;
            this.playSound = playSound;
        }
    }

    private void Awake()
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

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>();
    }

    void Start()
    {
        // Subscribe to round changes
        if (GlobalClock.Instance != null)
        {
            GlobalClock.Instance.OnTimeSegmentChanged += OnRoundChanged;
        }
    }

    void OnRoundChanged(int round)
    {
        ClearAllToasts();
    }

    public void ClearAllToasts()
    {
        // Clear active toasts
        foreach (GameObject toast in activeToasts)
        {
            if (toast != null)
                Destroy(toast);
        }
        activeToasts.Clear();
        
        // Clear queue
        toastQueue.Clear();
    }

    public static void ShowToast(string message, ToastType type, bool playSound = false)
    {
        if (Instance != null)
        {
            Instance.EnqueueToast(message, type, playSound);
        }
    }

    private void EnqueueToast(string message, ToastType type, bool playSound)
    {
        toastQueue.Enqueue(new ToastData(message, type, playSound));
        ProcessToastQueue();
    }

    private void ProcessToastQueue()
    {
        if (toastQueue.Count > 0 && activeToasts.Count < maxToastCount)
        {
            ToastData toastData = toastQueue.Dequeue();
            CreateToast(toastData);
        }
    }

    private void CreateToast(ToastData toastData)
    {
        if (toastPrefab == null || toastParent == null) return;

        GameObject toastObj = Instantiate(toastPrefab, toastParent);
        ToastUI toastUI = toastObj.GetComponent<ToastUI>();

        if (toastUI != null)
        {
            toastUI.Initialize(toastData.message, toastData.type, displayDuration);
            toastUI.OnClose += () => OnToastClosed(toastObj); // disable auto dismiss
        }

        activeToasts.Add(toastObj);
        RepositionToasts();

        // Share message with ActionTrackingManager
        ActionTrackingManager.AddMessage(toastData.message);

        if (toastData.playSound && audioSource != null && soundEffects != null)
        {
            int soundIndex = (int)toastData.type;
            if (soundIndex < soundEffects.Length && soundEffects[soundIndex] != null)
            {
                audioSource.PlayOneShot(soundEffects[soundIndex]);
            }
        }

        StartCoroutine(RemoveToastAfterDelay(toastObj, displayDuration));
    }

    // Close toast message functionality
    private void OnToastClosed(GameObject toastObj)
    {
        if (toastObj != null && activeToasts.Contains(toastObj))
        {
            activeToasts.Remove(toastObj);
            Destroy(toastObj);
            RepositionToasts();
            ProcessToastQueue();
        }
    }

    private void RepositionToasts()
    {
        for (int i = 0; i < activeToasts.Count; i++)
        {
            if (activeToasts[i] != null)
            {
                ToastUI toastUI = activeToasts[i].GetComponent<ToastUI>();
                Vector2 targetPos = new Vector2(0, -i * toastSpacing);
                
                if (toastUI != null)
                {
                    toastUI.UpdateTargetPosition(targetPos);
                }
            }
        }
    }

    private IEnumerator RemoveToastAfterDelay(GameObject toastObj, float delay)
    {
        yield return new WaitForSecondsRealtime(delay);

        if (toastObj != null)
        {
            activeToasts.Remove(toastObj);
            Destroy(toastObj);
            RepositionToasts();
            ProcessToastQueue();
        }
    }

    void OnDestroy()
    {
        if (GlobalClock.Instance != null)
        {
            GlobalClock.Instance.OnTimeSegmentChanged -= OnRoundChanged;
        }
    }

    [ContextMenu("Test Toast")]
    private void TestToast()
    {
        ShowToast("This is a test toast!", ToastType.Info, true);
    }

    [ContextMenu("Test Toast Long Message")]
    private void TestToast_Long()
    {
        ShowToast("This is a test toast for long message to see if it adjusts its width!", ToastType.Info, true);
    }
}