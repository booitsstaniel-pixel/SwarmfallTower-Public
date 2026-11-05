using System;
using System.Collections.Generic;

namespace Swarmfall
{
    static class M
    {
        static readonly Random Rng = new Random();

        public static float Clamp(float v, float a, float b) { return v < a ? a : v > b ? b : v; }
        public static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
        public static float Smoothstep(float a, float b, float v)
        {
            float t = Clamp((v - a) / (b - a), 0, 1);
            return t * t * (3 - 2 * t);
        }
        // Frame-rate independent smoothing toward a target.
        public static float Damp(float a, float b, float k, float dt) { return b + (a - b) * Exp(-k * dt); }
        public static float Rand() { return (float)Rng.NextDouble(); }
        public static float Range(float a, float b) { return a + Rand() * (b - a); }
        public static int RangeInt(int a, int b) { return a + Rng.Next(b - a); }
        public static T Pick<T>(IList<T> list) { return list[Rng.Next(list.Count)]; }
        public static T Weighted<T>(IList<T> list, Func<T, float> weightOf)
        {
            float total = 0;
            foreach (T item in list) total += weightOf(item);
            float roll = Rand() * total;
            foreach (T item in list)
            {
                roll -= weightOf(item);
                if (roll <= 0) return item;
            }
            return list[list.Count - 1];
        }
        public static float Sqrt(float v) { return (float)Math.Sqrt(v); }
        public static float Sin(float v) { return (float)Math.Sin(v); }
        public static float Cos(float v) { return (float)Math.Cos(v); }
        public static float Atan2(float y, float x) { return (float)Math.Atan2(y, x); }
        public static float Exp(float v) { return (float)Math.Exp(v); }
        public static float Pow(float a, float b) { return (float)Math.Pow(a, b); }
        public static float Abs(float v) { return v < 0 ? -v : v; }
        public static float Max(float a, float b) { return a > b ? a : b; }
        public static float Min(float a, float b) { return a < b ? a : b; }
        public static float Hypot(float x, float y) { return (float)Math.Sqrt(x * x + y * y); }
        public static float Hypot(float x, float y, float z) { return (float)Math.Sqrt(x * x + y * y + z * z); }
        public const float Pi = 3.14159265f;
        public const float Tau = 6.2831853f;
    }

    // Small deterministic generator, so a seed always gives the same map.
    sealed class SeededRandom
    {
        uint s;
        public SeededRandom(uint seed) { s = seed; }
        public float Next()
        {
            unchecked
            {
                s += 0x6d2b79f5;
                uint t = s;
                t = (t ^ (t >> 15)) * (t | 1);
                t ^= t + (t ^ (t >> 7)) * (t | 61);
                return (t ^ (t >> 14)) / 4294967296f;
            }
        }
    }

    sealed class Noise
    {
        readonly float[] table = new float[256 * 256];
        public Noise(uint seed)
        {
            var rng = new SeededRandom(seed);
            for (int i = 0; i < table.Length; i++) table[i] = rng.Next();
        }
        float At(int x, int y) { return table[((y & 255) << 8) | (x & 255)]; }
        float Value(float x, float y)
        {
            int ix = (int)Math.Floor(x);
            int iy = (int)Math.Floor(y);
            float fx = x - ix;
            float fy = y - iy;
            float sx = fx * fx * (3 - 2 * fx);
            float sy = fy * fy * (3 - 2 * fy);
            return M.Lerp(M.Lerp(At(ix, iy), At(ix + 1, iy), sx), M.Lerp(At(ix, iy + 1), At(ix + 1, iy + 1), sx), sy);
        }
        public float Fbm(float x, float y, int octaves)
        {
            float sum = 0, amp = 0.5f, freq = 1;
            for (int i = 0; i < octaves; i++)
            {
                sum += Value(x * freq, y * freq) * amp;
                amp *= 0.5f;
                freq *= 2;
            }
            return sum;
        }
    }

    struct Col
    {
        public float R, G, B;
        public Col(uint hex)
        {
            R = ((hex >> 16) & 255) / 255f;
            G = ((hex >> 8) & 255) / 255f;
            B = (hex & 255) / 255f;
        }
        public Col(float r, float g, float b) { R = r; G = g; B = b; }
        public Col Mul(float k) { return new Col(M.Min(1, R * k), M.Min(1, G * k), M.Min(1, B * k)); }
        public Col Lerp(Col o, float t) { return new Col(M.Lerp(R, o.R, t), M.Lerp(G, o.G, t), M.Lerp(B, o.B, t)); }
        // Colour with its lightness scaled, keeping hue. Used for sprite shading.
        public static uint Shade(uint hex, float k)
        {
            Col c = new Col(hex).Mul(k);
            return ((uint)(c.R * 255) << 16) | ((uint)(c.G * 255) << 8) | (uint)(c.B * 255);
        }
    }
}
