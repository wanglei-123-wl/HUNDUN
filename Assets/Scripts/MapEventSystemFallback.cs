using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(EventSystem))]
public sealed class MapEventSystemFallback : MonoBehaviour
{
    private void Awake()
    {
        EventSystem local = GetComponent<EventSystem>();
        foreach (EventSystem candidate in FindObjectsOfType<EventSystem>())
        {
            if (candidate == local || !candidate.isActiveAndEnabled)
                continue;

            local.enabled = false;
            BaseInputModule inputModule = GetComponent<BaseInputModule>();
            if (inputModule != null)
                inputModule.enabled = false;
            break;
        }
    }
}
