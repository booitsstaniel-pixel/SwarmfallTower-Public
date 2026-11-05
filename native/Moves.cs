using System;
using System.Collections.Generic;

namespace Swarmfall
{
    sealed class MoveMods
    {
        public float Dmg = 1, Cd = 1, Area = 1, Speed = 1, Dur = 1, Crit;
        public int Count, Pierce;
    }

    sealed class Move
    {
        public string Id;
        public MoveDef Def;
        public int Level = 1;
        public float CdLeft = 0.3f, MaxCd, T, Tick;
        public MoveMods Mods = new MoveMods();
        public Move[] Parts;

        public Move(string id, MoveDef def, MoveMods mods = null)
        {
            Id = id;
            Def = def;
            MaxCd = def.Cd;
            if (mods != null) Mods = mods;
        }
    }

    sealed class FusionOption
    {
        public Move A, B;
        public MoveDef Def;
    }

    sealed class Zone
    {
        public float X, Y, Z, R, Left, Tick, Every, Dmg, Pull;
        public string Elem;
        public uint Color;
    }

    sealed class Target
    {
        public int I;
        public float Nx, Nz, D, Y;
    }

    // One method per kind of move. Each fires once and returns false to retry soon (no target yet).
    static class Moves
    {
        public const int FuseLevel = 8;
        const int FusionMaxLevel = 5;

        public static int Cap(Player player, Move m)
        {
            return m.Def.Kind == Kind.Fusion ? FusionMaxLevel : FuseLevel + (int)player.Stats.MoveCap;
        }

        static MoveDef FusionFor(Move a, Move b, string partnerId)
        {
            SignatureFusion sig;
            if (Data.Signature.TryGetValue(partnerId, out sig) && (a.Id == sig.A || a.Id == sig.B) && (b.Id == sig.A || b.Id == sig.B)) return sig.Def;
            string ea = a.Def.Elem, eb = b.Def.Elem;
            string key = string.CompareOrdinal(ea, eb) < 0 ? ea + "+" + eb : eb + "+" + ea;
            MoveDef reaction;
            if (Data.ReactionFusions.TryGetValue(key, out reaction)) return reaction;
            if (ea == eb) return Data.TypeFusions[ea];
            return Data.GlitchFusion;
        }

        // Every pair of max-level moves the player could fuse right now.
        public static List<FusionOption> FusionOptions(Player player)
        {
            var ready = new List<Move>();
            foreach (Move m in player.Moves) if (m.Def.Kind != Kind.Fusion && m.Level >= FuseLevel) ready.Add(m);
            var options = new List<FusionOption>();
            for (int i = 0; i < ready.Count; i++)
            {
                for (int j = i + 1; j < ready.Count; j++) options.Add(new FusionOption { A = ready[i], B = ready[j], Def = FusionFor(ready[i], ready[j], player.Partner.Id) });
            }
            return options;
        }

        public static void Fuse(Player player, FusionOption o)
        {
            // The fusion inherits part of what you invested in both moves.
            var mods = new MoveMods
            {
                Dmg = M.Sqrt(o.A.Mods.Dmg * o.B.Mods.Dmg),
                Cd = M.Sqrt(o.A.Mods.Cd * o.B.Mods.Cd),
                Area = M.Sqrt(o.A.Mods.Area * o.B.Mods.Area),
            };
            player.Moves.Remove(o.A);
            player.Moves.Remove(o.B);
            player.Moves.Add(new Move(o.Def.Id, o.Def, mods));
            player.Fused.Add(o.A.Id);
            player.Fused.Add(o.B.Id);
        }

