using System.Collections.Generic;

namespace Swarmfall
{
    // Struct-of-arrays particle pool. Renderer.cs draws these as square billboards, which suits the pixel look.
    sealed class ParticleLayer
    {
        public readonly int Capacity;
        public int Count;
        public readonly float[] X, Y, Z, Vx, Vy, Vz, R, G, B, Size, Alpha, Life, MaxLife, S0, S1, Grav, Drag;
        readonly float[][] all;

        public ParticleLayer(int capacity)
        {
            Capacity = capacity;
            X = new float[capacity]; Y = new float[capacity]; Z = new float[capacity];
            Vx = new float[capacity]; Vy = new float[capacity]; Vz = new float[capacity];
            R = new float[capacity]; G = new float[capacity]; B = new float[capacity];
            Size = new float[capacity]; Alpha = new float[capacity];
            Life = new float[capacity]; MaxLife = new float[capacity];
            S0 = new float[capacity]; S1 = new float[capacity];
            Grav = new float[capacity]; Drag = new float[capacity];
            all = new[] { X, Y, Z, Vx, Vy, Vz, R, G, B, Size, Alpha, Life, MaxLife, S0, S1, Grav, Drag };
        }

        public void Spawn(float x, float y, float z, float vx, float vy, float vz, float life, float s0, float s1, uint color, float gravity, float drag)
        {
            int i = Count;
            if (i >= Capacity) i = M.RangeInt(0, Capacity);
            else Count++;
            var c = new Col(color);
            X[i] = x; Y[i] = y; Z[i] = z;
            Vx[i] = vx; Vy[i] = vy; Vz[i] = vz;
            R[i] = c.R; G[i] = c.G; B[i] = c.B;
            Life[i] = MaxLife[i] = life;
            S0[i] = s0; S1[i] = s1;
            Grav[i] = gravity; Drag[i] = drag;
            Size[i] = s0; Alpha[i] = 1;
        }

        public void Update(float dt)
        {
            int i = 0;
            while (i < Count)
            {
                Life[i] -= dt;
                if (Life[i] <= 0)
                {
                    int last = --Count;
                    foreach (float[] arr in all) arr[i] = arr[last];
                    continue;
                }
                float damp = M.Exp(-Drag[i] * dt);
                Vx[i] *= damp;
                Vy[i] = Vy[i] * damp - Grav[i] * dt;
                Vz[i] *= damp;
                X[i] += Vx[i] * dt;
                Y[i] += Vy[i] * dt;
                Z[i] += Vz[i] * dt;
                float t = Life[i] / MaxLife[i];
                Size[i] = S1[i] + (S0[i] - S1[i]) * t;
                Alpha[i] = M.Min(1, t * 2.5f);
                i++;
            }
        }
    }

    sealed class Ring
    {
        public float X, Y, Z, R0, R1, Life, MaxLife, Thick, Fill, Radius, Alpha, FillAlpha;
        public Col Color;
    }

    sealed class Beam
    {
        public float Ax, Ay, Az, Bx, By, Bz, Width, Life, MaxLife;
        public Col Color;
    }

    sealed class Vfx
    {
        public readonly ParticleLayer Add = new ParticleLayer(3500);
        public readonly ParticleLayer Norm = new ParticleLayer(2500);
        public readonly List<Ring> Rings = new List<Ring>();
        public readonly List<Beam> Beams = new List<Beam>();
        const int MaxRings = 160;
        const int MaxBeams = 48;

        public void Burst(float x, float y, float z, uint color, float count, float speed, float size = 0.3f, float life = 0.5f, float gravity = 6, bool glow = true)
        {
            ParticleLayer layer = glow ? Add : Norm;
            for (int i = 0; i < count; i++)
            {
                float a = M.Range(0, M.Tau);
                float e = M.Range(-0.2f, 1);
                float v = speed * M.Range(0.4f, 1);
                layer.Spawn(x, y, z, M.Cos(a) * v * M.Cos(e), M.Sin(e) * v + 1, M.Sin(a) * v * M.Cos(e), life * M.Range(0.6f, 1.2f), size * M.Range(0.6f, 1.3f), 0, color, gravity, 2.5f);
            }
        }

        public void Dust(float x, float y, float z, uint color, int count, float speed)
        {
            for (int i = 0; i < count; i++)
            {
                float a = M.Range(0, M.Tau);
                float v = speed * M.Range(0.3f, 1);
                Norm.Spawn(x, y + 0.1f, z, M.Cos(a) * v, M.Range(0.3f, 1.2f), M.Sin(a) * v, M.Range(0.3f, 0.6f), M.Range(0.25f, 0.5f), 0.7f, color, 0, 3);
            }
        }

        public void Trail(float x, float y, float z, uint color, float size, float life)
        {
            Add.Spawn(x, y, z, 0, 0, 0, life, size, 0, color, 0, 0);
        }

        void AddRing(float x, float y, float z, float r0, float r1, uint color, float life, float thick, float fill)
        {
            if (Rings.Count >= MaxRings) Rings.RemoveAt(0);
            Rings.Add(new Ring { X = x, Y = y, Z = z, R0 = r0, R1 = r1, Color = new Col(color), Life = life, MaxLife = life, Thick = thick, Fill = fill, Radius = r0, Alpha = 1 });
        }

        public void RingFx(float x, float y, float z, float r, uint color, float life = 0.35f, float thick = 0.16f)
        {
            AddRing(x, y, z, r * 0.15f, r, color, life, thick, 0);
        }

        // A filled zone that grows in opacity, used to warn of incoming attacks.
        public void Telegraph(float x, float y, float z, float r, uint color, float life)
        {
            AddRing(x, y, z, r, r, color, life, 0.05f, 0.28f);
        }

        public void BeamFx(float ax, float ay, float az, float bx, float by, float bz, uint color, float width = 0.3f, float life = 0.14f)
        {
            if (Beams.Count >= MaxBeams) Beams.RemoveAt(0);
            Beams.Add(new Beam { Ax = ax, Ay = ay, Az = az, Bx = bx, By = by, Bz = bz, Color = new Col(color), Width = width, Life = life, MaxLife = life });
        }

        public void Update(float dt)
        {
            Add.Update(dt);
            Norm.Update(dt);
            int n = 0;
            for (int k = 0; k < Rings.Count; k++)
            {
                Ring it = Rings[k];
                it.Life -= dt;
                if (it.Life <= 0) continue;
                float t = 1 - it.Life / it.MaxLife;
                it.Radius = it.R0 + (it.R1 - it.R0) * (1 - (1 - t) * (1 - t));
                it.Alpha = it.Fill > 0 ? 0.85f : 1 - t;
                it.FillAlpha = it.Fill > 0 ? it.Fill * (0.4f + t * 0.6f) : 0;
                Rings[n++] = it;
            }
            Rings.RemoveRange(n, Rings.Count - n);
            n = 0;
            for (int k = 0; k < Beams.Count; k++)
            {
                Beam b = Beams[k];
                b.Life -= dt;
                if (b.Life > 0) Beams[n++] = b;
            }
            Beams.RemoveRange(n, Beams.Count - n);
        }
    }
}
