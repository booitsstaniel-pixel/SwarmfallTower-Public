using System;
using System.Collections.Generic;
using System.Windows.Forms;

namespace Swarmfall
{
    enum GameState { Menu, Play, Choice, Paused, Dead, Won }

    // Keyboard and mouse state. Hit is true only on the frame a key went down.
    sealed class Input
    {
        readonly HashSet<Keys> keys = new HashSet<Keys>();
        readonly HashSet<Keys> hits = new HashSet<Keys>();
        public float LookX, LookY;
        public int MouseX, MouseY;
        public bool Clicked;

        public void KeyDown(Keys k)
        {
            if (keys.Add(k)) hits.Add(k);
        }
        public void KeyUp(Keys k) { keys.Remove(k); }
        public void ClearKeys() { keys.Clear(); }
        public bool Down(Keys k) { return keys.Contains(k); }
        public bool Hit(Keys k) { return hits.Contains(k); }
        public void EndFrame()
        {
            hits.Clear();
            LookX = LookY = 0;
            Clicked = false;
        }
    }

    sealed class TimedAction
    {
        public float T;
        public Action Fn;
    }

    // Owns one playthrough: input, simulation, camera and game states. Draws nothing itself,
    // so the whole game can also run headless for the --selftest mode.
    sealed class Run
    {
        const float MaxFrame = 1 / 20f;
        const float HitstopSpeed = 0.08f;
        const float StreakWindow = 3;
        const float MouseSens = 0.0024f;
        const int SkipGold = 20;
        const float XpScale = 0.65f;
        const float WetDuration = 4, ReactionCooldown = 0.6f;

        public readonly Input Input = new Input();
        public readonly Audio Audio = new Audio();
        public readonly Vfx Vfx = new Vfx();
        public readonly Atlas Atlas = new Atlas();
        public readonly Enemies Enemies;
        public readonly Projectiles Projectiles;
        public readonly Pickups Pickups;
        public readonly Encounters Encounters;
        public readonly Director Director;
        public readonly Capture Capture;
        public readonly Hud Hud;
        public readonly List<Zone> Zones = new List<Zone>();
        readonly List<TimedAction> timers = new List<TimedAction>();
        readonly Queue<Func<Choice>> choices = new Queue<Func<Choice>>();
        // Set when the player presses the interact key at the portal; travel happens once nothing is waiting.
        bool enterPortal;

        public Terrain Terrain;
        public int TerrainVersion;
        public Player Player;
        public GameState State = GameState.Menu;
        public GameMode Mode;
        public int DifficultyIndex;
        public Difficulty Diff { get { return Data.Difficulties[DifficultyIndex]; } }
        float shardCarry;
        public float Time, Heat, LootMult = 1;
        public int Zone = 1, Money, Kills, Streak, PendingLevels;
        float streakT, lastBolt;
        public bool GodMode;
        public float CamYaw = 0.5f, CamPitch = 0.5f, CamDist = 9;
        public float CamTx, CamTy, CamTz, Trauma, HitstopT, MenuT;
        public float ShakeX, ShakeY;
        public int TargetUid;
        readonly int biomeOffset = M.RangeInt(0, Data.Biomes.Length);
        public bool WantsMouseLook;

        public readonly Profile Profile;
        // What this run unlocked, for the end screen.
        public readonly List<string> RunNews = new List<string>();
        float profileCheckT;
        // Shards earned this run. They are banked as they come, so quitting early keeps them.
        public int RunShards;
        const int KillsPerShard = 50;
        const int ZoneShards = 15;
        const int WinShards = 50;
        const int SurvivalMinuteShards = 5;

        public Run() : this(Profile.InMemory()) { }

