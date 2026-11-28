using QiDao.Bootstrap.Contracts;
using UnityEngine;

namespace QiDao.Bootstrap
{
    internal static class BootstrapRuntime
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            GameStartupServices.Reset();
        }
    }
}
