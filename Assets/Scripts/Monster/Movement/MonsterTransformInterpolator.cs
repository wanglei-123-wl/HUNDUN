using System;
using Game.Client.BattleSync.Protocol;
using Game.Client.MapTraversal;
using UnityEngine;

public class MonsterTransformInterpolator : MonoBehaviour
{
    private const float ArrivalTolerance = .01f;
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
    private readonly MonsterPositionPlayback playback = new();
    private Func<MapTraversalQuery> getMapTraversal;
    private CapsuleCollider groundBody;
    private bool allowGroundPrediction;
    private Action onArrival;

    public Vector3 CurrentVelocity { get; private set; }
    public bool HasTarget => hasTarget;
    internal bool IsFinishingMovement => onArrival != null;

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
        double now = Time.realtimeSinceStartupAsDouble;
        Vector3 desiredPosition = targetPosition;
        if (IsFinishingMovement)
            desiredPosition = playback.SampleArrival(now);
        else if (movementPath != null)
            desiredPosition = SamplePath(movementPath, pathAgeAtReceipt + now - pathReceivedAt);
        else if (playback.TrySample(now, out Vector3 sampledPosition))
        {
            desiredPosition = sampledPosition;
            if (allowGroundPrediction && playback.NeedsPrediction && CheckPrediction(targetPosition))
                playback.TrySample(now, out desiredPosition);
        }
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

        if (IsFinishingMovement && Vector3.Distance(targetTransform.position, targetPosition) <= ArrivalTolerance
            && Vector3.Distance(desiredPosition, targetPosition) <= ArrivalTolerance)
            CompleteArrival();
    }

    public void SetTarget(Vector3 position, Quaternion rotation)
    {
        CancelArrival();
        playback.Clear();
        targetPosition = position;
        targetRotation = rotation;
        hasTarget = true;
    }

    public void SetTarget(MonsterViewState state)
    {
        if (state.resetMovement) playback.Clear();
        if (IsFinishingMovement && state.preservePresentation && !state.useTimedMovement
            && !state.finishMovement && !state.resetMovement)
        {
            if (state.preserveTransform) return;
            targetRotation = state.rotation;
            if ((state.position - targetPosition).sqrMagnitude >= .000001f)
            {
                targetPosition = state.position;
                FinishMovement(state.serverTime, onArrival);
            }
            return;
        }
        if (!state.finishMovement && (state.resetMovement || !state.preserveTransform)) CancelArrival();
        if (!state.preserveTransform)
        {
            targetPosition = state.position;
            targetRotation = state.rotation;
            hasTarget = true;
            allowGroundPrediction = state.allowGroundPrediction;
        }
        if (state.finishMovement) return;
        if (state.resetMovement && !state.useTimedMovement)
        {
            StopMovement();
            return;
        }
        if (state.preserveTransform) return;
        if (!state.useTimedMovement || state.serverTime <= 0)
        {
            playback.Clear();
            return;
        }
        playback.Add(state.position, state.serverTime, Time.realtimeSinceStartupAsDouble, snapDistance);
    }

    internal void FinishMovement(long serverTime, Action arrived)
    {
        float speed = movementPath?.speed ?? Mathf.Max(3f, CurrentVelocity.magnitude);
        if (movementPath != null) playback.Clear();
        movementPath = null;
        allowGroundPrediction = false;
        onArrival = arrived;
        if (!hasTarget || targetTransform == null)
        {
            CompleteArrival();
            return;
        }
        Vector3 displayedPosition = targetTransform.position;
        float remaining = Vector3.Distance(displayedPosition, targetPosition);
        if (remaining <= ArrivalTolerance || remaining >= snapDistance)
        {
            CompleteArrival();
            return;
        }
        playback.BeginArrival(displayedPosition, targetPosition, serverTime,
            Time.realtimeSinceStartupAsDouble, snapDistance, speed);
    }

    private void CompleteArrival()
    {
        Action arrived = onArrival;
        StopMovement();
        arrived?.Invoke();
    }

    private void CancelArrival() => onArrival = null;

    internal void BindGroundPrediction(Func<MapTraversalQuery> getMap, CapsuleCollider body)
    {
        getMapTraversal = getMap;
        groundBody = body;
    }

    private bool CheckPrediction(Vector3 position)
    {
        if (!playback.TryGetPrediction(out Vector3 displacement, out double seconds)
            || groundBody == null || !groundBody.enabled || groundBody.direction != 1) return false;
        MapTraversalQuery map = getMapTraversal?.Invoke();
        if (map == null) return false;
        Vector3 scale = groundBody.transform.lossyScale;
        double radius = groundBody.radius * Math.Max(Math.Abs(scale.x), Math.Abs(scale.z));
        double height = Math.Max(radius * 2, groundBody.height * Math.Abs(scale.y));
        Vector3 end = position + displacement;
        if (!map.CanTraverseGroundSegment(position.x, position.y, position.z, end.x, end.z, radius, height, out double y))
            return false;
        playback.SetPrediction(new Vector3(end.x, (float)y, end.z), seconds);
        return true;
    }

    public void StopMovement()
    {
        CancelArrival();
        playback.Clear();
        movementPath = null;
        if (hasTarget && targetTransform != null)
            targetTransform.position = targetPosition;
        CurrentVelocity = Vector3.zero;
    }

    public void SetMovementPath(MonsterPathDto path, long serverTime)
    {
        bool hadPath = movementPath != null;
        movementPath = path != null && path.IsValid && serverTime > 0 ? path : null;
        if (hadPath != (movementPath != null)) playback.Clear();
        if (movementPath == null) return;
        CancelArrival();
        pathReceivedAt = Time.realtimeSinceStartupAsDouble;
        pathAgeAtReceipt = AgeAt(path, serverTime);
        targetRotation = Quaternion.LookRotation(new Vector3(path.endX - path.x, 0f, path.endZ - path.z));
    }

    public void CorrectMovementPath(Vector3 position, long serverTime)
    {
        if (movementPath != null &&
            Vector3.Distance(position, SamplePath(movementPath, AgeAt(movementPath, serverTime))) > .25f)
            SetMovementPath(null, 0);
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
        CancelArrival();
        playback.Clear();
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
        CancelArrival();
        playback.Clear();
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