        public Run(Profile profile)
        {
            Profile = profile;
            // The menu remembers the last mode and difficulty you chose.
            Mode = Profile.Get("lastMode") == 1 ? GameMode.Survival : GameMode.Classic;
            DifficultyIndex = (int)Math.Min(Profile.Get("lastDifficulty"), Data.Difficulties.Length - 1);
            foreach (Partner p in Data.Partners) foreach (Stage st in p.AllStages()) Atlas.AddCreature(st.Id, st.Art);
            foreach (EnemyDef e in Data.Enemies) Atlas.AddCreature(e.Id, e.Art);
            foreach (EnemyDef e in Data.Bosses) Atlas.AddCreature(e.Id, e.Art);
            Atlas.AddCreature(Data.FinalBoss.Id, Data.FinalBoss.Art);
            Atlas.AddCreature("merchant", Data.MerchantArt);
            Atlas.AddCreature("drone", Data.DroneArt);
            Atlas.AddIcon("disc", "disc", 0xffffff, 0xffffff);
            Atlas.AddIcon("orb", "orb", 0xffffff, 0xffffff);
            Atlas.AddIcon("gem", "gem", 0x49e0c8, 0x49e0c8);
            Atlas.AddIcon("coin", "coin", 0xffc53b, 0xffc53b);
            Atlas.AddIcon("heart", "heart", 0xff5a7a, 0xff5a7a);
            Atlas.AddIcon("chest", "chest", 0xb5763a, 0xffd24a);
            Atlas.AddIcon("cursed", "chest", 0x4a2f6f, 0xb98cff);
            Atlas.AddIcon("altarTrade", "altar", 0x6a7a8a, 0x49e0c8);
            Atlas.AddIcon("altarRisk", "altar", 0x6a5a6a, 0xff4a5e);
            Atlas.AddIcon("altarCharge", "altar", 0x8a8070, 0xffd24a);
            Atlas.AddIcon("pod", "pod", 0xffa63b, 0x5a3b2a);
            Atlas.AddIcon("machine", "machine", 0x5a6a9a, 0x49e0c8);
            Atlas.AddIcon("fossil", "fossil", 0xb09a7a, 0xb09a7a);
            Atlas.AddIcon("stone", "stone", 0x6a6280, 0xff5da2);
            Atlas.AddIcon("portal", "portal", 0x8a5cff, 0x49e0c8);

            Enemies = new Enemies(this);
            foreach (EnemyDef e in Data.Enemies) Enemies.Register(e);
            foreach (EnemyDef e in Data.WildOnly) Enemies.Register(e);
            foreach (EnemyDef e in Data.Bosses) Enemies.Register(e);
            Enemies.Register(Data.FinalBoss);
            Projectiles = new Projectiles(this);
            Pickups = new Pickups(this);
            Encounters = new Encounters(this);
            Director = new Director(this);
            Capture = new Capture(this);
            Hud = new Hud(this);
            Terrain = new Terrain(7, Data.Biomes[0]);
            TerrainVersion++;
        }

        public void Start(Partner partner)
        {
            Player = new Player(this, partner);
            Profile.Add("runs");
            Profile.LastPartner = partner.Id;
            Profile.Save();
            RunNews.Clear();
            RunShards = 0;
            Time = 0;
            Money = 0;
            Kills = 0;
            Streak = 0;
            Heat = 0;
            LootMult = 1;
            PendingLevels = 0;
            choices.Clear();
            LoadZone(1);
            State = GameState.Play;
            WantsMouseLook = true;
        }

