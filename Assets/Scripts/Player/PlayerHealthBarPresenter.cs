using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerHealthBarPresenter : MonoBehaviour
{
    [SerializeField] private Image fillImage;
    [SerializeField] private TMP_Text healthText;
    [SerializeField] private CombatStateStore stateStore;
    [SerializeField] private GameSession gameSession;

    private bool subscribed;

    private void Awake()
    {
        ResolveReferences();
        ShowUnavailable();
    }

    private void OnEnable()
    {
        ResolveReferences();
        Subscribe();
        RefreshFromStore();
    }

    private void Start()
    {
        RefreshFromStore();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void HandleStateChanged(CombatEntityKey key, CombatStateData state)
    {
        if (!IsLocalPlayer(key))
        {
            return;
        }

        ApplyHealth(state.Hp, state.MaxHp);
    }

    private void RefreshFromStore()
    {
        ResolveReferences();

        if (stateStore == null || gameSession == null || gameSession.LocalCharacterDbId <= 0)
        {
            return;
        }

        string entityId = gameSession.LocalCharacterDbId.ToString(CultureInfo.InvariantCulture);
        if (stateStore.TryGetState("player", entityId, out CombatStateData state))
        {
            ApplyHealth(state.Hp, state.MaxHp);
        }
    }

    private bool IsLocalPlayer(CombatEntityKey key)
    {
        if (gameSession == null || gameSession.LocalCharacterDbId <= 0)
        {
            ResolveReferences();
        }

        return gameSession != null
            && gameSession.LocalCharacterDbId > 0
            && string.Equals(key.EntityType, "player", System.StringComparison.Ordinal)
            && string.Equals(
                key.EntityId,
                gameSession.LocalCharacterDbId.ToString(CultureInfo.InvariantCulture),
                System.StringComparison.Ordinal);
    }

    private void ApplyHealth(int currentHp, int maxHp)
    {
        int safeMaxHp = Mathf.Max(0, maxHp);
        int safeCurrentHp = safeMaxHp > 0
            ? Mathf.Clamp(currentHp, 0, safeMaxHp)
            : Mathf.Max(0, currentHp);

        if (fillImage != null)
        {
            fillImage.fillAmount = safeMaxHp <= 0
                ? 0f
                : Mathf.Clamp01(safeCurrentHp / (float)safeMaxHp);
        }

        if (healthText != null)
        {
            healthText.text = safeCurrentHp + " / " + safeMaxHp;
        }
    }

    private void ShowUnavailable()
    {
        if (fillImage != null)
        {
            fillImage.fillAmount = 0f;
        }

        if (healthText != null)
        {
            healthText.text = "-- / --";
        }
    }

    private void Subscribe()
    {
        if (subscribed || stateStore == null)
        {
            return;
        }

        stateStore.StateChanged += HandleStateChanged;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || stateStore == null)
        {
            return;
        }

        stateStore.StateChanged -= HandleStateChanged;
        subscribed = false;
    }

    private void ResolveReferences()
    {
        if (stateStore == null)
        {
            stateStore = FindObjectOfType<CombatStateStore>();
        }

        if (gameSession == null)
        {
            gameSession = FindObjectOfType<GameSession>();
        }
    }
}
