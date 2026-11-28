#if UNITY_EDITOR || QIDAO_HOTUPDATE_PROBE
using UnityEngine;

namespace QiDao.GameLogic
{
    // Only the isolated acceptance bundle references this development fixture.
    public sealed class HotCodeProbe : MonoBehaviour
    {
        public int marker = 42;
        public string ExecutedVersion { get; private set; }
        private void Awake() => ExecutedVersion = Version;
        public string InvokeGameMethod() => "game-" + Version + "-" + marker;
        public static string Version
        {
            get
            {
#if QIDAO_HOTUPDATE_PROBE_B
                return "B";
#else
                return "A";
#endif
            }
        }
        public static void CompleteGameSceneInitializers() => GameRuntimeInitializers.RunAfterGameScene();
    }
}
#endif
