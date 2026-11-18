using UnityEngine;

[DisallowMultipleComponent]
public sealed class VillageTeleporter : MonoBehaviour
{
    [SerializeField, InspectorName("传送点 ID")]
    private string sourcePointId;

    public string SourcePointId => sourcePointId ?? string.Empty;
}
