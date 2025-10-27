using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ServerTestUI : MonoBehaviour
{
    public Button testButton;
    public TMP_Text resultText; // Or TMP_Text if using TextMeshPro

    private string host = "janus.hss.cmu.edu";
    private int port = 8998;

    void Start()
    {
        if (testButton != null)
        {
            testButton.onClick.AddListener(() => TestServerCommunication());
        }
        if (resultText != null)
        {
            resultText.text = "Press the button to test connection.";
        }
    }

    async void TestServerCommunication()
    {
        try
        {
            resultText.text = "Connecting...";

            using (TcpClient client = new TcpClient())
            {
                await client.ConnectAsync(host, port);
                resultText.text = "Connected! Sending message...";

                using (NetworkStream stream = client.GetStream())
                {
                    string jsonToSend = "{\"arg\":\"hello\"}\n"; // newline at end = message boundary
                    byte[] dataToSend = Encoding.UTF8.GetBytes(jsonToSend);
                    await stream.WriteAsync(dataToSend, 0, dataToSend.Length);

                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                    {
                        string response = await reader.ReadLineAsync();
                        resultText.text = $"Response: {response}";
                        Debug.Log($"📩 Received: {response}");
                    }
                }
            }
        }
        catch (Exception e)
        {
            resultText.text = $"Error: {e.Message}";
            Debug.LogError(e);
        }
    }
}
