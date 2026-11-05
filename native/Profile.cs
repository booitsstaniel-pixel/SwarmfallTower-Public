using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Swarmfall
{
    // Everything that lasts between runs: lifetime counters, the roster, Lab upgrades, and the last partner played.
    //
    // The save is a small text file ending in a marker line, so a cut-off file is detected.
    // Writes go to a temporary file first and then replace the save, keeping the previous one as a backup.
    // A damaged save is set aside rather than deleted. A save from a newer version is never overwritten.
    // Lines this version doesn't recognise are kept, so later versions can add to the format.
    sealed class Profile
    {
        const string Header = "swarmfall-save";
        const int Version = 1;
        const string EndMarker = "end";

        readonly string path;
        readonly Dictionary<string, long> counters = new Dictionary<string, long>();
        readonly HashSet<string> roster = new HashSet<string>();
        readonly Dictionary<string, int> upgrades = new Dictionary<string, int>();
        readonly HashSet<string> notified = new HashSet<string>();
        readonly List<string> unknownLines = new List<string>();
        bool readOnly;
        public string LastPartner;
        // The starter picked in the first-launch window. Null until then.
        public string StarterChoice;
        public string Warning;

        Profile(string path)
        {
            this.path = path;
        }

        // A profile that never touches the disk. Tests and screenshot scripts use this, so they can't affect a real save.
        public static Profile InMemory() { return new Profile(null); }

        public static string DefaultPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SwarmfallTower", "save.txt");
        }

        public static Profile Load(string path)
        {
            var profile = new Profile(path);
            if (!File.Exists(path)) return profile;
            try
            {
                profile.Parse(File.ReadAllLines(path));
            }
            catch (FormatException e)
            {
                profile.SetAside(e.Message);
            }
            catch (IOException e)
            {
                // The file exists but can't be read. Saving over it could lose progress, so don't.
                profile.readOnly = true;
                profile.Warning = "Your save could not be read, so progress this session won't be saved. " + e.Message;
            }
            catch (UnauthorizedAccessException e)
            {
                profile.readOnly = true;
                profile.Warning = "Your save could not be read, so progress this session won't be saved. " + e.Message;
            }
            return profile;
        }

        void Parse(string[] lines)
        {
            if (lines.Length < 2 || !lines[0].StartsWith(Header + " ")) throw new FormatException("It has no valid header.");
            int version;
            if (!int.TryParse(lines[0].Substring(Header.Length + 1), out version)) throw new FormatException("Its version is unreadable.");
            if (version > Version)
            {
                readOnly = true;
                Warning = "This save is from a newer version of the game, so this version won't change it.";
                return;
            }
            if (lines[lines.Length - 1] != EndMarker) throw new FormatException("It was cut off.");
            for (int i = 1; i < lines.Length - 1; i++)
            {
                string line = lines[i];
                int eq = line.IndexOf('=');
                if (eq < 0) throw new FormatException("Line " + (i + 1) + " is damaged.");
                string key = line.Substring(0, eq), value = line.Substring(eq + 1);
                if (key.StartsWith("counter."))
                {
                    long n;
                    if (!long.TryParse(value, out n) || n < 0) throw new FormatException("Line " + (i + 1) + " is damaged.");
                    counters[key.Substring(8)] = n;
                }
                else if (key.StartsWith("upgrade."))
                {
                    int level;
                    if (!int.TryParse(value, out level) || level < 0) throw new FormatException("Line " + (i + 1) + " is damaged.");
                    upgrades[key.Substring(8)] = level;
                }
                else if (key == "roster") AddAll(roster, value);
                else if (key == "notified") AddAll(notified, value);
                else if (key == "last") LastPartner = MigratePartner(value);
                else if (key == "starter") StarterChoice = value;
                else unknownLines.Add(line);
            }
        }

        // Creatures that were replaced. Their old IDs can still be in older saves.
        static string MigratePartner(string id)
        {
            switch (id)
            {
                case "sproutling": return "golem-stage-i";
                case "emberkit": return "burrowkit";
                case "tidepup": return "fugiri";
                default: return id;
            }
        }

        static void AddAll(HashSet<string> set, string csv)
        {
            foreach (string id in csv.Split(',')) if (id.Length > 0) set.Add(id);
        }

        // Moves a damaged save aside and starts fresh, so the player can still play and nothing is deleted.
        void SetAside(string reason)
        {
            counters.Clear();
            roster.Clear();
            upgrades.Clear();
            notified.Clear();
            unknownLines.Clear();
            LastPartner = null;
            StarterChoice = null;
            string aside = path + ".damaged-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            try
            {
                File.Move(path, aside);
                Warning = "Your save was damaged, so a new one was started. " + reason + " The old file was kept at " + aside;
            }
            catch (Exception e)
            {
                if (!(e is IOException || e is UnauthorizedAccessException)) throw;
                readOnly = true;
                Warning = "Your save was damaged and could not be moved, so progress this session won't be saved. " + reason;
            }
        }

        // Returns false if the save could not be written. Never throws, so a full disk can't crash a run.
        public bool Save()
        {
            if (path == null || readOnly) return path == null;
            var text = new StringBuilder();
            text.AppendLine(Header + " " + Version);
            var keys = new List<string>(counters.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (string key in keys) text.AppendLine("counter." + key + "=" + counters[key]);
            var ids = new List<string>(upgrades.Keys);
            ids.Sort(StringComparer.Ordinal);
            foreach (string id in ids) text.AppendLine("upgrade." + id + "=" + upgrades[id]);
            text.AppendLine("roster=" + string.Join(",", roster));
            text.AppendLine("notified=" + string.Join(",", notified));
            if (LastPartner != null) text.AppendLine("last=" + LastPartner);
            if (StarterChoice != null) text.AppendLine("starter=" + StarterChoice);
            foreach (string line in unknownLines) text.AppendLine(line);
            text.AppendLine(EndMarker);
            string temp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(temp, text.ToString());
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
                return true;
            }
            catch (Exception e)
            {
                if (!(e is IOException || e is UnauthorizedAccessException)) throw;
                Warning = "Your progress could not be saved. " + e.Message;
                return false;
            }
        }

        public long Get(string counter)
        {
            long n;
            counters.TryGetValue(counter, out n);
            return n;
        }

        public void Set(string counter, long value) { counters[counter] = value; }
        public void Add(string counter, long amount = 1) { counters[counter] = Get(counter) + amount; }
        public void Best(string counter, long value) { if (value > Get(counter)) counters[counter] = value; }

        // Levels above the maximum (say, from a hand-edited save) count as the maximum.
        public int Level(UpgradeDef u)
        {
            int n;
            upgrades.TryGetValue(u.Id, out n);
            return Math.Min(n, u.Max);
        }

        public int Cost(UpgradeDef u) { return (int)Math.Round(u.BaseCost * Math.Pow(u.Growth, Level(u))); }
        public bool CanBuy(UpgradeDef u) { return Level(u) < u.Max && Get("shards") >= Cost(u); }

        public bool Buy(UpgradeDef u)
        {
            if (!CanBuy(u)) return false;
            counters["shards"] = Get("shards") - Cost(u);
            upgrades[u.Id] = Level(u) + 1;
            Save();
            return true;
        }

        public bool Owns(Partner p) { return p.Id == StarterChoice || roster.Contains(p.Id); }

        // True until a starter is picked. An unknown saved starter counts as not picked, so nobody gets stuck with no creature.
        // A save that picked a creature that is no longer offered (Furrowkit) keeps it and isn't asked again.
        public bool NeedsStarter
        {
            get { return StarterChoice == null || !Array.Exists(Data.Partners, p => (p.Starter || Array.IndexOf(Data.FormerStarters, p.Id) >= 0) && p.Id == StarterChoice); }
        }

        public bool ChooseStarter(Partner p)
        {
            if (!NeedsStarter || !p.Starter) return false;
            StarterChoice = p.Id;
            LastPartner = p.Id;
            Save();
            return true;
        }
        public void AddToRoster(Partner p) { roster.Add(p.Id); }
        public bool ChallengeDone(Partner p) { return Get(p.ChallengeCounter) >= p.ChallengeTarget; }
        public bool Ready(Partner p) { return !Owns(p) && ChallengeDone(p); }

        public int OwnedCount()
        {
            int n = 0;
            foreach (Partner p in Data.Partners) if (Owns(p)) n++;
            return n;
        }

        // A creature whose challenge is complete, waiting to be caught. Null when there are none.
        public Partner NextTarget()
        {
            foreach (Partner p in Data.Partners) if (Ready(p)) return p;
            return null;
        }

        // Partners whose challenge has just been completed. Each is reported once, ever.
        public List<Partner> TakeNewlyReady()
        {
            var fresh = new List<Partner>();
            foreach (Partner p in Data.Partners)
            {
                if (Ready(p) && notified.Add(p.Id)) fresh.Add(p);
            }
            return fresh;
        }
    }
}
