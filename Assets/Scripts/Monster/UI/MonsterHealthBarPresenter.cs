using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class MonsterHealthBarPresenter : MonoBehaviour
{
    [SerializeField] private MonsterNetworkView monsterNetworkView;
    [SerializeField] private GameObject healthBarRoot;
    [SerializeField] private Image fillImage;
    [SerializeField] private TMP_Text healthText;
    [SerializeField] private bool hideWhenDead;

    private bool subscribed;

    private void Awake()
    {
        if (ResolveReferences())
        {
            SetHealthBarVisible(false);
        }
    }

    private void OnEnable()
    {
        if (!ResolveReferences())
        {
            return;
        }

        Subscribe();

        if (monsterNetworkView != null && monsterNetworkView.MaxHp > 0)
        {
            HandleHealthChanged(monsterNetworkView.CurrentHp, monsterNetworkView.MaxHp);
        }
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void HandleHealthChanged(int currentHp, int maxHp)
    {
        int safeMaxHp = Mathf.Max(0, maxHp);
        int safeCurrentHp = safeMaxHp > 0
            ? Mathf.Clamp(currentHp, 0, safeMaxHp)
            : 0;

        if (fillImage != null)
        {
            fillImage.fillAmount = safeMaxHp > 0
                ? Mathf.Clamp01(safeCurrentHp / (float)safeMaxHp)
                : 0f;
        }

        if (healthText != null)
        {
            healthText.text = safeCurrentHp + " / " + safeMaxHp;
        }

        bool visible = safeMaxHp > 0
            && (!hideWhenDead || safeCurrentHp > 0);
        SetHealthBarVisible(visible);
    }

    private bool ResolveReferences()
    {
        if (monsterNetworkView == null)
        {
            monsterNetworkView = GetComponent<MonsterNetworkView>();
        }

        if (monsterNetworkView == null)
        {
            monsterNetworkView = GetComponentInParent<MonsterNetworkView>();
        }

        MonsterOverheadUiReferences[] uiReferences =
            GetComponentsInChildren<MonsterOverheadUiReferences>(true);
        if (uiReferences.Length > 1)
        {
            Debug.LogError("Monster health bar found multiple overhead UI references.", this);
            return false;
        }

        if (uiReferences.Length == 1)
        {
            MonsterOverheadUiReferences ui = uiReferences[0];
            if (!ui.HasRequiredReferences)
            {
                Debug.LogError("Monster overhead UI is missing its fill, health text, or name text reference.", ui);
                return false;
            }

            healthBarRoot = ui.Root;
            fillImage = ui.FillImage;
            healthText = ui.HealthText;
        }

        return true;
    }

    private void Subscribe()
    {
        if (subscribed || monsterNetworkView == null)
        {
            return;
        }

        monsterNetworkView.OnHealthChanged += HandleHealthChanged;
        subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!subscribed || monsterNetworkView == null)
        {
            return;
        }

        monsterNetworkView.OnHealthChanged -= HandleHealthChanged;
        subscribed = false;
    }

    private void SetHealthBarVisible(bool visible)
    {
        if (healthBarRoot != null && healthBarRoot.activeSelf != visible)
        {
            healthBarRoot.SetActive(visible);
        }
    }
}
