using System;
using System.Collections.Generic;

namespace Swarmfall
{
    struct HitOpts
    {
        public float Kx, Kz, Stun, Slow, Burn, CritBonus;
        public bool Crit;
        public static HitOpts From(Status s, float kx = 0, float kz = 0, float critBonus = 0)
        {
            return new HitOpts { Stun = s.Stun, Slow = s.Slow, Burn = s.Burn, Kx = kx, Kz = kz, CritBonus = critBonus };
        }
    }

    // Enemies are flat arrays, not objects, so thousands cost little and create no garbage.
    // A kill only marks the slot; Sweep compacts at the start of the next update.
    sealed class Enemies
    {
        const int CellSize = 3;
        const float GridHalf = Terrain.ArenaRadius + 12;
        static readonly int Dim = (int)Math.Ceiling(GridHalf * 2 / CellSize);
        const int MaxNeighbours = 8;
        const float PlayerMass = 0.4f;

        readonly Run run;
        public readonly int Capacity;
        public int Count;
        int nextUid = 1;
        int frame;
        public readonly List<EnemyDef> Kinds = new List<EnemyDef>();
        readonly Dictionary<string, int> kindIndex = new Dictionary<string, int>();

        public readonly float[] X, Y, Z, Hp, MaxHp, Speed, Radius, Hgt, Dmg, Atk, Flash, Anim, Kbx, Kbz, StunT, SlowT, BurnT, BurnTick, Fx, Fz, SepX, SepZ, DirX, DirZ, StateT, Xp, Hover, WetT, ReactT;
        public readonly int[] Kind, Flags, State, Uid;
        public readonly bool[] Dead;
        readonly float[][] floats;
        readonly int[][] ints;
        readonly int[] head;
        readonly int[] next;

        public Enemies(Run run, int capacity = 1400)
        {
            this.run = run;
            Capacity = capacity;
            floats = new float[29][];
            for (int i = 0; i < floats.Length; i++) floats[i] = new float[capacity];
            X = floats[0]; Y = floats[1]; Z = floats[2]; Hp = floats[3]; MaxHp = floats[4]; Speed = floats[5]; Radius = floats[6]; Hgt = floats[7]; Dmg = floats[8];
            Atk = floats[9]; Flash = floats[10]; Anim = floats[11]; Kbx = floats[12]; Kbz = floats[13]; StunT = floats[14]; SlowT = floats[15]; BurnT = floats[16]; BurnTick = floats[17];
            Fx = floats[18]; Fz = floats[19]; SepX = floats[20]; SepZ = floats[21]; DirX = floats[22]; DirZ = floats[23]; StateT = floats[24]; Xp = floats[25]; Hover = floats[26]; WetT = floats[27]; ReactT = floats[28];
            ints = new[] { new int[capacity], new int[capacity], new int[capacity], new int[capacity] };
            Kind = ints[0]; Flags = ints[1]; State = ints[2]; Uid = ints[3];
            Dead = new bool[capacity];
            head = new int[Dim * Dim];
            next = new int[capacity];
            Clear();
        }

        public void Register(EnemyDef def)
        {
            kindIndex[def.Id] = Kinds.Count;
            Kinds.Add(def);
        }

        public void Clear()
        {
            Count = 0;
            for (int i = 0; i < head.Length; i++) head[i] = -1;
        }

        static int CellOf(float v)
        {
            int c = (int)((v + GridHalf) / CellSize);
            return c < 0 ? 0 : c >= Dim ? Dim - 1 : c;
        }

        public int Spawn(string id, float x, float z, float hpMult, float dmgMult, int extraFlags = 0)
        {
            if (Count >= Capacity) return -1;
            int k = kindIndex[id];
            EnemyDef def = Kinds[k];
            int i = Count++;
            int flags = def.Flags | extraFlags;
            bool elite = (flags & Flag.Elite) != 0;
            float scale = elite ? 1.35f : 1;
            float hp = def.Hp * hpMult * (elite ? 4 : 1);
            X[i] = x;
            Z[i] = z;
            Y[i] = run.Terrain.HeightAt(x, z);
            Hp[i] = MaxHp[i] = hp;
            Speed[i] = def.Speed * M.Range(0.9f, 1.1f) * (elite ? 0.92f : 1);
            Hgt[i] = def.Size * scale;
            Radius[i] = def.Size * scale * 0.34f;
            Dmg[i] = def.Dmg * dmgMult * (elite ? 1.4f : 1);
            Atk[i] = M.Range(0.8f, 2.5f);
            Flash[i] = 0;
            Anim[i] = M.Range(0, 6);
            Kbx[i] = Kbz[i] = 0;
            StunT[i] = SlowT[i] = BurnT[i] = BurnTick[i] = WetT[i] = ReactT[i] = 0;
            Fx[i] = 0;
            Fz[i] = 1;
            SepX[i] = SepZ[i] = 0;
            DirX[i] = DirZ[i] = 0;
            State[i] = 0;
            StateT[i] = 0;
            Xp[i] = def.Xp * (elite ? 5 : 1);
            Hover[i] = (flags & Flag.Flying) != 0 ? 1.6f : 0;
            Kind[i] = k;
            Flags[i] = flags;
            Uid[i] = nextUid++;
            Dead[i] = false;
            return i;
        }

