using UnityEngine;

public class MonsterViewRoot : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private MonsterTransformInterpolator transformInterpolator;
    [SerializeField] private MonoBehaviour animatorDriverComponent;
    [SerializeField] private Transform modelRoot;

    private IMonsterAnimatorDriver animatorDriver;

    public string EntityId { get; private set; }
    public Transform ModelRoot => modelRoot;
    public MonsterTransformInterpolator TransformInterpolator => transformInterpolator;

    private void Awake()
    {
        ResolveReferences();
    }

    public void Initialize(string entityId, MonsterViewState initialState)
    {
        ResolveReferences();
        EntityId = entityId;

        if (transformInterpolator != null)
        {
            transformInterpolator.Snap(initialState.position, initialState.rotation);
        }

        ApplyPresentationState(initialState);
    }

    public void RebindAnimator()
    {
        ResolveReferences();
        animatorDriver?.RebindAnimator();
    }

    public void BootstrapState(string state)
    {
        ResolveReferences();
        animatorDriver?.BootstrapState(state);
    }

    public void ApplyState(MonsterViewState state)
    {
        ResolveReferences();

        if (string.IsNullOrEmpty(EntityId) && !string.IsNullOrEmpty(state.entityId))
        {
            EntityId = state.entityId;
        }

        if (transformInterpolator != null)
        {
            transformInterpolator.SetTarget(state);
        }

        ApplyPresentationState(state);
    }

    public void ApplyTransform(Vector3 position, Quaternion rotation)
    {
        ResolveReferences();
        transformInterpolator?.SetTarget(position, rotation);
    }

    public void SetLocomotion(MonsterLocomotionMode mode)
    {
        ResolveReferences();
        animatorDriver?.SetLocomotion(mode);
    }

    public void SetMoveSpeed(float normalizedSpeed)
    {
        ResolveReferences();
        animatorDriver?.SetMoveSpeed(normalizedSpeed);
    }

    public void PlayAction(MonsterActionData action)
    {
        ResolveReferences();
        float actionSpeed = action.actionSpeed <= 0f ? 1f : action.actionSpeed;
        animatorDriver?.PlayAction(action.actionSlot, actionSpeed);
    }

    public void ResetView()
    {
        ResolveReferences();
        EntityId = "";
        transformInterpolator?.ResetView();
        animatorDriver?.ResetView();
    }

    private void ApplyPresentationState(MonsterViewState state)
    {
        animatorDriver?.SetLocomotion(state.locomotionMode);
        animatorDriver?.SetMoveSpeed(state.moveSpeed);
    }

    private void ResolveReferences()
    {
        if (transformInterpolator == null)
        {
            transformInterpolator = GetComponent<MonsterTransformInterpolator>();
        }

        if (modelRoot == null)
        {
            modelRoot = transform;
        }

        if (animatorDriverComponent != null)
        {
            animatorDriver = animatorDriverComponent as IMonsterAnimatorDriver;

            if (animatorDriver == null)
            {
                Debug.LogError(
                    "MonsterViewRoot animatorDriverComponent must implement IMonsterAnimatorDriver.",
                    this
                );
            }

            return;
        }

        MonoBehaviour[] components = GetComponentsInChildren<MonoBehaviour>(true);

        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] is IMonsterAnimatorDriver driver)
            {
                animatorDriverComponent = components[i];
                animatorDriver = driver;
                return;
            }
        }

        Debug.LogError("MonsterViewRoot requires an IMonsterAnimatorDriver component.", this);
    }
}
