using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

internal static class PlayerMovementEventParser
{
    private const string MessageId = "player_movement_event";
    private const int GroundFieldCount = 14;
    private const int AirFieldCount = 16;

    // The array indexes match MovementBroadcastEvent and MovementBroadcastMode in Game.Protocol.
    private static readonly string[] Events =
    {
        "control", "start", "stop", "turn_start", "turn_stop", "facing", "walk",
        "run", "jump", "fall", "land", "ceiling", "heartbeat"
    };

    private static readonly string[] Modes = { "normal", "controlled", "recovery" };

    private static readonly JsonLoadSettings LoadSettings = new JsonLoadSettings
    {
        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
    };

    public static bool TryParse(string json, out PlayerMovementEventMessage message)
    {
        message = null;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        JObject root;
        try
        {
            using (var source = new StringReader(json))
            using (var reader = new JsonTextReader(source) { DateParseHandling = DateParseHandling.None })
            {
                root = JObject.Load(reader, LoadSettings);
                if (reader.Read())
                    return false;
            }
        }
        catch (JsonException)
        {
            return false;
        }

        if (ReadString(root["msgId"]) != MessageId || ReadString(root["type"]) != MessageId)
            return false;

        string sceneId = ReadString(root["sceneId"]);
        string sceneSessionId = ReadString(root["sceneSessionId"]);
        if (string.IsNullOrWhiteSpace(sceneId) || string.IsNullOrWhiteSpace(sceneSessionId)
            || !(root["data"] is JArray data)
            || (data.Count != GroundFieldCount && data.Count != AirFieldCount))
            return false;

        if (!TryInteger(data[0], out long characterDbId, minimum: 1)
            || !TryInteger(data[1], out long visibilityRevision)
            || !TryInteger(data[2], out long epoch, minimum: 1)
            || !TryInteger(data[3], out long revision, minimum: 1)
            || !TryInteger(data[4], out long eventCode, maximum: Events.Length - 1)
            || !TryInteger(data[5], out long modeCode, maximum: Modes.Length - 1)
            || !TryInteger(data[6], out long serverTime)
            || !TryNumber(data[7], out float x)
            || !TryNumber(data[8], out float y)
            || !TryNumber(data[9], out float z)
            || !TryNumber(data[10], out float yaw)
            || !TryInteger(data[11], out long flagsValue, maximum: byte.MaxValue)
            || !TryNumber(data[12], out float velocityX)
            || !TryNumber(data[13], out float velocityZ))
            return false;

        var flags = (MovementEventFlags)flagsValue;
        bool airborne = (flags & MovementEventFlags.Falling) != 0;
        if (data.Count != (airborne ? AirFieldCount : GroundFieldCount))
            return false;

        float velocityY = 0;
        float jumpSpeed = 0;
        if (airborne && (!TryNumber(data[14], out velocityY) || !TryNumber(data[15], out jumpSpeed)))
            return false;

        message = new PlayerMovementEventMessage
        {
            sceneId = sceneId,
            sceneSessionId = sceneSessionId,
            characterDbId = characterDbId,
            visibilityRevision = visibilityRevision,
            @event = Events[eventCode],
            movement = new RemoteMovementSnapshot
            {
                epoch = epoch,
                revision = revision,
                mode = Modes[modeCode],
                serverTime = serverTime,
                info = new MovementEventInfo
                {
                    x = x, y = y, z = z, yaw = yaw, flags = flags,
                    velocityX = velocityX, velocityY = velocityY, velocityZ = velocityZ,
                    jumpSpeed = jumpSpeed
                }
            }
        };
        return true;
    }

    private static string ReadString(JToken token) =>
        token?.Type == JTokenType.String ? (string)token : null;

    private static bool TryInteger(JToken token, out long value, long minimum = 0, long maximum = long.MaxValue)
    {
        value = 0;
        if (!(token is JValue integer) || token.Type != JTokenType.Integer
            || !(integer.Value is long number) || number < minimum || number > maximum)
            return false;
        value = number;
        return true;
    }

    private static bool TryNumber(JToken token, out float value)
    {
        value = 0;
        if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
            return false;
        value = (float)(double)token;
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