        static float DamageOf(Run run, Move m)
        {
            Player p = run.Player;
            float bonus;
            p.ElemBonus.TryGetValue(m.Def.Elem, out bonus);
            return m.Def.Dmg * (1 + 0.143f * (m.Level - 1)) * m.Mods.Dmg * p.Stats.Might * (1 + bonus);
        }
        static int CountOf(Run run, Move m) { return m.Def.Count + m.Mods.Count + (int)run.Player.Stats.ExtraShots; }
        static float RadiusOf(Run run, Move m) { return m.Def.Radius * m.Mods.Area * run.Player.Stats.Area; }
        static uint ColorOf(Move m) { return Data.ElemColor(m.Def.Elem); }

        static Target Aim(Run run, string elem, float range)
        {
            Player p = run.Player;
            Enemies e = run.Enemies;
            int i = e.Nearest(p.X, p.Z, range, elem);
            if (i < 0) return null;
            float dx = e.X[i] - p.X;
            float dz = e.Z[i] - p.Z;
            float d = M.Hypot(dx, dz);
            if (d < 0.001f) d = 0.001f;
            return new Target { I = i, Nx = dx / d, Nz = dz / d, D = d, Y = e.Y[i] + e.Hgt[i] * 0.55f };
        }

        static Shot ShootAt(Run run, Move m, float angleOffset, Target t)
        {
            Player p = run.Player;
            Shot s = run.Projectiles.Spawn();
            if (s == null) return null;
            float a = M.Atan2(t.Nz, t.Nx) + angleOffset;
            float speed = m.Def.Speed * m.Mods.Speed;
            float startY = p.Y + 1;
            float flight = M.Max(0.2f, t.D / speed);
            s.X = p.X; s.Y = startY; s.Z = p.Z;
            s.Vx = M.Cos(a) * speed;
            s.Vy = (t.Y - startY) / flight;
            s.Vz = M.Sin(a) * speed;
            s.Life = m.Def.Range / speed * 1.25f;
            s.Dmg = DamageOf(run, m);
            s.Elem = m.Def.Elem;
            s.Color = ColorOf(m);
            s.Pierce = m.Def.Pierce + m.Mods.Pierce;
            s.St = m.Def.St;
            s.CritBonus = m.Mods.Crit;
            return s;
        }

        static bool Fire(Run run, Move m)
        {
            switch (m.Def.Kind)
            {
                case Kind.Fusion: return FireFusion(run, m);
                case Kind.Bolt: return FireBolt(run, m);
                case Kind.Barrage: return FireBarrage(run, m);
                case Kind.Boomerang: return FireBoomerang(run, m);
                case Kind.Nova: return FireNova(run, m);
                case Kind.Cone: return FireCone(run, m);
                case Kind.Beam: return FireBeam(run, m);
                case Kind.Lob: return FireLob(run, m);
                case Kind.Smite: return FireSmite(run, m);
                case Kind.Vortex: return FireVortex(run, m);
                default: return false;
            }
        }

        // A fusion fires each of its parts as a small move that shares the parent's level and upgrades.
        static bool FireFusion(Run run, Move m)
        {
            MoveDef[] parts = m.Def.Parts;
            if (m.Def.Glitch)
            {
                var copies = new List<MoveDef>(Data.TypeFusions.Values);
                parts = M.Pick(copies).Parts;
                m.Parts = null;
            }
            if (m.Parts == null)
            {
                m.Parts = new Move[parts.Length];
                for (int i = 0; i < parts.Length; i++) m.Parts[i] = new Move(m.Id, parts[i], m.Mods);
            }
            bool fired = false;
            foreach (Move part in m.Parts)
            {
                part.Level = m.Level;
                if (Fire(run, part)) fired = true;
            }
            return fired;
        }

        static bool FireBolt(Run run, Move m)
        {
            Target t = Aim(run, m.Def.Elem, m.Def.Range);
            if (t == null) return false;
            int n = CountOf(run, m);
            float step = m.Def.SpreadAll ? M.Tau / n : 0.16f;
            for (int k = 0; k < n; k++) ShootAt(run, m, (k - (n - 1) / 2f) * step, t);
            run.Audio.Shoot();
            return true;
        }