        void Sweep()
        {
            int i = 0;
            while (i < Count)
            {
                if (!Dead[i])
                {
                    i++;
                    continue;
                }
                int last = --Count;
                if (i == last) continue;
                foreach (float[] arr in floats) arr[i] = arr[last];
                foreach (int[] arr in ints) arr[i] = arr[last];
                Dead[i] = Dead[last];
            }
        }

        void BuildGrid()
        {
            for (int i = 0; i < head.Length; i++) head[i] = -1;
            for (int i = 0; i < Count; i++)
            {
                int c = CellOf(Z[i]) * Dim + CellOf(X[i]);
                next[i] = head[c];
                head[c] = i;
            }
        }

        // Calls fn(index) for live enemies whose centre is within r (plus their own radius).
        public void Query(float x, float z, float r, Action<int> fn)
        {
            int x0 = CellOf(x - r - 3), x1 = CellOf(x + r + 3);
            int z0 = CellOf(z - r - 3), z1 = CellOf(z + r + 3);
            for (int cz = z0; cz <= z1; cz++)
            {
                for (int cx = x0; cx <= x1; cx++)
                {
                    for (int i = head[cz * Dim + cx]; i >= 0; i = next[i])
                    {
                        if (Dead[i] || (Flags[i] & Flag.Downed) != 0) continue;
                        float dx = X[i] - x;
                        float dz = Z[i] - z;
                        float rr = r + Radius[i];
                        if (dx * dx + dz * dz <= rr * rr) fn(i);
                    }
                }
            }
        }

        public int Nearest(float x, float z, float r, string elem = null)
        {
            int best = -1;
            float bestD = r * r;
            int x0 = CellOf(x - r), x1 = CellOf(x + r);
            int z0 = CellOf(z - r), z1 = CellOf(z + r);
            for (int cz = z0; cz <= z1; cz++)
            {
                for (int cx = x0; cx <= x1; cx++)
                {
                    for (int i = head[cz * Dim + cx]; i >= 0; i = next[i])
                    {
                        if (Dead[i] || (Flags[i] & Flag.Downed) != 0) continue;
                        if (elem != null && TypeMultiplier(i, elem) == 0) continue;
                        float dx = X[i] - x;
                        float dz = Z[i] - z;
                        float d = dx * dx + dz * dz;
                        if (d < bestD)
                        {
                            bestD = d;
                            best = i;
                        }
                    }
                }
            }
            return best;
        }

        // What a hit of this element is worth against this enemy.
        public float TypeMultiplier(int i, string elem)
        {
            EnemyDef def = Kinds[Kind[i]];
            float eff = Data.Effectiveness(elem, def.Elem, def.Elem2);
            return eff == 0 && (Flags[i] & Flag.Boss) != 0 ? Data.BossImmunityFloor : eff;
        }

        // What this enemy's attack is worth against the player's types, using its better type.
        public float AttackDamage(int i, float amount)
        {
            EnemyDef def = Kinds[Kind[i]];
            Player p = run.Player;
            string first = p.Elem, second = p.SecondElem;
            float eff = Data.Effectiveness(Data.BestElem(def.Elem, def.Elem2, first, second), first, second);
            if ((Flags[i] & Flag.Boss) != 0) eff = eff == 0 ? Data.BossImmunityFloor : M.Min(eff, Data.BossAttackCap);
            return amount * eff;
        }

