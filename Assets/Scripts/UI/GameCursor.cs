using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class GameCursor : MonoBehaviour
{
    [SerializeField, InspectorName("光标贴图")] private Texture2D cursorTexture;
    [SerializeField, InspectorName("点击热点（从左上角计）")]
    private Vector2 hotspot = new Vector2(8f, 6f);
    [SerializeField, InspectorName("场景预览图")] private Image cursorPreview;

    private bool previewWasEnabled;

    private void OnEnable()
    {
        ApplyCursor();
        if (cursorPreview == null) return;
        previewWasEnabled = cursorPreview.enabled;
        cursorPreview.raycastTarget = false;
        cursorPreview.enabled = false;
    }

    private void OnApplicationFocus(bool focused)
    {
        if (focused) ApplyCursor();
    }

    private void ApplyCursor()
    {
        if (cursorTexture != null)
            Cursor.SetCursor(cursorTexture, hotspot, CursorMode.Auto);
    }

    private void OnDisable()
    {
        if (cursorPreview != null) cursorPreview.enabled = previewWasEnabled;
        Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
    }

#if UNITY_EDITOR
    // Player Settings supplies the standalone cursor from startup onward.
    // Apply the same default when entering Play Mode from any editor scene.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyEditorDefaultCursor()
    {
        Texture2D texture = UnityEditor.PlayerSettings.defaultCursor;
        if (texture != null)
            Cursor.SetCursor(texture, UnityEditor.PlayerSettings.cursorHotspot, CursorMode.Auto);
    }
#endif
}