        static bool FireBarrage(Run run, Move m)
        {
            Target first = Aim(run, m.Def.Elem, m.Def.Range);
            if (first == null) return false;
            int n = CountOf(run, m);
            for (int k = 0; k < n; k++)
            {
                run.After(k * 0.07f, () =>
                {
                    Target again = Aim(run, m.Def.Elem, m.Def.Range) ?? first;
                    ShootAt(run, m, M.Range(-m.Def.Spread, m.Def.Spread), again);
                    run.Audio.Shoot();
                });
            }
            return true;
        }

        static bool FireBoomerang(Run run, Move m)
        {
            Target t = Aim(run, m.Def.Elem, m.Def.Range);
            if (t == null) return false;
            int n = CountOf(run, m);
            for (int k = 0; k < n; k++)
            {
                Shot s = ShootAt(run, m, (k - (n - 1) / 2f) * 0.45f, t);
                if (s == null) continue;
                s.Returns = true;
                s.BackAt = m.Def.Range / (m.Def.Speed * m.Mods.Speed) * 0.7f;
                s.Life = 3.4f;
                s.Vy = 0;
                s.Radius = 0.85f;
                s.Size = 1.1f;
            }
            run.Audio.Whoosh();
            return true;
        }

        static bool FireNova(Run run, Move m)
        {
            float r = RadiusOf(run, m);
            if (Aim(run, m.Def.Elem, r + 1) == null) return false;
            Player p = run.Player;
            run.Blast(p.X, p.Y, p.Z, r, DamageOf(run, m), m.Def.Elem, ColorOf(m), m.Def.Knock > 0 ? m.Def.Knock : 3, m.Def.St, m.Mods.Crit);
            return true;
        }

        static bool FireCone(Run run, Move m)
        {
            Target t = Aim(run, m.Def.Elem, m.Def.Range);
            if (t == null) return false;
            Enemies e = run.Enemies;
            Player p = run.Player;
            float range = m.Def.Range * m.Mods.Area * p.Stats.Area;
            float cosArc = M.Cos(m.Def.Arc / 2);
            float dmg = DamageOf(run, m);
            e.Query(p.X, p.Z, range, i =>
            {
                float dx = e.X[i] - p.X;
                float dz = e.Z[i] - p.Z;
                float d = M.Hypot(dx, dz);
                if (d < 0.001f) d = 0.001f;
                if ((dx * t.Nx + dz * t.Nz) / d >= cosArc) run.DamageEnemy(i, dmg, m.Def.Elem, HitOpts.From(m.Def.St, dx / d * 4, dz / d * 4, m.Mods.Crit));
            });
            float baseA = M.Atan2(t.Nz, t.Nx);
            for (int k = 0; k < 36; k++)
            {
                float a = baseA + M.Range(-m.Def.Arc / 2, m.Def.Arc / 2);
                float s = M.Range(0.3f, 1) * range * 2.2f;
                run.Vfx.Add.Spawn(p.X, p.Y + 1, p.Z, M.Cos(a) * s, M.Range(-0.3f, 1), M.Sin(a) * s, 0.42f, 0.5f, 0, ColorOf(m), 0, 1);
            }
            run.Audio.Whoosh();
            return true;
        }

        static bool FireBeam(Run run, Move m)
        {
            Target t = Aim(run, m.Def.Elem, m.Def.Range);
            if (t == null) return false;
            Enemies e = run.Enemies;
            Player p = run.Player;
            float len = m.Def.Range * m.Mods.Area * p.Stats.Area;
            float width = m.Def.Width * m.Mods.Area * p.Stats.Area;
            float dmg = DamageOf(run, m);
            e.Query(p.X + t.Nx * len / 2, p.Z + t.Nz * len / 2, len / 2 + width, i =>
            {
                float dx = e.X[i] - p.X;
                float dz = e.Z[i] - p.Z;
                float along = dx * t.Nx + dz * t.Nz;
                float side = M.Abs(dx * t.Nz - dz * t.Nx);
                if (along > 0 && along < len && side < width + e.Radius[i]) run.DamageEnemy(i, dmg, m.Def.Elem, HitOpts.From(m.Def.St, t.Nx * 2, t.Nz * 2, m.Mods.Crit));
            });
            run.Vfx.BeamFx(p.X, p.Y + 1, p.Z, p.X + t.Nx * len, p.Y + 1, p.Z + t.Nz * len, ColorOf(m), width * 0.55f, 0.18f);
            run.Audio.Zap();
            return true;
        }

