#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

internal static class GrassRenderDiagnostics
{
    private const int SampleSize = 64;
    private static readonly HashSet<string> loggedAddresses = new HashSet<string>();
    private static bool worldLogged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        loggedAddresses.Clear();
        worldLogged = false;
    }

    public static void LogEndpoint(string built, string selected)
    {
        Debug.Log("[GrassDiag][Endpoint] built=" + built
            + " selected=" + (selected ?? built)
            + " unity=" + Application.unityVersion
            + " buildGuid=" + Application.buildGUID);
    }

    public static void LogResolvedAddress(string original, string resolved)
    {
        try
        {
            if (string.IsNullOrEmpty(resolved)
                || (!resolved.Contains("catalog_") && !resolved.Contains("remotegamecontent_scenes_all")))
                return;

            if (loggedAddresses.Add(original + "\n" + resolved))
                Debug.Log("[GrassDiag][Address] original=" + original + " resolved=" + resolved);
        }
        catch (Exception error)
        {
            Debug.LogWarning("[GrassDiag][Address] unavailable=" + error.Message);
        }
    }

    public static void LogCatalog(string[] updatedLocators)
    {
        try
        {
            Debug.Log("[GrassDiag][Catalog] updatedLocators="
                + string.Join(",", updatedLocators)
                + " worldKey=" + RemoteGameContent.WorldSceneAddress);
            foreach (var locator in Addressables.ResourceLocators)
            {
                if (!locator.Locate(RemoteGameContent.WorldSceneAddress, null,
                    out IList<IResourceLocation> locations) || locations == null)
                    continue;

                Debug.Log("[GrassDiag][Catalog] locator=" + locator.LocatorId
                    + " worldLocations=" + locations.Count);
                var visited = new HashSet<IResourceLocation>();
                foreach (IResourceLocation location in locations)
                    LogLocation(location, visited);
            }
        }
        catch (Exception error)
        {
            Debug.LogWarning("[GrassDiag][Catalog] unavailable="
                + error.GetType().Name + ": " + error.Message);
        }
    }

    private static void LogLocation(IResourceLocation location,
        HashSet<IResourceLocation> visited)
    {
        if (location == null || !visited.Add(location))
            return;

        var options = location.Data as AssetBundleRequestOptions;
        string resolved = Addressables.InternalIdTransformFunc == null
            ? location.InternalId
            : Addressables.InternalIdTransformFunc(location);
        Debug.Log("[GrassDiag][Location] key=" + location.PrimaryKey
            + " provider=" + location.ProviderId
            + " raw=" + location.InternalId
            + " resolved=" + resolved
            + " bundleHash=" + (options == null ? "<none>" : options.Hash)
            + " crc=" + (options == null ? "<none>" : options.Crc.ToString()));

        if (location.Dependencies == null)
            return;
        foreach (IResourceLocation dependency in location.Dependencies)
            LogLocation(dependency, visited);
    }

    public static void LogWorld()
    {
        if (worldLogged || SceneManager.GetActiveScene().name != RemoteGameContent.Map001SceneName)
            return;
        worldLogged = true;
        try
        {
            LogWorldCore();
        }
        catch (Exception error)
        {
            Debug.LogWarning("[GrassDiag][World] unavailable="
                + error.GetType().Name + ": " + error.Message);
        }
    }

    private static void LogWorldCore()
    {
        int quality = QualitySettings.GetQualityLevel();
        Debug.Log("[GrassDiag][World] scene=" + SceneManager.GetActiveScene().name
            + " quality=" + quality + ":" + QualitySettings.names[quality]
            + " softVegetation=" + QualitySettings.softVegetation
            + " graphics=" + SystemInfo.graphicsDeviceType
            + " colorSpace=" + QualitySettings.activeColorSpace
            + " terrains=" + Terrain.activeTerrains.Length);

        bool alphaProbeValid = CheckAlphaProbe();
        var sampled = new HashSet<Texture2D>();
        foreach (Terrain terrain in Terrain.activeTerrains)
        {
            if (terrain == null || terrain.terrainData == null)
                continue;

            DetailPrototype[] details = terrain.terrainData.detailPrototypes;
            Debug.Log("[GrassDiag][Terrain] name=" + terrain.name
                + " data=" + terrain.terrainData.name
                + " details=" + details.Length
                + " drawFoliage=" + terrain.drawTreesAndFoliage);
            for (int index = 0; index < details.Length; index++)
            {
                DetailPrototype detail = details[index];
                Texture2D texture = detail.prototypeTexture;
                Debug.Log("[GrassDiag][Detail] terrain=" + terrain.name
                    + " index=" + index
                    + " mode=" + detail.renderMode
                    + " mesh=" + (detail.prototype == null ? "<none>" : detail.prototype.name)
                    + " texture=" + (texture == null ? "<none>" : texture.name)
                    + " format=" + (texture == null ? "<none>" : texture.format.ToString())
                    + " graphicsFormat=" + (texture == null ? "<none>" : texture.graphicsFormat.ToString()));
                if (alphaProbeValid && texture != null && sampled.Add(texture))
                    SampleAlpha(texture);
            }
        }
    }

    private static bool CheckAlphaProbe()
    {
        Texture2D probe = null;
        try
        {
            probe = new Texture2D(SampleSize, SampleSize, TextureFormat.RGBA32, false, true);
            probe.filterMode = FilterMode.Point;
            var pixels = new Color32[SampleSize * SampleSize];
            for (int y = 0; y < SampleSize; y++)
                for (int x = 0; x < SampleSize; x++)
                    pixels[y * SampleSize + x] = new Color32(255, 255, 255,
                        x < SampleSize / 2 ? (byte)0 : (byte)255);
            probe.SetPixels32(pixels);
            probe.Apply(false, false);

            bool read = TryReadAlpha(probe, out int transparent,
                out byte minimum, out byte maximum, out _, out string error);
            bool valid = read && minimum == 0 && maximum == 255
                && transparent == SampleSize * SampleSize / 2;
            Debug.Log("[GrassDiag][AlphaProbe] valid=" + valid
                + " transparent=" + transparent + "/" + pixels.Length
                + " min=" + minimum + " max=" + maximum
                + (error == null ? "" : " error=" + error));
            return valid;
        }
        catch (Exception error)
        {
            Debug.LogWarning("[GrassDiag][AlphaProbe] unavailable="
                + error.GetType().Name + ": " + error.Message);
            return false;
        }
        finally
        {
            if (probe != null)
                UnityEngine.Object.Destroy(probe);
        }
    }

    private static void SampleAlpha(Texture2D texture)
    {
        if (!TryReadAlpha(texture, out int transparent, out byte minimum,
            out byte maximum, out byte corner, out string error))
        {
            Debug.LogWarning("[GrassDiag][Alpha] texture=" + texture.name
                + " unavailable=" + error);
            return;
        }
        Debug.Log("[GrassDiag][Alpha] texture=" + texture.name
            + " sample=" + SampleSize + "x" + SampleSize
            + " min=" + minimum + " max=" + maximum
            + " transparent=" + transparent + "/" + (SampleSize * SampleSize)
            + " corner=" + corner);
    }

    private static bool TryReadAlpha(Texture2D texture, out int transparent,
        out byte minimum, out byte maximum, out byte corner, out string error)
    {
        transparent = 0;
        minimum = byte.MaxValue;
        maximum = byte.MinValue;
        corner = 0;
        error = null;
        RenderTexture target = null;
        Texture2D readback = null;
        RenderTexture previous = RenderTexture.active;
        try
        {
            if (!SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGB32))
            {
                error = "ARGB32 render target unsupported";
                return false;
            }

            target = RenderTexture.GetTemporary(SampleSize, SampleSize, 0,
                RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(texture, target);
            RenderTexture.active = target;
            readback = new Texture2D(SampleSize, SampleSize, TextureFormat.RGBA32, false, true);
            readback.ReadPixels(new Rect(0, 0, SampleSize, SampleSize), 0, 0, false);
            readback.Apply(false, false);

            Color32[] pixels = readback.GetPixels32();
            corner = pixels[0].a;
            foreach (Color32 pixel in pixels)
            {
                if (pixel.a < 128)
                    transparent++;
                if (pixel.a < minimum)
                    minimum = pixel.a;
                if (pixel.a > maximum)
                    maximum = pixel.a;
            }
            return true;
        }
        catch (Exception exception)
        {
            error = exception.GetType().Name + ": " + exception.Message;
            return false;
        }
        finally
        {
            RenderTexture.active = previous;
            if (readback != null)
                UnityEngine.Object.Destroy(readback);
            if (target != null)
                RenderTexture.ReleaseTemporary(target);
        }
    }
}
#endif
