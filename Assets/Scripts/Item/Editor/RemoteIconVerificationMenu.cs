using UnityEditor;
using UnityEngine;

namespace Game.Items.Editor
{
    internal static class RemoteIconVerificationMenu
    {
        private const string MenuPath = "Tools/Items/Strict Remote Icon Verification";
        private const string SessionKey = "Game.Items.StrictRemoteIconVerification";

        [MenuItem(MenuPath)]
        private static void Toggle()
        {
            bool enabled = !SessionState.GetBool(SessionKey, false);
            SessionState.SetBool(SessionKey, enabled);
            Menu.SetChecked(MenuPath, enabled);
            Debug.Log("[ResourceTrial] Strict remote icon verification "
                + (enabled ? "enabled" : "disabled") + ". Enter Play mode to verify.");
        }

        [MenuItem(MenuPath, true)]
        private static bool Validate()
        {
            Menu.SetChecked(MenuPath, SessionState.GetBool(SessionKey, false));
            return !EditorApplication.isPlaying;
        }
    }
}
