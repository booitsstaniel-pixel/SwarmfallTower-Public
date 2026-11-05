using System;
using System.Collections.Generic;

namespace Swarmfall
{
    enum Phase { Zone, Survive, Guardian, Final, Cleared }

    // Runs one zone: a horde that keeps growing, then a guardian, then a portal.
    // Earns spawn credits over time and spends them on enemy cards as packs around the player.
    // Also adapts how many enemies may be alive at once to the frame rate actually measured.
    sealed class Director
    {
        const float FpsLow = 42;
        const float FpsHigh = 55;
        const int LiveFloor = 60;
        const int LiveMax = 500;
        const float EliteEvery = 32;
        // Boss HP multipliers, tuned against the damage a build reaches by each zone.
        static readonly float[] GuardianHp = { 5, 18, 60 };
        const float FinalHp = 90;
        const float DifficultyRampTime = 90, OpeningCapGrowth = 0.5f;
        const int OpeningCap = 30;
        const int GuardianShards = 15, FinalBossShards = 40;
        const float SurvivalBossEvery = 300, SurvivalChestEvery = 75, EndlessNight = 2700, NightDrain = 0.02f;

        readonly Run run;
        public int LiveCap = LiveMax;
        float fpsAcc;
        int fpsFrames, goodStreak;
        public Phase Phase;
        public float NextBossT;
        // No healing of any kind during the Endless Night, so lifesteal and regen can't outlast the drain.
        public bool HealingBlocked { get { return run.Mode == GameMode.Survival && Elapsed >= EndlessNight; } }
        float chestT;
        int milestones;
        bool night;
        public bool WildsAllowed { get { return Phase == Phase.Zone || Phase == Phase.Survive; } }
        public float T, Elapsed;
        float credits, eliteT;
        public int BossUid;
        public EnemyDef BossDef;

        public Director(Run run)
        {
            this.run = run;
            Reset();
        }

        public void Reset()
        {
            Phase = run.Mode == GameMode.Survival ? Phase.Survive : Phase.Zone;
            NextBossT = SurvivalBossEvery;
            chestT = SurvivalChestEvery;
            milestones = 0;
            night = false;
            T = Data.ZoneTime;
            Elapsed = 0;
            credits = 6;
            eliteT = EliteEvery;
            BossUid = 0;
            BossDef = null;
        }

        // Harder difficulties ease in over the first 90 seconds, so a run isn't lost before the first level-ups.
        float Ramp(float mult) { return 1 + (mult - 1) * M.Min(1, run.Time / DifficultyRampTime); }
        // Survival keeps getting harder at a growing rate, so every run eventually ends.
        float SurvivalLate { get { float t = run.Mode == GameMode.Survival ? Elapsed / 600 : 0; return t * t; } }
        float SurvivalHp { get { float t = run.Mode == GameMode.Survival ? Elapsed / 600 : 0; return t * t * t * 6; } }
        // The horde can't outgrow a new player's starting moves: the limit rises steadily from the start of a run.
        int SpawnCap { get { return Math.Min(LiveCap, OpeningCap + (int)(run.Time * OpeningCapGrowth)); } }
        public float CurrentHpMult { get { return HpMult; } }
        public float CurrentDmgMult { get { return DmgMult; } }
        float HpMult { get { return (1 + (run.Zone - 1) * 2.6f + Elapsed / Data.ZoneTime * 1.3f) * (1 + run.Heat * 0.5f) * Ramp(run.Diff.Hp) + SurvivalHp; } }
        float DmgMult { get { return (1 + (run.Zone - 1) * 1.5f + Elapsed / Data.ZoneTime * 0.8f + run.Heat * 0.3f) * Ramp(run.Diff.Dmg) + SurvivalLate * 3; } }
        // Which enemy cards are unlocked: rises with each zone and through the zone.
        float Tier { get { return run.Mode == GameMode.Survival ? 1 + Elapsed / 120 : (run.Zone - 1) * 2 + 1 + M.Min(1, Elapsed / 120); } }
        float CreditRate { get { return (1.2f + (run.Zone - 1) * 2.4f + run.Zone * 0.7f + Elapsed / 60 * (1.1f + run.Zone * 0.4f)) * (1 + run.Heat) * Ramp(run.Diff.Credits) * (night ? 2 : 1); } }

        public void RingPoint(out float x, out float z)
        {
            Player player = run.Player;
            for (int tries = 0; tries < 12; tries++)
            {
                float a = M.Range(0, M.Tau);
                float d = M.Range(20, 30);
                x = player.X + M.Cos(a) * d;
                z = player.Z + M.Sin(a) * d;
                if (run.Terrain.InBounds(x, z, 3)) return;
            }
            float back = M.Atan2(-player.Z, -player.X) + M.Range(-1, 1);
            x = player.X + M.Cos(back) * 22;
            z = player.Z + M.Sin(back) * 22;
        }

