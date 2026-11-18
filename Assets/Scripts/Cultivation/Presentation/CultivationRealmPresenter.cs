using Game.CharacterPanel.Protocol;
using TMPro;
using UnityEngine;

namespace Game.Cultivation.Presentation
{
    [DisallowMultipleComponent]
    public sealed class CultivationRealmPresenter : MonoBehaviour
    {
        [SerializeField] private GameSession session;
        [SerializeField] private TMP_Text currentRealmText;
        [SerializeField] private TMP_Text nextRealmText;

        private NetworkManager network;

        private void OnEnable()
        {
            if (session == null)
                session = FindObjectOfType<GameSession>();
            if (session != null)
            {
                session.LocalProgressionSnapshotChanged += Render;
                session.LocalWorldEntered += HandleWorldEntered;
            }

            network = NetworkManager.Instance;
            if (network != null)
                network.OnDisconnected += HandleDisconnected;

            if (nextRealmText != null)
            {
                nextRealmText.enableAutoSizing = true;
                nextRealmText.fontSizeMin = 10f;
                nextRealmText.fontSizeMax = 20f;
                nextRealmText.enableWordWrapping = true;
            }

            Render(session != null ? session.LocalProgressionSnapshot : null);
            if (session != null && session.LocalProgressionSnapshot == null)
                session.RequestProgressionSnapshot();
        }

        private void OnDisable()
        {
            if (session != null)
            {
                session.LocalProgressionSnapshotChanged -= Render;
                session.LocalWorldEntered -= HandleWorldEntered;
            }
            if (network != null)
                network.OnDisconnected -= HandleDisconnected;
            network = null;
        }

        private void HandleWorldEntered(long epoch, long characterDbId)
        {
            Render(session != null ? session.LocalProgressionSnapshot : null);
        }

        private void HandleDisconnected(string reason)
        {
            Render(null);
        }

        private void Render(ProgressionSnapshotResult result)
        {
            if (result == null || session == null
                || session.LocalCharacterDbId <= 0
                || result.characterDbId != session.LocalCharacterDbId)
            {
                if (currentRealmText != null)
                    currentRealmText.text = "--";
                if (nextRealmText != null)
                    nextRealmText.text = "--";
                return;
            }

            if (!result.success || string.IsNullOrWhiteSpace(result.jingjieName)
                || !JingjieNextRealmFields.IsValid(result.hasNextJingjie,
                    result.nextJingjieId, result.nextJingjieStage,
                    result.nextJingjieName))
                return;

            if (currentRealmText != null)
                currentRealmText.text = result.jingjieName;
            if (nextRealmText != null)
                nextRealmText.text = result.hasNextJingjie
                    ? result.nextJingjieName : "无下一突破路径";
        }
    }
}