        // Applies damage after element matchups.
        public bool Hit(int i, float amount, string elem, HitOpts opts)
        {
            if (Dead[i] || (Flags[i] & Flag.Downed) != 0) return false;
            EnemyDef def = Kinds[Kind[i]];
            float eff = TypeMultiplier(i, elem);
            if (eff == 0)
            {
                run.Hud.Text(X[i], Y[i] + Hgt[i] + 0.2f, Z[i], "Immune", Hud.TextWeak);
                return false;
            }
            float dmg = M.Max(1, amount * eff);
            Hp[i] -= dmg;
            Flash[i] = 1;
            bool heavy = (Flags[i] & (Flag.Boss | Flag.Elite)) != 0;
            float knock = heavy ? 0.15f : 1;
            Kbx[i] += opts.Kx * knock;
            Kbz[i] += opts.Kz * knock;
            if (opts.Stun > 0 && (Flags[i] & Flag.Boss) == 0) StunT[i] = M.Max(StunT[i], opts.Stun);
            if (opts.Slow > 0) SlowT[i] = M.Max(SlowT[i], opts.Slow);
            if (opts.Burn > 0) BurnT[i] = M.Max(BurnT[i], opts.Burn);
            run.Hud.Text(X[i], Y[i] + Hgt[i] + 0.2f, Z[i], ((int)Math.Round(dmg)).ToString(), opts.Crit ? Hud.TextCrit : eff > 1 ? Hud.TextStrong : eff < 1 ? Hud.TextWeak : Hud.TextNormal);
            run.Audio.Hit();
            if (Hp[i] <= 0)
            {
                // A wild creature is weakened rather than knocked out, so it can be caught.
                if ((Flags[i] & Flag.Wild) != 0)
                {
                    Hp[i] = 1;
                    Flags[i] |= Flag.Downed;
                    BurnT[i] = SlowT[i] = StunT[i] = 0;
                    run.Capture.OnDowned();
                }
                else
                {
                    Kill(i);
                }
            }
            return true;
        }

        void Kill(int i)
        {
            if (Dead[i]) return;
            Dead[i] = true;
            run.OnEnemyKilled(i, Kinds[Kind[i]]);
        }

