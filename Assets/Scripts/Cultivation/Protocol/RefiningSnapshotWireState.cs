using System;
using System.Globalization;
using System.Text;

namespace Game.Cultivation.Protocol
{
    // JsonUtility does not expose whether a nested reference was explicitly null.
    public static class RefiningSnapshotWireState
    {
        public static bool TryRead(string json, out bool hasSnapshot,
            out bool snapshotIsNull, out bool hasActiveTask,
            out bool activeTaskIsNull)
        {
            return TryRead(json, "activeTask", out hasSnapshot,
                out snapshotIsNull, out hasActiveTask, out activeTaskIsNull);
        }

        public static bool TryRead(string json, string taskField,
            out bool hasSnapshot, out bool snapshotIsNull,
            out bool hasTask, out bool taskIsNull)
        {
            hasSnapshot = false;
            snapshotIsNull = false;
            hasTask = false;
            taskIsNull = false;
            if (string.IsNullOrEmpty(json) || string.IsNullOrEmpty(taskField))
                return false;

            int position = 0;
            if (!FindProperty(json, ref position, 0, "snapshot",
                out hasSnapshot, out int snapshotStart, out int snapshotEnd))
                return false;
            SkipWhitespace(json, ref position);
            if (position != json.Length || !hasSnapshot)
                return position == json.Length && !hasSnapshot;

            snapshotIsNull = IsNull(json, snapshotStart, snapshotEnd);
            if (snapshotIsNull)
                return true;
            if (json[snapshotStart] != '{')
                return false;

            position = snapshotStart;
            if (!FindProperty(json, ref position, 0, taskField,
                out hasTask, out int taskStart, out int taskEnd)
                || position != snapshotEnd)
                return false;
            if (!hasTask)
                return true;
            taskIsNull = IsNull(json, taskStart, taskEnd);
            return taskIsNull || json[taskStart] == '{';
        }

        public static bool TryReadTaskBoolean(string json, string taskField,
            string field, out bool present, out bool? value)
        {
            present = false;
            value = null;
            if (string.IsNullOrEmpty(field) || !TryRead(json, taskField,
                out bool hasSnapshot, out bool snapshotIsNull,
                out bool hasTask, out bool taskIsNull))
                return false;
            if (!hasSnapshot || snapshotIsNull || !hasTask || taskIsNull)
                return true;

            int position = 0;
            if (!FindProperty(json, ref position, 0, "snapshot",
                out _, out int snapshotStart, out _))
                return false;
            position = snapshotStart;
            if (!FindProperty(json, ref position, 0, taskField,
                out _, out int taskStart, out _))
                return false;
            position = taskStart;
            if (!FindProperty(json, ref position, 0, field,
                out present, out int start, out int end))
                return false;
            if (!present || IsNull(json, start, end))
                return true;
            if (end - start == 4
                && string.CompareOrdinal(json, start, "true", 0, 4) == 0)
                value = true;
            else if (end - start == 5
                && string.CompareOrdinal(json, start, "false", 0, 5) == 0)
                value = false;
            else
                return false;
            return true;
        }

        private static bool FindProperty(string json, ref int position,
            int depth, string name, out bool found, out int start, out int end)
        {
            found = false;
            start = end = -1;
            if (depth > 64 || position >= json.Length
                || json[position++] != '{')
                return false;
            SkipWhitespace(json, ref position);
            if (Consume(json, ref position, '}'))
                return true;
            while (position < json.Length)
            {
                if (!ReadString(json, ref position, out string key))
                    return false;
                SkipWhitespace(json, ref position);
                if (!Consume(json, ref position, ':'))
                    return false;
                SkipWhitespace(json, ref position);
                int valueStart = position;
                if (!SkipValue(json, ref position, depth + 1))
                    return false;
                if (name != null && string.Equals(key, name, StringComparison.Ordinal))
                {
                    if (found)
                        return false;
                    found = true;
                    start = valueStart;
                    end = position;
                }
                SkipWhitespace(json, ref position);
                if (Consume(json, ref position, '}'))
                    return true;
                if (!Consume(json, ref position, ','))
                    return false;
                SkipWhitespace(json, ref position);
            }
            return false;
        }

