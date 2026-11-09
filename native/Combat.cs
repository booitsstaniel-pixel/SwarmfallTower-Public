using System;
using System.Collections.Generic;

namespace Swarmfall
{
    sealed class Shot
    {
        public float X, Y, Z, Vx, Vy, Vz, Life, Radius = 0.45f, Dmg, Gravity, Blast, BackAt, T, Knock, Size = 0.8f, CritBonus;
        public int Pierce;
        // Above zero, the shot is drawn as a long pointed harpoon of this length instead of a ball.
        public float Spear;
        public string Elem;
        public uint Color;
        public bool Hostile, Returns, Turned;
        public Status St;
        public readonly HashSet<int> Hits = new HashSet<int>();
    }

    sealed class Projectiles
    {
        const int Max = 900;
        readonly Run run;
        public readonly List<Shot> Items = new List<Shot>();
        readonly Stack<Shot> free = new Stack<Shot>();

        public Projectiles(Run run) { this.run = run; }

        public void Clear()
        {
            foreach (Shot s in Items) free.Push(s);
            Items.Clear();
        }

        // Returns a clean shot from the pool, already in play. Null when the pool is full.
        public Shot Spawn()
        {
            if (Items.Count >= Max) return null;
            Shot p = free.Count > 0 ? free.Pop() : new Shot();
            p.Hits.Clear();
            p.Vy = 0;
            p.Radius = 0.45f;
            p.Pierce = 0;
            p.Spear = 0;
            p.Hostile = false;
            p.Gravity = 0;
            p.Blast = 0;
            p.Returns = false;
            p.Turned = false;
            p.T = 0;
            p.BackAt = 0;
            p.St = default(Status);
            p.Knock = 0;
            p.Size = 0.8f;
            p.CritBonus = 0;
            Items.Add(p);
            return p;
        }

        public void EnemyShot(float x, float y, float z, float nx, float nz, float speed, float dmg, uint color, string elem = null)
        {
            Shot p = Spawn();
            if (p == null) return;
            p.X = x; p.Y = y; p.Z = z;
            p.Vx = nx * speed; p.Vz = nz * speed;
            p.Life = 4;
            p.Radius = 0.5f;
            p.Dmg = dmg;
            p.Elem = elem;
            p.Color = color;
            p.Hostile = true;
            p.Size = 1;
        }

        public void Update(float dt)
        {
            Terrain terrain = run.Terrain;
            Vfx vfx = run.Vfx;
            Player player = run.Player;
            int n = 0;
            for (int k = 0; k < Items.Count; k++)
            {
                Shot p = Items[k];
                p.T += dt;
                p.Life -= dt;
                p.Vy -= p.Gravity * dt;

                if (p.Returns && p.T > p.BackAt)
                {
                    float dx = player.X - p.X;
                    float dz = player.Z - p.Z;
                    float d = M.Hypot(dx, dz);
                    if (d < 0.001f) d = 1;
                    float speed = M.Max(11, M.Hypot(p.Vx, p.Vz) + 22 * dt);
                    p.Vx = dx / d * speed;
                    p.Vz = dz / d * speed;
                    if (!p.Turned)
                    {
                        p.Turned = true;
                        p.Hits.Clear();
                    }
                    if (d < 1) p.Life = 0;
                }

                p.X += p.Vx * dt;
                p.Y += p.Vy * dt;
                p.Z += p.Vz * dt;
                if (p.Spear <= 0 && M.Rand() < 0.45f) vfx.Trail(p.X, p.Y, p.Z, p.Color, p.Size * 0.55f, 0.22f);

                bool alive = p.Life > 0;
                if (alive)
                {
                    if (p.Gravity > 0)
                    {
                        if (p.Y <= terrain.HeightAt(p.X, p.Z))
                        {
                            float y = terrain.HeightAt(p.X, p.Z);
                            run.Blast(p.X, y, p.Z, p.Blast, p.Dmg, p.Elem, p.Color, p.Knock, p.St, p.CritBonus);
                            alive = false;
                        }
                    }
                    else if (p.Returns ? p.Y < terrain.HeightAt(p.X, p.Z) - 1 : terrain.BlocksShot(p.X, p.Y, p.Z))
                    {
                        vfx.Burst(p.X, p.Y, p.Z, p.Color, 4, 3, 0.18f, 0.25f);
                        alive = false;
                    }
                }
                if (alive)
                {
                    if (p.Hostile)
                    {
                        float dx = player.X - p.X;
                        float dz = player.Z - p.Z;
                        float dy = player.Y + 0.8f - p.Y;
                        float reach = player.Radius + p.Radius;
                        if (dx * dx + dz * dz < reach * reach && M.Abs(dy) < 1.5f)
                        {
                            player.Hurt(p.Dmg, p.Elem);
                            alive = false;
                        }
                    }
                    else if (p.Gravity <= 0)
                    {
                        alive = CollideEnemies(p);
                    }
                }
                if (alive) Items[n++] = p;
                else free.Push(p);
            }
            // Boss patterns and hits can add shots during this loop; keep anything added past k.
            Items.RemoveRange(n, Items.Count - n);
        }

