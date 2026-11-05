using System;
using System.Collections.Generic;

namespace Swarmfall
{
    enum CaptureState { None, Roaming, Downed, Throwing, Wobbling }

    // Runs wild encounters. A wild creature appears during a zone and keeps its distance. Beating it
    // leaves it weakened instead of knocking it out, and you have a few seconds to get close and throw an orb.
    // A catch fights beside you for the rest of the run. If its roster challenge is complete,
    // catching it also adds it to the roster for good.
    sealed class Capture
    {
        const float FirstWild = 35;
        const float WildEvery = 70;
        const float WildLife = 50;
        const float BrokeFreeLife = 30;
        const float DownedLife = 12;
        const float ThrowRange = 7;
        const float ThrowTime = 0.5f;
        const float WobbleTime = 1.2f;
        const float WildHp = 6;
        // Keeps big creatures catchable early, whenever a challenge brings one out.
        const float WildMaxBaseHp = 120;
        const float BaseChance = 0.6f;
        const float ChancePerCost = 0.04f;
        const float ChancePerAttempt = 0.2f;
        const float ChancePerLuck = 0.15f;
        const float MaxChance = 0.95f;
        public const int AllySlots = 2;
        const int CatchShards = 5;

        readonly Run run;
        public CaptureState State;
        float nextWild, life, stateT;
        int uid, attempts, wobbleSound;
        EnemyDef def;
        public bool OrbVisible;
        public float OrbX, OrbY, OrbZ;
        public int Caught;

        public Capture(Run run)
        {
            this.run = run;
            Reset();
        }

        public void Reset()
        {
            State = CaptureState.None;
            nextWild = FirstWild;
            uid = 0;
            OrbVisible = false;
        }

        public string WildName { get { return def != null ? def.Name : ""; } }

        int WildIndex()
        {
            Enemies e = run.Enemies;
            for (int i = 0; i < e.Count; i++) if (e.Uid[i] == uid && !e.Dead[i]) return i;
            return -1;
        }

        public bool Locate(out float x, out float z)
        {
            x = z = 0;
            if (State == CaptureState.None) return false;
            int i = WildIndex();
            if (i < 0) return false;
            x = run.Enemies.X[i];
            z = run.Enemies.Z[i];
            return true;
        }

        public float Distance()
        {
            float x, z;
            return Locate(out x, out z) ? M.Hypot(x - run.Player.X, z - run.Player.Z) : -1;
        }

        public float Chance()
        {
            if (def == null) return 0;
            return M.Min(MaxChance, BaseChance - def.Cost * ChancePerCost + attempts * ChancePerAttempt + run.Player.Stats.Luck * ChancePerLuck);
        }

        // Forces a wild creature to appear now. The director normally calls this on a timer.
        public void SpawnWild(EnemyDef wild)
        {
            float x, z;
            run.Director.RingPoint(out x, out z);
            float hpScale = M.Min(wild.Hp * WildHp, WildMaxBaseHp) / wild.Hp;
            int i = run.Enemies.Spawn(wild.Id, x, z, run.Director.CurrentHpMult * hpScale, run.Director.CurrentDmgMult, Flag.Wild);
            if (i < 0) return;
            def = wild;
            uid = run.Enemies.Uid[i];
            attempts = 0;
            life = WildLife;
            State = CaptureState.Roaming;
            Partner member = RosterTarget(wild);
            run.Hud.Banner("A wild " + wild.Name + " appeared!", member != null ? "Catch it to add it to your roster for good" : "Weaken it, then throw a capture orb");
            run.Audio.Alarm();
        }

        // The roster entry this creature would unlock, if its challenge is complete and it isn't owned yet.
        Partner RosterTarget(EnemyDef wild)
        {
            foreach (Partner p in Data.Partners) if (p.Id == wild.Id && run.Profile.Ready(p)) return p;
            return null;
        }

        // A creature waiting to join the roster comes first. Otherwise, any creature for this point in the zone.
        EnemyDef NextWild()
        {
            Partner target = run.Profile.NextTarget();
            if (target != null)
            {
                EnemyDef e = Data.WildDef(target.Id);
                if (e != null) return e;
            }
            return M.Pick(run.Director.Cards());
        }

        // Called by Enemies when the wild creature runs out of HP.
        public void OnDowned()
        {
            State = CaptureState.Downed;
            stateT = DownedLife;
            run.Hud.Toast(WildName + " is weakened. Get close and press E");
            run.Audio.Coin();
        }

        public string Prompt()
        {
            if (State != CaptureState.Downed) return "";
            float d = Distance();
            if (d < 0) return "";
            if (d > ThrowRange) return WildName + " is weakened: get closer (" + (int)d + " m, " + (int)Math.Ceiling(stateT) + "s left)";
            return "E: throw capture orb (" + (int)Math.Round(Chance() * 100) + "% chance, " + (int)Math.Ceiling(stateT) + "s left)";
        }

        // Returns true when the key press was used for a throw.
        public bool TryThrow()
        {
            if (State != CaptureState.Downed) return false;
            float d = Distance();
            if (d < 0 || d > ThrowRange) return false;
            State = CaptureState.Throwing;
            stateT = ThrowTime;
            OrbVisible = true;
            run.Audio.Whoosh();
            return true;
        }

        void Remove()
        {
            int i = WildIndex();
            if (i >= 0) run.Enemies.Dead[i] = true;
            State = CaptureState.None;
            OrbVisible = false;
            nextWild = WildEvery;
        }

