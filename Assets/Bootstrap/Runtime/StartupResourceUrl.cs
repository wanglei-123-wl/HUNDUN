using System;

public static class StartupResourceUrl
{
    public static bool TryNormalize(string input, string builtAddress,
        out string normalized, out string error)
    {
        normalized = null;
        error = null;
        if (!TryReadHttpUri(builtAddress, out Uri built))
        {
            error = "无法读取客户端内置资源地址";
            return false;
        }

        if (!TryReadHttpUri(input, out Uri selected)
            || !string.IsNullOrEmpty(selected.UserInfo)
            || !string.IsNullOrEmpty(selected.Query)
            || !string.IsNullOrEmpty(selected.Fragment))
        {
            error = "请输入有效的 HTTP 或 HTTPS 资源地址";
            return false;
        }

        string buildTarget = built.AbsolutePath.TrimEnd('/');
        buildTarget = buildTarget.Substring(buildTarget.LastIndexOf('/') + 1);
        string path = selected.AbsolutePath.TrimEnd('/');
        if (path.Length == 0)
            path = "/" + buildTarget;
        else if (!path.EndsWith("/" + buildTarget, StringComparison.OrdinalIgnoreCase))
        {
            error = "资源地址末尾应为 /" + buildTarget;
            return false;
        }

        normalized = selected.GetLeftPart(UriPartial.Authority) + path;
        return true;
    }

    public static string Redirect(string internalId, string builtAddress,
        string selectedAddress)
    {
        if (!TryReadHttpUri(internalId, out Uri source)
            || !TryReadHttpUri(builtAddress, out Uri built)
            || !TryReadHttpUri(selectedAddress, out Uri selected))
            return internalId;

        string buildTarget = built.AbsolutePath.TrimEnd('/');
        buildTarget = buildTarget.Substring(buildTarget.LastIndexOf('/') + 1);
        string marker = "/" + buildTarget + "/";
        int markerIndex = source.AbsolutePath.LastIndexOf(marker,
            StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
            return internalId;

        string relativePath = source.AbsolutePath.Substring(markerIndex + marker.Length);
        if (!relativePath.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase)
            && !relativePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            && !relativePath.EndsWith(".hash", StringComparison.OrdinalIgnoreCase))
            return internalId;

        return selectedAddress.TrimEnd('/') + "/" + relativePath + source.Query;
    }

    private static bool TryReadHttpUri(string value, out Uri uri)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrEmpty(uri.Host);
    }
}
