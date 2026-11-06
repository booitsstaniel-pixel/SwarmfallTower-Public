using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace Swarmfall
{
    // A simple player for automated testing: it walks to encounters, takes the first offer it can,
    // and never dodges on purpose. Real players should do better, so treat its results as a floor.
    sealed class Bot
    {
        readonly Run run;
        readonly bool adventurous;
        int frame;
        readonly HashSet<Encounter> visited = new HashSet<Encounter>();

        public Bot(Run run, bool adventurous)
        {
            this.run = run;
            this.adventurous = adventurous;
        }

        void Tap(Keys k)
        {
            run.Input.KeyUp(k);
            run.Input.KeyDown(k);
        }

        public void Step()
        {
            frame++;
            Player p = run.Player;
            if (run.State == GameState.Choice)
            {
                Choice o = run.Hud.Opts;
                if (o.Tools)
                {
                    int level = o.Cards.FindIndex(c => c.Key.StartsWith("level:"));
                    run.Hud.ChooseCard(level >= 0 ? level : 0);
                    return;
                }
                int i = o.Cards.FindIndex(c => !c.Disabled);
                if (i >= 0) run.Hud.ChooseCard(i);
                else run.Hud.CloseButton();
                return;
            }
            if (run.State != GameState.Play) return;

            int ready = 0;
            foreach (Move m in p.Moves) if (m.Def.Kind != Kind.Fusion && m.Level >= Moves.FuseLevel) ready++;
            float goalX = 0, goalZ = 0;
            bool hasGoal = false;
            Encounter goalEnc = null;
            float wildX, wildZ;
            bool chaseWild = run.Capture.State == CaptureState.Downed || (adventurous && run.Capture.State == CaptureState.Roaming);
            if (chaseWild && run.Capture.Locate(out wildX, out wildZ))
            {
                goalX = wildX;
                goalZ = wildZ;
                hasGoal = true;
                if (run.Capture.State == CaptureState.Downed && frame % 10 == 0) Tap(Keys.E);
            }
            else if (run.Pickups.HasPortal)
            {
                goalX = run.Pickups.PortalX;
                goalZ = run.Pickups.PortalZ;
                hasGoal = true;
                // The portal needs the interact key.
                if (run.Pickups.NearPortal(p.X, p.Z) && frame % 10 == 0) Tap(Keys.E);
            }
            else
            {
                float best = 1e9f;
                foreach (Encounter o in run.Encounters.Items)
                {
                    if (o.Used || o.Type == EncKind.Stone) continue;
                    if (!adventurous && (o.Type == EncKind.Risk || o.Type == EncKind.Cursed || o.Type == EncKind.Fossil || o.Type == EncKind.Merchant)) continue;
                    if (o.Type == EncKind.Machine && ready < 2) continue;
                    if (o.Type == EncKind.Merchant && (run.Money < 80 || visited.Contains(o))) continue;
                    if (o.Type == EncKind.Chest && o.Cost > run.Money) continue;
                    float d = M.Hypot(o.X - p.X, o.Z - p.Z);
                    if (d < best)
                    {
                        best = d;
                        goalEnc = o;
                        goalX = o.X;
                        goalZ = o.Z;
                        hasGoal = true;
                    }
                }
            }
            Input input = run.Input;
            input.KeyUp(Keys.W);
            input.KeyUp(Keys.A);
            input.KeyUp(Keys.S);
            input.KeyUp(Keys.D);
            run.CamYaw = 0;
            if (hasGoal)
            {
                float dx = goalX - p.X, dz = goalZ - p.Z;
                float d = M.Hypot(dx, dz);
                bool hold = goalEnc != null && goalEnc.Type == EncKind.Charge && d < 3;
                if (!hold)
                {
                    if (dz < -0.6f) input.KeyDown(Keys.W);
                    if (dz > 0.6f) input.KeyDown(Keys.S);
                    if (dx < -0.6f) input.KeyDown(Keys.A);
                    if (dx > 0.6f) input.KeyDown(Keys.D);
                }
                if (d < 2.5f && goalEnc != null && goalEnc.Type != EncKind.Charge && goalEnc.Type != EncKind.Pod)
                {
                    Tap(Keys.E);
                    visited.Add(goalEnc);
                }
            }
            else
            {
                float a = frame / 200f;
                input.KeyDown(M.Sin(a) > 0 ? Keys.W : Keys.S);
                input.KeyDown(M.Cos(a) > 0 ? Keys.D : Keys.A);
            }
            if (frame % 120 == 0) Tap(Keys.R);
            if (frame % 47 == 0) Tap(Keys.ShiftKey);
            if (frame % 311 == 0) Tap(Keys.Space);
        }
    }

    // Headless playthroughs. Plays full runs with no window and writes a report, so game logic
    // can be checked by an automated tool: Swarmfall.exe --selftest report.txt
    static class SelfTest
    {
        public static int Run_(string path)
        {
            var report = new StringBuilder();
            bool ok = true;
            foreach (Partner partner in Data.Partners)
            {
                if (!partner.Starter && partner.Id != "skitter" && partner.Id != "burrowkit" && partner.Id != "sleach" && partner.Forms == null) continue;
                foreach (bool god in new[] { false, true })
                {
                    ok &= PlayRun(partner, god, report);
                }
            }
            ok &= CheckFusions(report);
            ok &= CheckSheets(report);
            ok &= CheckEvolution(report);
            ok &= CheckCapture(report);
            ok &= CheckSave(report);
            ok &= CheckPortalSwarm(report);
            ok &= CheckStarterChoice(report);
            ok &= CheckSlimeForms(report);
            ok &= CheckRoster(report);
            ok &= CheckUpgrades(report);
            ok &= CheckSecondTypes(report);
            ok &= CheckTypeChart(report);
            ok &= CheckReactions(report);
            ok &= CheckDifficulty(report);
            ok &= CheckSurvival(report);
            ok &= CheckUpdater(report);
            report.AppendLine(ok ? "RESULT: PASS" : "RESULT: FAIL");
            File.WriteAllText(path, report.ToString());
            return ok ? 0 : 1;
        }

        static bool PlayRun(Partner partner, bool god, StringBuilder report)
        {
            var run = new Run();
            run.Start(partner);
            run.GodMode = god;
            var bot = new Bot(run, god);
            string phaseLast = "";
            float lowest = 1;
            int peak = 0;
            float fusionAt = -1;
            report.AppendLine("--- " + partner.Name + (god ? " (invincible, tries every encounter)" : " (mortal, plays it safe)"));
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int frames = 0;
            try
            {
                for (int f = 0; f < 150000 && run.State != GameState.Won && run.State != GameState.Dead; f++)
                {
                    bot.Step();
                    run.Frame(1 / 60f);
                    run.Input.EndFrame();
                    frames++;
                    lowest = M.Min(lowest, run.Player.Hp / run.Player.Stats.MaxHp);
                    peak = Math.Max(peak, run.Enemies.Count);
                    if (fusionAt < 0 && run.Player.Moves.Exists(m => m.Def.Kind == Kind.Fusion)) fusionAt = run.Time;
                    string phase = run.Zone + ":" + run.Director.Phase;
                    if (phase != phaseLast)
                    {
                        report.AppendLine("  " + phase + " t=" + run.Time.ToString("0") + " lv" + run.Player.Level + " kills=" + run.Kills + " gold=" + run.Money + " lowestHp=" + (lowest * 100).ToString("0") + "%");
                        phaseLast = phase;
                        lowest = 1;
                    }
                    Player p = run.Player;
                    if (float.IsNaN(p.X + p.Y + p.Z) || float.IsInfinity(p.X + p.Y + p.Z)) throw new InvalidOperationException("Player position became invalid");
                }
            }
            catch (Exception e)
            {
                report.AppendLine("  ERROR: " + e);
                return false;
            }
            var moves = new List<string>();
            foreach (Move m in run.Player.Moves) moves.Add(m.Def.Name + ":" + m.Level);
            report.AppendLine("  END " + run.State + " zone=" + run.Zone + " time=" + run.Time.ToString("0") + " level=" + run.Player.Level + " kills=" + run.Kills + " peakEnemies=" + peak
                + " stage=" + run.Player.Stage.Name + " caught=" + run.Capture.Caught + " allies=" + run.Player.Allies().Count + " items=" + run.Player.ItemCounts.Count + " drones=" + run.Player.Drones.Count + " fusionAt=" + fusionAt.ToString("0") + " moves=[" + string.Join(", ", moves) + "]"
                + " simMsPerFrame=" + (watch.Elapsed.TotalMilliseconds / Math.Max(1, frames)).ToString("0.00"));
            bool finished = run.State == GameState.Won || run.State == GameState.Dead;
            if (god && run.State != GameState.Won)
            {
                report.AppendLine("  ERROR: an invincible run should always reach the end");
                return false;
            }
            return finished;
        }

        // Plays frames, answering any reward or level-up window with its first available card.
        static void AdvanceChoosing(Run run, int frames)
        {
            for (int f = 0; f < frames; f++)
            {
                if (run.State == GameState.Choice && run.Hud.Opts != null)
                {
                    int i = run.Hud.Opts.Cards.FindIndex(c => !c.Disabled);
                    if (i >= 0) run.Hud.ChooseCard(i);
                    else run.Hud.CloseButton();
                }
                Advance(run, 1);
            }
        }

        // After a guardian falls the portal opens and the final swarm keeps growing; only the interact key at the
        // portal moves on to the next zone.
        static bool CheckPortalSwarm(StringBuilder report)
        {
            bool ok = true;
            var run = new Run();
            run.Start(Data.Partners[0]);
            run.GodMode = true;
            run.Director.SummonGuardian(false);
            Advance(run, 2);
            int boss = run.Director.BossIndex();
            ok &= Expect(boss >= 0, "a guardian should appear", report);
            if (boss >= 0) run.DamageEnemy(boss, 1e9f, "basic", default(HitOpts));
            AdvanceChoosing(run, 120);
            ok &= Expect(run.Director.Phase == Phase.Swarm && run.Pickups.HasPortal, "beating the guardian should open the portal and start the final swarm, not " + run.Director.Phase, report);

            Player p = run.Player;
            p.X = run.Pickups.PortalX;
            p.Z = run.Pickups.PortalZ;
            AdvanceChoosing(run, 240);
            ok &= Expect(run.Zone == 1 && run.Pickups.NearPortal(p.X, p.Z), "standing in the portal should not move on by itself", report);
            int capEarly = run.Director.SpawnCap, countEarly = run.Enemies.Count;
            AdvanceChoosing(run, 60 * 40);
            p.X = run.Pickups.PortalX;
            p.Z = run.Pickups.PortalZ;
            int capLate = run.Director.SpawnCap, countLate = run.Enemies.Count;
            ok &= Expect(capLate > capEarly + 100 && countLate > countEarly, "the final swarm should keep growing (cap " + capEarly + " -> " + capLate + ", enemies " + countEarly + " -> " + countLate + ")", report);

            run.Input.KeyDown(Keys.E);
            Advance(run, 1);
            run.Input.KeyUp(Keys.E);
            AdvanceChoosing(run, 30);
            ok &= Expect(run.Zone == 2 && run.Director.Phase == Phase.Zone && !run.Pickups.HasPortal, "pressing E at the portal should go to zone 2", report);
            report.AppendLine("--- Portal and final swarm: swarm cap " + capEarly + " -> " + capLate + ", enemies " + countEarly + " -> " + countLate + " " + (ok ? "ok" : "FAILED"));
            return ok;
        }

        static void Advance(Run run, int frames)
        {
            for (int f = 0; f < frames; f++)
            {
                run.Frame(1 / 60f);
                run.Input.EndFrame();
            }
        }

        // Offline: a temp folder stands in for GitHub. Downloads are checked but never installed over this exe.
        static bool CheckUpdater(StringBuilder report)
        {
            bool ok = true;
            int release;
            long size;
            string sha;
            ok &= Expect(Updater.TryParse("release=7\r\nsize=10\nsha256=" + new string('A', 64) + "\n", out release, out size, out sha) && release == 7 && size == 10 && sha == new string('a', 64), "latest.txt should parse", report);
            ok &= Expect(!Updater.TryParse("release=7\nsize=10\n", out release, out size, out sha) && !Updater.TryParse("<html>Not Found</html>", out release, out size, out sha) && !Updater.TryParse(null, out release, out size, out sha), "a broken latest.txt should be rejected", report);

            string dir = Path.Combine(Path.GetTempPath(), "swarmfall-update-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string download = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName + ".download";
            string savedSource = Updater.Source;
            try
            {
                byte[] fake = Encoding.ASCII.GetBytes("not really a game, just update test bytes");
                File.WriteAllBytes(Path.Combine(dir, "Swarmfall.exe"), fake);
                Updater.Source = new Uri(dir + Path.DirectorySeparatorChar).AbsoluteUri;
                Func<int, string, UpdateState> attempt = (rel, hash) =>
                {
                    File.WriteAllText(Path.Combine(dir, "latest.txt"), "release=" + rel + "\nsize=" + fake.Length + "\nsha256=" + hash + "\n");
                    Updater.State = UpdateState.Idle;
                    Updater.CheckAsync();
                    for (int i = 0; i < 200 && Updater.Busy; i++) System.Threading.Thread.Sleep(25);
                    if (Updater.State != UpdateState.Available) return Updater.State;
                    Updater.DownloadAsync();
                    for (int i = 0; i < 200 && Updater.Busy; i++) System.Threading.Thread.Sleep(25);
                    return Updater.State;
                };
                ok &= Expect(attempt(Updater.Release, Updater.Sha256(fake)) == UpdateState.UpToDate, "the same release should count as up to date", report);
                ok &= Expect(attempt(Updater.Release - 1, Updater.Sha256(fake)) == UpdateState.UpToDate, "an older release should never be offered", report);
                ok &= Expect(attempt(Updater.Release + 1, new string('0', 64)) == UpdateState.Failed && Updater.Error != null && !File.Exists(download), "a download with the wrong checksum should be refused and removed", report);
                ok &= Expect(attempt(Updater.Release + 1, Updater.Sha256(fake)) == UpdateState.Ready && File.Exists(download) && Updater.LatestRelease == Updater.Release + 1, "a newer release should download and check out", report);
            }
            finally
            {
                Updater.Source = savedSource;
                Updater.State = UpdateState.Idle;
                Updater.Error = null;
                try { File.Delete(download); } catch { }
                Directory.Delete(dir, true);
            }
            report.AppendLine("--- Updater: latest.txt, newer only, checksum, download " + (ok ? "ok" : "FAILED"));
            return ok;
        }

        static bool Expect(bool condition, string what, StringBuilder report)
        {
            if (!condition) report.AppendLine("  FAILED: " + what);
            return condition;
        }

        // Every partner evolves twice, at the listed levels, and gains what its stage says.
        static bool CheckEvolution(StringBuilder report)
        {
            bool ok = true;
            foreach (Partner partner in Data.Partners)
            {
                var run = new Run();
                run.Start(partner);
                run.GodMode = true;
                run.Hud.Opts = null;
                Player p = run.Player;
                if (partner.Evolutions.Length == 0) continue;
                ok &= Expect(p.Stages.Length == partner.Evolutions.Length + 1, partner.Name + " should have every stage", report);
                float hpBefore = p.Stats.MaxHp;
                foreach (Stage next in partner.Evolutions)
                {
                    int stageBefore = p.StageIndex;
                    p.Level = next.Level - 1;
                    run.State = GameState.Play;
                    Advance(run, 2);
                    ok &= Expect(p.StageIndex == stageBefore, partner.Name + " evolved too early", report);
                    p.Level = next.Level;
                    Advance(run, 2);
                    ok &= Expect(p.Stage == next, partner.Name + " should be " + next.Name + " at level " + next.Level, report);
                    ok &= Expect(run.Atlas.Frame(p.Stage.Id) >= 0, next.Name + " needs art", report);
                }
                ok &= Expect(p.Stats.MaxHp > hpBefore || partner.Id == "none", partner.Name + " should gain HP from evolving", report);
                p.Level = 50;
                run.State = GameState.Play;
                Advance(run, 2);
                ok &= Expect(p.StageIndex == partner.Evolutions.Length, partner.Name + " should stop at its final stage", report);
            }
            report.AppendLine("--- Evolution: all partners " + (ok ? "ok" : "FAILED"));
            return ok;
        }

        // Round trips, cut-off and garbage files, newer versions, and keeping unknown lines. Uses a temp folder only.
        static bool CheckSave(StringBuilder report)
        {
            bool ok = true;
            string dir = Path.Combine(Path.GetTempPath(), "swarmfall-save-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "sub", "save.txt");
            try
            {
                Partner skitter = Array.Find(Data.Partners, x => x.Id == "skitter");
                Profile a = Profile.Load(path);
                ok &= Expect(a.Warning == null && a.Get("kills") == 0, "a missing save should start fresh", report);
                a.Add("kills", 1234);
                a.Best("bestLevel", 7);
                a.Best("bestLevel", 3);
                a.AddToRoster(skitter);
                a.LastPartner = "fugiri";
                ok &= Expect(a.Save(), "saving should succeed and create the folder", report);
                ok &= Expect(!File.Exists(path + ".tmp"), "no temp file should be left behind", report);

                a.LastPartner = "sproutling";
                a.Save();
                ok &= Expect(Profile.Load(path).LastPartner == "golem-stage-i", "a save last played as Sproutling should open on the golem line", report);
                ok &= Expect(Array.Find(Data.Partners, x => x.Id == "sproutling") == null, "Sproutling should be gone", report);
                a.LastPartner = "fugiri";
                a.Save();
                Profile b = Profile.Load(path);
                ok &= Expect(b.Warning == null && b.Get("kills") == 1234 && b.Get("bestLevel") == 7, "counters should survive a reload", report);
                ok &= Expect(b.Owns(skitter) && b.LastPartner == "fugiri", "roster and last partner should survive a reload", report);
                b.Add("kills");
                b.Save();
                ok &= Expect(File.Exists(path + ".bak") && Profile.Load(path + ".bak").Get("kills") == 1234, "the previous save should be kept as a backup", report);

                // Unknown lines from a later feature must survive a save.
                string text = File.ReadAllText(path).Replace("end", "future.thing=3" + Environment.NewLine + "end");
                File.WriteAllText(path, text);
                Profile c = Profile.Load(path);
                c.Save();
                ok &= Expect(File.ReadAllText(path).Contains("future.thing=3"), "unknown lines should be preserved", report);

                // A cut-off file is set aside, not deleted, and the game starts fresh.
                File.WriteAllText(path, File.ReadAllText(path).Replace("end", ""));
                Profile d = Profile.Load(path);
                ok &= Expect(d.Warning != null && d.Get("kills") == 0, "a cut-off save should start fresh with a warning", report);
                ok &= Expect(Directory.GetFiles(Path.GetDirectoryName(path), "save.txt.damaged-*").Length == 1, "a cut-off save should be kept aside", report);

                // Garbage numbers and files.
                File.WriteAllText(path, "swarmfall-save 1\ncounter.kills=lots\nend\n");
                ok &= Expect(Profile.Load(path).Warning != null, "a damaged number should be detected", report);
                File.WriteAllText(path, "not a save at all");
                ok &= Expect(Profile.Load(path).Warning != null, "a non-save file should be detected", report);

                // A save from a newer game is left exactly as it was.
                string future = "swarmfall-save 99\ncounter.kills=5\nend\n";
                File.WriteAllText(path, future);
                Profile e = Profile.Load(path);
                e.Add("kills");
                e.Save();
                ok &= Expect(e.Warning != null && File.ReadAllText(path) == future, "a newer save must not be overwritten", report);

                // An in-memory profile writes nothing.
                Profile m = Profile.InMemory();
                m.Add("kills");
                ok &= Expect(m.Save(), "an in-memory save should quietly do nothing", report);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
            report.AppendLine("--- Saves: reload, backup, damage, newer versions " + (ok ? "ok" : "FAILED"));
            return ok;
        }

        static int AddTarget(Run run)
        {
            float x = run.Player.X + 12, z = run.Player.Z;
            run.Terrain.PushOut(ref x, ref z, 3);
            return run.Enemies.Spawn("shockling", x, z, 1000, 1);
        }

        // Approved starter lines gain their second type only at the final stage.
        static bool CheckSecondTypes(StringBuilder report)
        {
            bool ok = true;
            ok &= Expect(Data.Elements["leaf"].Name == "Bloom", "the grass type should be called Bloom", report);
            foreach (string[] line in new[] { new[] { "golem-stage-i", "stone", "leaf", "spitter" }, new[] { "fugiri", "water", "iron", "sporeling" }, new[] { "burrowkit", "fire", "stone", "ramhorn" }, new[] { "chichar", "volt", "venom", "skitter" } })
            {
                Partner partner = Array.Find(Data.Partners, x => x.Id == line[0]);
                ok &= Expect(partner != null && partner.Elem == line[1], line[0] + " should exist with the right type", report);
                if (partner == null) continue;
                var run = new Run();
                run.Start(partner);
                Player p = run.Player;
                ok &= Expect(p.SecondElem == null, partner.Name + " should start with one type", report);
                p.Evolve();
                ok &= Expect(p.SecondElem == null, p.Stage.Name + " should still have one type", report);
                p.Evolve();
                ok &= Expect(p.SecondElem == line[2] && p.Partner.Elem == line[1], p.Stage.Name + " should gain its approved second type and keep its first type", report);
                float bonus;
                p.ElemBonus.TryGetValue(line[2], out bonus);
                ok &= Expect(M.Abs(bonus - 0.25f) < 0.001f, p.Stage.Name + " should get +25% second-type move damage", report);

                // Each target is weaker to the new type than to the original type.
                run.Enemies.Clear();
                int i = run.Enemies.Spawn(line[3], p.X + 12, p.Z, 1000, 1);
                Advance(run, 1);
                float before = run.Enemies.Hp[i];
                p.Stats.Crit = 0;
                run.Blast(run.Enemies.X[i], 0, run.Enemies.Z[i], 2, 100, partner.Elem, 0xffffff, 0, default(Status), 0, p.SecondElem);
                ok &= Expect(M.Abs(before - run.Enemies.Hp[i] - 200) < 1, p.Stage.Name + " blasts should choose its stronger second type", report);
            }
            // Sleach starts Basic/Iron; at Dealeach, Volt replaces Iron rather than adding a third type.
            Partner sleach = Array.Find(Data.Partners, x => x.Id == "sleach");
            ok &= Expect(sleach != null && sleach.Evolutions.Length == 1 && sleach.Evolutions[0].Name == "Dealeach", "Sleach should evolve once, into Dealeach", report);
            if (sleach != null)
            {
                var srun = new Run();
                srun.Start(sleach);
                Player sp = srun.Player;
                ok &= Expect(sp.Elem == "basic" && sp.SecondElem == "iron", "Sleach should be Basic/Iron", report);
                MoveDef harpoon = Data.Move(sleach.Move);
                ok &= Expect(harpoon.Id == "harpoon-lance" && harpoon.Elem == "iron" && harpoon.Kind == Kind.Lance, "Sleach should start with Harpoon Lance, an Iron line move", report);
                ok &= Expect(string.Join(",", Upgrades.PoolOf(harpoon)) == "dmg,area,cd", "Harpoon Lance should level up damage, area and attack speed only", report);

                // The harpoon is a long pointed shot that flies its range, pierces everything and then disappears.
                srun.GodMode = true;
                srun.State = GameState.Play;
                srun.Enemies.Clear();
                srun.Projectiles.Clear();
                int target = AddTarget(srun);
                Advance(srun, 1);
                float hpBefore = srun.Enemies.Hp[target];
                srun.Projectiles.Clear();
                Move lance = sp.Moves.Find(x => x.Id == "harpoon-lance");
                ok &= Expect(lance != null && Moves.Fire(srun, lance), "Harpoon Lance should fire at a target in range", report);
                Shot spear = srun.Projectiles.Items.Find(x => x.Spear > 0);
                ok &= Expect(spear != null && spear.Pierce >= 999 && M.Abs(spear.Life * M.Hypot(spear.Vx, spear.Vz) - harpoon.Range * sp.Stats.Area) < 0.5f, "Harpoon Lance should be a piercing harpoon that flies its range", report);
                Advance(srun, 60);
                ok &= Expect(srun.Enemies.Hp[target] < hpBefore && !srun.Projectiles.Items.Exists(x => x.Spear > 0), "Harpoon Lance should hit its target and be gone after its flight", report);
                srun.Enemies.Clear();
                srun.Projectiles.Clear();
                sp.Evolve();
                ok &= Expect(sp.Stage.Name == "Dealeach" && sp.Elem == "basic" && sp.SecondElem == "volt", "Dealeach should be Basic/Volt, with Volt replacing Iron", report);
                ok &= Expect(sleach.TypeLine() == "BASIC / IRON · VOLT AT LV 16", "Sleach's type line should show the swap: " + sleach.TypeLine(), report);
                ok &= Expect(Data.WildDef("sleach") != null && Data.Signature.ContainsKey("sleach"), "Sleach should be catchable and have a signature fusion", report);
            }
            Partner single = Array.Find(Data.Partners, x => x.Id == "skitter");
            ok &= Expect(single.DualStage() == null, "other lines should keep one type", report);
            ok &= Expect(Data.BestElem("leaf", "stone", "water") == "leaf" && Data.BestElem("leaf", "stone", "fire") == "stone" && Data.BestElem("leaf", null, "fire") == "leaf", "the better type should be chosen", report);
            report.AppendLine("--- Second types: Toyat, Fugiri, Furrowkit and Chichar lines, blasts " + (ok ? "ok" : "FAILED"));
            return ok;
        }

        // Every matchup against the approved chart, then how immunity and dual types behave in play.
        static bool CheckTypeChart(StringBuilder report)
        {
            bool ok = true;
            string[] order = { "fire", "water", "leaf", "volt", "stone", "iron", "venom", "spirit", "basic" };
            // Rows attack, columns defend. h is half, 0 is immune, - is normal.
            string[] chart = { "hh2-h2-2-", "2hh-22---", "h2h-2h---", "-2hh02---", "2-h2-h20-", "hh2h2h-2-", "-22-h0hh2", "h--2-h220", "----hh-0-" };
            for (int a = 0; a < order.Length; a++)
            {
                for (int d = 0; d < order.Length; d++)
                {
                    char c = chart[a][d];
                    float want = c == '2' ? 2 : c == 'h' ? 0.5f : c == '0' ? 0 : 1;
                    ok &= Expect(Data.Effectiveness(order[a], order[d]) == want, order[a] + " against " + order[d] + " should be " + want, report);
                }
                ok &= Expect(Data.TypeFusions.ContainsKey(order[a]), order[a] + " should have a type fusion", report);
                ok &= Expect(Array.FindAll(Data.MoveList, m => m.Elem == order[a]).Length >= 2, order[a] + " should have two moves to fuse", report);
            }
            ok &= Expect(Data.Effectiveness("water", "stone", "iron") == 4 && Data.Effectiveness("fire", "water", "stone") == 0.25f && Data.Effectiveness("volt", "water", "stone") == 0, "dual types should multiply", report);

            var run = new Run();
            Partner artSource = Array.Find(Data.Partners, x => x.Id == "burrowkit");
            run.Start(new Partner { Id = artSource.Id, Name = "Stone test fixture", Elem = "stone", Move = "boulder-lob", Art = artSource.Art });
            run.GodMode = true;
            Enemies e = run.Enemies;
            run.Player.Stats.Crit = 0;
            run.Player.Stats.Lifesteal = 1;
            run.Player.Hp = 10;
            int wisp = e.Spawn("wisp", run.Player.X + 12, run.Player.Z, 1000, 1);
            Advance(run, 1);
            float hp = e.Hp[wisp], healed = run.Player.Hp;
            ok &= Expect(!run.DamageEnemy(wisp, 100, "stone", default(HitOpts)) && e.Hp[wisp] == hp, "stone should not hurt a spirit", report);
            ok &= Expect(run.Player.Hp == healed, "an immune hit should give no lifesteal", report);
            ok &= Expect(e.Nearest(e.X[wisp], e.Z[wisp], 1, "stone") != wisp && e.Nearest(e.X[wisp], e.Z[wisp], 1, "water") == wisp, "moves should not aim at a target that is immune to them", report);
            e.Flags[wisp] |= Flag.Boss;
            ok &= Expect(e.TypeMultiplier(wisp, "stone") == Data.BossImmunityFloor, "a boss should not be fully immune", report);

            int warden = e.Spawn("tower-warden", run.Player.X - 12, run.Player.Z, 1000, 1);
            hp = e.Hp[warden];
            e.Hit(warden, 100, "water", default(HitOpts));
            ok &= Expect(M.Abs(hp - e.Hp[warden] - 400) < 1, "tide should do 4x to stone and iron", report);

            int shock = e.Spawn("shockling", run.Player.X, run.Player.Z + 12, 1000, 1);
            ok &= Expect(e.AttackDamage(shock, 10) == 0, "volt attacks should not hurt a stone partner", report);
            run.GodMode = false;
            run.Player.Iframes = 0;
            hp = run.Player.Hp;
            ok &= Expect(!run.Player.Hurt(0) && run.Player.Hp == hp, "a zero attack should do nothing", report);
            run.GodMode = true;
            int neutral = e.Spawn("wisp", run.Player.X, run.Player.Z - 12, 1000, 1);
            ok &= Expect(e.AttackDamage(neutral, 10) == 10, "a neutral attack should be unchanged", report);
            e.Flags[shock] |= Flag.Boss;
            ok &= Expect(e.AttackDamage(shock, 10) == 10 * Data.BossImmunityFloor, "a boss attack should always get through", report);
            ok &= Expect(e.AttackDamage(warden, 10) == 20, "iron attacks should double against stone", report);

            var golem = new Run();
            golem.Start(Array.Find(Data.Partners, x => x.Id == "golem-stage-i"));
            golem.Player.Evolve();
            golem.Player.Evolve();
            int big = golem.Enemies.Spawn("tower-warden", golem.Player.X + 12, golem.Player.Z, 1000, 1);
            ok &= Expect(golem.Enemies.AttackDamage(big, 10) == 40, "iron should do 4x to bloom and stone", report);
            golem.Enemies.Flags[big] |= Flag.Boss;
            ok &= Expect(golem.Enemies.AttackDamage(big, 10) == 20, "a boss attack should be capped at 2x", report);
            report.AppendLine("--- Type chart: all matchups, immunity, bosses, dual types " + (ok ? "ok" : "FAILED"));
            return ok;
        }

        // Each reaction raises damage, clears what it should, and has a cooldown.
        static bool CheckReactions(StringBuilder report)
        {
            bool ok = true;
            var run = new Run();
            run.Start(Data.Partners[0]);
            run.GodMode = true;
            Enemies e = run.Enemies;
            run.Player.Stats.Crit = 0;
            Func<int> fresh = () =>
            {
                e.Clear();
                int i = AddTarget(run);
                e.Spawn(Data.Enemies[0].Id, e.X[i] + 1.5f, e.Z[i], 1000, 1);
                Advance(run, 1);
                return 0;
            };
            Func<int, string, float> hit = (i, elem) =>
            {
                float before = e.Hp[i];
                run.DamageEnemy(i, 100, elem, default(HitOpts));
                return before - e.Hp[i];
            };

            int t = fresh();
            ok &= Expect(M.Abs(hit(t, "spirit") - 100 * Data.Effectiveness("spirit", "volt")) < 1, "a plain hit should not react", report);
            t = fresh();
            hit(t, "water");
            ok &= Expect(e.WetT[t] > 0, "water should make a target wet", report);
            float shock = hit(t, "volt");
            ok &= Expect(M.Abs(shock - 160 * Data.Effectiveness("volt", "volt")) < 1, "wet + volt should do 60% more", report);
            ok &= Expect(e.WetT[t] <= 0 && e.Hp[1] < e.MaxHp[1], "shock should use up the wet and hit neighbours", report);
            ok &= Expect(run.Profile.Get("reactions") == 1, "reactions should be counted", report);

            t = fresh();
            e.BurnT[t] = 3;
            hit(t, "water");
            ok &= Expect(e.BurnT[t] <= 0 && e.StunT[1] > 0, "water on a burning target should make steam that stuns neighbours", report);

            t = fresh();
            e.BurnT[t] = 3;
            hit(t, "leaf");
            ok &= Expect(e.BurnT[1] > 0, "leaf on a burning target should spread the fire", report);

            t = fresh();
            e.StunT[t] = 1;
            float shatter = hit(t, "stone");
            ok &= Expect(M.Abs(shatter - 180 * Data.Effectiveness("stone", "volt")) < 1 && e.StunT[t] <= 0, "stone on a stunned target should shatter", report);

            // No chain reaction: the splash has no element, and a cooldown stops instant repeats.
            t = fresh();
            e.WetT[t] = 3;
            hit(t, "volt");
            e.WetT[t] = 3;
            float repeat = hit(t, "volt");
            ok &= Expect(M.Abs(repeat - 100 * Data.Effectiveness("volt", "volt")) < 1, "a second reaction straight away should be blocked", report);

            report.AppendLine("--- Reactions: damage, statuses, splash, cooldown " + (ok ? "ok" : "FAILED"));
            return ok;
        }

        // Difficulty scales enemies and rewards, and is remembered.
        static bool CheckDifficulty(StringBuilder report)
        {
            bool ok = true;
            Func<int, Run> make = d =>
            {
                Profile profile = Profile.InMemory();
                profile.Set("lastDifficulty", d);
                var r = new Run(profile);
                r.Start(Data.Partners[1]);
                r.Time = 200;
                return r;
            };
            Run normal = make(0), hard = make(1), nightmare = make(2);
            ok &= Expect(normal.Diff.Name == "Normal" && nightmare.Diff.Name == "Nightmare", "the saved difficulty should be used", report);
            ok &= Expect(M.Abs(hard.Director.CurrentHpMult / normal.Director.CurrentHpMult - 1.7f) < 0.01f, "Hard should give enemies 70% more HP", report);
            ok &= Expect(M.Abs(nightmare.Director.CurrentHpMult / normal.Director.CurrentHpMult - 2.6f) < 0.01f, "Nightmare should give enemies 160% more HP", report);
            ok &= Expect(hard.Director.CurrentDmgMult > normal.Director.CurrentDmgMult, "Hard should hit harder", report);
            for (int k = 0; k < 4; k++) hard.Earn(1);
            ok &= Expect(hard.RunShards == 7, "fractions of shards should add up, not be lost", report);
            nightmare.Earn(10);
            ok &= Expect(nightmare.RunShards == 25, "Nightmare should pay 2.5x", report);
            Profile out_of_range = Profile.InMemory();
            out_of_range.Set("lastDifficulty", 99);
            ok &= Expect(new Run(out_of_range).Diff.Name == "Nightmare", "a bad saved value should not crash", report);
            report.AppendLine("--- Difficulty: scaling, shards, memory " + (ok ? "ok" : "FAILED"));
            return ok;
        }

        // No portal or guardian stone, a boss on schedule, a pact after it, and a saved best time.
        static bool CheckSurvival(StringBuilder report)
        {
            bool ok = true;
            Profile profile = Profile.InMemory();
            profile.Set("lastMode", 1);
            var run = new Run(profile);
            run.Start(Data.Partners[1]);
            run.GodMode = true;
            ok &= Expect(run.Mode == GameMode.Survival && run.Director.Phase == Phase.Survive, "the saved mode should be used", report);
            ok &= Expect(!run.Encounters.Items.Exists(o => o.Type == EncKind.Stone), "Survival should have no guardian stone", report);
            var bot = new Bot(run, false);
            bool sawBoss = false, sawPact = false, sawPortal = false, sawChest = false;
            int chestsAtStart = run.Encounters.Items.Count;
            for (int f = 0; f < 60 * 660; f++)
            {
                if (run.State == GameState.Choice && run.Hud.Opts != null && run.Hud.Opts.Title == "Choose a pact") sawPact = true;
                bot.Step();
                run.Frame(1 / 60f);
                run.Input.EndFrame();
                if (run.Director.Phase == Phase.Guardian) sawBoss = true;
                if (run.Pickups.HasPortal) sawPortal = true;
                if (run.Encounters.Items.Count > chestsAtStart) sawChest = true;
            }
            ok &= Expect(sawBoss && run.Time > 300, "a milestone boss should arrive after 5 minutes", report);
            ok &= Expect(sawPact, "beating a milestone should offer a pact", report);
            ok &= Expect(!sawPortal && run.Zone == 1, "Survival should stay on one map", report);
            ok &= Expect(sawChest, "chests should keep appearing", report);
            ok &= Expect(run.State != GameState.Won, "Survival should never be won", report);
            run.GodMode = false;
            run.Player.Stats.Revives = 0;
            run.Player.Hp = 1;
            run.Player.Iframes = 0;
            run.Player.Hurt(1000);
            ok &= Expect(run.State == GameState.Dead && profile.Get("survivalBest") >= 600, "dying should record the time survived", report);
            string nightFailure = NightFailure();
            ok &= Expect(nightFailure == null, "the Endless Night should end every run: " + nightFailure, report);
            report.AppendLine("--- Survival: boss, pact, chests, one map, best time (" + run.Time.ToString("0") + "s, heat " + run.Heat.ToString("0.00") + ") " + (ok ? "ok" : "FAILED"));
            return ok;
        }

        // The Endless Night must end a run, even for a very strong player. Returns null if it did.
        static string NightFailure()
        {
            Profile nightProfile = Profile.InMemory();
            nightProfile.Set("lastMode", 1);
            var nightRun = new Run(nightProfile);
            nightRun.Start(Data.Partners[1]);
            nightRun.Director.Elapsed = 2700;
            nightRun.Player.Stats.MaxHp = 5000;
            nightRun.Player.Hp = 5000;
            nightRun.Player.Stats.Regen = 20;
            nightRun.Player.Stats.Armor = 100000;
            var nightBot = new Bot(nightRun, false);
            for (int f = 0; f < 60 * 300 && nightRun.State != GameState.Dead; f++)
            {
                nightBot.Step();
                nightRun.Frame(1 / 60f);
                nightRun.Input.EndFrame();
            }
            if (nightRun.State == GameState.Dead) return null;
            return "state=" + nightRun.State + " title=" + (nightRun.Hud.Opts != null ? nightRun.Hud.Opts.Title : "none") + " cards=" + (nightRun.Hud.Opts != null ? nightRun.Hud.Opts.Cards.Count : 0) + " phase=" + nightRun.Director.Phase + " elapsed=" + (int)nightRun.Director.Elapsed + " hp=" + (int)nightRun.Player.Hp + "/" + (int)nightRun.Player.Stats.MaxHp + " revives=" + nightRun.Player.Stats.Revives + " level=" + nightRun.Player.Level + " regen=" + nightRun.Player.Stats.Regen.ToString("0.0") + " steal=" + nightRun.Player.Stats.Lifesteal.ToString("0.00") + " dead=" + nightRun.Player.IsDead + " time=" + (int)nightRun.Time;
        }

        // How far the non-dodging bot gets at each difficulty, for judging balance. Reported, never failed on.
        public static int RunBalance(string path)
        {
            var report = new StringBuilder();
            ReportBalance(report);
            File.WriteAllText(path, report.ToString());
            return 0;
        }

        static void ReportBalance(StringBuilder report)
        {
            report.AppendLine("--- Balance, no upgrades, bot does not dodge:");
            for (int d = 0; d < Data.Difficulties.Length; d++)
            {
                foreach (int mode in new[] { 0, 1 })
                {
                    var line = new StringBuilder("  " + Data.Difficulties[d].Name + (mode == 1 ? " Survival:" : " Classic:"));
                    foreach (Partner partner in Data.Partners)
                    {
                        if (!partner.Starter) continue;
                        Profile profile = Profile.InMemory();
                        profile.Set("lastDifficulty", d);
                        profile.Set("lastMode", mode);
                        var run = new Run(profile);
                        run.Start(partner);
                        var bot = new Bot(run, false);
                        for (int f = 0; f < 60 * 2400 && run.State != GameState.Won && run.State != GameState.Dead; f++)
                        {
                            bot.Step();
                            run.Frame(1 / 60f);
                            run.Input.EndFrame();
                        }
                        line.Append("\r\n    " + partner.Name + "=" + (run.State == GameState.Won ? "won" : run.State == GameState.Dead ? "died" : "alive") + "@" + (int)run.Time + "s L" + run.Player.Level + " hp" + (int)run.Player.Stats.MaxHp + " arm" + run.Player.Stats.Armor.ToString("0") + " might" + run.Player.Stats.Might.ToString("0.0") + " lasthit" + (int)run.Player.LastHit + " alive" + run.Enemies.Count + " hpx" + run.Director.CurrentHpMult.ToString("0.0") + " dmgx" + run.Director.CurrentDmgMult.ToString("0.0") + " heat" + run.Heat.ToString("0.00"));
                    }
                    report.AppendLine(line.ToString());
                }
            }
        }

        // Buying, costs, limits, saving, and every upgrade actually taking effect in a run.
        static bool CheckUpgrades(StringBuilder report)
        {
            bool ok = true;
            Func<string, UpgradeDef> find = id => Array.Find(Data.LabUpgrades, u => u.Id == id);
            Profile profile = Profile.InMemory();
            UpgradeDef might = find("might");

            ok &= Expect(!profile.Buy(might) && profile.Level(might) == 0, "buying with no shards should fail", report);
            profile.Add("shards", 50);
            ok &= Expect(profile.Cost(might) == 20 && profile.Buy(might), "the first level should cost 20", report);
            ok &= Expect(profile.Get("shards") == 30 && profile.Level(might) == 1, "buying should take shards and add a level", report);
            ok &= Expect(profile.Cost(might) == 30, "the next level should cost more", report);
            profile.Add("shards", 1000000);
            while (profile.Buy(might)) { }
            ok &= Expect(profile.Level(might) == might.Max, "levels should stop at the maximum", report);

            // A run with nothing bought has the base limits.
            var plain = new Run(Profile.InMemory());
            plain.Start(Data.Partners[0]);
            Stats b = plain.Player.Stats;
            ok &= Expect(b.MoveSlots == 3 && b.VitaminSlots == 3 && b.CardOptions == 3, "base slots should be 3 moves, 3 vitamins, 3 options", report);
            ok &= Expect(Upgrades.RollLevelCards(plain.Player).Count == 3, "level-ups should offer 3 cards", report);
            plain.Player.Moves.Clear();
            plain.Player.Moves.Add(new Move("droplet", Data.Move("droplet")));
            plain.Player.Moves.Add(new Move("wail", Data.Move("wail")));
            plain.Player.Moves.Add(new Move("riptide", Data.Move("riptide")));
            for (int i = 0; i < 40; i++)
            {
                ok &= Expect(!Upgrades.RollLevelCards(plain.Player).Exists(c => c.Key.StartsWith("new:")), "full slots should not offer a new move", report);
            }

            // Everything maxed, then a new run. Each upgrade should show up on the player.
            foreach (UpgradeDef u in Data.LabUpgrades)
            {
                while (profile.Buy(u)) profile.Add("shards", 1000000);
            }
            var run = new Run(profile);
            run.Start(Data.Partners[0]);
            Player p = run.Player;
            Stats s = p.Stats;
            ok &= Expect(M.Abs(s.Might - b.Might * 1.32f) < 0.01f, "damage should be up 32%", report);
            ok &= Expect(M.Abs(s.MaxHp - (b.MaxHp + 48)) < 0.01f && M.Abs(p.Hp - s.MaxHp) < 0.01f, "max HP should be up 48, and start full", report);
            ok &= Expect(M.Abs(s.Speed - b.Speed * 1.15f) < 0.01f, "speed should be up 15%", report);
            ok &= Expect(M.Abs(s.Armor - (b.Armor + 5)) < 0.01f && M.Abs(s.Regen - (b.Regen + 1)) < 0.01f, "armor and regen should be up", report);
            ok &= Expect(s.MoveSlots == 4 && s.VitaminSlots == 5 && s.CardOptions == 4, "slots and options should be up", report);
            ok &= Expect(p.Refreshes == 5 && p.Skips == 5 && p.Banishes == 3, "run tools should be up", report);
            ok &= Expect(s.Revives == 1, "the run should start with a revive", report);
            ok &= Expect(Upgrades.RollLevelCards(p).Count == 4, "level-ups should offer 4 cards", report);

            // Shards are earned from play, and survive a save.
            string dir = Path.Combine(Path.GetTempPath(), "swarmfall-lab-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                string path = Path.Combine(dir, "save.txt");
                Profile saved = Profile.Load(path);
                var earner = new Run(saved);
                earner.Start(Data.Partners[0]);
                earner.GodMode = true;
                Bot bot = new Bot(earner, false);
                for (int f = 0; f < 60 * 120; f++)
                {
                    bot.Step();
                    earner.Frame(1 / 60f);
                    earner.Input.EndFrame();
                }
                ok &= Expect(earner.RunShards > 0 && saved.Get("shards") == earner.RunShards, "playing should earn shards", report);
                saved.Add("shards", 500);
                saved.Buy(find("slot"));
                saved.Buy(might);
                long left = saved.Get("shards");
                saved.Save();
                Profile again = Profile.Load(path);
                ok &= Expect(again.Level(find("slot")) == 1 && again.Level(might) == 1 && again.Get("shards") == left, "upgrades and shards should survive a reload", report);
                File.WriteAllText(path, File.ReadAllText(path).Replace("upgrade.slot=1", "upgrade.slot=99"));
                ok &= Expect(Profile.Load(path).Level(find("slot")) == 1, "a level above the maximum should count as the maximum", report);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }

            report.AppendLine("--- Lab: buying, limits, every effect, earning, saving " + (ok ? "ok" : "FAILED"));
            return ok;
        }

        // A new save picks one of three starters, once. Only that one is unlocked, and old IDs carry over.
        static bool CheckStarterChoice(StringBuilder report)
        {
            bool ok = true;
            Partner[] starters = Array.FindAll(Data.Partners, x => x.Starter);
            ok &= Expect(starters.Length == 3 && starters[0].Name == "Chichar" && starters[1].Name == "Fugiri" && starters[2].Name == "Toyat", "the starters should be Chichar, Fugiri and Toyat, in that order", report);
            Partner furrowkit = Array.Find(Data.Partners, x => x.Id == "burrowkit");
            ok &= Expect(furrowkit != null && !furrowkit.Starter && furrowkit.Name == "Furrowkit", "Furrowkit should stay in the roster but no longer be offered as a starter", report);
            foreach (string gone in new[] { "emberkit", "tidepup", "sproutling" }) ok &= Expect(!Array.Exists(Data.Partners, x => x.Id == gone), gone + " should be gone", report);
            ok &= Expect(Data.Move(Data.Partners[0].Move).Elem == "volt", "Chichar should start with a Volt move", report);
            ok &= Expect(Data.Signature.ContainsKey("chichar") && Data.Signature.ContainsKey("burrowkit") && Data.Signature.ContainsKey("fugiri") && !Data.Signature.ContainsKey("emberkit") && !Data.Signature.ContainsKey("tidepup"), "signature fusions should belong to the current lines", report);
            // Select screen: the newer drawn creatures first, then every placeholder creature made from an enemy.
            bool placeholderSeen = false, orderOk = true;
            foreach (Partner x in Data.Partners)
            {
                bool placeholder = Array.Exists(Data.Enemies, e => e.Id == x.Id);
                if (placeholder) placeholderSeen = true;
                else if (placeholderSeen) orderOk = false;
            }
            ok &= Expect(orderOk && Data.Partners[3] == furrowkit && Data.Partners[4].Id == "slime-basic", "placeholder creatures should come after the newer creatures", report);
            ok &= Expect(Data.WildDef("burrowkit") != null && Data.WildDef("chichar") != null, "Furrowkit and Chichar should be catchable in the wild", report);

            string dir = Path.Combine(Path.GetTempPath(), "swarmfall-starter-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "save.txt");
            try
            {
                Profile a = Profile.Load(path);
                ok &= Expect(a.NeedsStarter && a.OwnedCount() == 0, "a new save should have nothing unlocked", report);
                Partner toyat = Array.Find(starters, x => x.Name == "Toyat"), fugiri = Array.Find(starters, x => x.Name == "Fugiri");
                ok &= Expect(!a.ChooseStarter(Array.Find(Data.Partners, x => x.Id == "skitter")) && a.NeedsStarter, "only a starter can be chosen", report);
                ok &= Expect(a.ChooseStarter(toyat) && !a.NeedsStarter, "choosing a starter should work", report);
                ok &= Expect(a.OwnedCount() == 1 && a.Owns(toyat) && !a.Owns(fugiri), "only the chosen starter should be unlocked", report);
                ok &= Expect(!a.ChooseStarter(fugiri) && !a.Owns(fugiri), "a save can only choose once", report);

                Profile b = Profile.Load(path);
                ok &= Expect(!b.NeedsStarter && b.Owns(toyat) && !b.Owns(fugiri) && b.LastPartner == toyat.Id, "the choice should survive a reload", report);

                // The other starters are earned like everyone else, and can really be caught.
                ok &= Expect(!b.Ready(fugiri), "other starters should begin locked", report);
                b.Add(fugiri.ChallengeCounter, fugiri.ChallengeTarget);
                ok &= Expect(b.Ready(fugiri) && b.NextTarget() == fugiri, "another starter's challenge should make it available", report);
                var run = new Run(b);
                run.Start(toyat);
                run.GodMode = true;
                run.Capture.SpawnWild(Data.WildDef(fugiri.Id));
                ok &= Expect(run.Capture.State == CaptureState.Roaming && run.Capture.WildName == fugiri.Name, "another starter should appear in the wild", report);
                ok &= Expect(!Array.Exists(Data.Enemies, x => x.Id == fugiri.Id), "starters should never be regular enemies", report);

                // Older saves get the window too, keep their roster, and their last partner carries over.
                File.WriteAllText(path, "swarmfall-save 1\ncounter.runs=4\nroster=skitter\nnotified=\nlast=emberkit\nend\n");
                Profile old = Profile.Load(path);
                ok &= Expect(old.Warning == null && old.NeedsStarter && old.Get("runs") == 4 && old.LastPartner == "burrowkit", "an older save should load and ask for a starter", report);
                ok &= Expect(old.Owns(Array.Find(Data.Partners, x => x.Id == "skitter")), "an older save should keep its roster", report);
                File.WriteAllText(path, "swarmfall-save 1\nroster=\nnotified=\nlast=tidepup\nstarter=nonsense\nend\n");
                Profile odd = Profile.Load(path);
                ok &= Expect(odd.LastPartner == "fugiri" && odd.NeedsStarter, "an unknown starter should ask again instead of leaving nothing playable", report);

                // A save that picked Furrowkit before Chichar replaced it keeps Furrowkit and isn't asked again.
                File.WriteAllText(path, "swarmfall-save 1\nroster=\nnotified=\nlast=burrowkit\nstarter=burrowkit\nend\n");
                Profile former = Profile.Load(path);
                ok &= Expect(!former.NeedsStarter && former.Owns(furrowkit) && former.OwnedCount() == 1 && !former.ChooseStarter(starters[0]), "a save that chose Furrowkit should keep it as its starter", report);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
            report.AppendLine("--- Starter choice: new save, one unlock, reload, older saves " + (ok ? "ok" : "FAILED"));
            return ok;
        }

        // The slime picks one of 8 forms at its level, and each form is a real, different stage.
        static bool CheckSlimeForms(StringBuilder report)
        {
            bool ok = true;
            Partner slime = Array.Find(Data.Partners, x => x.Forms != null);
            ok &= Expect(slime != null && slime.Id == "slime-basic" && slime.Forms.Length == 8 && slime.Evolutions.Length == 0, "the slime should have 8 alternative forms", report);
            string[] elems = { "fire", "water", "leaf", "volt", "stone", "iron", "venom", "spirit" };
            var seen = new HashSet<string>();
            foreach (Stage form in slime.Forms)
            {
                seen.Add(form.Elem);
                var run = new Run();
                run.Start(slime);
                run.GodMode = true;
                Player p = run.Player;
                ok &= Expect(p.Elem == "basic", "a slime should start as Basic", report);
                ok &= Expect(run.Atlas.Frame(form.Id) >= 0, form.Name + " needs art", report);
                p.Level = 9;
                Advance(run, 2);
                ok &= Expect(run.State == GameState.Play && p.StageIndex == 0, "the slime should not change before its level", report);
                p.Level = 10;
                Advance(run, 2);
                ok &= Expect(run.State == GameState.Choice && run.Hud.Opts != null && run.Hud.Opts.Cards.Count == 3 && run.Hud.Opts.CloseLabel == null, "the slime should be offered 3 forms and have to pick", report);
                run.Hud.CloseButton();
                run.CloseChoice();
                p.TakeForm(form);
                ok &= Expect(p.Stage == form && p.Elem == form.Elem && p.SecondElem == null && p.StageIndex == 1, form.Name + " should replace its type", report);
                float bonus;
                p.ElemBonus.TryGetValue(form.Elem, out bonus);
                ok &= Expect(bonus >= 0.25f, form.Name + " should boost moves of its type", report);
                p.Level = 60;
                Advance(run, 3);
                ok &= Expect(run.State != GameState.Choice || !run.Hud.Opts.Title.EndsWith("changing"), "the slime should only change once", report);
                ok &= Expect(p.Stage == form, form.Name + " should have no later stage", report);
            }
            ok &= Expect(seen.Count == 8 && Array.TrueForAll(elems, seen.Contains), "the forms should cover all 8 elements", report);
            // A form matching your best move is always on offer.
            var boosted = new Run();
            boosted.Start(slime);
            boosted.Player.Moves.Clear();
            boosted.Player.Moves.Add(new Move("ember-shot", Data.Move("ember-shot")));
            for (int k = 0; k < 20; k++) ok &= Expect(boosted.Player.FormChoice().Cards[0].Title == "Bublava", "the first form should match the strongest move", report);
            report.AppendLine("--- Slime forms: 8 forms, choice, types, one change " + (ok ? "ok" : "FAILED"));
            return ok;
        }

        // Locked, then challenge done, then appears in the wild, then caught and playable.
        static bool CheckRoster(StringBuilder report)
        {
            bool ok = true;
            Profile profile = Profile.InMemory();
            profile.ChooseStarter(Data.Partners[0]);
            ok &= Expect(profile.OwnedCount() == 1, "only the chosen starter should begin on the roster", report);
            foreach (Partner p in Data.Partners)
            {
                if (p == Data.Partners[0]) continue;
                ok &= Expect(!profile.Owns(p) && !profile.ChallengeDone(p), p.Name + " should begin locked", report);
                ok &= Expect(p.ChallengeCounter != null && p.ChallengeTarget > 0, p.Name + " needs a challenge", report);
                Data.Move(p.Move);
            }
            Partner skitter = null;
            foreach (Partner p in Data.Partners) if (p.Id == "skitter") skitter = p;
            ok &= Expect(profile.NextTarget() == null, "nothing should be waiting before a challenge is done", report);

            var run = new Run(profile);
            run.Start(Data.Partners[0]);
            run.GodMode = true;
            Advance(run, 2);
            ok &= Expect(profile.Get("runs") == 1, "starting a run should count", report);

            profile.Add("kills", 500);
            ok &= Expect(profile.Ready(skitter) && profile.NextTarget() == skitter, "completing a challenge should make it available", report);
            Advance(run, 70);
            while (run.State == GameState.Choice) { run.Hud.ChooseCard(0); Advance(run, 1); }
            ok &= Expect(run.RunNews.Count == 1, "completing a challenge should be announced", report);
            int news = run.RunNews.Count;
            Advance(run, 70);
            while (run.State == GameState.Choice) { run.Hud.ChooseCard(0); Advance(run, 1); }
            ok &= Expect(run.RunNews.Count == news, "it should only be announced once", report);

            // The director brings it out by itself.
            int guard = 0;
            while (run.Capture.State == CaptureState.None && guard++ < 60 * 200)
            {
                if (run.State == GameState.Choice) run.Hud.ChooseCard(0);
                Advance(run, 1);
            }
            ok &= Expect(run.Capture.WildName == skitter.Name, "the waiting creature should be the next wild one", report);

            // Weaken and catch it, retrying after any failed throw.
            guard = 0;
            while (!profile.Owns(skitter) && guard++ < 60 * 400)
            {
                if (run.State == GameState.Choice) run.Hud.ChooseCard(0);
                float wx, wz;
                if (run.Capture.State == CaptureState.Roaming && run.Capture.Locate(out wx, out wz))
                {
                    for (int i = 0; i < run.Enemies.Count; i++) if ((run.Enemies.Flags[i] & Flag.Wild) != 0) run.Enemies.Hit(i, 1e9f, null, default(HitOpts));
                    run.Player.X = wx + 2;
                    run.Player.Z = wz;
                }
                if (run.Capture.State == CaptureState.Downed)
                {
                    run.Input.KeyUp(Keys.E);
                    run.Input.KeyDown(Keys.E);
                }
                Advance(run, 1);
            }
            ok &= Expect(profile.Owns(skitter) && profile.OwnedCount() == 2, "catching it should add it to the roster", report);
            ok &= Expect(profile.NextTarget() != skitter && !profile.Ready(skitter), "it should no longer be waiting", report);
            ok &= Expect(run.RunNews.Exists(n => n.Contains("joined")), "the end screen should mention the new member", report);

            // A roster member can be played, with its own art and starting move.
            var second = new Run(profile);
            second.Start(skitter);
            ok &= Expect(second.Player.Moves[0].Id == skitter.Move && second.Player.Stage.Id == "skitter", "the new member should be playable", report);
            ok &= Expect(second.Atlas.Frame("skitter") >= 0 && second.Enemies.Kinds.Count == Data.Enemies.Length + Data.WildOnly.Length + Data.Bosses.Length + 1, "shared art should still register every enemy", report);

            report.AppendLine("--- Roster: locked, challenge, wild, catch, playable " + (ok ? "ok" : "FAILED"));
            return ok;
        }

        // Weakening, a failed throw, a catch, the ally's bonus, full slots and fleeing.
        static bool CheckCapture(StringBuilder report)
        {
            bool ok = true;
            var run = new Run();
            run.Start(Data.Partners[0]);
            run.GodMode = true;
            Player p = run.Player;
            Capture c = run.Capture;
            EnemyDef wild = Array.Find(Data.Enemies, x => x.Elem == "stone");
            float armorBefore = p.Stats.Armor;

            Func<int> wildIndex = () =>
            {
                for (int i = 0; i < run.Enemies.Count; i++) if ((run.Enemies.Flags[i] & Flag.Wild) != 0 && !run.Enemies.Dead[i]) return i;
                return -1;
            };
            Action weaken = () =>
            {
                int i = wildIndex();
                run.Enemies.Hit(i, 1e9f, null, default(HitOpts));
                p.X = run.Enemies.X[i] + 2;
                p.Z = run.Enemies.Z[i];
            };
            // Level-up screens pause the game, so answer any that open while a check is waiting.
            Action answerLevelUps = () =>
            {
                while (run.State == GameState.Choice && run.Hud.Opts != null && !run.Hud.Opts.Title.StartsWith("Caught"))
                {
                    run.Hud.ChooseCard(0);
                    Advance(run, 1);
                }
            };
            Func<int> throwUntilDone = () =>
            {
                answerLevelUps();
                run.Input.KeyDown(Keys.E);
                Advance(run, 1);
                run.Input.KeyUp(Keys.E);
                int guard = 0;
                while ((c.State == CaptureState.Throwing || c.State == CaptureState.Wobbling) && guard++ < 300)
                {
                    answerLevelUps();
                    Advance(run, 1);
                }
                return guard;
            };

            c.SpawnWild(wild);
            ok &= Expect(c.State == CaptureState.Roaming, "a wild creature should be roaming", report);
            weaken();
            ok &= Expect(c.State == CaptureState.Downed, "beating it should weaken it", report);
            int downed = wildIndex();
            ok &= Expect(downed >= 0 && !run.Enemies.Dead[downed], "a weakened creature should not be knocked out", report);
            ok &= Expect(run.Enemies.Nearest(run.Enemies.X[downed], run.Enemies.Z[downed], 0.5f) != downed, "moves should not target a weakened creature", report);

            // Keep throwing, re-weakening after any failure, until it is caught.
            int tries = 0;
            float firstChance = c.Chance();
            while (c.Caught == 0 && tries++ < 40)
            {
                throwUntilDone();
                if (c.State == CaptureState.Roaming)
                {
                    ok &= Expect(c.Chance() > firstChance, "the next throw should be easier", report);
                    weaken();
                }
            }
            ok &= Expect(c.Caught == 1, "a catch should eventually happen", report);
            ok &= Expect(p.Allies().Count == 1, "the catch should join as an ally", report);
            ok &= Expect(M.Abs(p.Stats.Armor - armorBefore - 3) < 0.01f, "a stone ally should give +3 armor", report);

            // Fill the slots, then a third catch must offer a replacement.
            p.AddAlly(Data.Enemies[0]);
            c.SpawnWild(Data.Enemies[2]);
            weaken();
            tries = 0;
            while (c.Caught == 1 && tries++ < 40)
            {
                throwUntilDone();
                if (c.State == CaptureState.Roaming) weaken();
            }
            Advance(run, 2);
            answerLevelUps();
            Advance(run, 2);
            ok &= Expect(run.State == GameState.Choice && run.Hud.Opts != null && run.Hud.Opts.Cards.Count == 2, "full slots should offer a replacement", report);
            float armorFull = p.Stats.Armor;
            run.Hud.ChooseCard(0);
            ok &= Expect(p.Allies().Count == 2, "replacing should keep 2 allies", report);
            ok &= Expect(M.Abs(p.Stats.Armor - (armorFull - 3)) < 0.01f, "replacing should remove the old bonus", report);

            // A weakened creature that is left alone escapes.
            int caughtBefore = c.Caught;
            c.SpawnWild(wild);
            weaken();
            p.X += 30;
            for (int f = 0; f < 60 * 13; f++)
            {
                answerLevelUps();
                Advance(run, 1);
            }
            ok &= Expect(c.State != CaptureState.Downed && c.Caught == caughtBefore, "an ignored creature should escape", report);

            report.AppendLine("--- Capture: weaken, throw, ally bonus, full slots, escape " + (ok ? "ok" : "FAILED"));
            return ok;
        }

        // Writes a sheet where every cell shows its direction and pose, for checking the pipeline by eye.
        public static void WriteDiagnosticSheet(string path)
        {
            string[] rows = { "F", "FR", "R", "BR", "B", "BL", "L", "FL" };
            string[] poses = { "idle", "w1", "w2", "w3", "w4", "air" };
            using (var bmp = new Bitmap(Atlas.Cell * Atlas.SheetColumns, Atlas.Cell * Atlas.SheetRows, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            using (var big = new System.Drawing.Font("Arial", 18, FontStyle.Bold, GraphicsUnit.Pixel))
            using (var little = new System.Drawing.Font("Arial", 11, FontStyle.Bold, GraphicsUnit.Pixel))
            {
                g.Clear(Color.Transparent);
                for (int row = 0; row < Atlas.SheetRows; row++)
                {
                    for (int col = 0; col < Atlas.SheetColumns; col++)
                    {
                        int x = col * Atlas.Cell, y = row * Atlas.Cell;
                        using (var fill = new SolidBrush(Color.FromArgb(255, 60 + row * 25, 220 - row * 20, 90 + col * 30))) g.FillEllipse(fill, x + 4, y + 8, 56, 50);
                        g.DrawString(rows[row], big, Brushes.Black, x + 14, y + 16);
                        g.DrawString(poses[col], little, Brushes.White, x + 16, y + 38);
                    }
                }
                bmp.Save(path, ImageFormat.Png);
            }
        }

        static Stream SheetStream(int w, int h, bool transparentCell)
        {
            var ms = new MemoryStream();
            using (var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Red);
                if (transparentCell)
                {
                    g.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
                    using (var clear = new SolidBrush(Color.Transparent)) g.FillRectangle(clear, Atlas.Cell, Atlas.Cell, Atlas.Cell, Atlas.Cell);
                }
                bmp.Save(ms, ImageFormat.Png);
            }
            ms.Position = 0;
            return ms;
        }

        static bool Rejects(string needle, Action act, StringBuilder report)
        {
            try
            {
                act();
            }
            catch (InvalidDataException e)
            {
                if (e.Message.Contains(needle)) return true;
                report.AppendLine("  wrong error: " + e.Message);
                return false;
            }
            report.AppendLine("  a bad sheet was accepted, expected: " + needle);
            return false;
        }

        // Sheet loading, rejection of bad files, atlas paging, and direction and pose selection.
        static bool CheckSheets(StringBuilder report)
        {
            bool ok = true;
            int fullW = Atlas.Cell * Atlas.SheetColumns, fullH = Atlas.Cell * Atlas.SheetRows;
            var atlas = new Atlas();
            atlas.AddIcon("disc", "disc", 0xffffff, 0xffffff);
            atlas.LoadSheet("a", SheetStream(fullW, fullH, false), "a.png");
            int start = atlas.Frame("a");
            ok &= atlas.IsDirectional("a");

            // Facing is relative to the camera: right, forward, and the row it should pick.
            float d = 0.7071f;
            float[,] cases = { { 0, -1, 0 }, { d, -d, 1 }, { 1, 0, 2 }, { d, d, 3 }, { 0, 1, 4 }, { -d, d, 5 }, { -1, 0, 6 }, { -d, -d, 7 } };
            bool flip;
            for (int i = 0; i < 8; i++)
            {
                int frame = atlas.CreatureFrame(start, cases[i, 0], cases[i, 1], 0, false, false, out flip);
                if (frame != start + (int)cases[i, 2] * Atlas.SheetColumns || flip)
                {
                    report.AppendLine("  wrong row for direction " + i + ": frame " + (frame - start));
                    ok = false;
                }
            }
            int walk0 = atlas.CreatureFrame(start, 0, -1, 0, true, false, out flip) - start;
            int walk3 = atlas.CreatureFrame(start, 0, -1, 3.5f, true, false, out flip) - start;
            int walk4 = atlas.CreatureFrame(start, 0, -1, 4, true, false, out flip) - start;
            int air = atlas.CreatureFrame(start, 0, -1, 2, true, true, out flip) - start;
            if (walk0 != 1 || walk3 != 4 || walk4 != 1 || air != 5)
            {
                report.AppendLine("  wrong poses: " + walk0 + " " + walk3 + " " + walk4 + " " + air);
                ok = false;
            }

            ok &= Rejects("must be 384 x 512", () => atlas.LoadSheet("b", SheetStream(100, 100, false), "b.png"), report);
            ok &= Rejects("empty cell at row 1, column 1", () => atlas.LoadSheet("c", SheetStream(fullW, fullH, true), "c.png"), report);
            ok &= Rejects("not a readable image", () => atlas.LoadSheet("d", new MemoryStream(new byte[] { 1, 2, 3 }), "d.png"), report);

            // More sheets than one page holds must spill to new pages without splitting a creature.
            var paged = new Atlas();
            for (int i = 0; i < 12; i++) paged.LoadSheet("s" + i, SheetStream(fullW, fullH, false), "s.png");
            for (int i = 0; i < 12; i++)
            {
                int first = paged.Frame("s" + i);
                if (Atlas.PageOf(first) != Atlas.PageOf(first + 47))
                {
                    report.AppendLine("  sheet " + i + " was split across pages");
                    ok = false;
                }
            }
            ok &= paged.PageCount > 1;
            report.AppendLine("--- Sprite sheets: directions, poses, bad files, paging (" + paged.PageCount + " pages for 12 sheets) " + (ok ? "ok" : "FAILED"));
            return ok;
        }

        static bool CheckFusions(StringBuilder report)
        {
            var run = new Run();
            run.Start(Data.Partners[2]);
            Player p = run.Player;
            p.Moves.Clear();
            foreach (string id in new[] { "razor-seed", "spore-cloud", "skyfall", "droplet" })
            {
                var m = new Move(id, Data.Move(id));
                m.Level = 8;
                p.Moves.Add(m);
            }
            List<FusionOption> options = Moves.FusionOptions(p);
            var names = new List<string>();
            foreach (FusionOption o in options) names.Add(o.A.Def.Name + "+" + o.B.Def.Name + "=" + o.Def.Name);
            report.AppendLine("--- Fusion rules: " + string.Join(", ", names));
            bool ok = names.Contains("Razor Seed+Spore Cloud=Verdant Gale") && names.Contains("Razor Seed+Skyfall=Glitch") && names.Contains("Razor Seed+Droplet=Bloom Surge") && names.Contains("Skyfall+Droplet=Shock Tide");
            Moves.Fuse(p, options[0]);
            p.AddDrone();
            run.GodMode = true;
            for (int f = 0; f < 3000; f++)
            {
                if (run.State == GameState.Choice) run.Hud.ChooseCard(0);
                run.Frame(1 / 60f);
                run.Input.EndFrame();
            }
            // Revive check.
            run.GodMode = false;
            p.Stats.Revives = 1;
            p.Hp = 1;
            p.Iframes = 0;
            p.Hurt(50);
            bool revived = !p.IsDead && p.Hp > 1 && p.Stats.Revives == 0;
            report.AppendLine("  fusion fired kills=" + run.Kills + " revived=" + revived);
            return ok && revived && run.Kills > 0;
        }
    }

    interface IFrameScript
    {
        bool BeforeFrame();
        void AfterFrame(int w, int h);
    }

    static class ScreenGrab
    {
        public static Bitmap Screen(int w, int h)
        {
            var raw = new byte[w * h * 4];
            Gl.Finish();
            Gl.ReadPixels(0, 0, w, h, Gl.RGBA, Gl.UNSIGNED_BYTE, raw);
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            var flipped = new byte[raw.Length];
            for (int y = 0; y < h; y++)
            {
                int src = (h - 1 - y) * w * 4, dst = y * w * 4;
                for (int x = 0; x < w; x++)
                {
                    flipped[dst + x * 4] = raw[src + x * 4 + 2];
                    flipped[dst + x * 4 + 1] = raw[src + x * 4 + 1];
                    flipped[dst + x * 4 + 2] = raw[src + x * 4];
                    flipped[dst + x * 4 + 3] = 255;
                }
            }
            Marshal.Copy(flipped, 0, data.Scan0, flipped.Length);
            bmp.UnlockBits(data);
            return bmp;
        }
    }

    // Plays by itself in a real window and saves screenshots of each main screen, for testing the renderer.
    sealed class ShotScript : IFrameScript
    {
        readonly Run run;
        readonly string dir;
        readonly Bot bot;
        readonly Partner partner;
        int step, frames;
        string pendingShot;

        public ShotScript(Run run, string dir, Partner partner)
        {
            this.run = run;
            this.dir = dir;
            this.partner = partner;
            bot = new Bot(run, true);
            Directory.CreateDirectory(dir);
        }

        void Next(string shot = null)
        {
            if (shot != null) pendingShot = shot;
            step++;
            frames = 0;
        }

        // Returns true when the script is finished.
        public bool BeforeFrame()
        {
            frames++;
            switch (step)
            {
                case 0:
                    // Some progress, so the shot shows every kind of card.
                    if (frames == 1)
                    {
                        run.Profile.Add("kills", 320);
                        run.Profile.Add("runs", 3);
                        run.Profile.Best("bestLevel", 18);
                        run.Profile.Add("shards", 260);
                    }
                    int index = Array.IndexOf(Data.Partners, partner);
                    int mx, my;
                    if (frames == 8)
                    {
                        // A locked creature, to show its challenge in the details panel.
                        run.Hud.RosterPoint(Array.FindIndex(Data.Partners, x => x.Id == "skitter"), out mx, out my);
                        run.Input.MouseX = mx;
                        run.Input.MouseY = my;
                        run.Input.Clicked = true;
                    }
                    if (frames == 12) pendingShot = "0-menu-locked";
                    if (frames == 20) pendingShot = "1-menu";
                    if (frames == 24)
                    {
                        run.Hud.LabTabPoint(true, out mx, out my);
                        run.Input.MouseX = mx;
                        run.Input.MouseY = my;
                        run.Input.Clicked = true;
                    }
                    if (frames == 28)
                    {
                        run.Hud.LabCardPoint(0, out mx, out my);
                        run.Input.MouseX = mx;
                        run.Input.MouseY = my;
                        run.Input.Clicked = true;
                    }
                    if (frames == 32)
                    {
                        if (run.Profile.Level(Data.LabUpgrades[0]) != 1) throw new InvalidOperationException("Clicking a Lab card did not buy it.");
                        pendingShot = "1b-lab";
                    }
                    if (frames == 36)
                    {
                        run.Hud.LabTabPoint(false, out mx, out my);
                        run.Input.MouseX = mx;
                        run.Input.MouseY = my;
                        run.Input.Clicked = true;
                    }
                    if (frames == 40)
                    {
                        run.Hud.RosterPoint(index, out mx, out my);
                        run.Input.MouseX = mx;
                        run.Input.MouseY = my;
                        run.Input.Clicked = true;
                    }
                    if (frames == 44)
                    {
                        run.Hud.StartPoint(out mx, out my);
                        run.Input.MouseX = mx;
                        run.Input.MouseY = my;
                        run.Input.Clicked = true;
                    }
                    if (frames == 48)
                    {
                        if (run.State != GameState.Play || run.Player.Partner != partner) throw new InvalidOperationException("Clicking through the roster menu did not start the game.");
                        run.GodMode = true;
                        Next();
                    }
                    return false;
                case 1:
                    bot.Step();
                    if (run.Time > 45 && run.State == GameState.Play) Next("2-play");
                    return false;
                case 2:
                    if (run.State == GameState.Choice)
                    {
                        Next("3-levelup");
                        return false;
                    }
                    bot.Step();
                    return false;
                case 3:
                    if (frames < 2) return false;
                    run.Hud.ChooseCard(0);
                    run.Pause();
                    Next("4-pause");
                    return false;
                case 4:
                    if (frames < 2) return false;
                    run.Resume();
                    run.Director.SummonGuardian(false);
                    Next();
                    return false;
                case 5:
                    bot.Step();
                    if (frames == 240)
                    {
                        run.Player.Level = Math.Max(run.Player.Level, 10);
                        Next("5-boss");
                    }
                    return false;
                case 6:
                    // Pose checks, on the real player sprite in a real fight.
                    if (run.State == GameState.Choice) run.Hud.ChooseCard(0);
                    if (frames == 2)
                    {
                        run.Input.KeyUp(Keys.Space);
                        run.Input.KeyDown(Keys.Space);
                    }
                    if (frames == 16) Next("6-airborne");
                    return false;
                case 7:
                    if (run.State == GameState.Choice) run.Hud.ChooseCard(0);
                    if (frames == 40)
                    {
                        run.Input.KeyDown(Keys.W);
                        run.Player.FlashT = 1;
                        Next("7-hitflash");
                    }
                    return false;
                case 8:
                    if (run.State == GameState.Choice) run.Hud.ChooseCard(0);
                    if (frames == 6)
                    {
                        run.Input.KeyUp(Keys.ShiftKey);
                        run.Input.KeyDown(Keys.ShiftKey);
                    }
                    if (frames == 12) Next("8-slide");
                    return false;
                case 9:
                    // Put a tall pillar directly between the camera and the player.
                    if (frames == 1)
                    {
                        Collider pillar = run.Terrain.Colliders.Find(o => o.Tall);
                        run.Player.X = pillar.X + (pillar.R + 1.5f);
                        run.Player.Z = pillar.Z;
                        run.Player.Y = run.Terrain.HeightAt(run.Player.X, run.Player.Z);
                        run.CamTx = run.Player.X;
                        run.CamTz = run.Player.Z;
                        run.CamYaw = -M.Pi / 2;
                    }
                    if (run.State == GameState.Choice) run.Hud.ChooseCard(0);
                    if (frames == 30) Next("9-camera-pillar");
                    return false;
                case 10:
                    if (frames < 2) return false;
                    run.GodMode = false;
                    run.Player.Stats.Revives = 0;
                    run.Player.Hp = 1;
                    run.Player.Iframes = 0;
                    run.Player.Hurt(100);
                    Next();
                    return false;
                case 11:
                    if (frames == 5) pendingShot = "10-end";
                    return frames > 8;
            }
            return true;
        }

        public void AfterFrame(int w, int h)
        {
            if (pendingShot == null) return;
            using (Bitmap bmp = ScreenGrab.Screen(w, h)) bmp.Save(Path.Combine(dir, pendingShot + ".png"), ImageFormat.Png);
            pendingShot = null;
        }
    }

    // Walks through a wild encounter and both evolutions in a real window, saving each step.
    // Captures the mode and difficulty buttons, the Survival HUD, a pact choice, the see-through
    // sprite in front of the player, and the Survival results screen.
    // Clicks through the first-launch window as a player would, then shows a slime choosing its form.
    sealed class StarterShotScript : IFrameScript
    {
        readonly Run run;
        readonly string dir;
        int frames;
        string pendingShot;

        public StarterShotScript(Run run, string dir)
        {
            this.run = run;
            this.dir = dir;
            Directory.CreateDirectory(dir);
        }

        void Click(int x, int y)
        {
            run.Input.MouseX = x;
            run.Input.MouseY = y;
            run.Input.Clicked = true;
        }

        public bool BeforeFrame()
        {
            frames++;
            int x, y;
            if (frames == 8) pendingShot = "0-window";
            if (frames == 12)
            {
                run.Hud.StarterConfirmPoint(out x, out y);
                Click(x, y);
            }
            if (frames == 16 && !run.Profile.NeedsStarter) throw new InvalidOperationException("Confirming with nothing picked should do nothing.");
            if (frames == 20)
            {
                run.Hud.StarterPoint(2, out x, out y);
                Click(x, y);
            }
            if (frames == 26) pendingShot = "1-picked";
            if (frames == 30)
            {
                run.Hud.StarterConfirmPoint(out x, out y);
                Click(x, y);
            }
            if (frames == 36)
            {
                if (run.Profile.StarterChoice != "golem-stage-i" || run.Profile.OwnedCount() != 1) throw new InvalidOperationException("Clicking through the window did not unlock only Toyat.");
                pendingShot = "2-roster";
            }
            if (frames == 44)
            {
                Partner slime = Array.Find(Data.Partners, p => p.Forms != null);
                run.Start(slime);
                run.GodMode = true;
            }
            if (frames == 120) pendingShot = "3-slime";
            if (frames == 124)
            {
                if (run.State == GameState.Choice) run.Hud.ChooseCard(0);
                run.Player.Level = 10;
            }
            if (frames > 124 && frames < 140 && run.State == GameState.Choice && !run.Hud.Opts.Title.EndsWith("changing")) run.Hud.ChooseCard(0);
            if (frames == 140) pendingShot = "4-forms";
            if (frames == 146 && run.State == GameState.Choice) run.Hud.ChooseCard(0);
            if (frames == 200) pendingShot = "5-form";
            return frames > 206;
        }

        public void AfterFrame(int w, int h)
        {
            if (pendingShot == null) return;
            using (Bitmap bmp = ScreenGrab.Screen(w, h)) bmp.Save(Path.Combine(dir, pendingShot + ".png"), ImageFormat.Png);
            pendingShot = null;
        }
    }

    sealed class ModeShotScript : IFrameScript
    {
        readonly Run run;
        readonly string dir;
        int frames, pactFrame;
        string pendingShot;

        public ModeShotScript(Run run, string dir)
        {
            this.run = run;
            this.dir = dir;
            Directory.CreateDirectory(dir);
        }

        public bool BeforeFrame()
        {
            frames++;
            if (frames == 2) { run.Mode = GameMode.Survival; run.DifficultyIndex = 1; }
            bool onPact = run.State == GameState.Choice && run.Hud.Opts != null && run.Hud.Opts.Title == "Choose a pact";
            if (onPact && pactFrame == 0) { pactFrame = frames; pendingShot = "3-pact"; }
            if (run.State == GameState.Choice && !onPact && frames < 900) run.Hud.ChooseCard(0);
            if (frames == 10) pendingShot = "0-menu-survival-hard";
            if (frames == 20)
            {
                run.Start(Data.Partners[0]);
                run.GodMode = true;
            }
            if (frames == 200) pendingShot = "1-survival-hud";
            if (frames == 210)
            {
                // A chest directly between the camera and the player.
                run.CamYaw = 0;
                run.Encounters.DropChest(run.Player.X, run.Player.Z + 5);
            }
            if (frames == 225) pendingShot = "2-see-through";
            if (frames == 230) run.Director.NextBossT = 0;
            if (frames == 240)
            {
                int boss = run.Director.BossIndex();
                if (boss < 0) throw new InvalidOperationException("The milestone boss did not arrive.");
                run.Enemies.Hit(boss, 1e9f, null, default(HitOpts));
            }
            if (frames == 900 && pactFrame == 0) throw new InvalidOperationException("The pact screen never opened.");
            if (pactFrame > 0 && frames == pactFrame + 5)
            {
                run.Hud.CloseButton();
                run.Time = 754;
                run.GodMode = false;
                run.Player.Stats.Revives = 0;
                run.Player.Iframes = 0;
                run.Player.Hurt(1e7f);
            }
            if (pactFrame > 0 && frames == pactFrame + 15) pendingShot = "4-end";
            if (pactFrame > 0 && frames > pactFrame + 20) return true;
            return false;
        }

        public void AfterFrame(int w, int h)
        {
            if (pendingShot == null) return;
            using (Bitmap bmp = ScreenGrab.Screen(w, h)) bmp.Save(Path.Combine(dir, pendingShot + ".png"), ImageFormat.Png);
            pendingShot = null;
        }
    }

    sealed class EvolutionShotScript : IFrameScript
    {
        readonly Run run;
        readonly string dir;
        readonly Partner partner;
        readonly Bot bot;
        int step, frames;
        string pendingShot;

        public EvolutionShotScript(Run run, string dir, Partner partner)
        {
            this.run = run;
            this.dir = dir;
            this.partner = partner;
            bot = new Bot(run, false);
            Directory.CreateDirectory(dir);
        }

        void Next(string shot = null)
        {
            if (shot != null) pendingShot = shot;
            step++;
            frames = 0;
        }

        int WildIndex()
        {
            Enemies e = run.Enemies;
            for (int i = 0; i < e.Count; i++) if ((e.Flags[i] & Flag.Wild) != 0 && !e.Dead[i]) return i;
            return -1;
        }

        void WeakenWild()
        {
            int i = WildIndex();
            if (i < 0) return;
            run.Enemies.Hit(i, 1e9f, null, default(HitOpts));
            run.Player.X = run.Enemies.X[i] + 3;
            run.Player.Z = run.Enemies.Z[i] + 2;
            run.Player.Y = run.Terrain.HeightAt(run.Player.X, run.Player.Z);
        }

        public bool BeforeFrame()
        {
            frames++;
            if (run.State == GameState.Choice) run.Hud.ChooseCard(0);
            Capture c = run.Capture;
            switch (step)
            {
                case 0:
                    run.Start(partner);
                    run.GodMode = true;
                    Next();
                    return false;
                case 1:
                    bot.Step();
                    if (frames == 600)
                    {
                        run.Capture.SpawnWild(Data.Enemies[4]);
                        int i = WildIndex();
                        run.Enemies.X[i] = run.Player.X + 7;
                        run.Enemies.Z[i] = run.Player.Z - 7;
                        Next();
                    }
                    return false;
                case 2:
                    if (frames == 30) Next("1-wild");
                    return false;
                case 3:
                    if (frames == 2) WeakenWild();
                    if (frames == 30) Next("2-weakened");
                    return false;
                case 4:
                    if (c.State == CaptureState.Downed)
                    {
                        run.Input.KeyUp(Keys.E);
                        run.Input.KeyDown(Keys.E);
                    }
                    else if (c.State == CaptureState.Throwing && frames > 2)
                    {
                        run.Input.KeyUp(Keys.E);
                        if (c.Caught == 0 && frames >= 1) Next("3-throw");
                    }
                    return false;
                case 5:
                    if (c.State == CaptureState.Wobbling && frames > 20) Next("4-wobble");
                    return false;
                case 6:
                    // Retry until it is caught, showing a break-out the first time one happens.
                    if (c.Caught > 0)
                    {
                        Next();
                        return false;
                    }
                    if (c.State == CaptureState.Roaming)
                    {
                        WeakenWild();
                    }
                    else if (c.State == CaptureState.Downed)
                    {
                        run.Input.KeyUp(Keys.E);
                        run.Input.KeyDown(Keys.E);
                    }
                    return false;
                case 7:
                    if (frames == 40) Next("5-caught");
                    return false;
                case 8:
                    bot.Step();
                    if (frames == 300)
                    {
                        run.Player.Level = Math.Max(run.Player.Level, 10);
                        Next();
                    }
                    return false;
                case 9:
                    if (frames == 14) Next("6-evolve-stage2");
                    return false;
                case 10:
                    bot.Step();
                    if (frames == 600)
                    {
                        run.Player.Level = Math.Max(run.Player.Level, 22);
                        Next();
                    }
                    return false;
                case 11:
                    if (frames == 14) Next("7-evolve-stage3");
                    return false;
                case 12:
                    bot.Step();
                    if (frames == 400)
                    {
                        run.Pause();
                        Next("8-final-pause");
                    }
                    return false;
                case 13:
                    return frames > 3;
            }
            return true;
        }

        public void AfterFrame(int w, int h)
        {
            if (pendingShot == null) return;
            using (Bitmap bmp = ScreenGrab.Screen(w, h)) bmp.Save(Path.Combine(dir, pendingShot + ".png"), ImageFormat.Png);
            pendingShot = null;
        }
    }

    // Shows one creature in the real renderer in all 8 directions and all 6 poses, as one labelled image.
    // Use it with a diagnostic sheet to check directions, or with real art to review a creature.
    sealed class TurnaroundScript : IFrameScript
    {
        const int Crop = 420, Tile = 150;
        static readonly string[] Dirs = { "front", "front-right", "right", "back-right", "back", "back-left", "left", "front-left" };
        static readonly string[] Poses = { "idle", "walk 1", "walk 2", "walk 3", "walk 4", "air" };
        readonly Run run;
        readonly string path;
        readonly Partner partner;
        readonly int stage;
        readonly Bitmap sheet = new Bitmap(Tile * 8 + 60, Tile * 6 + 20, PixelFormat.Format32bppArgb);
        int dir, pose, wait;
        bool started;

        public TurnaroundScript(Run run, string path, Partner partner, int stage)
        {
            this.stage = stage;
            this.run = run;
            this.path = path;
            this.partner = partner;
        }

        public bool BeforeFrame()
        {
            if (!started)
            {
                started = true;
                run.Start(partner);
                if (partner.Forms != null && stage > 0) run.Player.Stages = new[] { run.Player.Stages[0], partner.Forms[Math.Min(stage, partner.Forms.Length) - 1] };
                run.Player.StageIndex = Math.Min(stage, run.Player.Stages.Length - 1);
                run.Encounters.Items.Clear();
                run.State = GameState.Choice;
                run.Hud.Hidden = true;
                run.CamYaw = 0;
                run.CamPitch = 0.42f;
                using (var g = Graphics.FromImage(sheet))
                using (var font = new System.Drawing.Font("Arial", 11, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    g.Clear(Color.FromArgb(30, 26, 48));
                    for (int i = 0; i < 8; i++) g.DrawString(Dirs[i], font, Brushes.White, 64 + i * Tile, 3);
                    for (int i = 0; i < 6; i++) g.DrawString(Poses[i], font, Brushes.White, 4, 20 + i * Tile + 60);
                }
            }
            if (dir >= 8) return true;
            Player p = run.Player;
            float a = dir * M.Pi / 4;
            p.FacingX = M.Sin(a);
            p.FacingZ = M.Cos(a);
            p.Moving = pose >= 1 && pose <= 4;
            p.Grounded = pose != 5;
            p.Anim = pose - 0.5f;
            return false;
        }

        public void AfterFrame(int w, int h)
        {
            if (dir >= 8 || wait++ < 2) return;
            wait = 0;
            using (Bitmap screen = ScreenGrab.Screen(w, h))
            using (var g = Graphics.FromImage(sheet))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                var src = new Rectangle((w - Crop) / 2, h / 2 - Crop / 2 - 40, Crop, Crop);
                g.DrawImage(screen, new Rectangle(60 + dir * Tile, 20 + pose * Tile, Tile, Tile), src, GraphicsUnit.Pixel);
            }
            if (++pose < 6) return;
            pose = 0;
            if (++dir >= 8)
            {
                sheet.Save(path, ImageFormat.Png);
                sheet.Dispose();
            }
        }
    }
}


