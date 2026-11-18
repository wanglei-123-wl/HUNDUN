using UnityEngine;

[DisallowMultipleComponent]
public sealed class LocalPlayerProvider : MonoBehaviour
{
    public static LocalPlayerProvider Instance { get; private set; }

    public Transform PlayerTransform { get; private set; }
    public bool HasPlayer => PlayerTransform != null;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogError("Only one LocalPlayerProvider can exist in the active scene.", this);
            enabled = false;
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            PlayerTransform = null;
            Instance = null;
        }
    }

    public void SetPlayer(Transform playerTransform)
    {
        PlayerTransform = playerTransform;
    }

    public void ClearPlayer(Transform expectedPlayerTransform)
    {
        if (expectedPlayerTransform != null && PlayerTransform != expectedPlayerTransform)
        {
            return;
        }

        PlayerTransform = null;
    }
}
