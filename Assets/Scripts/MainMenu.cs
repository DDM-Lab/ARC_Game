using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    [SerializeField] private string gameStartScene = "SampleScene";
    
    // Run when the game is started.
    public void PlayGame()
    {
        SceneManager.LoadScene(gameStartScene);
    }

    // Run when the game is exited.
    public void QuitGame()
    {
        Debug.Log("Quitting game...");
        Application.Quit();
    }
}
