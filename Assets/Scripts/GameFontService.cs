using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

// Owns the font for the whole play session, including Single scene changes.
// StartupContentUpdate must not release this handle when the startup scene unloads.
public static class GameFontService
{
    private static AsyncOperationHandle<TMP_FontAsset> fontHandle;
    private static List<TMP_FontAsset> registeredFallbacks;
    private static TMP_FontAsset registeredFont;

    public static bool IsReady => fontHandle.IsValid()
        && fontHandle.Status == AsyncOperationStatus.Succeeded && fontHandle.Result != null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        Release();
        Application.quitting -= Release;
        Application.quitting += Release;
    }

    public static IEnumerator Load()
    {
        if (!fontHandle.IsValid())
            fontHandle = Addressables.LoadAssetAsync<TMP_FontAsset>(RemoteGameContent.BootstrapFontAddress);
        yield return fontHandle;
        if (!IsReady)
        {
            Release();
            yield break;
        }

        // TMP-created UI keeps the small local default font and searches the
        // downloaded font for game characters that are not in the startup corpus.
        var fallbacks = TMP_Settings.fallbackFontAssets;
        if (!fallbacks.Contains(fontHandle.Result))
        {
            fallbacks.Add(fontHandle.Result);
            registeredFallbacks = fallbacks;
            registeredFont = fontHandle.Result;
        }
    }

    // Called before a catalog replacement while startup still uses only its
    // local font. A retry can then load the new font rather than retain an old bundle.
    public static void Release()
    {
        if (registeredFallbacks != null)
            registeredFallbacks.Remove(registeredFont);
        registeredFallbacks = null;
        registeredFont = null;
        if (fontHandle.IsValid())
            Addressables.Release(fontHandle);
        fontHandle = default;
    }
}
