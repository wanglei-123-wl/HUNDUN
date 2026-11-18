using System.Collections;
using System;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;

public sealed class StartupSceneController : MonoBehaviour
{
    private const float MinimumStartupSeconds = 4.5f;

    private IEnumerator Start()
    {
        float startedAt = Time.realtimeSinceStartup;
        StartupContentUpdate contentUpdate = GetComponent<StartupContentUpdate>();
        if (contentUpdate == null)
        {
            Debug.LogError("Startup content update component is missing.");
            yield break;
        }
        StartupResourceServerSettings resourceSettings =
            GetComponent<StartupResourceServerSettings>();
        if (resourceSettings == null)
        {
            Debug.LogError("Startup resource server settings component is missing.");
            yield break;
        }
        if (!resourceSettings.TryApply(out string addressError))
        {
            Debug.LogError("Startup resource server configuration failed: " + addressError);
            yield break;
        }
        yield return contentUpdate.Run();

        float remaining = MinimumStartupSeconds - (Time.realtimeSinceStartup - startedAt);
        if (remaining > 0f)
            yield return new WaitForSecondsRealtime(remaining);

        AsyncOperationHandle<SceneInstance> login;
        try
        {
            login = RemoteGameContent.LoadSceneAsync(RemoteGameContent.LoginSceneName);
        }
        catch (Exception error)
        {
            Debug.LogError("Startup scene cannot open login scene: " + error);
            yield break;
        }
        yield return login;
        if (login.Status != AsyncOperationStatus.Succeeded)
        {
            Debug.LogError("Startup scene failed to load login scene: "
                + login.OperationException);
            Addressables.Release(login);
        }
    }
}