        void Prize(float x, float y, float z)
        {
            run.Pickups.Spawn(Pickups.Coin, x, y, z, 12);
            run.Pickups.Spawn(Pickups.Gem, x, y, z, def.Xp * 4);
        }

        public void Update(float dt)
        {
            Enemies e = run.Enemies;
            if (State == CaptureState.None)
            {
                if (!run.Director.WildsAllowed) return;
                nextWild -= dt;
                if (nextWild <= 0) SpawnWild(NextWild());
                return;
            }
            int i = WildIndex();
            if (i < 0)
            {
                State = CaptureState.None;
                OrbVisible = false;
                nextWild = WildEvery;
                return;
            }
            float x = e.X[i], y = e.Y[i], z = e.Z[i];
            switch (State)
            {
                case CaptureState.Roaming:
                    life -= dt;
                    if (life <= 0)
                    {
                        run.Hud.Toast(WildName + " got away");
                        Remove();
                    }
                    break;
                case CaptureState.Downed:
                    stateT -= dt;
                    if (M.Rand() < dt * 8) run.Vfx.Trail(x + M.Range(-0.6f, 0.6f), y + e.Hgt[i], z + M.Range(-0.6f, 0.6f), 0xffd24a, 0.35f, 0.5f);
                    if (stateT <= 0)
                    {
                        run.Hud.Toast(WildName + " recovered and fled, dropping a prize");
                        Prize(x, y, z);
                        Remove();
                    }
                    break;
                case CaptureState.Throwing:
                    stateT -= dt;
                    float t = 1 - M.Max(0, stateT) / ThrowTime;
                    Player p = run.Player;
                    OrbX = M.Lerp(p.X, x, t);
                    OrbZ = M.Lerp(p.Z, z, t);
                    OrbY = M.Lerp(p.Y + 1, y + 0.4f, t) + M.Sin(t * M.Pi) * 3;
                    if (stateT <= 0)
                    {
                        e.Flags[i] |= Flag.Captive;
                        State = CaptureState.Wobbling;
                        stateT = WobbleTime;
                        wobbleSound = 0;
                        run.Vfx.Burst(x, y + 0.5f, z, 0xffffff, 16, 5);
                    }
                    break;
                case CaptureState.Wobbling:
                    stateT -= dt;
                    float gone = WobbleTime - stateT;
                    OrbX = x + M.Sin(gone * 16) * 0.18f;
                    OrbZ = z;
                    OrbY = y + 0.4f;
                    if (gone > wobbleSound * 0.4f && wobbleSound < 3)
                    {
                        wobbleSound++;
                        run.Audio.Pickup();
                    }
                    if (stateT <= 0) Resolve(i);
                    break;
            }
        }

        void Resolve(int i)
        {
            Enemies e = run.Enemies;
            bool caught = M.Rand() < Chance();
            if (caught)
            {
                Caught++;
                EnemyDef wild = def;
                run.Vfx.Burst(e.X[i], e.Y[i] + 0.5f, e.Z[i], 0xffd24a, 30, 7);
                run.Audio.LevelUp();
                run.Profile.Add("caught");
                run.Earn(CatchShards);
                Partner member = RosterTarget(wild);
                if (member != null)
                {
                    run.Profile.AddToRoster(member);
                    run.RunNews.Add(member.Name + " joined your roster");
                }
                run.Profile.Save();
                run.Hud.Banner("Caught " + wild.Name + "!", member != null ? "It joined your roster for good, and fights beside you for this run" : "It fights beside you for this run");
                Remove();
                Join(wild);
                return;
            }
            // It breaks out with some HP back, and the next throw is easier.
            attempts++;
            e.Flags[i] &= ~(Flag.Captive | Flag.Downed);
            e.Hp[i] = e.MaxHp[i] * 0.4f;
            e.Flash[i] = 1;
            State = CaptureState.Roaming;
            life = BrokeFreeLife;
            OrbVisible = false;
            run.Vfx.Burst(e.X[i], e.Y[i] + 0.5f, e.Z[i], 0xffffff, 20, 6);
            run.Hud.Banner("It broke free!", "Weaken it again. The next throw is easier");
            run.Audio.Denied();
        }

        void Join(EnemyDef wild)
        {
            Player p = run.Player;
            List<Drone> allies = p.Allies();
            if (allies.Count < AllySlots)
            {
                p.AddAlly(wild);
                return;
            }
            int sell = 30;
            run.QueueChoice(() =>
            {
                var cards = new List<Card>();
                foreach (Drone old in allies)
                {
                    Drone ally = old;
                    cards.Add(new Card
                    {
                        Key = "ally",
                        Title = "Replace " + ally.Name,
                        Tag = "Ally slots are full",
                        Color = 0x49e0c8,
                        Desc = "Lose: " + ally.BonusText + ". Gain " + wild.Name + ": " + Data.AllyBonusFor(wild.Elem).Text + ".",
                        Run = () =>
                        {
                            p.RemoveAlly(ally);
                            p.AddAlly(wild);
                        },
                    });
                }
                return new Choice { Title = "Caught " + wild.Name, Sub = "You can keep " + AllySlots + " allies.", Cards = cards, CloseLabel = "Release it (+" + sell + " gold)", OnClose = () => run.Money += sell };
            });
        }
    }
}
