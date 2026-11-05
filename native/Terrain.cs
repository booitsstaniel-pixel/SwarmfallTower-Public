using System;
using System.Collections.Generic;

namespace Swarmfall
{
    sealed class Collider
    {
        public float X, Z, R, Top, Base;
        public bool Tall;
    }

    sealed class Prop
    {
        public float X, Y, Z, S, H, Yaw;
        public bool Glow;
    }

    // A walled bowl: heightfield ground plus round pillars (tall) and plinths (jumpable).
    // Holds only the data, so the simulation can run without a window. Renderer.cs turns it into geometry.
    sealed class Terrain
    {
        public const int Half = 124;
        public const int Step = 2;
        public const int GridSize = Half * 2 / Step + 1;
        public const float ArenaRadius = 112;

        public readonly float[] Heights = new float[GridSize * GridSize];
        public readonly float[] Shades = new float[GridSize * GridSize];
        public readonly List<Collider> Colliders = new List<Collider>();
        public readonly List<Prop> Props = new List<Prop>();
        public readonly Biome Biome;

        public Terrain(uint seed, Biome biome)
        {
            Biome = biome;
            var noise = new Noise(seed);
            var rng = new SeededRandom(seed ^ 0x51f15e);
            for (int z = 0; z < GridSize; z++)
            {
                for (int x = 0; x < GridSize; x++)
                {
                    float wx = x * Step - Half;
                    float wz = z * Step - Half;
                    float r = M.Hypot(wx, wz);
                    // Terraced hills: flat ledges joined by short steep banks, so the map has high ground to find.
                    float raw = (noise.Fbm(wx * 0.019f + 11, wz * 0.019f + 7, 3) - 0.42f) * 30;
                    float band = (float)Math.Floor(raw / 3.2f);
                    float terraced = (band + M.Smoothstep(0.3f, 0.7f, raw / 3.2f - band)) * 3.2f;
                    float hills = terraced * M.Smoothstep(12, 38, r);
                    float rim = M.Smoothstep(ArenaRadius - 2, ArenaRadius + 8, r) * 16;
                    Heights[z * GridSize + x] = hills + rim;
                    Shades[z * GridSize + x] = 0.72f + noise.Fbm(wx * 0.09f, wz * 0.09f, 2) * 0.6f;
                }
            }
            ScatterColliders(rng);
            ScatterProps(rng);
        }

        void ScatterColliders(SeededRandom rng)
        {
            int tries = 0;
            while (Colliders.Count < 90 && tries++ < 1500)
            {
                float angle = rng.Next() * M.Tau;
                float dist = 16 + M.Sqrt(rng.Next()) * (ArenaRadius - 22);
                float x = M.Cos(angle) * dist;
                float z = M.Sin(angle) * dist;
                bool tall = rng.Next() < 0.45f;
                float r = tall ? 1.5f + rng.Next() : 2.2f + rng.Next() * 2.2f;
                bool blocked = false;
                foreach (Collider o in Colliders) if (M.Hypot(o.X - x, o.Z - z) < o.R + r + 3.5f) blocked = true;
                if (blocked) continue;
                float baseY = HeightAt(x, z);
                Colliders.Add(new Collider { X = x, Z = z, R = r, Base = baseY, Top = baseY + (tall ? 18 : 1.2f + rng.Next() * 1.2f), Tall = tall });
            }
        }

        void ScatterProps(SeededRandom rng)
        {
            for (int i = 0; i < 760; i++)
            {
                float angle = rng.Next() * M.Tau;
                float dist = 7 + M.Sqrt(rng.Next()) * (ArenaRadius - 6);
                float x = M.Cos(angle) * dist;
                float z = M.Sin(angle) * dist;
                float s = 0.2f + rng.Next() * rng.Next() * 1.1f;
                Props.Add(new Prop { X = x, Z = z, Y = HeightAt(x, z) + s * 0.1f, S = s, H = s * (0.5f + rng.Next()), Yaw = rng.Next() * M.Tau, Glow = rng.Next() < 0.16f });
            }
        }

        // Ground height with bilinear lookup. Cheap enough to call per enemy, per frame.
        public float HeightAt(float x, float z)
        {
            float gx = M.Clamp((x + Half) / Step, 0, GridSize - 1.001f);
            float gz = M.Clamp((z + Half) / Step, 0, GridSize - 1.001f);
            int ix = (int)gx;
            int iz = (int)gz;
            float fx = gx - ix;
            float fz = gz - iz;
            float[] h = Heights;
            int i = iz * GridSize + ix;
            return (h[i] * (1 - fx) + h[i + 1] * fx) * (1 - fz) + (h[i + GridSize] * (1 - fx) + h[i + GridSize + 1] * fx) * fz;
        }

        // Where something at height y would land: the ground, or a plinth it is above.
        public float Surface(float x, float z, float y)
        {
            float top = HeightAt(x, z);
            for (int k = 0; k < Colliders.Count; k++)
            {
                Collider o = Colliders[k];
                float dx = x - o.X;
                float dz = z - o.Z;
                if (dx * dx + dz * dz < o.R * o.R && o.Top <= y + 0.7f && o.Top > top) top = o.Top;
            }
            return top;
        }

        // The height the camera must stay above: the ground, or the top of a low plinth. Tall pillars don't count.
        public float CameraFloor(float x, float z)
        {
            float top = HeightAt(x, z);
            for (int k = 0; k < Colliders.Count; k++)
            {
                Collider o = Colliders[k];
                float dx = x - o.X;
                float dz = z - o.Z;
                if (!o.Tall && o.Top > top && dx * dx + dz * dz < (o.R + 0.5f) * (o.R + 0.5f)) top = o.Top;
            }
            return top;
        }

        public bool InBounds(float x, float z, float margin)
        {
            float r = ArenaRadius - margin;
            return x * x + z * z < r * r;
        }

        // Slide a circle out of any collider it overlaps and back inside the arena.
        public void PushOut(ref float x, ref float z, float radius, float y = float.NegativeInfinity)
        {
            for (int k = 0; k < Colliders.Count; k++)
            {
                Collider o = Colliders[k];
                if (y >= o.Top - 0.7f) continue;
                float dx = x - o.X;
                float dz = z - o.Z;
                float min = o.R + radius;
                float d2 = dx * dx + dz * dz;
                if (d2 < min * min)
                {
                    float d = M.Sqrt(d2);
                    if (d < 0.001f) d = 0.001f;
                    x = o.X + dx / d * min;
                    z = o.Z + dz / d * min;
                }
            }
            float r = M.Hypot(x, z);
            if (r > ArenaRadius)
            {
                x *= ArenaRadius / r;
                z *= ArenaRadius / r;
            }
        }

        public bool BlocksShot(float x, float y, float z)
        {
            for (int k = 0; k < Colliders.Count; k++)
            {
                Collider o = Colliders[k];
                float dx = x - o.X;
                float dz = z - o.Z;
                if (y < o.Top && dx * dx + dz * dz < o.R * o.R) return true;
            }
            return y < HeightAt(x, z);
        }
    }
}
