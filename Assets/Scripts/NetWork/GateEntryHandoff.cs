public static class GateEntryHandoff
{
    private static long connectionEpoch;
    private static long characterDbId;
    private static string sceneId;
    private static string mapId;
    private static string resultJson;
    private static string failureMessage;
    private static long retryCharacterDbId;

    public static bool HasPending => !string.IsNullOrEmpty(resultJson);
    public static bool RequiresHandoff { get; private set; }

    public static void BeginAttempt()
    {
        Clear();
        RequiresHandoff = true;
        SetFailure(null);
    }

    public static void Store(long epoch, long selectedCharacterDbId, string serverSceneId,
        string serverMapId, string json)
    {
        RequiresHandoff = true;
        connectionEpoch = epoch;
        characterDbId = selectedCharacterDbId;
        sceneId = serverSceneId;
        mapId = serverMapId;
        resultJson = json;
        SetFailure(null);
    }

    public static bool TryGetDestination(long epoch, long selectedCharacterDbId,
        out string serverSceneId, out string serverMapId)
    {
        bool matches = HasPending && epoch > 0 && epoch == connectionEpoch
            && selectedCharacterDbId > 0 && selectedCharacterDbId == characterDbId;
        serverSceneId = matches ? sceneId : null;
        serverMapId = matches ? mapId : null;
        return matches;
    }

    public static bool TryTake(long epoch, long selectedCharacterDbId, string expectedSceneId,
        string expectedMapId, out string json)
    {
        bool matches = HasPending && epoch > 0 && epoch == connectionEpoch
            && selectedCharacterDbId > 0 && selectedCharacterDbId == characterDbId
            && !string.IsNullOrWhiteSpace(expectedSceneId) && expectedSceneId == sceneId
            && !string.IsNullOrWhiteSpace(expectedMapId) && expectedMapId == mapId
            && WorldMapSceneCatalog.TryGetAddress(sceneId, mapId, out _);
        json = matches ? resultJson : null;
        Clear();
        return matches;
    }

    public static void Clear()
    {
        RequiresHandoff = false;
        Invalidate();
    }

    public static void Invalidate()
    {
        connectionEpoch = 0;
        characterDbId = 0;
        sceneId = null;
        mapId = null;
        resultJson = null;
    }

    public static void SetFailure(string message)
    {
        failureMessage = message;
        retryCharacterDbId = 0;
    }

    public static void AllowCharacterRetry(long characterDbId)
    {
        retryCharacterDbId = characterDbId;
        if (string.IsNullOrWhiteSpace(failureMessage))
            failureMessage = "进场失败，请重试。";
    }

    public static string TakeFailure(out long characterDbIdToRetry)
    {
        string message = failureMessage;
        characterDbIdToRetry = retryCharacterDbId;
        SetFailure(null);
        return message;
    }
}