        public List<EnemyDef> Cards()
        {
            var cards = new List<EnemyDef>();
            float tier = Tier;
            foreach (EnemyDef d in Data.Enemies) if (d.Tier <= tier) cards.Add(d);
            return cards;
        }

        void SpawnPack()
        {
            EnemyDef def = M.Weighted(Cards(), d => 1 / d.Cost);
            int size = def.Cost <= 1.5f ? M.RangeInt(2, 6) : def.Cost <= 3 ? M.RangeInt(1, 3) : 1;
            float sx, sz;
            RingPoint(out sx, out sz);
            for (int k = 0; k < size; k++)
            {
                if (credits < def.Cost || run.Enemies.Count >= SpawnCap) return;
                float x = M.Clamp(sx + M.Range(-2.5f, 2.5f), -120, 120);
                float z = M.Clamp(sz + M.Range(-2.5f, 2.5f), -120, 120);
                if (run.Enemies.Spawn(def.Id, x, z, HpMult, DmgMult) >= 0) credits -= def.Cost;
            }
        }

        void SpawnElite()
        {
            EnemyDef def = M.Pick(Cards());
            float sx, sz;
            RingPoint(out sx, out sz);
            run.Enemies.Spawn(def.Id, sx, sz, HpMult, DmgMult, Flag.Elite);
            run.Hud.Toast("An elite has appeared");
        }

        void SpawnBoss(EnemyDef def, float hpMult, float dmgMult)
        {
            Player p = run.Player;
            float a = M.Range(0, M.Tau);
            float x = p.X + M.Cos(a) * 16;
            float z = p.Z + M.Sin(a) * 16;
            run.Terrain.PushOut(ref x, ref z, 3);
            int i = run.Enemies.Spawn(def.Id, x, z, hpMult * run.Diff.Hp, dmgMult * run.Diff.Dmg, Flag.Boss);
            if (i < 0) return;
            BossUid = run.Enemies.Uid[i];
            BossDef = def;
            run.Hud.Banner(def.Name, Phase == Phase.Final ? "Final boss" : "Guardian");
            run.Hud.ShowBoss(def.Name);
            run.Audio.Boom(1.2f);
            run.AddTrauma(0.5f);
        }

        // Called when the timer runs out, or early from the guardian stone for extra gold.
        public void SummonGuardian(bool early)
        {
            if (Phase != Phase.Zone) return;
            Phase = Phase.Guardian;
            if (early)
            {
                int bonus = (int)Math.Round(T * 0.25f);
                run.Money += bonus;
                run.Hud.Toast("Early challenge: +" + bonus + " gold");
            }
            int zone = run.Zone;
            SpawnBoss(Data.Bosses[(zone - 1) % Data.Bosses.Length], GuardianHp[zone - 1], 1 + (zone - 1) * 0.6f);
            run.Encounters.MarkStoneUsed();
        }

        // A boss every 5 minutes in Survival, tougher each time. Beating one offers a pact.
        void SummonMilestone()
        {
            Phase = Phase.Guardian;
            milestones++;
            EnemyDef def = Elapsed >= EndlessNight ? Data.FinalBoss : Data.Bosses[(milestones - 1) % Data.Bosses.Length];
            SpawnBoss(def, 4 + Elapsed / 60 * 1.2f, 1 + Elapsed / 240 * 0.4f);
        }

        void UpdateSurvival(float dt)
        {
            NextBossT -= dt;
            if (NextBossT <= 0)
            {
                NextBossT = SurvivalBossEvery;
                SummonMilestone();
            }
            chestT -= dt;
            if (chestT <= 0)
            {
                chestT = SurvivalChestEvery;
                float x, z;
                RingPoint(out x, out z);
                run.Terrain.PushOut(ref x, ref z, 2);
                run.Encounters.DropChest(x, z);
                run.Hud.Toast("A chest has appeared nearby");
            }
        }

        // After 45 minutes, a drain that keeps growing guarantees every Survival run ends.
        void UpdateNight(float dt)
        {
            if (Elapsed < EndlessNight) return;
            if (!night)
            {
                night = true;
                run.Hud.Banner("The Endless Night", "Healing stops, and your HP drains faster and faster");
                run.Audio.Alarm();
            }
            Player p = run.Player;
            p.Hp -= p.Stats.MaxHp * NightDrain * (1 + (Elapsed - EndlessNight) / 60) * dt;
            if (p.Hp <= 0)
            {
                p.Hp = 1;
                p.Iframes = 0;
                p.Hurt(1e7f);
            }
        }

        void OfferPact()
        {
            var pool = new List<Pact>(Data.Pacts);
            var cards = new List<Card>();
            for (int k = 0; k < 3; k++)
            {
                int at = M.RangeInt(0, pool.Count);
                Pact pact = pool[at];
                pool.RemoveAt(at);
                cards.Add(new Card
                {
                    Key = "pact",
                    Title = pact.Name,
                    Tag = "Pact",
                    Color = 0xff5da2,
                    Desc = pact.Desc,
                    Run = () =>
                    {
                        run.Player.AddStat(pact.Gain.Stat, pact.Gain.Mul, pact.Gain.Value);
                        run.Heat += pact.Heat;
                    },
                });
            }
            run.QueueChoice(() => new Choice { Title = "Choose a pact", Sub = "Power now, in exchange for a harder horde.", Cards = cards, CloseLabel = "Refuse" });
        }

