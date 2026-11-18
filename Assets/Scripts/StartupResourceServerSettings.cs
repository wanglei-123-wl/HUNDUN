using UnityEngine;

public sealed class StartupResourceServerSettings : MonoBehaviour
{
    [SerializeField, InspectorName("资源服务器地址")]
    [Tooltip("打包时写入客户端。填写 HTTP/HTTPS 地址；留空则使用 Addressables Profile 的内置地址。")]
    private string resourceServerAddress = string.Empty;

    public bool TryApply(out string error)
    {
        return StartupResourceEndpoint.TryApply(resourceServerAddress, out error);
    }
}
