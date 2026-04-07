using UnityEngine;

/// <summary>
/// Attach to the Forest prefab.
/// On Awake, picks a random sprite from the sprites list and applies it.
/// </summary>
[RequireComponent(typeof(SpriteRenderer))]
public class ForestVisual : MonoBehaviour
{
    [Tooltip("All possible forest sprites — one is chosen at random when the prefab spawns")]
    public Sprite[] sprites;

    void Awake()
    {
        if (sprites == null || sprites.Length == 0) return;
        GetComponent<SpriteRenderer>().sprite = sprites[Random.Range(0, sprites.Length)];
    }
}