        public void LoadZone(int n)
        {
            Zone = n;
            Biome biome = Data.Biomes[(n - 1 + biomeOffset) % Data.Biomes.Length];
            uint seed = (uint)(100 + n * 37 + biomeOffset * 13);
            Terrain = new Terrain(seed, biome);
            TerrainVersion++;
            Enemies.Clear();
            Projectiles.Clear();
            Pickups.Clear();
            Zones.Clear();
            timers.Clear();
            Encounters.Populate(n, seed);
            if (Mode == GameMode.Survival) Encounters.Items.RemoveAll(o => o.Type == EncKind.Stone);
            Director.Reset();
            Capture.Reset();
            Player p = Player;
            p.X = 0;
            p.Z = 0;
            p.Y = Terrain.HeightAt(0, 0);
            p.Vx = p.Vy = p.Vz = 0;
            p.Apex = p.Y;
            p.Diving = false;
            CamTx = p.X;
            CamTy = p.Y;
            CamTz = p.Z;
            Hud.HideBoss();
            Hud.Banner(Mode == GameMode.Survival ? "Survival - " + biome.Name : "Zone " + n + " - " + biome.Name, Mode == GameMode.Survival ? "A boss every 5 minutes, and the horde never stops" : "Explore, get stronger, then face the guardian");
        }

        public void After(float delay, Action fn) { timers.Add(new TimedAction { T = delay, Fn = fn }); }
        public void AddTrauma(float v) { Trauma = M.Min(1, Trauma + v); }
        public void Freeze(float seconds) { HitstopT = M.Max(HitstopT, seconds); }

        public float ShopPrice(string tier)
        {
            float price = tier == "legend" ? 320 : tier == "epic" ? 150 : tier == "rare" ? 70 : 30;
            return price * (1 + (Zone - 1) * 0.35f);
        }

        // All player damage goes through here, so crits, stats and on-hit items apply once.
        public bool DamageEnemy(int i, float amount, string elem, HitOpts opts)
        {
            // An immune target takes nothing, so it can't react, crit or feed lifesteal.
            if (Enemies.TypeMultiplier(i, elem) == 0) return Enemies.Hit(i, amount, elem, opts);
            Stats stats = Player.Stats;
            bool crit = M.Rand() < stats.Crit + opts.CritBonus;
            float dmg = crit ? amount * stats.CritDmg : amount;
            int bonk = Player.ItemCount("bonk-stick");
            if (bonk > 0 && M.Rand() < 0.03f * bonk)
            {
                dmg *= 8;
                Hud.Text(Enemies.X[i], Enemies.Y[i] + Enemies.Hgt[i] + 1, Enemies.Z[i], "Bonk!", Hud.TextCrit);
            }
            float x = Enemies.X[i], z = Enemies.Z[i];
            dmg = ApplyReaction(i, elem, dmg);
            opts.Crit = crit;
            if (!Enemies.Hit(i, dmg, elem, opts)) return false;
            if (stats.Lifesteal > 0 && !Player.IsDead && !Director.HealingBlocked) Player.Hp = M.Min(stats.MaxHp, Player.Hp + dmg * stats.Lifesteal);
            if (crit && Player.ItemCount("storm-coil") > 0) CritBolt(x, z);
            return true;
        }