        public int BossIndex()
        {
            if (BossUid <= 0) return -1;
            Enemies e = run.Enemies;
            for (int i = 0; i < e.Count; i++) if (e.Uid[i] == BossUid && !e.Dead[i]) return i;
            return -1;
        }

        public void SummonAdds(float x, float z, int n)
        {
            List<EnemyDef> cards = Cards();
            for (int k = 0; k < n; k++)
            {
                EnemyDef def = M.Pick(cards);
                float px = x + M.Range(-4, 4);
                float pz = z + M.Range(-4, 4);
                if (run.Terrain.InBounds(px, pz, 3)) run.Enemies.Spawn(def.Id, px, pz, HpMult, DmgMult);
            }
        }

        public void Update(float dt)
        {
            if (Phase == Phase.Cleared) return;
            Elapsed += dt;
            if (Phase == Phase.Zone)
            {
                T -= dt;
                if (T <= 0) SummonGuardian(false);
            }

            if (Phase == Phase.Survive) UpdateSurvival(dt);
            if (run.Mode == GameMode.Survival) UpdateNight(dt);

            // The horde thins out during boss fights but never stops.
            bool fighting = Phase == Phase.Guardian || Phase == Phase.Final;
            credits = M.Min(60, credits + CreditRate * (fighting ? 0.45f : 1) * dt);
            int guard = 0;
            while (credits >= 1 && run.Enemies.Count < SpawnCap && guard++ < 6) SpawnPack();

            if (!fighting)
            {
                eliteT -= dt;
                if (eliteT <= 0)
                {
                    eliteT = EliteEvery;
                    SpawnElite();
                }
            }

            // BossUid stays at -1 during the pause before the final boss arrives.
            if (fighting && BossUid > 0 && BossIndex() < 0) OnBossDown();
        }

        void OnBossDown()
        {
            Hud hud = run.Hud;
            Pickups pickups = run.Pickups;
            if (run.Mode == GameMode.Survival)
            {
                BossUid = 0;
                hud.HideBoss();
                run.Audio.LevelUp();
                pickups.Vacuum();
                run.Profile.Add("guardians");
                run.Earn(GuardianShards);
                run.PendingLevels++;
                run.Encounters.QueueItemChoice("Milestone reward", "Choose one", 1);
                OfferPact();
                hud.Banner("Milestone cleared", "The horde keeps coming");
                Phase = Phase.Survive;
                return;
            }
            BossUid = 0;
            if (Phase == Phase.Guardian)
            {
                run.Profile.Add("guardians");
                run.Earn(GuardianShards);
            }
            else if (Phase == Phase.Final)
            {
                run.Earn(FinalBossShards);
            }
            hud.HideBoss();
            run.Audio.LevelUp();
            pickups.Vacuum();
            if (Phase == Phase.Final)
            {
                Phase = Phase.Cleared;
                run.After(1.4f, run.OnVictory);
                return;
            }
            run.Encounters.QueueItemChoice("Guardian reward", "Choose one", 1);
            run.PendingLevels++;
            if (run.Zone >= Data.ZoneCount)
            {
                Phase = Phase.Final;
                hud.Banner("The ground shakes", "Something is coming");
                BossUid = -1;
                run.After(3, () =>
                {
                    BossUid = 0;
                    SpawnBoss(Data.FinalBoss, FinalHp, 2.6f);
                });
                return;
            }
            Phase = Phase.Cleared;
            float x = run.Player.X + 6;
            float z = run.Player.Z;
            run.Terrain.PushOut(ref x, ref z, 2);
            pickups.SpawnPortal(x, z);
            hud.Banner("Zone clear", "Finish up here, then walk into the portal");
        }

        // Lowers the live enemy cap when frames drop, raises it again once they recover.
        public void SampleFps(float realDt)
        {
            fpsAcc += realDt;
            fpsFrames++;
            if (fpsAcc < 2) return;
            float fps = fpsFrames / fpsAcc;
            fpsAcc = 0;
            fpsFrames = 0;
            if (fps < FpsLow && run.Enemies.Count >= LiveCap * 0.85f)
            {
                goodStreak = 0;
                LiveCap = Math.Max(LiveFloor, (int)Math.Round(LiveCap * 0.85f));
            }
            else if (fps >= FpsHigh && LiveCap < LiveMax)
            {
                if (++goodStreak >= 3)
                {
                    goodStreak = 0;
                    LiveCap = Math.Min(LiveMax, LiveCap + 30);
                }
            }
            else
            {
                goodStreak = 0;
            }
        }
    }
}
