using System;
using System.Collections;
using System.Collections.Generic;

namespace QiDao.Bootstrap.Contracts
{
    // Keep this boundary independent of game data types and game assemblies.
    public interface IGameStartupContent : IDisposable
    {
        IEnumerable<string> RequiredResourceAddresses { get; }
        bool Succeeded { get; }
        string FailureMessage { get; }
        void ReleaseAttemptContent();
        IEnumerator PrepareContent(Action<string> reportStatus);
        IEnumerator OpenLoginScene();
    }
}
