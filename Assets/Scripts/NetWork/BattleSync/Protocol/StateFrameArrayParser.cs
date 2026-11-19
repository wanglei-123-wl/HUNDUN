using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Game.Client.BattleSync.Protocol
{
    public readonly struct StateFrameParseError
    {
        public string FieldPath { get; }
        public string Reason { get; }
        public string SceneId { get; }
        public long? FrameSequence { get; }

        public StateFrameParseError(string fieldPath, string reason,
            string sceneId = "", long? frameSequence = null)
        {
            FieldPath = fieldPath;
            Reason = reason;
            SceneId = sceneId;
            FrameSequence = frameSequence;
        }

        public StateFrameParseError WithContext(string sceneId, long frameSequence)
        {
            return new StateFrameParseError(FieldPath, Reason, sceneId, frameSequence);
        }
    }

    public static class StateFrameArrayParser
    {
        private enum IntegerWidth { Byte, Short, UShort, Int, Long }

        private static readonly IntegerWidth[] PlayerFields =
        {
            IntegerWidth.Int, IntegerWidth.Int, IntegerWidth.Int, IntegerWidth.Int,
            IntegerWidth.UShort, IntegerWidth.Short, IntegerWidth.Short, IntegerWidth.Short,
            IntegerWidth.Byte, IntegerWidth.Byte, IntegerWidth.UShort,
            IntegerWidth.Long, IntegerWidth.Long
        };

        private static readonly IntegerWidth[] MonsterFields =
        {
            IntegerWidth.Int, IntegerWidth.Int, IntegerWidth.Int, IntegerWidth.Int,
            IntegerWidth.UShort, IntegerWidth.Byte, IntegerWidth.UShort,
            IntegerWidth.Int, IntegerWidth.Long, IntegerWidth.Long
        };

        public static bool TryParse(string json, out StateFrameMessageDto message,
            out StateFrameParseError error)
        {
            message = null;
            error = default(StateFrameParseError);
            if (string.IsNullOrWhiteSpace(json))
            {
                error = new StateFrameParseError("$", "JSON is empty.");
                return false;
            }

            JObject root;
            try
            {
                using (StringReader source = new StringReader(json))
                using (JsonTextReader reader = new JsonTextReader(source) { DateParseHandling = DateParseHandling.None })
                {
                    JToken document = JToken.ReadFrom(reader, new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error
                    });
                    if (!(document is JObject parsed) || reader.Read())
                    {
                        error = new StateFrameParseError("$", "Expected one JSON object.");
                        return false;
                    }
                    root = parsed;
                }
            }
            catch (JsonException exception)
            {
                error = new StateFrameParseError("$", exception.Message);
                return false;
            }

            if (!TryString(root, "msgId", out string msgId) || msgId != "state_frame")
            {
                error = new StateFrameParseError("msgId", "Expected state_frame string.");
                return false;
            }
            if (!TryString(root, "type", out string type) || type != "state_frame")
            {
                error = new StateFrameParseError("type", "Expected state_frame string.");
                return false;
            }
            if (!TryString(root, "sceneId", out string sceneId) || string.IsNullOrWhiteSpace(sceneId))
            {
                error = new StateFrameParseError("sceneId", "Expected non-empty string.");
                return false;
            }
            if (!TryInteger(Get(root, "serverTick"), IntegerWidth.Long, out long serverTick)
                || serverTick <= 0)
            {
                error = new StateFrameParseError("serverTick", "Expected positive long integer.");
                return false;
            }
            if (!TryInteger(Get(root, "serverTime"), IntegerWidth.Long, out long serverTime))
            {
                error = new StateFrameParseError("serverTime", "Expected long integer.");
                return false;
            }
            if (!TryInteger(Get(root, "frameSequence"), IntegerWidth.Long, out long frameSequence)
                || frameSequence <= 0)
            {
                error = new StateFrameParseError("frameSequence", "Expected positive long integer.");
                return false;
            }
            if (!(Get(root, "players") is JArray players))
            {
                error = new StateFrameParseError("players", "Expected array.", sceneId, frameSequence);
                return false;
            }
            if (!(Get(root, "monsters") is JArray monsters))
            {
                error = new StateFrameParseError("monsters", "Expected array.", sceneId, frameSequence);
                return false;
            }

            CompactPlayerStateDto[] playerStates = new CompactPlayerStateDto[players.Count];
            for (int index = 0; index < players.Count; index++)
            {
                if (!TryRow(players[index], "players", index, PlayerFields, out long[] values, out error))
                {
                    error = error.WithContext(sceneId, frameSequence);
                    return false;
                }
                if (values[0] <= 0)
                {
                    error = FieldError("players", index, 0, "Entity ID must be positive.")
                        .WithContext(sceneId, frameSequence);
                    return false;
                }
                if (values[11] < 0 || values[12] < 0)
                {
                    int field = values[11] < 0 ? 11 : 12;
                    error = FieldError("players", index, field, "Sequence or visibility revision cannot be negative.")
                        .WithContext(sceneId, frameSequence);
                    return false;
                }
                playerStates[index] = new CompactPlayerStateDto
                {
                    netId = (int)values[0], x = (int)values[1], y = (int)values[2], z = (int)values[3],
                    yaw = (int)values[4], vx = (int)values[5], vy = (int)values[6], vz = (int)values[7],
                    move = (int)values[8], action = (int)values[9], flags = (int)values[10],
                    inputSeq = values[11], visibilityRevision = values[12]
                };
            }

            CompactMonsterStateDto[] monsterStates = new CompactMonsterStateDto[monsters.Count];
            for (int index = 0; index < monsters.Count; index++)
            {
                if (!TryRow(monsters[index], "monsters", index, MonsterFields, out long[] values, out error))
                {
                    error = error.WithContext(sceneId, frameSequence);
                    return false;
                }
                if (values[0] <= 0)
                {
                    error = FieldError("monsters", index, 0, "Entity ID must be positive.")
                        .WithContext(sceneId, frameSequence);
                    return false;
                }
                if (values[7] < 0 || values[8] < 0 || values[9] < 0)
                {
                    int field = values[7] < 0 ? 7 : values[8] < 0 ? 8 : 9;
                    error = FieldError("monsters", index, field, "Health or revision cannot be negative.")
                        .WithContext(sceneId, frameSequence);
                    return false;
                }
                if ((values[6] & BattleSyncProtocolConstants.StateFlags.MovementState) != 0
                    && values[5] > 7)
                {
                    error = FieldError("monsters", index, 5, "Monster AI state is outside the known range.")
                        .WithContext(sceneId, frameSequence);
                    return false;
                }
                monsterStates[index] = new CompactMonsterStateDto
                {
                    netId = (int)values[0], x = (int)values[1], y = (int)values[2], z = (int)values[3],
                    yaw = (int)values[4], aiState = (int)values[5], flags = (int)values[6],
                    hp = (int)values[7], combatRevision = values[8], visibilityRevision = values[9]
                };
            }

            if (!TryMonsterExtensions(root, monsterStates, serverTick, frameSequence, out string sceneSessionId,
                out MonsterStateDelta[] deltas, out MonsterPathUpdate[] paths, out string invalidField))
            {
                error = new StateFrameParseError(invalidField, "Invalid monster synchronization data.", sceneId, frameSequence);
                return false;
            }

            message = new StateFrameMessageDto
            {
                msgId = msgId, type = type, sceneId = sceneId,
                serverTick = serverTick, serverTime = serverTime, frameSequence = frameSequence,
                players = playerStates, monsters = monsterStates, sceneSessionId = sceneSessionId,
                monsterDeltas = deltas, monsterPaths = paths
            };
            return true;
        }

        private static readonly IntegerWidth[] PathFields =
        {
            IntegerWidth.Int, IntegerWidth.Long, IntegerWidth.Long, IntegerWidth.Long, IntegerWidth.Int,
            IntegerWidth.Int, IntegerWidth.Int, IntegerWidth.Int, IntegerWidth.Int, IntegerWidth.Int
        };

        private static bool TryMonsterExtensions(JObject root, CompactMonsterStateDto[] monsters,
            long tick, long sequence, out string sceneSessionId, out MonsterStateDelta[] deltas,
            out MonsterPathUpdate[] paths, out string invalidField)
        {
            sceneSessionId = null;
            deltas = Array.Empty<MonsterStateDelta>();
            paths = Array.Empty<MonsterPathUpdate>();
            invalidField = "sceneSessionId";
            if (Get(root, invalidField) != null &&
                (!TryString(root, invalidField, out sceneSessionId) || string.IsNullOrWhiteSpace(sceneSessionId)))
                return false;

            JToken deltaList = Get(root, "monsterDeltas"), pathList = Get(root, "monsterPaths");
            if (deltaList == null && pathList == null) return true;
            if (sceneSessionId == null) return false;

            var identities = new Dictionary<int, long>();
            foreach (CompactMonsterStateDto state in monsters)
            {
                invalidField = "monsters";
                if (identities.ContainsKey(state.netId)) return false;
                identities.Add(state.netId, state.visibilityRevision);
            }
            if (deltaList != null)
            {
                invalidField = "monsterDeltas";
                if (!(deltaList is JArray rows)) return false;
                deltas = new MonsterStateDelta[rows.Count];
                for (int i = 0; i < deltas.Length; i++)
                {
                    invalidField = "monsterDeltas[" + i + "]";
                    if (!(rows[i] is JArray row) || !TryDelta(row, sequence, out MonsterStateDelta delta)
                        || identities.ContainsKey(delta.fields.netId)) return false;
                    identities.Add(delta.fields.netId, delta.fields.visibilityRevision);
                    deltas[i] = delta;
                }
            }
            if (pathList != null)
            {
                invalidField = "monsterPaths";
                if (!(pathList is JArray rows)) return false;
                paths = new MonsterPathUpdate[rows.Count];
                for (int i = 0; i < paths.Length; i++)
                {
                    invalidField = "monsterPaths[" + i + "]";
                    if (!(rows[i] is JArray row) || !TryPath(row, tick, out MonsterPathUpdate path)
                        || !identities.TryGetValue(path.netId, out long visible) || visible != path.visibilityRevision)
                        return false;
                    // A path row must accompany one state row, and each monster can have at most one path update.
                    identities.Remove(path.netId);
                    paths[i] = path;
                }
            }
            return true;
        }

        private static bool TryDelta(JArray row, long sequence, out MonsterStateDelta delta)
        {
            delta = null;
            if (row.Count < 4 || !TryInteger(row[0], IntegerWidth.Int, out long id) || id <= 0
                || !TryInteger(row[1], IntegerWidth.Long, out long visibility) || visibility < 0
                || !TryInteger(row[2], IntegerWidth.Long, out long baseline) || baseline <= 0 || baseline >= sequence
                || !TryInteger(row[3], IntegerWidth.Byte, out long mask) || mask < 1 || mask > 7)
                return false;
            bool position = (mask & MonsterStateDelta.Position) != 0;
            bool behavior = (mask & MonsterStateDelta.Behavior) != 0;
            bool combat = (mask & MonsterStateDelta.Combat) != 0;
            if (row.Count != 4 + (position ? 4 : 0) + (behavior ? 2 : 0) + (combat ? 2 : 0)) return false;

            var fields = new CompactMonsterStateDto { netId = (int)id, visibilityRevision = visibility };
            int index = 4;
            if (position)
            {
                if (!TryInteger(row[index++], IntegerWidth.Int, out long x)
                    || !TryInteger(row[index++], IntegerWidth.Int, out long y)
                    || !TryInteger(row[index++], IntegerWidth.Int, out long z)
                    || !TryInteger(row[index++], IntegerWidth.UShort, out long yaw)) return false;
                fields.x = (int)x; fields.y = (int)y; fields.z = (int)z; fields.yaw = (int)yaw;
            }
            if (behavior)
            {
                if (!TryInteger(row[index++], IntegerWidth.Byte, out long ai)
                    || !TryInteger(row[index++], IntegerWidth.UShort, out long flags)
                    || (flags & BattleSyncProtocolConstants.StateFlags.MovementState) != 0 && ai > 7) return false;
                fields.aiState = (int)ai; fields.flags = (int)flags;
            }
            if (combat)
            {
                if (!TryInteger(row[index++], IntegerWidth.Int, out long hp) || hp < 0
                    || !TryInteger(row[index], IntegerWidth.Long, out long revision) || revision < 0) return false;
                fields.hp = (int)hp; fields.combatRevision = revision;
            }
            delta = new MonsterStateDelta { fields = fields, baseFrameSequence = baseline, mask = (int)mask };
            return true;
        }

        private static bool TryPath(JArray row, long tick, out MonsterPathUpdate path)
        {
            path = null;
            if ((row.Count != 2 && row.Count != PathFields.Length)
                || !TryInteger(row[0], IntegerWidth.Int, out long id) || id <= 0
                || !TryInteger(row[1], IntegerWidth.Long, out long visibility) || visibility < 0)
                return false;
            MonsterPathDto segment = null;
            if (row.Count != 2)
            {
                if (!TryRow(row, "monsterPaths", 0, PathFields, out long[] values, out _)) return false;
                segment = new MonsterPathDto
                {
                    startTick = values[2], startTime = values[3], speed = values[4] * .01f,
                    x = values[5] * .01f, y = values[6] * .01f, z = values[7] * .01f,
                    endX = values[8] * .01f, endZ = values[9] * .01f
                };
                if (segment.startTick < 0 || segment.startTick > tick || segment.startTime <= 0 || segment.speed < 0)
                    return false;
                if (values[5] == values[8] && values[7] == values[9])
                    segment = null; // Both endpoints rounded to the same centimeter.
                else if (!segment.IsValid) return false;
            }
            path = new MonsterPathUpdate { netId = (int)id, visibilityRevision = visibility, path = segment };
            return true;
        }

        private static bool TryRow(JToken token, string list, int index, IntegerWidth[] fields,
            out long[] values, out StateFrameParseError error)
        {
            values = null;
            error = default(StateFrameParseError);
            string path = list + "[" + index + "]";
            if (!(token is JArray row) || row.Count != fields.Length)
            {
                error = new StateFrameParseError(path, "Expected integer array of length " + fields.Length + ".");
                return false;
            }
            values = new long[fields.Length];
            for (int field = 0; field < fields.Length; field++)
            {
                if (TryInteger(row[field], fields[field], out values[field])) continue;
                error = new StateFrameParseError(path + "[" + field + "]",
                    "Expected integer in " + fields[field] + " range.");
                values = null;
                return false;
            }
            return true;
        }

        private static bool TryInteger(JToken token, IntegerWidth width, out long value)
        {
            value = 0;
            if (token == null || token.Type != JTokenType.Integer
                || !long.TryParse(token.ToString(Formatting.None), NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture, out value))
                return false;
            switch (width)
            {
                case IntegerWidth.Byte: return value >= byte.MinValue && value <= byte.MaxValue;
                case IntegerWidth.Short: return value >= short.MinValue && value <= short.MaxValue;
                case IntegerWidth.UShort: return value >= ushort.MinValue && value <= ushort.MaxValue;
                case IntegerWidth.Int: return value >= int.MinValue && value <= int.MaxValue;
                default: return true;
            }
        }

        private static StateFrameParseError FieldError(string list, int row, int field, string reason)
        {
            return new StateFrameParseError(list + "[" + row + "][" + field + "]", reason);
        }

        private static bool TryString(JObject root, string name, out string value)
        {
            JToken token = Get(root, name);
            value = token != null && token.Type == JTokenType.String ? (string)token : null;
            return value != null;
        }

        private static JToken Get(JObject root, string name)
        {
            return root.TryGetValue(name, StringComparison.Ordinal, out JToken value) ? value : null;
        }
    }
}
