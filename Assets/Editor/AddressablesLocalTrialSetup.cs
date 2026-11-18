using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

public static class AddressablesLocalTrialSetup
{
    private const string ProfileName = "LocalTrial";
    private const string GroupName = "RemoteTrialOnly";
    private const string IconGuid = "3363893543da1374099c36a427c5be5b";
    private const string IconAddress = "trial/icon/huichun";
    private const string RemoteBuildPath = "Build/RemoteContent/[BuildTarget]";
    private const string RemoteLoadPath = "http://127.0.0.1:18761/[BuildTarget]";

    [MenuItem("Tools/Resource Update/Configure Local Trial and Build")]
    public static void ConfigureAndBuild()
    {
        try
        {
            string iconPath = AssetDatabase.GUIDToAssetPath(IconGuid);
            if (string.IsNullOrEmpty(iconPath)
                || AssetDatabase.LoadAssetAtPath<Sprite>(iconPath) == null)
                throw new InvalidOperationException("The trial icon GUID does not resolve to a Sprite.");

            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings != null && settings.FindGroup(GroupName) == null)
                throw new InvalidOperationException(
                    "Addressables settings already exist; refusing to alter unrelated configuration.");

            settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            if (settings == null)
                throw new InvalidOperationException("Addressables settings could not be created.");

            string profileId = settings.profileSettings.AddProfile(
                ProfileName, settings.activeProfileId);
            settings.profileSettings.SetValue(profileId,
                AddressableAssetSettings.kRemoteBuildPath, RemoteBuildPath);
            settings.profileSettings.SetValue(profileId,
                AddressableAssetSettings.kRemoteLoadPath, RemoteLoadPath);
            settings.activeProfileId = profileId;
            settings.BuildRemoteCatalog = true;
            settings.DisableCatalogUpdateOnStartup = true;
            settings.CatalogRequestsTimeout = 5;

            if (!settings.RemoteCatalogBuildPath.SetVariableByName(
                    settings, AddressableAssetSettings.kRemoteBuildPath)
                || !settings.RemoteCatalogLoadPath.SetVariableByName(
                    settings, AddressableAssetSettings.kRemoteLoadPath))
                throw new InvalidOperationException("Remote catalog profile paths are unavailable.");

            AddressableAssetGroup group = settings.FindGroup(GroupName)
                ?? settings.CreateGroup(GroupName, false, false, true, null,
                    typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
            BundledAssetGroupSchema bundle = group.GetSchema<BundledAssetGroupSchema>();
            if (bundle == null
                || !bundle.BuildPath.SetVariableByName(
                    settings, AddressableAssetSettings.kRemoteBuildPath)
                || !bundle.LoadPath.SetVariableByName(
                    settings, AddressableAssetSettings.kRemoteLoadPath))
                throw new InvalidOperationException("Remote group schema could not be configured.");
            bundle.Timeout = 10;

            AddressableAssetEntry existing = settings.FindAssetEntry(IconGuid);
            if (existing != null && existing.parentGroup != group)
                throw new InvalidOperationException("Trial icon belongs to another Addressables group.");

            AddressableAssetEntry entry = existing ?? settings.CreateOrMoveEntry(IconGuid, group);
            entry.address = IconAddress;
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            AddressableAssetSettings.BuildPlayerContent(out var result);
            if (result == null || !string.IsNullOrEmpty(result.Error))
                throw new InvalidOperationException(
                    "Addressables build failed: " + (result == null ? "no result" : result.Error));

            Debug.Log("[AddressablesTrial] Build succeeded. Icon=" + iconPath
                + " address=" + IconAddress + " remoteBuild=" + RemoteBuildPath
                + " remoteLoad=" + RemoteLoadPath);
        }
        catch (Exception exception)
        {
            Debug.LogError("[AddressablesTrial] " + exception);
        }
    }
}