        static bool FireLob(Run run, Move m)
        {
            Target t = Aim(run, m.Def.Elem, m.Def.Range);
            if (t == null) return false;
            Enemies e = run.Enemies;
            Player p = run.Player;
            int n = CountOf(run, m);
            const float flight = 0.75f;
            const float gravity = 22;
            float r = RadiusOf(run, m);
            for (int k = 0; k < n; k++)
            {
                float tx = e.X[t.I] + (k > 0 ? M.Range(-3, 3) : 0);
                float tz = e.Z[t.I] + (k > 0 ? M.Range(-3, 3) : 0);
                float groundY = run.Terrain.HeightAt(tx, tz);
                Shot s = run.Projectiles.Spawn();
                if (s == null) break;
                s.X = p.X; s.Y = p.Y + 1; s.Z = p.Z;
                s.Vx = (tx - p.X) / flight;
                s.Vy = (groundY - (p.Y + 1) + 0.5f * gravity * flight * flight) / flight;
                s.Vz = (tz - p.Z) / flight;
                s.Gravity = gravity;
                s.Life = flight + 0.5f;
                s.Dmg = DamageOf(run, m);
                s.Elem = m.Def.Elem;
                s.Color = ColorOf(m);
                s.Blast = r;
                s.Knock = m.Def.Knock;
                s.St = m.Def.St;
                s.CritBonus = m.Mods.Crit;
                s.Size = 1.2f;
                run.Vfx.Telegraph(tx, groundY, tz, r, ColorOf(m), flight);
            }
            return true;
        }

        static bool FireSmite(Run run, Move m)
        {
            if (Aim(run, m.Def.Elem, m.Def.Range) == null) return false;
            Enemies e = run.Enemies;
            Player p = run.Player;
            var candidates = new List<int>();
            e.Query(p.X, p.Z, m.Def.Range, i => candidates.Add(i));
            int n = CountOf(run, m);
            float r = RadiusOf(run, m);
            float dmg = DamageOf(run, m);
            uint color = ColorOf(m);
            for (int k = 0; k < n && candidates.Count > 0; k++)
            {
                int pick = M.RangeInt(0, candidates.Count);
                int i = candidates[pick];
                candidates.RemoveAt(pick);
                float x = e.X[i], z = e.Z[i];
                float y = run.Terrain.HeightAt(x, z);
                run.Vfx.Telegraph(x, y, z, r, color, 0.35f);
                run.After(0.35f, () =>
                {
                    run.Vfx.BeamFx(x, y + 26, z, x, y, z, color, 0.8f, 0.2f);
                    run.Blast(x, y, z, r, dmg, m.Def.Elem, color, 2, m.Def.St, m.Mods.Crit);
                });
            }
            return true;
        }

        static bool FireVortex(Run run, Move m)
        {
            Target t = Aim(run, m.Def.Elem, m.Def.Range);
            if (t == null) return false;
            Enemies e = run.Enemies;
            float x = e.X[t.I], z = e.Z[t.I];
            float y = run.Terrain.HeightAt(x, z);
            float r = RadiusOf(run, m);
            run.Zones.Add(new Zone { X = x, Z = z, Y = y, R = r, Left = m.Def.Dur * m.Mods.Dur * run.Player.Stats.Duration, Every = 0.35f, Dmg = DamageOf(run, m), Elem = m.Def.Elem, Color = ColorOf(m), Pull = 11 });
            run.Vfx.RingFx(x, y, z, r, ColorOf(m), 0.4f);
            return true;
        }

