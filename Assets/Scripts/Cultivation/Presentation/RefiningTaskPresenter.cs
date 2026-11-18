using System;
using System.Globalization;
using Game.Cultivation.Protocol;
using Game.Cultivation.Runtime;
using Game.Inventory;
using Game.Items;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Cultivation.Presentation
{
    [DisallowMultipleComponent]
    public sealed class RefiningTaskPresenter : MonoBehaviour
    {
        [SerializeField] private GameSession session;
        [SerializeField] private CultivationMaterialDropTarget materialDropTarget;
        [SerializeField] private InventoryModuleRuntime inventory;
        [SerializeField] private TMP_Text countdownText;
        [SerializeField] private TMP_Text cultivationText;
        [SerializeField] private Image taskFill;
        [SerializeField] private Image materialIcon;

        private RefiningSessionRuntime runtime;
        private RefiningTaskClock clock;
        private RefiningTaskDto activeTask;
        private float nextCompletionCheck;
        private long lastDisplayedSeconds = -1;

        private void OnEnable()
        {
            if (session == null)
                session = FindObjectOfType<GameSession>();
            if (inventory == null)
                inventory = FindObjectOfType<InventoryModuleRuntime>();
            if (countdownText != null)
            {
                countdownText.enableAutoSizing = true;
                countdownText.fontSizeMin = 8f;
                countdownText.fontSizeMax = 15f;
                countdownText.enableWordWrapping = false;
            }
            if (cultivationText != null)
            {
                cultivationText.enableAutoSizing = true;
                cultivationText.fontSizeMin = 8f;
                cultivationText.fontSizeMax = 15f;
                cultivationText.enableWordWrapping = false;
            }
            if (session != null)
                session.LocalWorldEntered += HandleWorldEntered;
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.OnDisconnected += HandleDisconnected;
            BindRuntime();
            ShowSnapshot(runtime != null ? runtime.LastSnapshotResult : null);
        }

        private void OnDisable()
        {
            if (session != null)
                session.LocalWorldEntered -= HandleWorldEntered;
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.OnDisconnected -= HandleDisconnected;
            if (runtime != null)
                runtime.SnapshotChanged -= ShowSnapshot;
            runtime = null;
            ClearTask(false);
        }

        private void Update()
        {
            BindRuntime();
            if (clock == null)
                return;
            ShowTaskItem();
            double now = Time.realtimeSinceStartupAsDouble;
            long seconds = clock.RemainingSeconds(now);
            if (seconds != lastDisplayedSeconds)
            {
                lastDisplayedSeconds = seconds;
                if (countdownText != null)
                    countdownText.text = RefiningPreviewCalculator.FormatDuration(seconds);
            }
            if (taskFill != null)
                taskFill.fillAmount = clock.Progress(now);
            if (seconds == 0 && Time.unscaledTime >= nextCompletionCheck)
            {
                nextCompletionCheck = Time.unscaledTime + 5f;
                runtime?.RequestSnapshot();
            }
        }

        private void BindRuntime()
        {
            RefiningSessionRuntime current = RefiningSessionRuntime.Instance;
            if (current == runtime)
                return;
            if (runtime != null)
                runtime.SnapshotChanged -= ShowSnapshot;
            runtime = current;
            if (runtime != null)
                runtime.SnapshotChanged += ShowSnapshot;
            ShowSnapshot(runtime != null ? runtime.LastSnapshotResult : null);
        }

        private void ShowSnapshot(RefiningResultDto result)
        {
            RefiningSnapshotDto snapshot = result != null ? result.snapshot : null;
            RefiningTaskDto task = snapshot != null ? snapshot.activeTask : null;
            if (session == null || !session.IsWorldReady
                || snapshot == null
                || snapshot.characterDbId != session.LocalCharacterDbId
                || task == null || string.IsNullOrWhiteSpace(task.taskId))
            {
                ClearTask(true);
                return;
            }
            if (result.serverTime <= 0
                || !RefiningTaskClock.TryCreate(task, result.serverTime,
                    Time.realtimeSinceStartupAsDouble,
                    out RefiningTaskClock nextClock))
            {
                if (activeTask != null
                    && string.Equals(activeTask.taskId, task.taskId,
                        StringComparison.Ordinal)
                    && clock != null)
                    return;
                clock = null;
                activeTask = null;
                if (countdownText != null)
                    countdownText.text = "--";
                if (taskFill != null)
                    taskFill.fillAmount = 0f;
                runtime?.RequestSnapshot();
                return;
            }
            clock = nextClock;
            activeTask = task;
            lastDisplayedSeconds = -1;
            nextCompletionCheck = Time.unscaledTime;
            if (cultivationText != null)
                cultivationText.text = task.totalCultivation >= 0
                    ? "预计 +" + task.totalCultivation.ToString(
                        CultureInfo.InvariantCulture) : "--";
            Update();
        }

        private void ShowTaskItem()
        {
            if (materialIcon == null || inventory == null
                || activeTask == null || inventory.ItemDefinitions == null
                || !inventory.ItemDefinitions.TryGet(activeTask.itemId,
                    out ItemDefinition definition) || definition.Icon == null)
                return;
            if (materialIcon.sprite != definition.Icon)
                materialIcon.sprite = definition.Icon;
            materialIcon.preserveAspect = true;
            if (!materialIcon.gameObject.activeSelf)
                materialIcon.gameObject.SetActive(true);
        }

        private void ClearTask(bool preserveSelection)
        {
            bool keepPreview = preserveSelection && activeTask == null
                && materialDropTarget != null
                && materialDropTarget.TryGetSelection(out _, out _, out _);
            clock = null;
            activeTask = null;
            lastDisplayedSeconds = -1;
            if (taskFill != null)
                taskFill.fillAmount = 0f;
            if (keepPreview)
                return;
            if (countdownText != null)
                countdownText.text = "00:00:00";
            if (cultivationText != null)
                cultivationText.text = "--";
            if (materialIcon != null)
            {
                materialIcon.sprite = null;
                materialIcon.gameObject.SetActive(false);
            }
        }

        private void HandleWorldEntered(long epoch, long characterDbId)
        {
            ClearTask(false);
        }

        private void HandleDisconnected(string reason)
        {
            ClearTask(false);
        }
    }
}
