using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace QiDao.GameLogic
{
    internal static class GameRuntimeInitializers
    {
        private static bool initialized;
        private static bool afterSceneInitialized;
        private static List<MethodInfo> methods;

        public static void InitializeBeforeGameScene()
        {
            if (initialized) return;
            methods = typeof(GameEntry).Assembly.GetTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                .Where(m => m.IsDefined(typeof(RuntimeInitializeOnLoadMethodAttribute), false))
                .OrderBy(m => m.DeclaringType.FullName, StringComparer.Ordinal).ThenBy(m => m.Name, StringComparer.Ordinal).ToList();
            foreach (var phase in new[] { RuntimeInitializeLoadType.SubsystemRegistration,
                RuntimeInitializeLoadType.AfterAssembliesLoaded, RuntimeInitializeLoadType.BeforeSplashScreen,
                RuntimeInitializeLoadType.BeforeSceneLoad }) Run(phase);
            initialized = true;
        }

        public static void BeforeLoginSceneLoad()
        {
            if (Application.isEditor || afterSceneInitialized) return;
            SceneManager.sceneLoaded -= OnGameSceneLoaded;
            SceneManager.sceneLoaded += OnGameSceneLoaded;
        }

        private static void OnGameSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name != RemoteGameContent.LoginSceneName) return;
            SceneManager.sceneLoaded -= OnGameSceneLoaded;
            RunAfterGameScene();
        }

        public static void RunAfterGameScene()
        {
            if (afterSceneInitialized) return;
            Run(RuntimeInitializeLoadType.AfterSceneLoad);
            afterSceneInitialized = true;
        }

        private static void Run(RuntimeInitializeLoadType phase)
        {
            foreach (MethodInfo method in methods)
            {
                var attribute = (RuntimeInitializeOnLoadMethodAttribute)method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false)[0];
                if (attribute.loadType != phase) continue;
                if (method.GetParameters().Length != 0 || method.ReturnType != typeof(void) || method.ContainsGenericParameters)
                    throw new InvalidOperationException("Unsupported game runtime initializer: " + method.DeclaringType.FullName + "." + method.Name);
                method.Invoke(null, null);
            }
        }
    }
}