        // Orbit and aura run continuously around the player instead of firing shots.
        static void SustainOrbit(Run run, Move m, float dt)
        {
            Player p = run.Player;
            Enemies e = run.Enemies;
            int n = CountOf(run, m);
            float r = RadiusOf(run, m);
            m.T += dt * m.Def.Spin;
            m.Tick -= dt;
            bool ready = m.Tick <= 0;
            if (ready) m.Tick = m.Def.Cd * m.Mods.Cd / p.Stats.Haste;
            float dmg = DamageOf(run, m);
            for (int k = 0; k < n; k++)
            {
                float a = m.T + (float)k / n * M.Tau;
                float x = p.X + M.Cos(a) * r;
                float z = p.Z + M.Sin(a) * r;
                run.Vfx.Trail(x, p.Y + 1, z, ColorOf(m), 0.8f, 0.14f);
                if (!ready) continue;
                e.Query(x, z, 0.9f, i =>
                {
                    float dx = e.X[i] - p.X;
                    float dz = e.Z[i] - p.Z;
                    float d = M.Hypot(dx, dz);
                    if (d < 0.001f) d = 0.001f;
                    run.DamageEnemy(i, dmg, m.Def.Elem, HitOpts.From(m.Def.St, dx / d * 5, dz / d * 5, m.Mods.Crit));
                });
            }
        }

        static void SustainAura(Run run, Move m, float dt)
        {
            Player p = run.Player;
            Enemies e = run.Enemies;
            float r = RadiusOf(run, m);
            m.Tick -= dt;
            if (M.Rand() < dt * 30)
            {
                float a = M.Range(0, M.Tau);
                float d = M.Sqrt(M.Rand()) * r;
                run.Vfx.Norm.Spawn(p.X + M.Cos(a) * d, p.Y + 0.3f, p.Z + M.Sin(a) * d, 0, M.Range(0.3f, 1), 0, 0.8f, 0.5f, 0.1f, ColorOf(m), 0, 0);
            }
            if (m.Tick > 0) return;
            m.Tick = m.Def.Cd * m.Mods.Cd / p.Stats.Haste;
            run.Vfx.RingFx(p.X, p.Y, p.Z, r, ColorOf(m), m.Def.Cd, 0.04f);
            float dmg = DamageOf(run, m);
            e.Query(p.X, p.Z, r, i => run.DamageEnemy(i, dmg, m.Def.Elem, HitOpts.From(m.Def.St, 0, 0, m.Mods.Crit)));
        }

        public static void Update(Run run, float dt)
        {
            Stats stats = run.Player.Stats;
            foreach (Move m in run.Player.Moves)
            {
                if (m.Def.Kind == Kind.Orbit)
                {
                    SustainOrbit(run, m, dt);
                    continue;
                }
                if (m.Def.Kind == Kind.Aura)
                {
                    SustainAura(run, m, dt);
                    continue;
                }
                m.CdLeft -= dt;
                if (m.CdLeft > 0) continue;
                bool fired = Fire(run, m);
                m.MaxCd = m.Def.Cd * m.Mods.Cd / stats.Haste;
                m.CdLeft = fired ? m.MaxCd : 0.12f;
            }
        }

