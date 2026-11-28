using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class StartupResourceServerSettings : MonoBehaviour
{
    // Kept for scenes and prefabs saved before multiple resource servers were supported.
    [SerializeField, HideInInspector]
    private string resourceServerAddress = string.Empty;

    [SerializeField] private List<StartupResourceServerProfile> resourceServers =
        new List<StartupResourceServerProfile>();
    [SerializeField, HideInInspector] private string selectedServerId = string.Empty;

    private void OnValidate()
    {
        // Also cover entries added through Unity's default Inspector during a reload.
        if (resourceServers != null)
        {
            var ids = new HashSet<string>();
            foreach (StartupResourceServerProfile server in resourceServers)
                server?.EnsureUniqueId(ids);
        }
        MigrateLegacyAddress();
    }

    public bool TryGetSelectedAddress(out string address, out string error)
    {
        address = null;
        error = null;
        if (string.IsNullOrEmpty(selectedServerId))
        {
            address = resourceServerAddress;
            return true;
        }

        StartupResourceServerProfile selected = null;
        if (resourceServers != null)
        {
            foreach (StartupResourceServerProfile server in resourceServers)
            {
                if (server == null || server.Id != selectedServerId)
                    continue;
                if (selected != null)
                {
                    error = "资源服务器配置标识重复，请重新添加重复的配置项。";
                    return false;
                }
                selected = server;
            }
        }

        if (selected == null)
        {
            error = "当前选择的资源服务器不存在，请重新选择。";
            return false;
        }

        address = selected.Address;
        return true;
    }

    public bool MigrateLegacyAddress()
    {
        if (string.IsNullOrWhiteSpace(resourceServerAddress))
            return false;

        if (resourceServers == null)
            resourceServers = new List<StartupResourceServerProfile>();
        StartupResourceServerProfile original = resourceServers.Find(server => server != null
            && !string.IsNullOrEmpty(server.Id) && server.Address == resourceServerAddress);
        if (original == null)
        {
            original = new StartupResourceServerProfile("原有资源服", resourceServerAddress);
            resourceServers.Add(original);
        }
        if (string.IsNullOrEmpty(selectedServerId))
            selectedServerId = original.Id;
        resourceServerAddress = string.Empty;
        return true;
    }

    public bool TryApply(out string error)
    {
        if (!TryGetSelectedAddress(out string address, out error))
            return false;
        return StartupResourceEndpoint.TryApply(address, out error);
    }
}

[Serializable]
public sealed class StartupResourceServerProfile
{
    [SerializeField, HideInInspector] private string id = Guid.NewGuid().ToString("N");
    [SerializeField] private string name;
    [SerializeField] private string address;

    public string Id => id;
    public string Name => name;
    public string Address => address;

    public StartupResourceServerProfile(string name, string address)
    {
        this.name = name;
        this.address = address;
    }

    internal void EnsureUniqueId(HashSet<string> ids)
    {
        if (!string.IsNullOrEmpty(id) && ids.Add(id))
            return;
        do { id = Guid.NewGuid().ToString("N"); }
        while (!ids.Add(id));
    }
}
