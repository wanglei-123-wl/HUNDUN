using System;

namespace QiDao.Bootstrap.Contracts
{
    public static class GameStartupServices
    {
        private static Func<IGameStartupContent> createContent;

        public static void Register(Func<IGameStartupContent> factory)
        {
            createContent = factory ?? throw new ArgumentNullException(nameof(factory));
        }

        public static IGameStartupContent Create()
        {
            if (createContent == null)
                throw new InvalidOperationException("Game startup services have not been registered.");
            return createContent() ?? throw new InvalidOperationException("Game startup factory returned null.");
        }

        public static void Reset()
        {
            createContent = null;
        }
    }
}