        private static bool SkipValue(string json, ref int position, int depth)
        {
            if (depth > 64 || position >= json.Length)
                return false;
            char token = json[position];
            if (token == '{')
                return FindProperty(json, ref position, depth, null,
                    out _, out _, out _);
            if (token == '"')
                return ReadString(json, ref position, out _);
            if (token == '[')
            {
                position++;
                SkipWhitespace(json, ref position);
                if (Consume(json, ref position, ']'))
                    return true;
                while (position < json.Length)
                {
                    if (!SkipValue(json, ref position, depth + 1))
                        return false;
                    SkipWhitespace(json, ref position);
                    if (Consume(json, ref position, ']'))
                        return true;
                    if (!Consume(json, ref position, ','))
                        return false;
                    SkipWhitespace(json, ref position);
                }
                return false;
            }
            if (token == 'n')
                return ConsumeLiteral(json, ref position, "null");
            if (token == 't')
                return ConsumeLiteral(json, ref position, "true");
            if (token == 'f')
                return ConsumeLiteral(json, ref position, "false");
            return SkipNumber(json, ref position);
        }

        private static bool SkipNumber(string json, ref int position)
        {
            Consume(json, ref position, '-');
            if (position >= json.Length)
                return false;
            if (!Consume(json, ref position, '0'))
            {
                if (json[position] < '1' || json[position] > '9')
                    return false;
                do { position++; }
                while (position < json.Length && IsDigit(json[position]));
            }
            if (Consume(json, ref position, '.'))
            {
                int fraction = position;
                while (position < json.Length && IsDigit(json[position]))
                    position++;
                if (fraction == position)
                    return false;
            }
            if (Consume(json, ref position, 'e')
                || Consume(json, ref position, 'E'))
            {
                if (!Consume(json, ref position, '+'))
                    Consume(json, ref position, '-');
                int exponent = position;
                while (position < json.Length && IsDigit(json[position]))
                    position++;
                if (exponent == position)
                    return false;
            }
            return true;
        }

        private static bool ReadString(string json, ref int position,
            out string value)
        {
            value = null;
            if (!Consume(json, ref position, '"'))
                return false;
            var text = new StringBuilder();
            while (position < json.Length)
            {
                char current = json[position++];
                if (current == '"')
                {
                    value = text.ToString();
                    return true;
                }
                if (current < 0x20)
                    return false;
                if (current != '\\')
                {
                    text.Append(current);
                    continue;
                }
                if (position >= json.Length)
                    return false;
                char escape = json[position++];
                switch (escape)
                {
                    case '"': case '\\': case '/': text.Append(escape); break;
                    case 'b': text.Append('\b'); break;
                    case 'f': text.Append('\f'); break;
                    case 'n': text.Append('\n'); break;
                    case 'r': text.Append('\r'); break;
                    case 't': text.Append('\t'); break;
                    case 'u':
                        if (position + 4 > json.Length
                            || !int.TryParse(json.Substring(position, 4),
                                NumberStyles.HexNumber,
                                CultureInfo.InvariantCulture, out int code))
                            return false;
                        text.Append((char)code);
                        position += 4;
                        break;
                    default: return false;
                }
            }
            return false;
        }

        private static bool IsNull(string json, int start, int end)
        {
            return end - start == 4
                && string.CompareOrdinal(json, start, "null", 0, 4) == 0;
        }

        private static bool IsDigit(char value)
        {
            return value >= '0' && value <= '9';
        }

        private static bool ConsumeLiteral(string json, ref int position,
            string literal)
        {
            if (position + literal.Length > json.Length
                || string.CompareOrdinal(json, position, literal, 0,
                    literal.Length) != 0)
                return false;
            position += literal.Length;
            return true;
        }

        private static bool Consume(string json, ref int position, char expected)
        {
            if (position >= json.Length || json[position] != expected)
                return false;
            position++;
            return true;
        }

        private static void SkipWhitespace(string json, ref int position)
        {
            while (position < json.Length)
            {
                char value = json[position];
                if (value != ' ' && value != '\t'
                    && value != '\r' && value != '\n')
                    break;
                position++;
            }
        }
    }
}
