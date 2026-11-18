using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

public sealed class RemotePlayerHealthBarPresenter : MonoBehaviour
{
    [SerializeField] private Image fillImage;
    [SerializeField] private Canvas worldCanvas;

    private CombatStateStore stateStore;
    private CombatEntityKey entityKey;
    private bool subscribed;
    private bool bound;

    private void Awake()
    {
        ResolveViewReferences();
        SetVisible(false);
    }

    private void OnEnable()
    {
        Subscribe();
        if (bound) RefreshFromStore();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    public void Bind(CombatStateStore store, long characterDbId)
    {
        Unsubscribe();
        stateStore = store != null ? store : FindObjectOfType<CombatStateStore>();
        entityKey = new CombatEntityKey("player", characterDbId.ToString(CultureInfo.InvariantCulture));
        bound = characterDbId > 0 && entityKey.IsValid;
        ResolveViewReferences();
        Subscribe();
        RefreshFromStore();
    }

    private void HandleStateChanged(CombatEntityKey changedKey, CombatStateData state)
    {
        if (bound && changedKey.Equals(entityKey)) ApplyAuthoritativeState(state);
    }

    private void RefreshFromStore()
    {
        if (!bound || stateStore == null
            || !stateStore.TryGetState(entityKey.EntityType, entityKey.EntityId, out CombatStateData state))
        {
            SetVisible(false);
            return;
        }

        ApplyAuthoritativeState(state);
    }

    private void ApplyAuthoritativeState(CombatStateData state)
    {
        if (state == null || state.MaxHp <= 0)
        {
            SetVisible(false);
            return;
        }

        ResolveViewReferences();
        if (fillImage == null)
        {
            SetVisible(false);
            return;
        }

        fillImage.fillAmount = Mathf.Clamp01(state.Hp / (float)state.MaxHp);
        SetVisible(true);
    }

    private void Subscribe()
    {
        if (!isActiveAndEnabled || !bound || subscribed || stateStore == null) return;
        stateStore.StateChanged += HandleStateChanged;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || stateStore == null) return;
        stateStore.StateChanged -= HandleStateChanged;
        subscribed = false;
    }

    private void ResolveViewReferences()
    {
        if (worldCanvas == null) worldCanvas = GetComponentInChildren<Canvas>(true);
        if (fillImage != null) return;

        Image[] images = GetComponentsInChildren<Image>(true);
        for (int index = 0; index < images.Length; index++)
        {
            if (images[index] != null && images[index].type == Image.Type.Filled)
            {
                fillImage = images[index];
                return;
            }
        }
    }

    private void SetVisible(bool visible)
    {
        if (worldCanvas != null) worldCanvas.enabled = visible;
    }
}
