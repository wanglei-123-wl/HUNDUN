using UnityEngine;

namespace Game.UI.Notice
{
    public static class GameNoticeService
    {
        public const string SceneObjectName = "统一提示";

        private static bool missingNoticeLogged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InitializeSceneNotice()
        {
            if (FindController() != null)
            {
                return;
            }

            GameObject noticeObject = FindSceneObject(SceneObjectName);
            if (noticeObject != null)
            {
                noticeObject.AddComponent<UnifiedNoticeController>();
            }
        }

        public static bool Show(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return false;
            }

            UnifiedNoticeController controller = FindController();
            if (controller == null)
            {
                GameObject noticeObject = FindSceneObject(SceneObjectName);
                if (noticeObject != null)
                {
                    controller = noticeObject.GetComponent<UnifiedNoticeController>();
                    if (controller == null)
                    {
                        controller = noticeObject.AddComponent<UnifiedNoticeController>();
                    }
                }
            }

            if (controller == null)
            {
                if (!missingNoticeLogged)
                {
                    Debug.LogWarning(
                        "[UI] Cannot show notice because scene object '"
                        + SceneObjectName + "' was not found.");
                    missingNoticeLogged = true;
                }

                return false;
            }

            missingNoticeLogged = false;
            controller.Show(message);
            return true;
        }

        public static void Hide()
        {
            UnifiedNoticeController controller = FindController();
            if (controller != null)
            {
                controller.Hide();
            }
        }

        private static UnifiedNoticeController FindController()
        {
            UnifiedNoticeController[] controllers =
                Resources.FindObjectsOfTypeAll<UnifiedNoticeController>();
            for (int index = 0; index < controllers.Length; index++)
            {
                UnifiedNoticeController controller = controllers[index];
                if (controller != null && controller.gameObject.scene.IsValid())
                {
                    return controller;
                }
            }

            return null;
        }

        private static GameObject FindSceneObject(string objectName)
        {
            GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int index = 0; index < objects.Length; index++)
            {
                GameObject candidate = objects[index];
                if (candidate != null
                    && candidate.scene.IsValid()
                    && candidate.name == objectName)
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
