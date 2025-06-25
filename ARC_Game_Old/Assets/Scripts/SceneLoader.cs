using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.Collections;

public class SceneLoader : MonoBehaviour
{
    public string sceneToLoad;
    public Image fadeImage;
    public float fadeSpeed = 0.8f;
    
    public void LoadGameScene()
    {
        StartCoroutine(FadeAndLoadScene());
    }
    
    public void QuitGame()
    {
        StartCoroutine(FadeAndQuit());
    }
    
    private IEnumerator FadeAndLoadScene()
    {
        // Fade to black
        fadeImage.gameObject.SetActive(true);
        float alpha = 0;
        
        while (alpha < 1)
        {
            alpha += Time.deltaTime * fadeSpeed;
            fadeImage.color = new Color(0, 0, 0, alpha);
            yield return null;
        }
        
        // Load the new scene
        SceneManager.LoadScene(sceneToLoad);
    }
    
    private IEnumerator FadeAndQuit()
    {
        // Fade to black
        fadeImage.gameObject.SetActive(true);
        float alpha = 0;
        
        while (alpha < 1)
        {
            alpha += Time.deltaTime * fadeSpeed;
            fadeImage.color = new Color(0, 0, 0, alpha);
            yield return null;
        }
        
        // Quit the application
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}