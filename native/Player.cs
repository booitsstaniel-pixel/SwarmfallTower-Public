using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Swarmfall
{
    sealed class Stats
    {
        // Might and Armor apply to physical moves and hits, Focus and Will to special ones (MoveDef.Special).
        public float Might = 1, Focus = 1, Will, Haste = 1, Area = 1, Duration = 1, Speed = 8, MaxHp = 100, Regen, Armor, Magnet = 4.5f, Crit = 0.05f, XpGain = 1, Luck;
        public int MoveSlots = 3, VitaminSlots = 3, CardOptions = 3;
        public float JumpHeight = 1, ExtraJumps, ExtraShots, CritDmg = 2, Lifesteal, Thorns, GoldGain = 1, Revives, UltRate = 1, MoveCap;

        public float Get(string stat)
        {
            switch (stat)
            {
                case "might": return Might;
                case "focus": return Focus;
                case "will": return Will;
                case "haste": return Haste;
                case "area": return Area;
                case "duration": return Duration;
                case "speed": return Speed;
                case "maxHp": return MaxHp;
                case "regen": return Regen;
                case "armor": return Armor;
                case "magnet": return Magnet;
                case "crit": return Crit;
                case "xpGain": return XpGain;
                case "luck": return Luck;
                case "jumpHeight": return JumpHeight;
                case "extraJumps": return ExtraJumps;
                case "extraShots": return ExtraShots;
                case "critDmg": return CritDmg;
                case "lifesteal": return Lifesteal;
                case "thorns": return Thorns;
                case "goldGain": return GoldGain;
                case "revives": return Revives;
                case "ultRate": return UltRate;
                case "moveCap": return MoveCap;
                default: throw new ArgumentException(stat);
            }
        }

        public void Set(string stat, float v)
        {
            switch (stat)
            {
                case "might": Might = v; break;
                case "focus": Focus = v; break;
                case "will": Will = v; break;
                case "haste": Haste = v; break;
                case "area": Area = v; break;
                case "duration": Duration = v; break;
                case "speed": Speed = v; break;
                case "maxHp": MaxHp = v; break;
                case "regen": Regen = v; break;
                case "armor": Armor = v; break;
                case "magnet": Magnet = v; break;
                case "crit": Crit = v; break;
                case "xpGain": XpGain = v; break;
                case "luck": Luck = v; break;
                case "jumpHeight": JumpHeight = v; break;
                case "extraJumps": ExtraJumps = v; break;
                case "extraShots": ExtraShots = v; break;
                case "critDmg": CritDmg = v; break;
                case "lifesteal": Lifesteal = v; break;
                case "thorns": Thorns = v; break;
                case "goldGain": GoldGain = v; break;
                case "revives": Revives = v; break;
                case "ultRate": UltRate = v; break;
                case "moveCap": MoveCap = v; break;
                default: throw new ArgumentException(stat);
            }
        }

        // The damage multiplier for a physical (Might) or special (Focus) attack.
        public float Power(bool special) { return special ? Focus : Might; }

        // What gets taken off an incoming physical (Armor) or special (Will) hit.
        public float Guard(bool special) { return special ? Will : Armor; }
    }

    // Anything that fights beside the player: the fossil partner, or a captured ally.
    sealed class Drone
    {
        public float X, Y, Z, T = 0.5f, Angle, Fx, Fz = 1, Size = 1.8f, Anim;
        public string Name = "Ancient flyer", Elem = "stone", FrameId = "drone", BonusText;
        public bool IsAlly, Flying = true;
        public StatChange Bonus;
    }

    sealed class Player
    {
        const float Gravity = 30;
        const float JumpBase = 11;
        const float DiveSpeed = -34;
        const float SlideTime = 0.55f;
        const float SlideCooldown = 0.9f;
        const float SlideBoost = 1.9f;
        const float HurtIframes = 0.45f;
        const float StepHeight = 0.55f;
        const float JumpBuffer = 0.12f;
        const float Coyote = 0.1f;

        // XP needed for the next level.
        public static int XpForLevel(int l) { return (int)Math.Round(8 + l * 5.5f + M.Pow(l, 1.8f)); }

        readonly Run run;
        public readonly Partner Partner;
        public readonly Stats Stats = new Stats();
        public float X, Y, Z, Vx, Vy, Vz;
        public float FacingX, FacingZ = -1;
        public readonly float Radius = 0.55f;
        public float CrowdX, CrowdZ, Kx, Kz;
        public bool Grounded = true;
        public float AirT, JumpBufferT, SlideT, SlideCd, Apex, Iframes, FlashT, Squash, Anim;
        public int JumpsLeft;
        public bool Diving, Moving;
        public float Hp, Xp, Ult;
        public int Level = 1, XpNext = XpForLevel(1);
        public bool IsDead;
        public readonly List<Move> Moves = new List<Move>();
        public readonly Dictionary<string, int> Picks = new Dictionary<string, int>();
        public readonly Dictionary<string, int> ItemCounts = new Dictionary<string, int>();
        public readonly Dictionary<string, float> ElemBonus = new Dictionary<string, float>();
        public readonly HashSet<string> Fused = new HashSet<string>();
        public readonly HashSet<string> Banished = new HashSet<string>();
        public int Refreshes = 2, Skips = 2, Banishes = 1;
        public readonly List<Drone> Drones = new List<Drone>();
        public Stage[] Stages;
        public int StageIndex;
        public Stage Stage { get { return Stages[StageIndex]; } }
        // Second type, from whichever stage added it. Null until then.
        // First type. A branching form can replace it.
        public string Elem
        {
            get
            {
                for (int k = StageIndex; k >= 0; k--) if (Stages[k].Elem != null) return Stages[k].Elem;
                return Partner.Elem;
            }
        }

        public string SecondElem
        {
            get
            {
                for (int k = StageIndex; k >= 0; k--) if (Stages[k].AddElem != null) return Stages[k].AddElem;
                return Partner.Elem2;
            }
        }

        public Player(Run run, Partner partner)
        {
            this.run = run;
            Partner = partner;
            Stages = partner.Stages();
            Stats.Might = partner.Might;
            Stats.Focus = partner.Focus;
            Stats.MaxHp = partner.MaxHp;
            Stats.Armor = partner.Armor;
            Stats.Will = partner.Will;
            Stats.Regen = partner.Regen;
            Stats.Magnet = partner.Magnet;
            Hp = Stats.MaxHp;
            Moves.Add(new Move(partner.Move, Data.Move(partner.Move)));
            ApplyUpgrades();
        }

        // Lab upgrades take effect at the start of each run.
        void ApplyUpgrades()
        {
            foreach (UpgradeDef u in Data.LabUpgrades)
            {
                int level = run.Profile.Level(u);
                if (level == 0) continue;
                switch (u.Stat)
                {
                    case "moveSlots": Stats.MoveSlots += level; break;
                    case "vitaminSlots": Stats.VitaminSlots += level; break;
                    case "cardOptions": Stats.CardOptions += level; break;
                    case "refreshes": Refreshes += level; break;
                    case "skips": Skips += level; break;
                    case "banishes": Banishes += level; break;
                    default: AddStat(u.Stat, u.Mul, u.Per * level); break;
                }
            }
        }

        public void AddStat(string stat, bool mul, float amount)
        {
            // "power" is all damage (Might and Focus), "guard" is all defense (Armor and Will).
            if (stat == "power") { AddStat("might", mul, amount); AddStat("focus", mul, amount); return; }
            if (stat == "guard") { AddStat("armor", mul, amount); AddStat("will", mul, amount); return; }
            float before = Stats.Get(stat);
            float after = mul ? before * (1 + amount) : before + amount;
            Stats.Set(stat, after);
            if (stat == "maxHp")
            {
                if (after > before) Hp += after - before;
                Hp = M.Min(Hp, Stats.MaxHp);
            }
        }

        public void AddItem(ItemDef def)
        {
            ItemCounts[def.Id] = ItemCount(def.Id) + 1;
            foreach (StatChange c in def.Stats)
            {
                if (c.Stat.StartsWith("elem:"))
                {
                    string elem = c.Stat.Substring(5);
                    float have;
                    ElemBonus.TryGetValue(elem, out have);
                    ElemBonus[elem] = have + c.Value;
                }
                else
                {
                    AddStat(c.Stat, c.Mul, c.Value);
                }
            }
        }

        public int ItemCount(string id)
        {
            int n;
            ItemCounts.TryGetValue(id, out n);
            return n;
        }

        public void AddDrone()
        {
            Drones.Add(new Drone { X = X, Y = Y + 2, Z = Z, Angle = Drones.Count * 2 });
        }

        public List<Drone> Allies() { return Drones.FindAll(d => d.IsAlly); }

        public void AddAlly(EnemyDef def)
        {
            AllyBonus bonus = Data.AllyBonusFor(def.Elem);
            Drones.Add(new Drone
            {
                X = X, Y = Y, Z = Z, Angle = Drones.Count * 2, Name = def.Name, Elem = def.Elem, FrameId = def.Id, IsAlly = true,
                Flying = (def.Flags & Flag.Flying) != 0, Size = M.Clamp(def.Size * 1.1f, 1.5f, 2.6f), Bonus = bonus.Stat, BonusText = bonus.Text,
            });
            AddStat(bonus.Stat.Stat, bonus.Stat.Mul, bonus.Stat.Value);
        }

        public void RemoveAlly(Drone ally)
        {
            Drones.Remove(ally);
            StatChange b = ally.Bonus;
            if (b.Mul) AddStat(b.Stat, true, 1 / (1 + b.Value) - 1);
            else AddStat(b.Stat, false, -b.Value);
        }

        public bool CanEvolve { get { return StageIndex + 1 < Stages.Length && Level >= Stages[StageIndex + 1].Level; } }

        // A creature with forms picks its next stage instead of following a fixed line.
        public bool NeedsForm { get { return Partner.Forms != null && StageIndex == 0 && Level >= Partner.FormLevel; } }

        public void TakeForm(Stage form)
        {
            Stages = new[] { Stages[0], form };
            Evolve();
        }

        // Three forms to choose from. The first matches your strongest move's type, if a form has that type.
        public Choice FormChoice()
        {
            var pool = new List<Stage>(Partner.Forms);
            var offered = new List<Stage>();
            Move best = null;
            foreach (Move m in Moves) if (best == null || m.Level > best.Level) best = m;
            Stage match = best == null ? null : pool.Find(f => f.Elem == best.Def.Elem);
            if (match != null)
            {
                offered.Add(match);
                pool.Remove(match);
            }
            while (offered.Count < FormOffers && pool.Count > 0)
            {
                int at = M.RangeInt(0, pool.Count);
                offered.Add(pool[at]);
                pool.RemoveAt(at);
            }
            var cards = new List<Card>();
            foreach (Stage form in offered)
            {
                Stage chosen = form;
                cards.Add(new Card { Key = "form:" + chosen.Id, Title = chosen.Name, Tag = "Form - " + Data.Elements[chosen.Elem].Name, Color = Data.ElemColor(chosen.Elem), Desc = chosen.Gain, Run = () => TakeForm(chosen) });
            }
            return new Choice { Title = Partner.Name + " is changing", Sub = "Choose its form. This lasts for the rest of the run.", Cards = cards };
        }

        public void Evolve()
        {
            string from = Stage.Name;
            StageIndex++;
            run.Profile.Add("evolutions");
            Stage next = Stage;
            foreach (StatChange c in next.Stats) AddStat(c.Stat, c.Mul, c.Value);
            string gained = next.AddElem ?? next.Elem;
            if (gained != null)
            {
                float have;
                ElemBonus.TryGetValue(gained, out have);
                ElemBonus[gained] = have + SecondTypeBonus;
            }
            Heal(Stats.MaxHp * 0.35f);
            Ult = M.Min(1, Ult + 0.5f);
            Iframes = M.Max(Iframes, 1.5f);
            FlashT = 1;
            uint color = Data.ElemColor(Elem);
            run.Vfx.RingFx(X, Y, Z, 11, 0xffffff, 0.7f, 0.1f);
            run.Vfx.Burst(X, Y + 1.5f, Z, color, 40, 9, 0.45f, 0.7f);
            run.Blast(X, Y, Z, 9, (30 + Level * 2) * Stats.Focus, Elem, color, 14, new Status(0, 0, 0.8f), 0, SecondElem);
            run.Hud.Banner(from + " evolved into " + next.Name + "!", next.Gain);
            run.Audio.LevelUp();
            run.Freeze(0.2f);
            run.AddTrauma(0.5f);
        }

        void UpdateDrones(float dt)
        {
            Enemies enemies = run.Enemies;
            foreach (Drone d in Drones)
            {
                d.Angle += dt * 1.1f;
                float tx = X + M.Cos(d.Angle) * 2.6f;
                float tz = Z + M.Sin(d.Angle) * 2.6f;
                float k = 1 - M.Exp(-dt * 5);
                float stepX = (tx - d.X) * k, stepZ = (tz - d.Z) * k;
                d.X += stepX;
                d.Z += stepZ;
                float moved = M.Hypot(stepX, stepZ);
                if (moved > 0.0005f)
                {
                    d.Fx = stepX / moved;
                    d.Fz = stepZ / moved;
                }
                d.Anim += moved * 2.2f;
                float ground = d.Flying ? Y + 2.2f : run.Terrain.HeightAt(d.X, d.Z);
                d.Y += (ground - d.Y) * k;
                d.T -= dt;
                if (d.T > 0) continue;
                int i = enemies.Nearest(d.X, d.Z, 20, d.Elem);
                if (i < 0)
                {
                    d.T = 0.2f;
                    continue;
                }
                d.T = 0.9f / Stats.Haste;
                float dx = enemies.X[i] - d.X;
                float dz = enemies.Z[i] - d.Z;
                float dist = M.Hypot(dx, dz);
                if (dist < 0.001f) dist = 1;
                float flight = dist / 24;
                Shot s = run.Projectiles.Spawn();
                if (s == null) continue;
                s.X = d.X; s.Y = d.Y; s.Z = d.Z;
                s.Vx = dx / dist * 24;
                s.Vy = (enemies.Y[i] + enemies.Hgt[i] * 0.55f - d.Y) / flight;
                s.Vz = dz / dist * 24;
                s.Life = 1.2f;
                s.Dmg = (d.IsAlly ? 14 + Level * 1.6f : 22 + Level * 2.5f) * Stats.Might;
                s.Elem = d.Elem;
                s.Color = Data.ElemColor(d.Elem);
            }
        }

        public void AddXp(float amount)
        {
            Xp += amount * Stats.XpGain;
            while (Xp >= XpNext)
            {
                Xp -= XpNext;
                Level++;
                XpNext = XpForLevel(Level);
                run.Profile.Best("bestLevel", Level);
                run.PendingLevels++;
            }
        }

        public void Heal(float amount)
        {
            if (IsDead || amount <= 0 || run.Director.HealingBlocked) return;
            float before = Hp;
            Hp = M.Min(Stats.MaxHp, Hp + amount);
            float gained = Hp - before;
            if (gained >= 1) run.Hud.Text(X, Y + 2, Z, "+" + (int)Math.Round(gained), Hud.TextHeal);
        }

        public void Shove(float nx, float nz, float force)
        {
            Kx += nx * force;
            Kz += nz * force;
        }

        // Returns true when a revive saved the player.
        bool TryRevive()
        {
            if (Stats.Revives < 1) return false;
            Stats.Revives--;
            Hp = Stats.MaxHp * 0.6f;
            Iframes = 2.5f;
            run.Vfx.RingFx(X, Y, Z, 12, 0xffffff, 0.7f, 0.1f);
            run.Blast(X, Y, Z, 10, 60 * Stats.Focus, Elem, 0xffffff, 14, new Status(0, 0, 1), 0, SecondElem);
            run.Hud.Banner("Second Heart", "You got back up");
            return true;
        }

        float lastImmune = -1;
        public float LastHit;
        const float SecondTypeBonus = 0.25f;
        const int FormOffers = 3;
        // Physical hits are reduced by Armor, special hits by Will.
        public bool Hurt(float amount, bool special = false)
        {
            if (IsDead || Iframes > 0 || run.GodMode) return false;
            // Zero means the attacker's type can't touch this one.
            if (amount <= 0)
            {
                if (run.Time - lastImmune > 0.5f)
                {
                    lastImmune = run.Time;
                    run.Hud.Text(X, Y + 2, Z, "Immune", Hud.TextWeak);
                }
                return false;
            }
            float dmg = M.Max(1, amount - Stats.Guard(special));
            LastHit = amount;
            Hp -= dmg;
            Iframes = HurtIframes;
            FlashT = 1;
            run.Hud.Text(X, Y + 2, Z, "-" + (int)Math.Round(dmg), Hud.TextHurt);
            run.Hud.FlashDamage();
            run.Audio.Hurt();
            run.Vfx.Burst(X, Y + 1, Z, 0xff4a4a, 8, 5, 0.26f, 0.35f);
            run.AddTrauma(0.32f);
            run.Freeze(0.045f);
            if (Hp <= 0 && !TryRevive())
            {
                Hp = 0;
                IsDead = true;
                run.OnPlayerDead();
            }
            return true;
        }

        public void Update(float dt)
        {
            Input input = run.Input;
            Terrain terrain = run.Terrain;
            Vfx vfx = run.Vfx;
            Audio audio = run.Audio;
            Stats s = Stats;
            float yaw = run.CamYaw;
            float rightX = M.Cos(yaw), rightZ = -M.Sin(yaw);
            float fwdX = -M.Sin(yaw), fwdZ = -M.Cos(yaw);

            float ix = (input.Down(Keys.D) || input.Down(Keys.Right) ? 1 : 0) - (input.Down(Keys.A) || input.Down(Keys.Left) ? 1 : 0);
            float iz = (input.Down(Keys.W) || input.Down(Keys.Up) ? 1 : 0) - (input.Down(Keys.S) || input.Down(Keys.Down) ? 1 : 0);
            float len = M.Hypot(ix, iz);
            if (len > 1)
            {
                ix /= len;
                iz /= len;
            }
            float moveX = rightX * ix + fwdX * iz;
            float moveZ = rightZ * ix + fwdZ * iz;
            Moving = len > 0.05f;
            if (Moving)
            {
                float l = M.Hypot(moveX, moveZ);
                if (l < 0.001f) l = 1;
                FacingX = moveX / l;
                FacingZ = moveZ / l;
            }

            Iframes = M.Max(0, Iframes - dt);
            FlashT = M.Max(0, FlashT - dt * 5);
            SlideCd = M.Max(0, SlideCd - dt);
            Squash *= M.Exp(-dt * 12);
            Heal(s.Regen * dt);
            Ult = M.Min(1, Ult + dt / 90 * s.UltRate);
            UpdateDrones(dt);

            // Pressing slide on the ground dashes; in the air it dives straight down into a slam.
            if (input.Hit(Keys.ShiftKey) || input.Hit(Keys.ControlKey))
            {
                float above = Y - terrain.Surface(X, Z, Y);
                if (!Grounded && !Diving && above > 1.2f)
                {
                    Diving = true;
                    Vy = DiveSpeed;
                    Vx *= 0.35f;
                    Vz *= 0.35f;
                    Squash = -0.7f;
                    vfx.RingFx(X, Y + 0.6f, Z, 1.4f, 0xffffff, 0.25f);
                    audio.Whoosh();
                }
                else if (Grounded && SlideCd <= 0 && Moving)
                {
                    SlideT = SlideTime;
                    SlideCd = SlideCooldown;
                    Iframes = M.Max(Iframes, ItemCount("slipshell") > 0 ? 0.4f : 0.18f);
                    Vx = FacingX * s.Speed * SlideBoost;
                    Vz = FacingZ * s.Speed * SlideBoost;
                    vfx.Dust(X, Y, Z, 0xe8d8c0, 8, 4);
                    audio.Whoosh();
                }
            }

            if (SlideT > 0)
            {
                SlideT -= dt;
                float drag = M.Exp(-dt * 2.2f);
                Vx *= drag;
                Vz *= drag;
                if (M.Rand() < 0.5f) vfx.Dust(X, Y, Z, 0xe8d8c0, 1, 1.5f);
            }
            else if (Grounded)
            {
                float k = 1 - M.Exp(-dt * 14);
                Vx += (moveX * s.Speed - Vx) * k;
                Vz += (moveZ * s.Speed - Vz) * k;
            }
            else if (!Diving)
            {
                // In the air you can steer, but speed gained from a slide is kept.
                float accel = M.Min(s.Speed * 0.9f, 40 * dt * s.Speed / 8);
                Vx += moveX * accel;
                Vz += moveZ * accel;
                float speed = M.Hypot(Vx, Vz);
                float cap = s.Speed * 1.7f;
                if (speed > cap)
                {
                    Vx *= cap / speed;
                    Vz *= cap / speed;
                }
                if (!Moving)
                {
                    float drag = M.Exp(-dt * 1.2f);
                    Vx *= drag;
                    Vz *= drag;
                }
            }

            // Jump, with a small input buffer and coyote window so it never feels dropped.
            if (input.Hit(Keys.Space)) JumpBufferT = JumpBuffer;
            JumpBufferT -= dt;
            float jumpSpeed = JumpBase * M.Sqrt(s.JumpHeight);
            if (JumpBufferT > 0 && !Diving)
            {
                if (Grounded || AirT < Coyote)
                {
                    Vy = jumpSpeed;
                    Grounded = false;
                    AirT = Coyote;
                    JumpsLeft = (int)s.ExtraJumps;
                    JumpBufferT = 0;
                    Squash = -0.5f;
                    vfx.Dust(X, Y, Z, 0xe8d8c0, 5, 3);
                    audio.Jump();
                }
                else if (JumpsLeft > 0)
                {
                    JumpsLeft--;
                    Vy = jumpSpeed * 0.92f;
                    JumpBufferT = 0;
                    Squash = -0.5f;
                    vfx.RingFx(X, Y, Z, 1.6f, 0xffffff, 0.3f);
                    audio.Jump(1.3f);
                }
            }

            if (Diving)
            {
                Vy = M.Min(Vy, DiveSpeed);
                vfx.Trail(X + M.Range(-0.3f, 0.3f), Y + M.Range(0.3f, 1.4f), Z + M.Range(-0.3f, 0.3f), 0xffffff, 0.35f, 0.25f);
            }
            Vy -= Gravity * dt;

            float decay = M.Exp(-dt * 7);
            Kx *= decay;
            Kz *= decay;
            X += (Vx + Kx) * dt + CrowdX;
            Z += (Vz + Kz) * dt + CrowdZ;
            CrowdX = CrowdZ = 0;
            Y += Vy * dt;
            Apex = M.Max(Apex, Y);

            terrain.PushOut(ref X, ref Z, Radius, Y);

            float ground = terrain.Surface(X, Z, Y);
            bool snapping = Grounded && Y - ground < StepHeight && Vy <= 0;
            if (Y <= ground || snapping)
            {
                bool wasAir = !Grounded;
                Y = ground;
                Vy = 0;
                Grounded = true;
                AirT = 0;
                if (wasAir) Land(Apex - ground);
                Apex = ground;
            }
            else
            {
                Grounded = false;
                AirT += dt;
            }

            if (Moving && Grounded) Anim += dt * (SlideT > 0 ? 18 : 10);
        }

        void Land(float fall)
        {
            Squash = M.Min(0.6f, 0.15f + fall * 0.05f);
            run.Vfx.Dust(X, Y, Z, 0xe8d8c0, 6, 3);
            int boots = ItemCount("iron-boots");
            if (boots > 0 && fall > 1)
            {
                float bootDmg = (10 + Stats.Armor * 2.5f) * boots * Stats.Might;
                run.Blast(X, Y, Z, 3 + Stats.Armor * 0.12f, bootDmg, "stone", 0xc9a06a, 6, default(Status), 0);
            }
            if (!Diving) return;
            Diving = false;
            // The slam scales with how far you fell.
            float r = M.Min(9, 3.2f + fall * 0.4f) * Stats.Area;
            float dmg = (14 + fall * 3.5f) * Stats.Might;
            run.Blast(X, Y, Z, r, dmg, Elem, Data.ElemColor(Elem), 9, new Status(0, 0, 0.35f), 0, SecondElem);
            run.Audio.Boom(0.8f);
            Iframes = M.Max(Iframes, 0.25f);
        }

        public void Ultimate()
        {
            if (Ult < 1 || IsDead) return;
            Ult = 0;
            run.Profile.Add("ultimates");
            if (ItemCount("rally-bell") > 0) foreach (Move m in Moves) m.CdLeft = 0;
            uint color = Data.ElemColor(Elem);
            float dmg = 60 * Stats.Focus * (1 + Level * 0.12f) * (1 + StageIndex * 0.5f);
            Iframes = 1.5f;
            run.Vfx.RingFx(X, Y, Z, 16, 0xffffff, 0.6f, 0.08f);
            for (int k = 0; k < 3; k++)
            {
                float r = 8 + k * 5;
                run.After(k * 0.22f, () => run.Blast(X, Y, Z, r, dmg, Elem, color, 12, new Status(0, 0, 0.8f), 0, SecondElem));
            }
            run.AddTrauma(0.7f);
            run.Freeze(0.12f);
            run.Audio.Boom(1.4f);
        }
    }
}
