using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game.Cultivation.Protocol
{
    public static class CultivationCatalogWireState
    {
        public static bool TryReadOptionalNonNegativeLong(string json,
            string name, out long? value)
        {
            value = null;
            if (!TryProperty(json, 0, name, out int start, out int end))
                return true;
            string token = json.Substring(start, end - start);
            if (token == "null")
                return true;
            if (!long.TryParse(token, NumberStyles.None,
                    CultureInfo.InvariantCulture, out long number)
                || number < 0)
                return false;
            value = number;
            return true;
        }

        public static bool TryRestoreNullableReferences(string json,
            CultivationCatalogResultDto catalog)
        {
            if (catalog?.techniques == null
                || !TryProperty(json, 0, "techniques", out int start, out _)
                || !TryElements(json, start, out List<int> techniques)
                || techniques.Count != catalog.techniques.Length)
                return false;
            for (int techniqueIndex = 0; techniqueIndex < techniques.Count;
                techniqueIndex++)
            {
                CultivationTechniqueDto technique = catalog.techniques[techniqueIndex];
                if (technique?.levels == null
                    || !TryProperty(json, techniques[techniqueIndex], "levels",
                        out int levelsStart, out _)
                    || !TryElements(json, levelsStart, out List<int> levels)
                    || levels.Count != technique.levels.Length)
                    return false;
                for (int levelIndex = 0; levelIndex < levels.Count; levelIndex++)
                {
                    CultivationLevelDto level = technique.levels[levelIndex];
                    if (level == null
                        || !TryProperty(json, levels[levelIndex],
                            "referenceComprehension", out int valueStart,
                            out int valueEnd))
                        return false;
                    if (json.Substring(valueStart, valueEnd - valueStart) == "null")
                    {
                        level.hasReferenceComprehension = false;
                        level.referenceComprehension = 0;
                    }
                    else
                    {
                        if (!int.TryParse(json.Substring(valueStart,
                            valueEnd - valueStart), NumberStyles.Integer,
                            CultureInfo.InvariantCulture, out int value)
                            || value < 0)
                            return false;
                        level.referenceComprehension = value;
                        level.hasReferenceComprehension = true;
                    }
                }
            }
            return true;
        }

        private static bool TryProperty(string json, int position, string name,
            out int start, out int end)
        {
            start = end = -1;
            if (string.IsNullOrEmpty(json) || position >= json.Length
                || json[position++] != '{')
                return false;
            SkipSpace(json, ref position);
            while (position < json.Length && json[position] != '}')
            {
                if (!ReadString(json, ref position, out string key))
                    return false;
                SkipSpace(json, ref position);
                if (!Consume(json, ref position, ':'))
                    return false;
                SkipSpace(json, ref position);
                int valueStart = position;
                if (!SkipValue(json, ref position, 0))
                    return false;
                if (key == name)
                {
                    if (start >= 0)
                        return false;
                    start = valueStart;
                    end = position;
                }
                SkipSpace(json, ref position);
                if (Consume(json, ref position, ','))
                {
                    SkipSpace(json, ref position);
                    continue;
                }
                break;
            }
            return start >= 0 && Consume(json, ref position, '}');
        }

        private static bool TryElements(string json, int position,
            out List<int> elements)
        {
            elements = new List<int>();
            if (position >= json.Length || json[position++] != '[')
                return false;
            SkipSpace(json, ref position);
            if (Consume(json, ref position, ']'))
                return true;
            while (position < json.Length)
            {
                elements.Add(position);
                if (!SkipValue(json, ref position, 0))
                    return false;
                SkipSpace(json, ref position);
                if (Consume(json, ref position, ']'))
                    return true;
                if (!Consume(json, ref position, ','))
                    return false;
                SkipSpace(json, ref position);
            }
            return false;
        }

        private static bool SkipValue(string json, ref int position, int depth)
        {
            if (depth > 64 || position >= json.Length)
                return false;
            char token = json[position];
            if (token == '"')
                return ReadString(json, ref position, out _);
            if (token == '[' || token == '{')
            {
                char close = token == '[' ? ']' : '}';
                position++;
                SkipSpace(json, ref position);
                if (Consume(json, ref position, close))
                    return true;
                while (position < json.Length)
                {
                    if (token == '{')
                    {
                        if (!ReadString(json, ref position, out _))
                            return false;
                        SkipSpace(json, ref position);
                        if (!Consume(json, ref position, ':'))
                            return false;
                        SkipSpace(json, ref position);
                    }
                    if (!SkipValue(json, ref position, depth + 1))
                        return false;
                    SkipSpace(json, ref position);
                    if (Consume(json, ref position, close))
                        return true;
                    if (!Consume(json, ref position, ','))
                        return false;
                    SkipSpace(json, ref position);
                }
                return false;
            }
            if (token == 'n' || token == 't' || token == 'f')
            {
                string literal = token == 'n' ? "null"
                    : token == 't' ? "true" : "false";
                if (position + literal.Length > json.Length
                    || string.CompareOrdinal(json, position, literal, 0,
                        literal.Length) != 0)
                    return false;
                position += literal.Length;
                return true;
            }
            int start = position;
            if (json[position] == '-')
                position++;
            while (position < json.Length && char.IsDigit(json[position]))
                position++;
            if (position < json.Length && json[position] == '.')
            {
                position++;
                while (position < json.Length && char.IsDigit(json[position]))
                    position++;
            }
            if (position < json.Length && (json[position] == 'e'
                || json[position] == 'E'))
            {
                position++;
                if (position < json.Length && (json[position] == '+'
                    || json[position] == '-'))
                    position++;
                while (position < json.Length && char.IsDigit(json[position]))
                    position++;
            }
            return position > start;
        }

        private static bool ReadString(string json, ref int position,
            out string value)
        {
            value = null;
            if (!Consume(json, ref position, '"'))
                return false;
            int start = position;
            while (position < json.Length)
            {
                char current = json[position++];
                if (current == '\\')
                {
                    position++;
                    continue;
                }
                if (current == '"')
                {
                    value = json.Substring(start, position - start - 1);
                    return true;
                }
                if (current < 0x20)
                    return false;
            }
            return false;
        }

        private static void SkipSpace(string json, ref int position)
        {
            while (position < json.Length && char.IsWhiteSpace(json[position]))
                position++;
        }

        private static bool Consume(string json, ref int position, char token)
        {
            if (position >= json.Length || json[position] != token)
                return false;
            position++;
            return true;
        }
    }
}
