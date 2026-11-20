using System;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal sealed class PlayerDrinkEventMessage
{
    internal const string MessageId = "player_drink_event";
    internal const double MaxAgeSeconds = 1;

    private static readonly JsonLoadSettings LoadSettings = new JsonLoadSettings
    {
        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
    };

    public Guid ActionId { get; }
    public long CharacterDbId { get; }
    public string SceneId { get; }
    public string SceneSessionId { get; }
    public long VisibilityRevision { get; }
    public long ServerTime { get; }

    private PlayerDrinkEventMessage(Guid actionId, long characterDbId, string sceneId,
        string sceneSessionId, long visibilityRevision, long serverTime)
    {
        ActionId = actionId;
        CharacterDbId = characterDbId;
        SceneId = sceneId;
        SceneSessionId = sceneSessionId;
        VisibilityRevision = visibilityRevision;
        ServerTime = serverTime;
    }

    public static bool TryParse(string json, out PlayerDrinkEventMessage message)
    {
        message = null;
        if (string.IsNullOrEmpty(json)) return false;

        JObject fields;
        try
        {
            using var input = new StringReader(json);
            using var reader = new JsonTextReader(input) { DateParseHandling = DateParseHandling.None };
            fields = JObject.Load(reader, LoadSettings);
            if (reader.Read()) return false;
        }
        catch (JsonException)
        {
            return false;
        }

        string sceneId = StringField(fields["sceneId"]);
        string sceneSessionId = StringField(fields["sceneSessionId"]);
        if (StringField(fields["msgId"]) != MessageId || StringField(fields["type"]) != MessageId
            || !Guid.TryParseExact(StringField(fields["actionId"]), "N", out Guid actionId) || actionId == Guid.Empty
            || string.IsNullOrWhiteSpace(sceneId) || string.IsNullOrWhiteSpace(sceneSessionId)
            || !TryInteger(fields["characterDbId"], out long characterDbId) || characterDbId <= 0
            || !TryInteger(fields["visibilityRevision"], out long visibilityRevision) || visibilityRevision < 0
            || !TryInteger(fields["serverTime"], out long serverTime) || serverTime <= 0)
            return false;

        message = new PlayerDrinkEventMessage(actionId, characterDbId, sceneId, sceneSessionId,
            visibilityRevision, serverTime);
        return true;
    }

    private static string StringField(JToken token) => token?.Type == JTokenType.String ? (string)token : null;

    private static bool TryInteger(JToken token, out long value)
    {
        value = 0;
        return token?.Type == JTokenType.Integer
            && long.TryParse(token.ToString(Formatting.None), NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out value);
    }
}