        public void Update(float dt)
        {
            Player player = run.Player;
            Terrain terrain = run.Terrain;
            Vfx vfx = run.Vfx;
            Sweep();
            BuildGrid();
            frame++;
            float px = player.X, py = player.Y, pz = player.Z;

            for (int i = 0; i < Count; i++)
            {
                if (Dead[i]) continue;
                int flags = Flags[i];
                if ((flags & Flag.Downed) != 0)
                {
                    Flash[i] = 0.25f + M.Sin(frame * 0.25f) * 0.2f;
                    continue;
                }
                Flash[i] = M.Max(0, Flash[i] - dt * 6);
                Atk[i] -= dt;
                if (WetT[i] > 0) WetT[i] -= dt;
                if (ReactT[i] > 0) ReactT[i] -= dt;

                if (BurnT[i] > 0)
                {
                    BurnT[i] -= dt;
                    BurnTick[i] -= dt;
                    if (BurnTick[i] <= 0)
                    {
                        BurnTick[i] = 0.5f;
                        Hit(i, 2 + MaxHp[i] * 0.012f, null, default(HitOpts));
                        vfx.Trail(X[i], Y[i] + Hgt[i] * 0.6f, Z[i], 0xff7a3c, 0.4f, 0.4f);
                        if (Dead[i]) continue;
                    }
                }

                float dx = px - X[i];
                float dz = pz - Z[i];
                float dist = M.Hypot(dx, dz);
                if (dist < 0.001f) dist = 0.001f;
                float nx = dx / dist;
                float nz = dz / dist;
                // The map is big, so anything left far behind rejoins the fight instead of idling out there.
                if (dist > 62)
                {
                    float sx, sz;
                    run.Director.RingPoint(out sx, out sz);
                    X[i] = sx;
                    Z[i] = sz;
                    continue;
                }
                float speed = Speed[i];
                if (SlowT[i] > 0)
                {
                    SlowT[i] -= dt;
                    speed *= 0.55f;
                }
                float vx = nx * speed;
                float vz = nz * speed;

                if (StunT[i] > 0)
                {
                    StunT[i] -= dt;
                    vx = vz = 0;
                }
                else if ((flags & Flag.Boss) != 0)
                {
                    UpdateBoss(i, dt, nx, nz, speed, ref vx, ref vz);
                }
                else if ((flags & Flag.Wild) != 0)
                {
                    // Wild creatures keep their distance, circling away instead of attacking.
                    float circle = M.Sin(Anim[i] * 0.3f) > 0 ? 1 : -1;
                    if (dist < 11)
                    {
                        vx = (-nx + nz * circle * 0.6f) * speed * 0.8f;
                        vz = (-nz - nx * circle * 0.6f) * speed * 0.8f;
                    }
                    else
                    {
                        vx = nz * circle * speed * 0.5f;
                        vz = -nx * circle * speed * 0.5f;
                    }
                }
                else if ((flags & Flag.Charger) != 0)
                {
                    if (State[i] == 0 && Atk[i] <= 0 && dist < 13)
                    {
                        State[i] = 1;
                        StateT[i] = 0.65f;
                        DirX[i] = nx;
                        DirZ[i] = nz;
                        vfx.RingFx(X[i], Y[i], Z[i], Radius[i] + 1.4f, 0xff4a4a, 0.5f);
                    }
                    if (State[i] == 1)
                    {
                        vx = vz = 0;
                        StateT[i] -= dt;
                        Flash[i] = M.Max(Flash[i], 0.35f);
                        if (StateT[i] <= 0)
                        {
                            State[i] = 2;
                            StateT[i] = 0.55f;
                        }
                    }
                    else if (State[i] == 2)
                    {
                        vx = DirX[i] * speed * 4.2f;
                        vz = DirZ[i] * speed * 4.2f;
                        StateT[i] -= dt;
                        if (StateT[i] <= 0)
                        {
                            State[i] = 0;
                            Atk[i] = M.Range(2.5f, 4);
                        }
                    }
                }
                else if ((flags & Flag.Shooter) != 0)
                {
                    if (dist < 9)
                    {
                        vx = -nx * speed * 0.8f;
                        vz = -nz * speed * 0.8f;
                    }
                    else if (dist < 15)
                    {
                        vx = vz = 0;
                    }
                    if (Atk[i] <= 0 && dist < 19)
                    {
                        Atk[i] = M.Range(2.6f, 4);
                        run.Projectiles.EnemyShot(X[i], Y[i] + Hgt[i] * 0.6f, Z[i], nx, nz, 11, AttackDamage(i, Dmg[i]), 0x7fd0ff, true);
                    }
                }

                // Separation runs on alternate frames per enemy; the last result carries over in between.
                if ((i + frame) % 2 == 0) Separate(i);
                vx += SepX[i];
                vz += SepZ[i];

                float kbDecay = M.Exp(-6 * dt);
                Kbx[i] *= kbDecay;
                Kbz[i] *= kbDecay;
                float nextX = X[i] + (vx + Kbx[i]) * dt;
                float nextZ = Z[i] + (vz + Kbz[i]) * dt;

                float speedSq = vx * vx + vz * vz;
                if (speedSq > 0.01f)
                {
                    float s = M.Sqrt(speedSq);
                    Fx[i] = vx / s;
                    Fz[i] = vz / s;
                    Anim[i] += dt * M.Min(8, s * 1.4f);
                }

                if (Hover[i] > 0)
                {
                    float r = M.Hypot(nextX, nextZ);
                    if (r > Terrain.ArenaRadius)
                    {
                        nextX *= Terrain.ArenaRadius / r;
                        nextZ *= Terrain.ArenaRadius / r;
                    }
                    X[i] = nextX;
                    Z[i] = nextZ;
                    Y[i] = terrain.HeightAt(nextX, nextZ) + Hover[i] + M.Sin(Anim[i]) * 0.2f;
                }
                else
                {
                    terrain.PushOut(ref nextX, ref nextZ, Radius[i]);
                    X[i] = nextX;
                    Z[i] = nextZ;
                    Y[i] = terrain.HeightAt(nextX, nextZ);
                }

                // Touching the player: pushes both apart by mass, and deals contact damage.
                float cdx = X[i] - px;
                float cdz = Z[i] - pz;
                float reach = Radius[i] + player.Radius;
                float overlap = cdx * cdx + cdz * cdz;
                if (overlap < reach * reach && py < Y[i] + Hgt[i] * 0.85f && py > Y[i] - 1.2f)
                {
                    float d = M.Sqrt(overlap);
                    if (d < 0.001f) d = 0.001f;
                    float ox = cdx / d;
                    float oz = cdz / d;
                    float push = reach - d;
                    float mass = (flags & Flag.Boss) != 0 ? 1000 : Radius[i] * Radius[i];
                    float share = mass / (mass + PlayerMass);
                    X[i] += ox * push * (1 - share);
                    Z[i] += oz * push * (1 - share);
                    player.CrowdX -= ox * push * share;
                    player.CrowdZ -= oz * push * share;
                    if (StunT[i] <= 0 && player.Hurt(AttackDamage(i, Dmg[i])))
                    {
                        player.Shove(-ox, -oz, (flags & (Flag.Boss | Flag.Charger)) != 0 ? 9 : 3.5f);
                        if (player.Stats.Thorns > 0) Hit(i, player.Stats.Thorns, null, default(HitOpts));
                        if (Dead[i]) continue;
                    }
                }
            }
        }

