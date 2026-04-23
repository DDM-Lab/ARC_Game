using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ActionTrackingManager : MonoBehaviour
{
    public static ActionTrackingManager Instance { get; private set; }

    [Header("Round Settings")]
    public int currentDay = 1;
    public int currentRound = 1;

    private List<ActionMessage> allMessages = new List<ActionMessage>();
    private Queue<ActionMessage> unreadMessages = new Queue<ActionMessage>();

    public class ActionMessage
    {
        public string message;
        public int day;
        public int round;
        public float timestamp;

        public ActionMessage(string msg, int d, int r)
        {
            message = msg;
            day = d;
            round = r;
            timestamp = Time.time;
        }

        public string GetDayRoundText()
        {
            return $"{day}-{round}";
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
    }

    // Called by ToastManager or any other system to add a message
    public static void AddMessage(string message)
    {
        if (Instance != null)
        {
            Instance.AddMessageInternal(message);
        }
    }

    private void AddMessageInternal(string message)
    {
        ActionMessage newMessage = new ActionMessage(message, currentDay, currentRound);
        allMessages.Add(newMessage);
        unreadMessages.Enqueue(newMessage);
        
        // Notify the panel if it exists
        ActionTrackingPanel panel = FindObjectOfType<ActionTrackingPanel>();
        if (panel != null)
        {
            panel.OnNewMessage(newMessage);
        }
        
        // Notify the rotator if it exists
        ActionMessageRotator rotator = FindObjectOfType<ActionMessageRotator>();
        if (rotator != null)
        {
            rotator.OnNewMessage();
        }
    }

    // Get next unread message for the rotator
    public ActionMessage GetNextUnreadMessage()
    {
        if (unreadMessages.Count > 0)
        {
            return unreadMessages.Dequeue();
        }
        return null;
    }

    // Get all messages for the panel
    public List<ActionMessage> GetAllMessages()
    {
        return new List<ActionMessage>(allMessages);
    }

    // Set current day and round
    public void SetDayAndRound(int day, int round)
    {
        currentDay = day;
        currentRound = round;
    }

    // Clear all messages (for new game)
    public void ClearMessages()
    {
        allMessages.Clear();
        unreadMessages.Clear();
    }
}