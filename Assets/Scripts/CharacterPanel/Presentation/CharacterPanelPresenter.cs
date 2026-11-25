using System;
using System.Globalization;
using Game.Cultivation.Presentation;
using Game.Cultivation.Protocol;
using Game.Cultivation.Runtime;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.CharacterPanel.Presentation
{
    [DisallowMultipleComponent]
    public sealed class CharacterPanelPresenter : MonoBehaviour
    {
        [SerializeField] private GameSession session;
        [SerializeField] private NetworkManager network;
        [SerializeField] private CombatStateStore stateStore;
        [SerializeField] private TMP_Text nickname;
        [SerializeField] private TMP_Text realm;
        [SerializeField] private TMP_Text health;
        [SerializeField] private TMP_Text mana;
        [SerializeField] private Image healthFill;
        [SerializeField] private Image manaFill;
        [SerializeField] private TMP_Text attack;
        [SerializeField] private TMP_Text defense;
        [SerializeField] private TMP_Text magicAttack;
        [SerializeField] private TMP_Text magicDefense;
        [SerializeField] private TMP_Text critRate;
        [SerializeField] private TMP_Text critDamage;
        [SerializeField] private TMP_Text alchemyTitle;
        [SerializeField] private TMP_Text forgingTitle;
        [SerializeField] private TMP_Text comprehension;
        [SerializeField] private TMP_Text currentExperience;
        [SerializeField] private TMP_Text requiredExperience;
        [SerializeField] private Slider experienceSlider;
        [SerializeField] private Image experienceFill;
        private const string ProfessionRankNames = "一二三四五六七八九";
        private CultivationSessionRuntime cultivationRuntime;
        private bool subscribed;
        private int currentHp = -1;
        private int maxHp;

        private void OnEnable()
        {
            ResolveReferences();
            Subscribe();
            Refresh();
            if (session != null && session.LocalAttributeSnapshot == null)
                session.RequestAttributeSnapshot();
            if (session != null && session.LocalProgressionSnapshot == null)
                session.RequestProgressionSnapshot();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (subscribed || session == null) return;
            session.LocalWorldEntered += HandleWorldEntered;
            session.LocalAttributeSnapshotChanged += HandleAttributeSnapshotChanged;
            session.LocalProgressionSnapshotChanged += HandleProgressionSnapshotChanged;
            session.LocalManaChanged += RenderMana;
            if (network != null) network.OnDisconnected += HandleDisconnected;
            if (stateStore != null) stateStore.StateChanged += HandleStateChanged;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            BindCultivation(null);
            if (!subscribed) return;
            if (session != null) session.LocalWorldEntered -= HandleWorldEntered;
            if (session != null) session.LocalAttributeSnapshotChanged -= HandleAttributeSnapshotChanged;
            if (session != null) session.LocalProgressionSnapshotChanged -=
                HandleProgressionSnapshotChanged;
            if (session != null) session.LocalManaChanged -= RenderMana;
            if (network != null) network.OnDisconnected -= HandleDisconnected;
            if (stateStore != null) stateStore.StateChanged -= HandleStateChanged;
            subscribed = false;
        }

        private void BindCultivation(CultivationSessionRuntime runtime)
        {
            if (cultivationRuntime == runtime) return;
            if (cultivationRuntime != null)
                cultivationRuntime.SnapshotChanged -= RenderCultivation;
            cultivationRuntime = runtime;
            if (cultivationRuntime != null)
                cultivationRuntime.SnapshotChanged += RenderCultivation;
        }

        private void HandleWorldEntered(long epoch, long characterDbId)
        {
            Refresh();
        }

        private void HandleDisconnected(string reason)
        {
            Clear();
        }

        private void HandleAttributeSnapshotChanged(AttributeSnapshotResult result)
        {
            RenderAttributes(result);
        }

        private void HandleProgressionSnapshotChanged(ProgressionSnapshotResult result)
        {
            RenderProgression(result);
        }

        private void HandleStateChanged(CombatEntityKey key, CombatStateData state)
        {
            if (session == null || session.LocalEnterWorldHp < 0
                || session.LocalCharacterDbId <= 0 || state == null || state.MaxHp <= 0
                || state.CombatRevision < session.LocalEnterWorldCombatRevision
                || !string.Equals(key.EntityType, "player", StringComparison.Ordinal)
                || !string.Equals(key.EntityId,
                    session.LocalCharacterDbId.ToString(CultureInfo.InvariantCulture),
                    StringComparison.Ordinal)) return;

            currentHp = state.Hp;
            maxHp = state.MaxHp;
            RenderHealth();
        }

        private void Refresh()
        {
            if (nickname != null)
                nickname.text = session != null && session.LocalCharacterDbId > 0
                    && !string.IsNullOrWhiteSpace(session.LocalNickname)
                    ? session.LocalNickname : "--";
            if (realm != null) realm.text = "--";
            currentHp = session != null ? session.LocalEnterWorldHp : -1;
            maxHp = session != null ? session.LocalEnterWorldMaxHp : 0;
            if (session != null && session.LocalEnterWorldHp >= 0
                && session.LocalCharacterDbId > 0 && stateStore != null
                && stateStore.TryGetState("player",
                    session.LocalCharacterDbId.ToString(CultureInfo.InvariantCulture),
                    out CombatStateData state))
                HandleStateChanged(
                    new CombatEntityKey("player",
                        session.LocalCharacterDbId.ToString(CultureInfo.InvariantCulture)),
                    state);
            RenderHealth();
            RenderMana();
            RenderAttributes(session != null ? session.LocalAttributeSnapshot : null);
            RenderProgression(session != null ? session.LocalProgressionSnapshot : null);
            if (alchemyTitle != null || forgingTitle != null || comprehension != null)
            {
                BindCultivation(CultivationSessionRuntime.Instance);
                CultivationSnapshotDto snapshot = cultivationRuntime?.Snapshot;
                RenderCultivation(snapshot);
                if (snapshot == null && session != null && session.IsWorldReady)
                    cultivationRuntime?.RequestSnapshot();
            }
        }

        private void RenderCultivation(CultivationSnapshotDto snapshot)
        {
            if (session == null || !session.IsWorldReady || session.LocalCharacterDbId <= 0
                || snapshot?.characterDbId != session.LocalCharacterDbId)
                snapshot = null;
            if (alchemyTitle != null)
                alchemyTitle.text = FormatProfession(snapshot?.alchemyProfession, "炼丹学徒", "炼丹师");
            if (forgingTitle != null)
                forgingTitle.text = FormatProfession(snapshot?.forgingProfession, "炼器学徒", "炼器师");
            if (comprehension != null)
                comprehension.text = snapshot?.totalComprehension.ToString(CultureInfo.InvariantCulture) ?? "--";
        }

        private static string FormatProfession(CultivationProfessionProgressDto progress,
            string apprenticeTitle, string professionTitle)
        {
            if (progress == null) return "--";
            if (progress.rank == -1) return "未入门";
            if (progress.rank == 0) return apprenticeTitle;
            return progress.rank >= 1 && progress.rank <= ProfessionRankNames.Length
                ? ProfessionRankNames[progress.rank - 1] + "品" + professionTitle
                : "--";
        }

        private void RenderAttributes(AttributeSnapshotResult result)
        {
            AttributeSnapshotFinal values = result != null && result.success
                && session != null && result.characterDbId == session.LocalCharacterDbId
                ? result.final : null;
            if (attack != null)
                attack.text = values == null ? "--" : values.attack.ToString(CultureInfo.InvariantCulture);
            if (defense != null)
                defense.text = values == null ? "--" : values.defense.ToString(CultureInfo.InvariantCulture);
            if (magicAttack != null)
                magicAttack.text = values == null ? "--" : values.magicAttack.ToString(CultureInfo.InvariantCulture);
            if (magicDefense != null)
                magicDefense.text = values == null ? "--" : values.magicDefense.ToString(CultureInfo.InvariantCulture);
            if (critRate != null)
                critRate.text = values == null ? "--" : FormatBps(values.critRateBps);
            if (critDamage != null)
                critDamage.text = values == null ? "--" : FormatBps(values.critDamageBps);
        }

        private void RenderProgression(ProgressionSnapshotResult result)
        {
            bool valid = result != null && result.success
                && session != null && result.characterDbId == session.LocalCharacterDbId;
            CultivationProgressDisplay cultivation =
                CultivationProgressDisplay.FromSnapshot(result,
                    session != null ? session.LocalCharacterDbId : 0,
                    session != null && session.IsWorldReady);
            if (realm != null)
                realm.text = valid && !string.IsNullOrWhiteSpace(result.jingjieName)
                    ? result.jingjieName : "--";
            if (currentExperience != null)
                currentExperience.text = requiredExperience == null
                    || requiredExperience == currentExperience
                    ? cultivation.Progress : cultivation.Current;
            if (requiredExperience != null && requiredExperience != currentExperience)
                requiredExperience.text = cultivation.Required;

            if (experienceSlider != null)
                experienceSlider.value = cultivation.FillAmount;
            if (experienceFill != null)
                experienceFill.fillAmount = cultivation.FillAmount;
        }

        private static string FormatBps(int value)
        {
            return (value / 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%";
        }

        private void RenderMana()
        {
            int currentMp = 0, maxMp = 0;
            bool available = session != null && session.TryGetLocalMana(out currentMp, out maxMp);
            if (mana != null)
                mana.text = available ? currentMp.ToString(CultureInfo.InvariantCulture) + "/"
                    + maxMp.ToString(CultureInfo.InvariantCulture) : "--";
            if (manaFill != null)
                manaFill.fillAmount = available && maxMp > 0 ? Mathf.Clamp01(currentMp / (float)maxMp) : 0f;
        }

        private void RenderHealth()
        {
            bool available = currentHp >= 0 && maxHp > 0;
            if (healthFill != null)
                healthFill.fillAmount = available
                    ? Mathf.Clamp01(currentHp / (float)maxHp)
                    : 0f;
            if (health != null)
                health.text = available
                    ? Math.Min(currentHp, maxHp).ToString(CultureInfo.InvariantCulture) + "/"
                        + maxHp.ToString(CultureInfo.InvariantCulture)
                    : "--";
        }

        private void Clear()
        {
            if (nickname != null) nickname.text = "--";
            if (realm != null) realm.text = "--";
            if (health != null) health.text = "--";
            if (mana != null) mana.text = "--";
            if (healthFill != null) healthFill.fillAmount = 0f;
            if (manaFill != null) manaFill.fillAmount = 0f;
            if (attack != null) attack.text = "--";
            if (defense != null) defense.text = "--";
            if (magicAttack != null) magicAttack.text = "--";
            if (magicDefense != null) magicDefense.text = "--";
            if (critRate != null) critRate.text = "--";
            if (critDamage != null) critDamage.text = "--";
            RenderCultivation(null);
            if (currentExperience != null) currentExperience.text = "--";
            if (requiredExperience != null) requiredExperience.text = "--";
            if (experienceSlider != null) experienceSlider.value = 0f;
            if (experienceFill != null) experienceFill.fillAmount = 0f;
            currentHp = -1;
            maxHp = 0;
        }

        private void ResolveReferences()
        {
            if (network == null) network = NetworkManager.Instance;
            if (stateStore == null) stateStore = FindObjectOfType<CombatStateStore>();
            if (session == null) session = FindObjectOfType<GameSession>();
            if (alchemyTitle == null)
                alchemyTitle = transform.Find("丹道头衔")?.GetComponent<TMP_Text>();
            if (forgingTitle == null)
                forgingTitle = transform.Find("器道头衔")?.GetComponent<TMP_Text>();
            if (comprehension == null)
                comprehension = transform.Find("属性面板/悟性")?.GetComponent<TMP_Text>();
            if (attack == null || defense == null || mana == null
                || magicAttack == null || magicDefense == null || critRate == null
                || critDamage == null || currentExperience == null
                || requiredExperience == null)
            {
                foreach (TMP_Text text in GetComponentsInChildren<TMP_Text>(true))
                {
                    if (text == null) continue;
                    switch (text.gameObject.name)
                    {
                        case "生命值": health = text; break;
                        case "灵力": if (mana == null) mana = text; break;
                        case "攻击": if (attack == null) attack = text; break;
                        case "防御": if (defense == null) defense = text; break;
                        case "法力": if (magicAttack == null) magicAttack = text; break;
                        case "法防": if (magicDefense == null) magicDefense = text; break;
                        case "暴击率": if (critRate == null) critRate = text; break;
                        case "暴击伤害": if (critDamage == null) critDamage = text; break;
                        case "修为":
                        case "经验":
                        case "当前经验":
                            if (currentExperience == null) currentExperience = text;
                            break;
                        case "升级所需经验":
                        case "所需经验":
                            if (requiredExperience == null) requiredExperience = text;
                            break;
                    }
                }
            }

            Transform experienceRoot = FindChildTransform("修为条")
                ?? FindChildTransform("经验条");
            if (experienceRoot != null)
            {
                if (experienceSlider == null)
                    experienceSlider = experienceRoot.GetComponentInChildren<Slider>(true);
                if (experienceFill == null)
                {
                    foreach (Image image in experienceRoot.GetComponentsInChildren<Image>(true))
                    {
                        if (image != null && image.type == Image.Type.Filled)
                        {
                            experienceFill = image;
                            break;
                        }
                    }
                }
            }
        }

        private Transform FindChildTransform(string objectName)
        {
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (child != null && string.Equals(child.name, objectName,
                    StringComparison.Ordinal))
                    return child;
            }
            return null;
        }
    }
}