        void Separate(int i)
        {
            float x = X[i], z = Z[i], r = Radius[i];
            float sx = 0, sz = 0;
            int checks = 0;
            int ccx = CellOf(x), ccz = CellOf(z);
            for (int cz = Math.Max(0, ccz - 1); cz <= Math.Min(Dim - 1, ccz + 1) && checks < MaxNeighbours; cz++)
            {
                for (int cx = Math.Max(0, ccx - 1); cx <= Math.Min(Dim - 1, ccx + 1) && checks < MaxNeighbours; cx++)
                {
                    for (int j = head[cz * Dim + cx]; j >= 0 && checks < MaxNeighbours; j = next[j])
                    {
                        if (j == i) continue;
                        float dx = x - X[j];
                        float dz = z - Z[j];
                        float min = r + Radius[j];
                        float d2 = dx * dx + dz * dz;
                        if (d2 > 0.0001f && d2 < min * min)
                        {
                            checks++;
                            float d = M.Sqrt(d2);
                            float f = (min - d) / min * 5;
                            sx += dx / d * f;
                            sz += dz / d * f;
                        }
                    }
                }
            }
            SepX[i] = sx;
            SepZ[i] = sz;
        }

        // Boss pattern: walk, then cycle through a ring of shots, a charge and a telegraphed slam.
        void UpdateBoss(int i, float dt, float nx, float nz, float speed, ref float vx, ref float vz)
        {
            Vfx vfx = run.Vfx;
            Audio audio = run.Audio;
            int state = State[i];
            if (state == 0)
            {
                if (Atk[i] <= 0)
                {
                    float roll = M.Rand();
                    State[i] = roll < 0.4f ? 1 : roll < 0.75f ? 2 : 4;
                    StateT[i] = 0.75f;
                    DirX[i] = nx;
                    DirZ[i] = nz;
                    vfx.RingFx(X[i], Y[i], Z[i], Radius[i] + 2.5f, 0xff4a4a, 0.6f, 0.1f);
                    audio.Alarm();
                }
            }
            else if (state == 1)
            {
                vx = vz = 0;
                StateT[i] -= dt;
                Flash[i] = M.Max(Flash[i], 0.3f);
                if (StateT[i] <= 0)
                {
                    const int n = 18;
                    float off = M.Range(0, M.Tau);
                    for (int k = 0; k < n; k++)
                    {
                        float a = off + (float)k / n * M.Tau;
                        run.Projectiles.EnemyShot(X[i], Y[i] + Hgt[i] * 0.5f, Z[i], M.Cos(a), M.Sin(a), 9.5f, AttackDamage(i, Dmg[i] * 0.6f), 0xff6ab0, true);
                    }
                    audio.Boom(0.6f);
                    FinishBossAttack(i);
                }
            }
            else if (state == 2)
            {
                vx = vz = 0;
                StateT[i] -= dt;
                Flash[i] = M.Max(Flash[i], 0.3f);
                if (StateT[i] <= 0)
                {
                    State[i] = 3;
                    StateT[i] = 0.8f;
                    audio.Whoosh();
                }
            }
            else if (state == 3)
            {
                vx = DirX[i] * speed * 5;
                vz = DirZ[i] * speed * 5;
                StateT[i] -= dt;
                if (StateT[i] <= 0) FinishBossAttack(i);
            }
            else if (state == 4)
            {
                vx = vz = 0;
                StateT[i] -= dt;
                if (StateT[i] <= 0)
                {
                    Player p = run.Player;
                    for (int k = 0; k < 4; k++)
                    {
                        float a = M.Range(0, M.Tau);
                        float d = k == 0 ? 0 : M.Range(3, 8);
                        run.EnemyBlast(p.X + M.Cos(a) * d, p.Z + M.Sin(a) * d, 3.2f, 0.95f, AttackDamage(i, Dmg[i]), 0xff5a7a);
                    }
                    if (M.Rand() < 0.5f) run.Director.SummonAdds(X[i], Z[i], 4);
                    FinishBossAttack(i);
                }
            }
        }

        void FinishBossAttack(int i)
        {
            State[i] = 0;
            bool enraged = Hp[i] < MaxHp[i] * 0.5f;
            Atk[i] = enraged ? M.Range(1.6f, 2.6f) : M.Range(2.6f, 3.8f);
        }
    }
}