        // Hitting a target that already carries a status can set off a reaction with the new element.
        // Returns the damage, raised if a reaction happened.
        float ApplyReaction(int i, string elem, float dmg)
        {
            Enemies e = Enemies;
            if (elem == null) return dmg;
            float result = dmg;
            if (e.ReactT[i] <= 0)
            {
                string name = null;
                uint color = 0xffffff;
                if (e.WetT[i] > 0 && elem == "volt")
                {
                    name = "Shock!";
                    color = Data.ElemColor("volt");
                    result = dmg * 1.6f;
                    e.WetT[i] = 0;
                    Splash(i, 4.5f, dmg * 0.5f, new HitOpts { Stun = 0.35f });
                }
                else if (e.WetT[i] > 0 && elem == "fire")
                {
                    name = "Evaporate!";
                    color = 0xf4efff;
                    result = dmg * 1.6f;
                    e.WetT[i] = 0;
                }
                else if (e.WetT[i] > 0 && elem == "leaf")
                {
                    name = "Bloom!";
                    color = Data.ElemColor("leaf");
                    result = dmg * 1.25f;
                    e.WetT[i] = 0;
                    e.SlowT[i] = M.Max(e.SlowT[i], 2.5f);
                    if (!Director.HealingBlocked) Player.Hp = M.Min(Player.Stats.MaxHp, Player.Hp + Player.Stats.MaxHp * 0.015f);
                }
                else if (e.BurnT[i] > 0 && elem == "water")
                {
                    name = "Steam!";
                    color = 0xf4efff;
                    result = dmg * 1.4f;
                    e.BurnT[i] = 0;
                    Splash(i, 4, dmg * 0.4f, new HitOpts { Stun = 0.45f });
                }
                else if (e.BurnT[i] > 0 && elem == "leaf")
                {
                    name = "Wildfire!";
                    color = Data.ElemColor("fire");
                    result = dmg * 1.3f;
                    Splash(i, 4.5f, dmg * 0.25f, new HitOpts { Burn = 3 });
                }
                else if (e.StunT[i] > 0 && elem == "stone")
                {
                    name = "Shatter!";
                    color = Data.ElemColor("stone");
                    result = dmg * 1.8f;
                    e.StunT[i] = 0;
                }
                if (name != null)
                {
                    e.ReactT[i] = ReactionCooldown;
                    Profile.Add("reactions");
                    Hud.Text(e.X[i], e.Y[i] + e.Hgt[i] + 1, e.Z[i], name, Hud.TextCrit);
                    Vfx.RingFx(e.X[i], e.Y[i], e.Z[i], 3, color, 0.3f, 0.2f);
                    Vfx.Burst(e.X[i], e.Y[i] + e.Hgt[i] * 0.5f, e.Z[i], color, 12, 6, 0.3f, 0.4f);
                }
            }
            if (elem == "water") e.WetT[i] = M.Max(e.WetT[i], WetDuration);
            return result;
        }

        // Hits everything near an enemy, except that enemy. These hits have no element, so they can't chain.
        void Splash(int i, float radius, float dmg, HitOpts opts)
        {
            Enemies e = Enemies;
            int uid = e.Uid[i];
            e.Query(e.X[i], e.Z[i], radius, j =>
            {
                if (e.Uid[j] != uid) e.Hit(j, dmg, null, opts);
            });
        }

        // Lightning jumps from a crit to another enemy. Rate-limited so it cannot chain out of control.
        void CritBolt(float x, float z)
        {
            if (Time - lastBolt < 0.2f) return;
            int target = -1;
            Enemies.Query(x, z, 8, j =>
            {
                if (target < 0 && (Enemies.X[j] != x || Enemies.Z[j] != z)) target = j;
            });
            if (target < 0) return;
            lastBolt = Time;
            float dmg = (12 + Player.Level * 2) * Player.ItemCount("storm-coil") * Player.Stats.Might;
            Vfx.BeamFx(Enemies.X[target], Enemies.Y[target] + 14, Enemies.Z[target], Enemies.X[target], Enemies.Y[target], Enemies.Z[target], Data.ElemColor("volt"), 0.45f, 0.16f);
            Enemies.Hit(target, dmg, "volt", new HitOpts { Stun = 0.2f });
        }

        public void Blast(float x, float y, float z, float r, float dmg, string elem, uint color, float knock, Status st, float critBonus, string secondElem = null)
        {
            Enemies.Query(x, z, r, i =>
            {
                float dx = Enemies.X[i] - x;
                float dz = Enemies.Z[i] - z;
                float d = M.Hypot(dx, dz);
                if (d < 0.001f) d = 0.001f;
                EnemyDef def = Enemies.Kinds[Enemies.Kind[i]];
                string used = Data.BestElem(elem, secondElem, def.Elem, def.Elem2);
                DamageEnemy(i, dmg, used, HitOpts.From(st, dx / d * knock, dz / d * knock, critBonus));
            });
            Vfx.RingFx(x, y, z, r, color, 0.35f, 0.2f);
            Vfx.Burst(x, y + 0.6f, z, color, M.Min(30, 8 + r * 3), r * 1.6f, 0.34f, 0.5f);
            AddTrauma(M.Min(0.22f, 0.05f + r * 0.012f));
            Audio.Boom(0.35f + r * 0.03f);
        }