        public static void UpdateZones(Run run, float dt)
        {
            Enemies e = run.Enemies;
            int n = 0;
            for (int k = 0; k < run.Zones.Count; k++)
            {
                Zone z = run.Zones[k];
                z.Left -= dt;
                if (z.Left <= 0) continue;
                z.Tick -= dt;
                bool doTick = z.Tick <= 0;
                if (doTick) z.Tick = z.Every;
                e.Query(z.X, z.Z, z.R, i =>
                {
                    float dx = z.X - e.X[i];
                    float dz = z.Z - e.Z[i];
                    float d = M.Hypot(dx, dz);
                    if (d < 0.001f) d = 0.001f;
                    if ((e.Flags[i] & Flag.Boss) == 0)
                    {
                        float pull = M.Min(d, z.Pull * dt);
                        e.X[i] += dx / d * pull;
                        e.Z[i] += dz / d * pull;
                    }
                    if (doTick) run.DamageEnemy(i, z.Dmg, z.Elem, default(HitOpts));
                });
                float swirl = z.Left * 7;
                for (int s = 0; s < 3; s++)
                {
                    float a = swirl + s / 3f * M.Tau;
                    run.Vfx.Trail(z.X + M.Cos(a) * z.R * 0.8f, z.Y + 0.3f, z.Z + M.Sin(a) * z.R * 0.8f, z.Color, 0.6f, 0.3f);
                }
                run.Zones[n++] = z;
            }
            run.Zones.RemoveRange(n, run.Zones.Count - n);
        }
    }

    sealed class Card
    {
        public string Key, Title, Tag, Desc, Tier;
        public uint Color;
        public bool Disabled;
        public Action Run;
    }

    sealed class Choice
    {
        public string Title, Sub, CloseLabel;
        public List<Card> Cards;
        public bool Tools;
        public Action OnClose;
    }

    static class Upgrades
    {
        const int VitaminMax = 8;

        sealed class ModDef
        {
            public float Low, High;
            public bool Whole;
            public Func<float, string> Label;
            public Action<MoveMods, float> Apply;
        }

        static string Pct(float v) { return Math.Round(v * 100) + "%"; }

        // Rolled strength runs from common to legendary.
        static readonly Dictionary<string, ModDef> Mods = new Dictionary<string, ModDef>
        {
            { "dmg", new ModDef { Low = 0.22f, High = 0.46f, Label = v => "+" + Pct(v) + " damage", Apply = (m, v) => m.Dmg *= 1 + v } },
            { "cd", new ModDef { Low = 0.08f, High = 0.17f, Label = v => "-" + Pct(v) + " cooldown", Apply = (m, v) => m.Cd *= 1 - v } },
            { "area", new ModDef { Low = 0.15f, High = 0.32f, Label = v => "+" + Pct(v) + " area", Apply = (m, v) => m.Area *= 1 + v } },
            { "count", new ModDef { Low = 1, High = 2, Whole = true, Label = v => "+" + v + (v > 1 ? " projectiles" : " projectile"), Apply = (m, v) => m.Count += (int)v } },
            { "pierce", new ModDef { Low = 1, High = 2, Whole = true, Label = v => "+" + v + " pierce", Apply = (m, v) => m.Pierce += (int)v } },
            { "speed", new ModDef { Low = 0.15f, High = 0.32f, Label = v => "+" + Pct(v) + " speed", Apply = (m, v) => m.Speed *= 1 + v } },
            { "crit", new ModDef { Low = 0.05f, High = 0.11f, Label = v => "+" + Pct(v) + " crit", Apply = (m, v) => m.Crit += v } },
            { "dur", new ModDef { Low = 0.2f, High = 0.42f, Label = v => "+" + Pct(v) + " duration", Apply = (m, v) => m.Dur *= 1 + v } },
        };

        // Which random upgrades each kind of move can roll when it levels up.
        static string[] PoolOf(Kind kind)
        {
            switch (kind)
            {
                case Kind.Bolt: return new[] { "dmg", "cd", "count", "pierce", "speed", "crit" };
                case Kind.Barrage: return new[] { "dmg", "cd", "count", "speed", "pierce", "crit" };
                case Kind.Boomerang: return new[] { "dmg", "cd", "count", "speed", "crit" };
                case Kind.Smite: return new[] { "dmg", "cd", "area", "count", "crit" };
                case Kind.Lob: return new[] { "dmg", "cd", "area", "count" };
                case Kind.Vortex: return new[] { "dmg", "cd", "area", "dur" };
                case Kind.Orbit: return new[] { "dmg", "cd", "area", "count" };
                case Kind.Aura: return new[] { "dmg", "cd", "area" };
                case Kind.Fusion: return new[] { "dmg", "cd", "area", "count" };
                default: return new[] { "dmg", "cd", "area", "crit" };
            }
        }

