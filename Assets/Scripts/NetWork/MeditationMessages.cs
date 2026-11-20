using System;

[Serializable]
public sealed class MeditationSnapshot
{
    public bool isMeditating;
    public long revision;
}

[Serializable]
internal sealed class MeditationSetRequest : NetMessageBase
{
    public string sceneSessionId;
    public long requestSequence;
    public bool isMeditating;
    public long movementEpoch;
    public long movementSequence;
}

[Serializable]
internal sealed class MeditationResultMessage : NetMessageBase
{
    public string sceneSessionId;
    public long requestSequence;
    public bool success;
    public string errorCode;
    public MeditationSnapshot meditation;
}

[Serializable]
internal sealed class MeditationStateMessage : NetMessageBase
{
    public string sceneId;
    public string sceneSessionId;
    public long characterDbId;
    public long visibilityRevision;
    public MeditationSnapshot meditation;
    public string reason;
    public long reliableMessageId;
}

[Serializable]
internal sealed class MeditationVitalsMessage : NetMessageBase
{
    public string sceneId;
    public string sceneSessionId;
    public long characterDbId;
    public int currentHp;
    public int maxHp;
    public int currentMp;
    public int maxMp;
    public long combatRevision;
}