        // Delayed attack that warns the player with a danger zone first.
        public void EnemyBlast(float x, float z, float r, float delay, float dmg, uint color)
        {
            float y = Terrain.HeightAt(x, z);
            Vfx.Telegraph(x, y, z, r, color, delay);
            After(delay, () =>
            {
                if (M.Hypot(Player.X - x, Player.Z - z) < r + Player.Radius && Player.Y - y < 2) Player.Hurt(dmg);
                Vfx.RingFx(x, y, z, r, color, 0.3f);
                Vfx.Burst(x, y + 0.4f, z, color, 14, 7, 0.3f);
                AddTrauma(0.15f);
                Audio.Boom(0.5f);
            });
        }

        public void OnEnemyKilled(int i, EnemyDef def)
        {
            float x = Enemies.X[i], y = Enemies.Y[i], z = Enemies.Z[i];
            Kills++;
            Profile.Add("kills");
            if (Kills % KillsPerShard == 0) Earn(1);
            Streak++;
            streakT = StreakWindow;
            Player.Ult = M.Min(1, Player.Ult + 0.012f * Player.Stats.UltRate);

            int flags = Enemies.Flags[i];
            bool elite = (flags & Flag.Elite) != 0;
            bool boss = (flags & Flag.Boss) != 0;
            bool big = elite || boss;
            Pickups.Spawn(Pickups.Gem, x, y, z, Enemies.Xp[i] * LootMult * XpScale);
            if (M.Rand() < (big ? 1 : 0.38f)) Pickups.Spawn(Pickups.Coin, x, y, z, big ? 12 : 1 + Zone / 2);
            if (M.Rand() < (big ? 0.5f : 0.012f)) Pickups.Spawn(Pickups.Heart, x, y, z, 0);
            if (elite && M.Rand() < 0.3f * LootMult) Encounters.DropChest(x, z);

            if (Player.ItemCount("reaper-sash") > 0 && Kills % 100 == 0 && Kills <= 3000) Player.Stats.Might *= 1.01f;
            int core = Player.ItemCount("ember-core");
            if (core > 0 && Enemies.BurnT[i] > 0 && M.Rand() < 0.5f)
            {
                After(0, () => Blast(x, y, z, 3.5f, (10 + Player.Level * 1.5f) * core * Player.Stats.Might, "fire", Data.ElemColor("fire"), 2, new Status(2, 0, 0), 0));
            }

            uint color = Data.ElemColor(def.Elem);
            Vfx.Burst(x, y + Enemies.Hgt[i] * 0.5f, z, color, big ? 40 : 9, big ? 9 : 5, big ? 0.5f : 0.28f, 0.5f);
            if (big)
            {
                Freeze(boss ? 0.22f : 0.07f);
                AddTrauma(boss ? 0.8f : 0.3f);
                Audio.Boom(boss ? 1.6f : 0.8f);
            }
            if (Streak % 25 == 0)
            {
                int gold = Math.Min(8, 2 + Streak / 25);
                Money += gold;
                Player.Heal(Player.Stats.MaxHp * 0.05f);
                Pickups.Vacuum();
                Hud.Toast("Streak x" + Streak + ": +" + gold + " gold and pickups pulled in");
            }
        }

        public void OnPlayerDead()
        {
            State = GameState.Dead;
            WantsMouseLook = false;
            EarnZones();
            CheckChallenges();
            Profile.Save();
            Hud.Opts = null;
        }

        public void OnVictory()
        {
            if (State == GameState.Dead || State == GameState.Won) return;
            State = GameState.Won;
            WantsMouseLook = false;
            Profile.Add("wins");
            Profile.Add(Diff.WinCounter);
            Earn(WinShards);
            EarnZones();
            CheckChallenges();
            Profile.Save();
            Hud.Opts = null;
        }

