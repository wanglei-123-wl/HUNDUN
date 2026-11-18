using UnityEngine;

public sealed class HealthBarBillboard : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private bool reverseFacing = false;

    private void LateUpdate()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera == null)
        {
            return;
        }

        transform.rotation = targetCamera.transform.rotation;

        if (reverseFacing)
        {
            transform.rotation *= Quaternion.Euler(0f, 180f, 0f);
        }
    }
}