        bool CollideEnemies(Shot p)
        {
            bool alive = true;
            Enemies enemies = run.Enemies;
            enemies.Query(p.X, p.Z, p.Radius, i =>
            {
                if (!alive || p.Hits.Contains(enemies.Uid[i])) return;
                if (p.Y < enemies.Y[i] - 0.5f || p.Y > enemies.Y[i] + enemies.Hgt[i] + 0.6f) return;
                p.Hits.Add(enemies.Uid[i]);
                float speed = M.Hypot(p.Vx, p.Vz);
                if (speed < 0.001f) speed = 1;
                float k = p.Knock > 0 ? p.Knock : 3;
                run.DamageEnemy(i, p.Dmg, p.Elem, HitOpts.From(p.St, p.Vx / speed * k, p.Vz / speed * k, p.CritBonus));
                run.Vfx.Burst(p.X, p.Y, p.Z, p.Color, 4, 4, 0.2f, 0.25f);
                if (!p.Returns && p.Pierce-- <= 0) alive = false;
            });
            return alive;
        }
    }

    sealed class Drop
    {
        public int Kind;
        public float X, Y, Z, Vx, Vy, Vz, Value, Age;
        public bool Pulled;
    }

    // Drops pop out, settle, then fly to the player once inside magnet range.
    sealed class Pickups
    {
        public const int Gem = 0, Coin = 1, Heart = 2;
        const int Max = 450;
        readonly Run run;
        public readonly List<Drop> Items = new List<Drop>();
        public bool HasPortal;
        public float PortalX, PortalY, PortalZ, PortalT;

        public Pickups(Run run) { this.run = run; }

        public void Clear()
        {
            Items.Clear();
            HasPortal = false;
        }

        public void Spawn(int kind, float x, float y, float z, float value)
        {
            if (Items.Count >= Max)
            {
                // Instead of losing XP when the pool is full, fold it into an existing gem.
                if (kind == Gem)
                {
                    foreach (Drop d in Items)
                    {
                        if (d.Kind != Gem) continue;
                        d.Value += value;
                        break;
                    }
                }
                return;
            }
            float a = M.Range(0, M.Tau);
            float s = M.Range(1, 3);
            Items.Add(new Drop { Kind = kind, X = x, Y = y + 0.6f, Z = z, Vx = M.Cos(a) * s, Vy = M.Range(3, 5.5f), Vz = M.Sin(a) * s, Value = value });
        }

        // How close the player must be to use the portal with the interact key.
        public const float PortalReach = 2.4f;

        public bool NearPortal(float x, float z)
        {
            return HasPortal && M.Hypot(PortalX - x, PortalZ - z) < PortalReach;
        }

        public void SpawnPortal(float x, float z)
        {
            HasPortal = true;
            PortalX = x;
            PortalZ = z;
            PortalY = run.Terrain.HeightAt(x, z);
            PortalT = 0;
        }

        // Sends every remaining drop to the player.
        public void Vacuum()
        {
            foreach (Drop p in Items) p.Pulled = true;
        }

        public void Update(float dt)
        {
            Player player = run.Player;
            Terrain terrain = run.Terrain;
            Audio audio = run.Audio;
            float magnet = player.Stats.Magnet;
            float px = player.X, py = player.Y + 0.7f, pz = player.Z;
            int n = 0;
            for (int k = 0; k < Items.Count; k++)
            {
                Drop p = Items[k];
                p.Age += dt;
                float dx = px - p.X;
                float dy = py - p.Y;
                float dz = pz - p.Z;
                float d = M.Hypot(dx, dy, dz);
                if (d < 0.001f) d = 0.001f;
                if (p.Age > 0.25f && d < magnet) p.Pulled = true;
                if (p.Pulled)
                {
                    float speed = 14 + p.Age * 3 + (magnet - M.Min(d, magnet)) * 2;
                    p.X += dx / d * speed * dt;
                    p.Y += dy / d * speed * dt;
                    p.Z += dz / d * speed * dt;
                    if (d < 1)
                    {
                        if (p.Kind == Gem)
                        {
                            player.AddXp(p.Value);
                            audio.Pickup();
                        }
                        else if (p.Kind == Coin)
                        {
                            int gold = (int)M.Max(1, (float)Math.Round(p.Value * player.Stats.GoldGain * run.LootMult));
                            run.Money += gold;
                            run.Profile.Add("gold", gold);
                            audio.Coin();
                        }
                        else
                        {
                            player.Heal(player.Stats.MaxHp * 0.2f);
                        }
                        continue;
                    }
                }
                else
                {
                    p.Vy -= 20 * dt;
                    p.X += p.Vx * dt;
                    p.Y += p.Vy * dt;
                    p.Z += p.Vz * dt;
                    float ground = terrain.HeightAt(p.X, p.Z) + 0.3f;
                    if (p.Y < ground)
                    {
                        p.Y = ground;
                        p.Vy = M.Abs(p.Vy) > 2 ? -p.Vy * 0.4f : 0;
                        p.Vx *= 0.5f;
                        p.Vz *= 0.5f;
                    }
                }
                Items[n++] = p;
            }
            Items.RemoveRange(n, Items.Count - n);
            if (HasPortal) PortalT += dt;
        }
    }
}