        public void Pause()
        {
            State = GameState.Paused;
            WantsMouseLook = false;
        }

        public void Resume()
        {
            State = GameState.Play;
            WantsMouseLook = true;
        }

        public void Earn(int shards)
        {
            // Difficulty scales rewards. The fraction carries over so small rewards aren't lost to rounding.
            shardCarry += shards * Diff.Shards;
            int whole = (int)shardCarry;
            shardCarry -= whole;
            RunShards += whole;
            Profile.Add("shards", whole);
        }

        // A reward for how far the run got, paid once when it ends.
        void EarnZones()
        {
            if (Mode == GameMode.Survival)
            {
                Earn((int)(Time / 60) * SurvivalMinuteShards);
                Profile.Best("survivalBest", (long)Time);
                Profile.Best("survivalBest" + Diff.Name, (long)Time);
                return;
            }
            Earn(Zone * ZoneShards);
        }

        void CheckChallenges()
        {
            foreach (Partner p in Profile.TakeNewlyReady())
            {
                string news = "Challenge complete: " + p.Name + " can now appear in the wild";
                RunNews.Add(news);
                Hud.Toast(news);
            }
        }

        // A choice is built when it opens, not when queued, so it reflects the current build.
        public void QueueChoice(Func<Choice> build) { choices.Enqueue(build); }

        void OpenChoice(Choice opts)
        {
            // Everything can be maxed out late in a run. With nothing to offer, pay out instead of showing an empty screen.
            if (opts.Cards.Count == 0)
            {
                Money += SkipGold;
                Hud.Toast("Nothing left to offer: +" + SkipGold + " gold");
                return;
            }
            State = GameState.Choice;
            WantsMouseLook = false;
            Hud.ShowCards(opts);
        }

        public void CloseChoice()
        {
            Hud.Opts = null;
            State = GameState.Play;
            WantsMouseLook = true;
        }

        void OfferLevelUp()
        {
            Audio.LevelUp();
            OpenChoice(new Choice { Title = "Level " + (Player.Level - PendingLevels + 1), Sub = "Choose one", Cards = Upgrades.RollLevelCards(Player), Tools = true });
            PendingLevels--;
        }

        // Level-up tools: limited uses per run of Refresh, Skip and Banish.
        public void UseRefresh()
        {
            if (Player.Refreshes < 1) return;
            Player.Refreshes--;
            ReplaceCards();
        }

        void ReplaceCards()
        {
            Hud.Opts.Cards = Upgrades.RollLevelCards(Player);
            Hud.Banishing = false;
            if (Hud.Opts.Cards.Count == 0) CloseChoice();
        }

        public void UseSkip()
        {
            if (Player.Skips < 1) return;
            Player.Skips--;
            Money += SkipGold;
            CloseChoice();
        }

        public void UseBanish(Card card)
        {
            if (Player.Banishes < 1 || card == null) return;
            Player.Banishes--;
            Player.Banished.Add(card.Key);
            ReplaceCards();
        }

