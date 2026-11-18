using UnityEngine;

public class MonsterTransformInterpolator : MonoBehaviour
{
    [SerializeField] private Transform targetTransform;
    [SerializeField] private float positionLerpSpeed = 12f;
    [SerializeField] private float rotationLerpSpeed = 15f;
    [SerializeField] private float snapDistance = 8f;

    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private Vector3 previousPosition;
    private bool hasTarget;

    public Vector3 CurrentVelocity { get; private set; }
    public bool HasTarget => hasTarget;

    private void Awake()
    {
        ResolveTargetTransform();
        ResetView();
    }

    private void Update()
    {
        if (!hasTarget || targetTransform == null)
        {
            CurrentVelocity = Vector3.zero;
            return;
        }

        previousPosition = targetTransform.position;
        float distance = Vector3.Distance(previousPosition, targetPosition);

        if (distance >= snapDistance)
        {
            targetTransform.SetPositionAndRotation(targetPosition, targetRotation);
        }
        else
        {
            float positionWeight = 1f - Mathf.Exp(-positionLerpSpeed * Time.deltaTime);
            float rotationWeight = 1f - Mathf.Exp(-rotationLerpSpeed * Time.deltaTime);

            targetTransform.position = Vector3.Lerp(previousPosition, targetPosition, positionWeight);
            targetTransform.rotation = Quaternion.Slerp(targetTransform.rotation, targetRotation, rotationWeight);
        }

        CurrentVelocity = (targetTransform.position - previousPosition)
            / Mathf.Max(Time.deltaTime, 0.0001f);
    }

    public void SetTarget(Vector3 position, Quaternion rotation)
    {
        targetPosition = position;
        targetRotation = rotation;
        hasTarget = true;
    }

    public void SetTarget(MonsterViewState state)
    {
        SetTarget(state.position, state.rotation);
    }

    public void Snap(Vector3 position, Quaternion rotation)
    {
        ResolveTargetTransform();

        if (targetTransform == null)
        {
            return;
        }

        targetPosition = position;
        targetRotation = rotation;
        targetTransform.SetPositionAndRotation(position, rotation);
        previousPosition = position;
        CurrentVelocity = Vector3.zero;
        hasTarget = true;
    }

    public void ResetView()
    {
        ResolveTargetTransform();

        if (targetTransform == null)
        {
            return;
        }

        targetPosition = targetTransform.position;
        targetRotation = targetTransform.rotation;
        previousPosition = targetTransform.position;
        CurrentVelocity = Vector3.zero;
        hasTarget = false;
    }

    private void ResolveTargetTransform()
    {
        if (targetTransform == null)
        {
            targetTransform = transform;
        }
    }
}