        static Rarity RollRarity(float luck)
        {
            return M.Weighted(Data.Rarities, r => r.Id == "common" ? r.Weight : r.Weight * (1 + luck));
        }

        static Card MoveCard(Player player, Move m)
        {
            Rarity rarity = RollRarity(player.Stats.Luck);
            int index = Array.IndexOf(Data.Rarities, rarity);
            float t = index / (float)(Data.Rarities.Length - 1);
            var pool = new List<string>(PoolOf(m.Def.Kind));
            int count = index >= 2 ? 3 : 2;
            var keys = new List<string>();
            var values = new List<float>();
            var texts = new List<string>();
            while (keys.Count < count && pool.Count > 0)
            {
                int at = M.RangeInt(0, pool.Count);
                string key = pool[at];
                pool.RemoveAt(at);
                ModDef def = Mods[key];
                float value = def.Whole ? (index >= 3 ? def.High : def.Low) : M.Lerp(def.Low, def.High, t);
                keys.Add(key);
                values.Add(value);
                texts.Add(def.Label(value));
            }
            string mark = m.Def.Kind == Kind.Fusion ? "Star " : "Lv ";
            return new Card
            {
                Key = "level:" + m.Id,
                Title = m.Def.Name,
                Tag = rarity.Label + " - " + mark + m.Level + " to " + (m.Level + 1),
                Color = rarity.Color,
                Desc = string.Join(", ", texts),
                Run = () =>
                {
                    m.Level++;
                    for (int i = 0; i < keys.Count; i++) Mods[keys[i]].Apply(m.Mods, values[i]);
                },
            };
        }

        static Card NewMoveCard(Player player, string id)
        {
            MoveDef def = Data.Move(id);
            return new Card
            {
                Key = "new:" + id,
                Title = def.Name,
                Tag = "New move - " + Data.Elements[def.Elem].Name,
                Color = Data.ElemColor(def.Elem),
                Desc = def.Desc,
                Run = () => player.Moves.Add(new Move(id, def)),
            };
        }

        static string AmountText(PassiveDef def, float amount)
        {
            if (def.Unit == "%") return "+" + Pct(amount);
            return "+" + Math.Round(amount * 10) / 10 + def.Unit;
        }

        static Card VitaminCard(Player player, PassiveDef def)
        {
            Rarity rarity = RollRarity(player.Stats.Luck);
            float amount = def.Value * rarity.Mult;
            int level;
            player.Picks.TryGetValue(def.Id, out level);
            return new Card
            {
                Key = "vitamin:" + def.Id,
                Title = def.Name,
                Tag = rarity.Label + " - " + (level > 0 ? "Vitamin " + level + " to " + (level + 1) : "New vitamin"),
                Color = rarity.Color,
                Desc = AmountText(def, amount) + " " + def.Desc,
                Run = () =>
                {
                    player.Picks[def.Id] = level + 1;
                    player.AddStat(def.Stat, def.Mul, amount);
                },
            };
        }

