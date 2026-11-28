using QiDao.Bootstrap.Contracts;
using UnityEngine;

namespace QiDao.GameLogic
{
    public static class GameEntry
    {
        public static void Initialize()
        {
#if QIDAO_HOTUPDATE_PROBE_FAIL
            throw new System.InvalidOperationException("Acceptance fixture: game entry initialization failed.");
#endif
            if (!Application.isEditor) GameRuntimeInitializers.InitializeBeforeGameScene();
            GameStartupServices.Register(() => new GameStartupContent());
        }
    }
}
