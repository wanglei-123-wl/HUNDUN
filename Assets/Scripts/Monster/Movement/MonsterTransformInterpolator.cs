using System;
using Game.Client.BattleSync.Protocol;
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
    private MonsterPathDto movementPath;
    private double pathReceivedAt;
    private double pathAgeAtReceipt;

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
        Vector3 desiredPosition = movementPath == null ? targetPosition
            : SamplePath(movementPath, pathAgeAtReceipt + Time.realtimeSinceStartupAsDouble - pathReceivedAt);
        float distance = Vector3.Distance(previousPosition, desiredPosition);

        if (distance >= snapDistance)
        {
            targetTransform.SetPositionAndRotation(desiredPosition, targetRotation);
        }
        else
        {
            float positionWeight = 1f - Mathf.Exp(-positionLerpSpeed * Time.deltaTime);
            float rotationWeight = 1f - Mathf.Exp(-rotationLerpSpeed * Time.deltaTime);

            targetTransform.position = Vector3.Lerp(previousPosition, desiredPosition, positionWeight);
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

    public void SetMovementPath(MonsterPathDto path, long serverTime)
    {
        movementPath = path != null && path.IsValid && serverTime > 0 ? path : null;
        if (movementPath == null) return;
        pathReceivedAt = Time.realtimeSinceStartupAsDouble;
        pathAgeAtReceipt = AgeAt(path, serverTime);
        targetRotation = Quaternion.LookRotation(new Vector3(path.endX - path.x, 0f, path.endZ - path.z));
    }

    public void CorrectMovementPath(Vector3 position, long serverTime)
    {
        if (movementPath != null &&
            Vector3.Distance(position, SamplePath(movementPath, AgeAt(movementPath, serverTime))) > .25f)
            movementPath = null;
    }

    private static double AgeAt(MonsterPathDto path, long serverTime) =>
        Math.Max(0d, ((double)serverTime - path.startTime) / 1000d);

    private static Vector3 SamplePath(MonsterPathDto path, double age)
    {
        Vector3 origin = new Vector3(path.x,path.y,path.z);
        Vector3 end = new Vector3(path.endX,path.y,path.endZ);
        return Vector3.MoveTowards(origin,end,(float)(path.speed * Math.Max(0d,age)));
    }

    public void Snap(Vector3 position, Quaternion rotation)
    {
        movementPath = null;
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
        movementPath = null;
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
