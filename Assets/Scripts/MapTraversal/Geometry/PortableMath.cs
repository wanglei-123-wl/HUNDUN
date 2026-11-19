using System;

namespace Game.Client.MapTraversal.Geometry
{
    internal static class PortableMath
    {
        public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static double Clamp(double value, double min, double max) => Math.Min(max, Math.Max(min, value));
        public static float Clamp(float value, float min, float max) => Math.Min(max, Math.Max(min, value));

        public static void Sort(Span<double> values)
        {
            for (int i = 1; i < values.Length; i++)
            {
                double value = values[i];
                int j = i - 1;
                while (j >= 0 && values[j] > value) { values[j + 1] = values[j]; j--; }
                values[j + 1] = value;
            }
        }
    }
}