        // Different offers each time: move upgrades, new moves and vitamins.
        public static List<Card> RollLevelCards(Player player)
        {
            int count = player.Stats.CardOptions;
            var banned = player.Banished;
            var upgradable = new List<Move>();
            foreach (Move m in player.Moves) if (m.Level < Moves.Cap(player, m) && !banned.Contains("level:" + m.Id)) upgradable.Add(m);
            var learnable = new List<string>();
            if (player.Moves.Count < player.Stats.MoveSlots)
            {
                foreach (MoveDef d in Data.MoveList)
                {
                    if (player.Fused.Contains(d.Id) || banned.Contains("new:" + d.Id)) continue;
                    if (player.Moves.Exists(m => m.Id == d.Id)) continue;
                    learnable.Add(d.Id);
                }
            }
            var vitamins = new List<PassiveDef>();
            foreach (PassiveDef d in Data.Passives)
            {
                if (banned.Contains("vitamin:" + d.Id)) continue;
                int level;
                bool owned = player.Picks.TryGetValue(d.Id, out level);
                if (owned ? level < VitaminMax : player.Picks.Count < player.Stats.VitaminSlots) vitamins.Add(d);
            }

            var cards = new List<Card>();
            var used = new HashSet<string>();
            int guard = 0;
            while (cards.Count < count && guard++ < 80)
            {
                float roll = M.Rand();
                Card card = null;
                if (learnable.Count > 0 && (roll < 0.26f || (player.Moves.Count < 2 && cards.Count == 0))) card = NewMoveCard(player, M.Pick(learnable));
                else if (upgradable.Count > 0 && roll < 0.72f) card = MoveCard(player, M.Pick(upgradable));
                else if (vitamins.Count > 0) card = VitaminCard(player, M.Pick(vitamins));
                else if (upgradable.Count > 0) card = MoveCard(player, M.Pick(upgradable));
                else if (learnable.Count > 0) card = NewMoveCard(player, M.Pick(learnable));
                if (card == null || !used.Add(card.Key)) continue;
                cards.Add(card);
            }
            return cards;
        }

        // minTier: 0 common or better, 1 rare or better, 2 epic or better.
        public static List<Card> RollItemCards(Player player, int count, int minTier)
        {
            var available = new List<ItemDef>();
            foreach (ItemDef d in Data.Items)
            {
                if (player.ItemCount(d.Id) < d.Max && Data.TierIndex(d.Tier) >= minTier) available.Add(d);
            }
            var cards = new List<Card>();
            var used = new HashSet<string>();
            int guard = 0;
            while (cards.Count < count && guard++ < 80 && available.Count > 0)
            {
                Rarity rarity = RollRarity(player.Stats.Luck);
                int wanted = Math.Max(Array.IndexOf(Data.Rarities, rarity), minTier);
                List<ItemDef> inTier = available.FindAll(d => Data.TierIndex(d.Tier) == wanted);
                ItemDef def = M.Pick(inTier.Count > 0 ? inTier : available);
                if (!used.Add(def.Id)) continue;
                int held = player.ItemCount(def.Id);
                Rarity r = Data.Rarities[Data.TierIndex(def.Tier)];
                cards.Add(new Card
                {
                    Key = "item:" + def.Id,
                    Title = def.Name,
                    Tier = def.Tier,
                    Tag = r.Label + " item" + (held > 0 ? " - " + held + " of " + def.Max : ""),
                    Color = r.Color,
                    Desc = def.Desc,
                    Run = () => player.AddItem(def),
                });
            }
            return cards;
        }

        public static List<Card> RollBlessings(Player player, float strength)
        {
            var pool = new List<PassiveDef>(Data.Passives);
            var cards = new List<Card>();
            for (int k = 0; k < 3; k++)
            {
                int at = M.RangeInt(0, pool.Count);
                PassiveDef def = pool[at];
                pool.RemoveAt(at);
                float amount = def.Value * 1.5f * strength;
                cards.Add(new Card
                {
                    Key = "bless:" + def.Id,
                    Title = def.Name,
                    Tag = strength > 1 ? "Ancestral blessing" : "Blessing",
                    Color = 0xffc53b,
                    Desc = AmountText(def, amount) + " " + def.Desc,
                    Run = () => player.AddStat(def.Stat, def.Mul, amount),
                });
            }
            return cards;
        }
    }
}