        public void Simulate(float dt)
        {
            Time += dt;
            if (Input.Hit(Keys.R)) Player.Ultimate();
            if (Input.Hit(Keys.E))
            {
                if (Pickups.NearPortal(Player.X, Player.Z)) enterPortal = true;
                else if (!Capture.TryThrow()) Encounters.Interact();
            }

            Player.Update(dt);
            Enemies.Update(dt);
            Moves.Update(this, dt);
            Moves.UpdateZones(this, dt);
            Projectiles.Update(dt);
            Pickups.Update(dt);
            Encounters.Update(dt);
            Director.Update(dt);
            Capture.Update(dt);

            profileCheckT -= dt;
            if (profileCheckT <= 0)
            {
                profileCheckT = 1;
                CheckChallenges();
            }

            for (int k = timers.Count - 1; k >= 0; k--)
            {
                TimedAction t = timers[k];
                t.T -= dt;
                if (t.T > 0) continue;
                timers.RemoveAt(k);
                t.Fn();
            }

            if (streakT > 0)
            {
                streakT -= dt;
                if (streakT <= 0) Streak = 0;
            }

            int target = Enemies.Nearest(Player.X, Player.Z, 20);
            TargetUid = target >= 0 ? Enemies.Uid[target] : 0;

            if (!Pickups.NearPortal(Player.X, Player.Z)) enterPortal = false;
            if (enterPortal && choices.Count == 0 && PendingLevels == 0)
            {
                enterPortal = false;
                LoadZone(Zone + 1);
                return;
            }

            if (State == GameState.Play && Player.CanEvolve) Player.Evolve();
            if (State == GameState.Play && Player.NeedsForm) OpenChoice(Player.FormChoice());

            if (State == GameState.Play)
            {
                if (choices.Count > 0) OpenChoice(choices.Dequeue()());
                else if (PendingLevels > 0) OfferLevelUp();
            }
        }

        void UpdateCamera(float realDt)
        {
            if (State == GameState.Play)
            {
                CamYaw -= Input.LookX * MouseSens;
                CamPitch = M.Clamp(CamPitch + Input.LookY * MouseSens, 0.12f, 1.15f);
                if (Input.Down(Keys.Q)) CamYaw += realDt * 1.8f;
                if (Input.Down(Keys.C)) CamYaw -= realDt * 1.8f;
            }
            if (Player != null && State != GameState.Menu)
            {
                CamTx = M.Damp(CamTx, Player.X, 18, realDt);
                CamTz = M.Damp(CamTz, Player.Z, 18, realDt);
                CamTy = M.Damp(CamTy, Player.Y, 10, realDt);
                CamDist = 9;
            }
            else
            {
                MenuT += realDt;
                CamYaw = MenuT * 0.08f;
                CamPitch = 0.28f;
                CamDist = 24;
            }
            Trauma = M.Max(0, Trauma - realDt * 1.5f);
            float shake = Trauma * Trauma;
            ShakeX = M.Range(-1, 1) * shake * 0.7f;
            ShakeY = M.Range(-1, 1) * shake * 0.5f;
        }

        // Advances one frame of game logic. Rendering is done separately by the caller.
        public void Frame(float rawDt)
        {
            float realDt = M.Min(rawDt, MaxFrame);
            if ((Input.Hit(Keys.Escape) || Input.Hit(Keys.P)) && (State == GameState.Play || State == GameState.Paused))
            {
                if (State == GameState.Play) Pause();
                else Resume();
            }
            if (Input.Hit(Keys.M)) Audio.Muted = !Audio.Muted;
            if (State == GameState.Choice)
            {
                if (Input.Hit(Keys.D1)) Hud.ChooseCard(0);
                else if (Input.Hit(Keys.D2)) Hud.ChooseCard(1);
                else if (Input.Hit(Keys.D3)) Hud.ChooseCard(2);
                else if (Input.Hit(Keys.D4)) Hud.ChooseCard(3);
                else if (Input.Hit(Keys.Escape)) Hud.CloseButton();
                else if (Hud.Opts != null && Hud.Opts.Tools)
                {
                    if (Input.Hit(Keys.R)) UseRefresh();
                    else if (Input.Hit(Keys.X)) UseSkip();
                    else if (Input.Hit(Keys.B)) Hud.ToggleBanish();
                }
            }

            if (HitstopT > 0) HitstopT -= realDt;
            float dt = realDt * (HitstopT > 0 ? HitstopSpeed : 1);

            if (State == GameState.Play)
            {
                Director.SampleFps(rawDt);
                Simulate(dt);
            }
            Vfx.Update(dt);
            UpdateCamera(realDt);
            Hud.Update(realDt);
        }
    }
}
