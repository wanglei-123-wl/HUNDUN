using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class MonsterOverheadUiReferences : MonoBehaviour
{
    [SerializeField] private Image fillImage;
    [SerializeField] private TMP_Text healthText;
    [SerializeField] private TMP_Text monsterNameText;

    public GameObject Root => gameObject;
    public Image FillImage => fillImage;
    public TMP_Text HealthText => healthText;
    public TMP_Text MonsterNameText => monsterNameText;
    public bool HasRequiredReferences =>
        fillImage != null && healthText != null && monsterNameText != null;
}
