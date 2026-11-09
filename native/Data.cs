using System;
using System.Collections.Generic;

namespace Swarmfall
{
    // All tuning lives here, so new creatures, moves and floors need no engine changes.
    static class Flag
    {
        public const int Flying = 1, Shooter = 2, Charger = 4, Elite = 8, Boss = 16, Wild = 32, Downed = 64, Captive = 128;
    }

    enum Kind { Bolt, Barrage, Boomerang, Nova, Cone, Beam, Lob, Smite, Vortex, Orbit, Aura, Fusion, Lance, Charge }

    struct Status
    {
        public float Burn, Slow, Stun;
        public Status(float burn, float slow, float stun) { Burn = burn; Slow = slow; Stun = stun; }
    }

    sealed class Element
    {
        public string Name;
        public uint Color;
        public string[] Strong = new string[0], Weak = new string[0], Immune = new string[0];
    }

    sealed class Art
    {
        public float BodyX, BodyY, Head;
        public string Ears;
        public bool Tail, Wings;
        public int Legs = 2;
        public uint Main, Accent;
        public uint Belly;
        public bool Angry;
    }

    // One form in an evolution line. Stage 0 is the partner itself.
    sealed class Stage
    {
        public string Id, Name;
        public int Level;
        public float Scale = 1;
        public string Gain;
        // A second type gained at this stage. Keeps the first one.
        public string AddElem;
        // Replaces the first type at this stage, for branching forms.
        public string Elem;
        public StatChange[] Stats = new StatChange[0];
        public Art Art;
    }

    sealed class AllyBonus
    {
        public StatChange Stat;
        public string Text;
    }

    sealed class Partner
    {
        public string Id, Name, Role, Blurb, Elem, Elem2, Move;
        // Might and Armor cover physical moves and hits, Focus and Will cover special ones (see MoveDef.Special).
        public float Might = 1, Focus = 1, MaxHp = 100, Armor, Will, Regen, Magnet = 4.5f;
        public Art Art;
        public Stage[] Evolutions = new Stage[0];
        // Starters are offered in the first-launch window. You pick one, and every other creature
        // has a challenge and must then be caught in the wild.
        public bool Starter;
        // Alternative second stages. At FormLevel the one matching the most common type among its moves is taken.
        public Stage[] Forms;
        public int FormLevel;
        public string ChallengeCounter, ChallengeText;
        public long ChallengeTarget;

        // The stage that adds a second type, or null if this line only ever has one.
        public Stage DualStage()
        {
            foreach (Stage s in Evolutions) if (s.AddElem != null) return s;
            return null;
        }

        // Stage 0 comes first, built from the partner's own name and art.
        // Every stage this creature can ever reach, including all its forms.
        public List<Stage> AllStages()
        {
            var all = new List<Stage>(Stages());
            if (Forms != null) all.AddRange(Forms);
            return all;
        }

        // How the type reads on menus, including any later change.
        public string TypeLine()
        {
            string first = Data.Elements[Elem].Name.ToUpper();
            if (Forms != null) return first + " · " + Forms.Length + " FORMS AT LV " + FormLevel;
            Stage dual = DualStage();
            if (dual != null && Elem2 != null) return first + " / " + Data.Elements[Elem2].Name.ToUpper() + " · " + Data.Elements[dual.AddElem].Name.ToUpper() + " AT LV " + dual.Level;
            if (dual != null) return first + " · + " + Data.Elements[dual.AddElem].Name.ToUpper() + " AT LV " + dual.Level;
            return Elem2 != null ? first + " / " + Data.Elements[Elem2].Name.ToUpper() : first;
        }

        public Stage[] Stages()
        {
            var all = new Stage[Evolutions.Length + 1];
            all[0] = new Stage { Id = Id, Name = Name, Art = Art };
            Array.Copy(Evolutions, 0, all, 1, Evolutions.Length);
            return all;
        }
    }

    sealed class EnemyDef
    {
        public string Id, Name, Elem, Elem2;
        public float Hp, Speed, Dmg, Size, Xp, Cost = 1, Tier = 1;
        public int Flags;
        public Art Art;
    }

    sealed class MoveDef
    {
        public string Id, Name, Elem, Desc, Tag;
        // Any type can be either. Each move picks by theme and function: physical moves (Might) are things that
        // strike, cut or crush; special moves (Focus) are energy, elements, sound and curses.
        public bool Special;
        public Kind Kind;
        public float Cd, Dmg, Range, Radius, Speed, Knock, Arc, Width, Dur, Spin, Spread;
        public int Count = 1, Pierce;
        public bool SpreadAll, Glitch;
        public Status St;
        public MoveDef[] Parts;
        // Only this stage can be offered this signature move. Null means anyone can learn it.
        public string Signature;
        // Level-up upgrades this move can roll, in place of the usual ones for its kind (see Upgrades.PoolOf).
        public string[] Pool;
    }

    sealed class PassiveDef
    {
        public string Id, Name, Stat, Unit, Desc;
        public bool Mul;
        public float Value;
    }

    sealed class StatChange
    {
        public string Stat;
        public bool Mul;
        public float Value;
        public StatChange(string stat, bool mul, float value) { Stat = stat; Mul = mul; Value = value; }
    }

    sealed class ItemDef
    {
        public string Id, Name, Tier, Desc;
        public int Max;
        public StatChange[] Stats;
    }

    // A permanent upgrade bought in the Lab with Shards. Stat is a Stats field, or a run tool handled in Player.ApplyUpgrades.
    sealed class UpgradeDef
    {
        public string Id, Name, Desc, Stat;
        public int Max, BaseCost;
        public float Growth = 1.5f, Per;
        public bool Mul;
    }

    enum GameMode { Classic, Survival }

    sealed class Difficulty
    {
        public string Name, Desc, WinCounter;
        public float Hp, Dmg, Credits, Shards;
    }

    // Offered after beating a Survival milestone: power now, in exchange for a harder horde.
    sealed class Pact
    {
        public string Name, Desc;
        public StatChange Gain;
        public float Heat;
    }

    sealed class Rarity
    {
        public string Id, Label;
        public float Mult, Weight;
        public uint Color;
    }

    sealed class Trade
    {
        public StatChange Up, Down;
        public string UpText, DownText;
    }

    sealed class Biome
    {
        public string Name;
        public uint Sky, Fog, FloorA, FloorB, Rim, Pillar, Plinth, Prop, Glow, HemiTop, HemiBot, Sun, Sprite;
    }

    sealed class SignatureFusion
    {
        public string A, B;
        public MoveDef Def;
    }

    static class Data
    {
        public const int ZoneCount = 3;
        const int SlimeFormLevel = 10;
        public const float ZoneTime = 240;

        public static readonly Dictionary<string, Element> Elements = new Dictionary<string, Element>
        {
            { "fire", new Element { Name = "Flame", Color = 0xff7a3c, Strong = new[] { "leaf", "iron", "spirit" }, Weak = new[] { "fire", "water", "stone" } } },
            { "water", new Element { Name = "Tide", Color = 0x46b8ff, Strong = new[] { "fire", "stone", "iron" }, Weak = new[] { "water", "leaf" } } },
            { "leaf", new Element { Name = "Bloom", Color = 0x6fdc5a, Strong = new[] { "water", "stone" }, Weak = new[] { "fire", "leaf", "iron" } } },
            { "volt", new Element { Name = "Volt", Color = 0xffd93b, Strong = new[] { "water", "iron" }, Weak = new[] { "leaf", "volt" }, Immune = new[] { "stone" } } },
            { "stone", new Element { Name = "Stone", Color = 0xc9a06a, Strong = new[] { "fire", "volt", "venom" }, Weak = new[] { "leaf", "iron" }, Immune = new[] { "spirit" } } },
            { "iron", new Element { Name = "Iron", Color = 0x9fb4c8, Strong = new[] { "leaf", "stone", "spirit" }, Weak = new[] { "fire", "water", "volt", "iron" } } },
            { "venom", new Element { Name = "Venom", Color = 0xb04fd0, Strong = new[] { "water", "leaf", "basic" }, Weak = new[] { "stone", "venom", "spirit" }, Immune = new[] { "iron" } } },
            { "spirit", new Element { Name = "Spirit", Color = 0xb98cff, Strong = new[] { "volt", "venom", "spirit" }, Weak = new[] { "fire", "iron" }, Immune = new[] { "basic" } } },
            { "basic", new Element { Name = "Basic", Color = 0xcfc9b8, Weak = new[] { "stone", "iron" }, Immune = new[] { "spirit" } } },
        };

        public static uint ElemColor(string elem)
        {
            Element e;
            return elem != null && Elements.TryGetValue(elem, out e) ? e.Color : 0xffffff;
        }

        // A creature with two types uses whichever works better against this target.
        public static string BestElem(string first, string second, string defender, string defender2 = null)
        {
            if (second == null) return first;
            return Effectiveness(second, defender, defender2) > Effectiveness(first, defender, defender2) ? second : first;
        }

        public static float Effectiveness(string attacker, string defender)
        {
            Element a;
            if (attacker == null || defender == null || !Elements.TryGetValue(attacker, out a)) return 1;
            if (Array.IndexOf(a.Immune, defender) >= 0) return 0;
            if (Array.IndexOf(a.Strong, defender) >= 0) return 2;
            if (Array.IndexOf(a.Weak, defender) >= 0) return 0.5f;
            return 1;
        }

        // A dual-type defender multiplies both matchups, giving 4x, 1/4 or 0.
        public static float Effectiveness(string attacker, string defender, string defender2)
        {
            return Effectiveness(attacker, defender) * Effectiveness(attacker, defender2);
        }

        // Bosses are never fully immune and never fully ignored, so no partner can be stuck against one.
        public const float BossImmunityFloor = 0.25f;
        // A boss's hits are already huge, so a 4x weakness counts as 2x against it.
        public const float BossAttackCap = 2;
        // The first-launch window offers these, in this order (user, 2026-11-05).
        static readonly Partner[] Starters =
        {
            // Names, types and sprites approved by the user; drawn sheets from Codex. Stats, move, challenge and
            // signature fusion were proposed by Claude and approved by the user on 2026-11-05.
            new Partner { Id = "chichar", Name = "Chichar", ChallengeCounter = "kills", ChallengeTarget = 300, ChallengeText = "Get 300 knockouts in total.", Role = "Trickster", Blurb = "A tiny fan-eared marmoset that grows into a lean, fanged loris.", Elem = "volt", Move = "skyfall", MaxHp = 95, Armor = 0, Will = 1,
                Art = new Art { BodyX = 4, BodyY = 4, Head = 5, Ears = "round", Tail = true, Main = 0xe2c27a, Accent = 0x7a4ab0, Belly = 0xf0dcae },
                Evolutions = new[]
                {
                    new Stage { Id = "bijiban", Name = "Bijiban", Level = 10, Scale = 1.2f, Gain = "+25 max HP, +1 armor, +1 will, +10% focus and +5% attack speed", Stats = new[] { S("maxHp", 25), S("armor", 1), S("will", 1), X("focus", 0.1f), X("haste", 0.05f) },
                        Art = new Art { BodyX = 5, BodyY = 6, Head = 5, Ears = "round", Tail = true, Main = 0xd8b46a, Accent = 0x7a4ab0, Belly = 0xecd4a0 } },
                    new Stage { Id = "chamzahar", Name = "Chamzahar", Level = 22, Scale = 1.5f, AddElem = "venom", Gain = "Gains Venom type, +50 max HP, +2 armor, +2 will, +10% focus and +15% might", Stats = new[] { S("maxHp", 50), S("armor", 2), S("will", 2), X("focus", 0.1f), X("might", 0.15f) },
                        Art = new Art { BodyX = 6, BodyY = 8, Head = 6, Ears = "round", Tail = true, Main = 0xc8a060, Accent = 0x6a3aa0, Belly = 0xe0c890, Angry = true } },
                } },
            new Partner { Id = "fugiri", Name = "Fugiri", ChallengeCounter = "guardians", ChallengeTarget = 2, ChallengeText = "Beat 2 guardians.", Role = "Drifter", Blurb = "Timid little puffer fish that hardens into a spiked metal sphere.", Elem = "water", Move = "droplet", MaxHp = 100, Armor = 1, Will = 1,
                Art = new Art { BodyX = 8, BodyY = 7, Head = 6, Legs = 0, Tail = true, Main = 0x2c2c6e, Accent = 0xf0c040, Belly = 0xf0e8c8 },
                Evolutions = new[]
                {
                    new Stage { Id = "yugiri", Name = "Yugiri", Level = 10, Scale = 1.25f, Gain = "+30 max HP, +3 armor, +2 will and +5% focus", Stats = new[] { S("maxHp", 30), S("armor", 3), S("will", 2), X("focus", 0.05f) },
                        Art = new Art { BodyX = 10, BodyY = 8, Head = 7, Ears = "horn", Legs = 0, Tail = true, Main = 0x3a3a44, Accent = 0xe8d9b0, Belly = 0xf0c040 } },
                    new Stage { Id = "yuninjo", Name = "Yuninjo", Level = 22, Scale = 1.6f, AddElem = "iron", Gain = "Gains Iron type, +60 max HP, +6 armor, +4 will, +10% focus and +10% might, but -10% speed", Stats = new[] { S("maxHp", 60), S("armor", 6), S("will", 4), X("focus", 0.1f), X("might", 0.1f), X("speed", -0.1f) },
                        Art = new Art { BodyX = 12, BodyY = 10, Head = 7, Ears = "horn", Legs = 0, Main = 0x2a1a3a, Accent = 0x5a3a70, Belly = 0x2a1a3a, Angry = true } },
                } },
            // Sprites from Codex, stored under these IDs.
            new Partner { Id = "golem-stage-i", Name = "Toyat", ChallengeCounter = "evolutions", ChallengeTarget = 3, ChallengeText = "Evolve 3 times.", Role = "Guardian", Blurb = "A little stone sprite bound by vines that grows into a living temple.", Elem = "stone", Move = "razor-seed", MaxHp = 115, Armor = 1, Will = 1, Regen = 0.6f,
                Art = new Art { BodyX = 6, BodyY = 6, Head = 6, Ears = "leaf", Main = 0xb0a68c, Accent = 0x6fdc5a, Belly = 0x8a8068 },
                Evolutions = new[]
                {
                    new Stage { Id = "golem-stage-ii", Name = "Eytetl", Level = 10, Scale = 1.25f, Gain = "+30 max HP, +2 armor, +1 will and +0.4 HP regen", Stats = new[] { S("maxHp", 30), S("armor", 2), S("will", 1), S("regen", 0.4f) },
                        Art = new Art { BodyX = 9, BodyY = 8, Head = 6, Main = 0xb0a68c, Accent = 0x5aa83c, Belly = 0x8a8068 } },
                    new Stage { Id = "golem-stage-iii", Name = "Xutetl", Level = 22, Scale = 1.6f, AddElem = "leaf", Gain = "Gains Bloom type, +60 max HP, +4 armor, +3 will and +10% might, but -10% speed", Stats = new[] { S("maxHp", 60), S("armor", 4), S("will", 3), S("regen", 0.6f), X("might", 0.1f), X("speed", -0.1f) },
                        Art = new Art { BodyX = 12, BodyY = 9, Head = 8, Ears = "round", Tail = true, Main = 0x4a7a34, Accent = 0xb0a68c, Belly = 0x3a5a2a } },
                } },
        };

        // A starter until 2026-11-05, when Chichar took its place. Now earned like other creatures, but saves that
        // picked it as their starter keep it (see FormerStarters). Drawn sprites from Codex under these older IDs;
        // keep the IDs, because saves use them.
        static readonly Partner Furrowkit =
            new Partner { Id = "burrowkit", Name = "Furrowkit", ChallengeCounter = "runs", ChallengeTarget = 5, ChallengeText = "Play 5 runs.", Role = "Digger", Blurb = "Quick little digger that grows into a huge rock-clawed tunneller.", Elem = "fire", Move = "ember-shot", MaxHp = 105, Armor = 1, Will = 1,
                Art = new Art { BodyX = 5, BodyY = 4, Head = 5, Ears = "point", Tail = true, Main = 0xe08a3c, Accent = 0xb09e86, Belly = 0xecd9b2 },
                Evolutions = new[]
                {
                    new Stage { Id = "burrower", Name = "Burrowbat", Level = 10, Scale = 1.2f, Gain = "+25 max HP, +2 armor, +2 will and +10% focus", Stats = new[] { S("maxHp", 25), S("armor", 2), S("will", 2), X("focus", 0.1f) },
                        Art = new Art { BodyX = 7, BodyY = 5, Head = 6, Ears = "point", Tail = true, Main = 0xc8562b, Accent = 0xb09e86, Belly = 0xebd7b0 } },
                    new Stage { Id = "stonehog", Name = "Borrog", Level = 22, Scale = 1.5f, AddElem = "stone", Gain = "Gains Stone type, +60 max HP, +4 armor, +2 will, +10% focus and +15% might, but -10% move speed", Stats = new[] { S("maxHp", 60), S("armor", 4), S("will", 2), X("focus", 0.1f), X("might", 0.15f), X("speed", -0.1f) },
                        Art = new Art { BodyX = 10, BodyY = 8, Head = 7, Ears = "horn", Main = 0x3a2c2b, Accent = 0xb09e86, Belly = 0xc8532a } },
                } };

        // Names, types and drawn sheets approved by the user (sheets from Codex). Two stages only, and Volt replaces
        // Iron at Dealeach instead of becoming a third type. Stats, move, challenge, evolve level and signature fusion
        // were proposed by Claude on 2026-11-05 and are not yet approved.
        static readonly Partner Sleach =
            new Partner { Id = "sleach", Name = "Sleach", Elem2 = "iron", ChallengeCounter = "guardians", ChallengeTarget = 4, ChallengeText = "Beat 4 guardians.", Role = "Lancer", Blurb = "A hunched seaweed kelpie with a steel harpoon horn that becomes a gold-masked sea serpent.", Elem = "basic", Move = "harpoon-lance", MaxHp = 105, Armor = 2, Will = 1,
                Art = new Art { BodyX = 6, BodyY = 5, Head = 5, Ears = "horn", Tail = true, Main = 0x7aa89a, Accent = 0x9fb4c8, Belly = 0xb8d0c4 },
                Evolutions = new[]
                {
                    new Stage { Id = "dealeach", Name = "Dealeach", Level = 16, Scale = 1.4f, AddElem = "volt", Gain = "Volt type replaces Iron, Lightning Charge can be learned, +45 max HP, +1 armor, +2 will, +15% might, +10% focus and +10% move speed", Stats = new[] { S("maxHp", 45), S("armor", 1), S("will", 2), X("might", 0.15f), X("focus", 0.1f), X("speed", 0.1f) },
                        Art = new Art { BodyX = 8, BodyY = 6, Head = 6, Ears = "horn", Tail = true, Main = 0x9ee8d0, Accent = 0xe0b040, Belly = 0xc8f4e0, Angry = true } },
                } };

        // Creatures that used to be offered in the first-launch window. A save that chose one keeps it as its starter.
        public static readonly string[] FormerStarters = { "burrowkit" };

        // What a captured ally gives you, by its element.
        public static AllyBonus AllyBonusFor(string elem)
        {
            switch (elem)
            {
                case "fire": return new AllyBonus { Stat = X("focus", 0.08f), Text = "+8% focus" };
                case "water": return new AllyBonus { Stat = S("maxHp", 25), Text = "+25 max HP" };
                case "leaf": return new AllyBonus { Stat = S("regen", 0.8f), Text = "+0.8 HP regen" };
                case "volt": return new AllyBonus { Stat = X("haste", 0.08f), Text = "+8% attack speed" };
                case "stone": return new AllyBonus { Stat = S("armor", 3), Text = "+3 armor" };
                case "iron": return new AllyBonus { Stat = S("critDmg", 0.2f), Text = "+20% crit damage" };
                case "venom": return new AllyBonus { Stat = X("area", 0.08f), Text = "+8% attack area" };
                case "basic": return new AllyBonus { Stat = X("speed", 0.06f), Text = "+6% move speed" };
                case "spirit": return new AllyBonus { Stat = S("will", 3), Text = "+3 will" };
                default: return new AllyBonus { Stat = S("crit", 0.05f), Text = "+5% crit chance" };
            }
        }

        public static readonly EnemyDef[] Enemies =
        {
            new EnemyDef { Id = "skitter", Name = "Skitter", Elem = "basic", Hp = 13, Speed = 3.6f, Dmg = 7, Size = 1.3f, Xp = 2, Cost = 1, Tier = 1, Art = new Art { BodyX = 5, BodyY = 4, Head = 5, Ears = "horn", Main = 0xa58a6a, Accent = 0xe9d6a0, Angry = true } },
            new EnemyDef { Id = "sporeling", Name = "Sporel", Elem = "leaf", Elem2 = "venom", Hp = 18, Speed = 3.1f, Dmg = 8, Size = 1.5f, Xp = 3, Cost = 1.5f, Tier = 2, Art = new Art { BodyX = 5, BodyY = 5, Head = 5, Ears = "leaf", Main = 0x5fa642, Accent = 0xd77ad0, Angry = true } },
            new EnemyDef { Id = "wisp", Name = "Wisp", Elem = "spirit", Hp = 16, Speed = 4.2f, Dmg = 8, Size = 1.4f, Xp = 3, Cost = 1.5f, Tier = 2, Flags = Flag.Flying, Art = new Art { BodyX = 5, BodyY = 5, Head = 5, Ears = "point", Legs = 0, Main = 0x6a4bb5, Accent = 0xc58cff, Angry = true } },
            new EnemyDef { Id = "batling", Name = "Batling", Elem = "basic", Hp = 12, Speed = 5.4f, Dmg = 6, Size = 1.5f, Xp = 3, Cost = 1.5f, Tier = 2, Flags = Flag.Flying, Art = new Art { BodyX = 4, BodyY = 4, Head = 4, Ears = "point", Wings = true, Legs = 0, Main = 0x4e5fc4, Accent = 0xb36bd8, Angry = true } },
            new EnemyDef { Id = "spitter", Name = "Spitter", Elem = "water", Hp = 22, Speed = 2.7f, Dmg = 11, Size = 1.7f, Xp = 5, Cost = 3, Tier = 2, Flags = Flag.Shooter, Art = new Art { BodyX = 6, BodyY = 5, Head = 5, Ears = "fin", Main = 0x3b8cc4, Accent = 0xf7d54a, Angry = true } },
            new EnemyDef { Id = "ramhorn", Name = "Ramhorn", Elem = "fire", Hp = 42, Speed = 3, Dmg = 16, Size = 2, Xp = 7, Cost = 4, Tier = 3, Flags = Flag.Charger, Art = new Art { BodyX = 7, BodyY = 5, Head = 5, Ears = "horn", Tail = true, Main = 0xc4493b, Accent = 0xffb03b, Angry = true } },
            new EnemyDef { Id = "shockling", Name = "Shockling", Elem = "volt", Hp = 30, Speed = 4.6f, Dmg = 12, Size = 1.6f, Xp = 5, Cost = 3, Tier = 4, Art = new Art { BodyX = 5, BodyY = 5, Head = 5, Ears = "point", Tail = true, Main = 0xe8c02e, Accent = 0x5a3b1a, Angry = true } },
            new EnemyDef { Id = "brute", Name = "Stone Brute", Elem = "stone", Hp = 130, Speed = 2.2f, Dmg = 22, Size = 2.8f, Xp = 15, Cost = 9, Tier = 5, Art = new Art { BodyX = 8, BodyY = 7, Head = 5, Ears = "horn", Main = 0x7d7686, Accent = 0xc9a06a, Angry = true } },
        };

        public static readonly EnemyDef[] Bosses =
        {
            new EnemyDef { Id = "hollow-king", Name = "Hollow King", Elem = "spirit", Hp = 900, Speed = 3, Dmg = 20, Size = 6, Xp = 90, Art = new Art { BodyX = 9, BodyY = 8, Head = 7, Ears = "horn", Tail = true, Main = 0x4a2f8f, Accent = 0xff5da2, Angry = true } },
            new EnemyDef { Id = "mire-tyrant", Name = "Mire Tyrant", Elem = "water", Elem2 = "venom", Hp = 1300, Speed = 3.2f, Dmg = 24, Size = 6.5f, Xp = 110, Art = new Art { BodyX = 10, BodyY = 7, Head = 7, Ears = "fin", Tail = true, Main = 0x1f7f88, Accent = 0xc6ff5a, Angry = true } },
            new EnemyDef { Id = "cinder-maw", Name = "Cinder Maw", Elem = "fire", Hp = 1800, Speed = 3.5f, Dmg = 28, Size = 7, Xp = 130, Art = new Art { BodyX = 9, BodyY = 8, Head = 8, Ears = "point", Wings = true, Main = 0xb5342a, Accent = 0xffc53b, Angry = true } },
        };

        public static readonly EnemyDef FinalBoss = new EnemyDef { Id = "tower-warden", Name = "The Warden", Elem = "stone", Elem2 = "iron", Hp = 2600, Speed = 3.8f, Dmg = 32, Size = 8, Xp = 200, Art = new Art { BodyX = 10, BodyY = 9, Head = 8, Ears = "horn", Wings = true, Tail = true, Main = 0x3d3a52, Accent = 0xffd24a, Angry = true } };
        static Partner FromEnemy(string id, string role, string blurb, string move, float might, float focus, float maxHp, float armor, float will, float regen, string counter, long target, string text)
        {
            foreach (EnemyDef e in Enemies)
            {
                if (e.Id != id) continue;
                return new Partner { Id = id, Name = e.Name, Role = role, Blurb = blurb, Elem = e.Elem, Elem2 = e.Elem2, Move = move, Might = might, Focus = focus, MaxHp = maxHp, Armor = armor, Will = will, Regen = regen, Art = e.Art, ChallengeCounter = counter, ChallengeTarget = target, ChallengeText = text };
            }
            throw new ArgumentException(id);
        }

        // Sprites from Codex. Names approved by the user; gameplay numbers remain proposals.
        static Stage SlimeForm(string id, string name, string elem, uint main, uint accent)
        {
            return new Stage { Id = id, Name = name, Elem = elem, Level = SlimeFormLevel, Scale = 1.35f,
                Gain = "Becomes " + Elements[elem].Name + " type, +45 max HP, +2 armor, +2 will and +15% might and focus",
                Stats = new[] { S("maxHp", 45), S("guard", 2), X("power", 0.15f) },
                Art = new Art { BodyX = 8, BodyY = 6, Head = 5, Legs = 0, Main = main, Accent = accent, Belly = main } };
        }

        static Partner Slime()
        {
            return new Partner { Id = "slime-basic", Name = "Quilven", Role = "Shifter", Blurb = "A tiny puddle of living mercury that can take on any element.", Elem = "basic", Move = "quick-jab", MaxHp = 90,
                Art = new Art { BodyX = 5, BodyY = 3, Head = 3, Legs = 0, Main = 0xb8b8c4, Accent = 0x6a4fa0, Belly = 0xb8b8c4 },
                ChallengeCounter = "caught", ChallengeTarget = 1, ChallengeText = "Catch 1 wild creature.",
                FormLevel = SlimeFormLevel,
                Forms = new[]
                {
                    SlimeForm("slime-flame", "Bublava", "fire", 0xd8342a, 0x4a4040),
                    SlimeForm("slime-tide", "Dewdrip", "water", 0x46b8ff, 0xc8f0ff),
                    SlimeForm("slime-bloom", "Sapple", "leaf", 0x7ac83c, 0x4a9a2c),
                    SlimeForm("slime-volt", "Zippadrip", "volt", 0xf0c020, 0x60c8ff),
                    SlimeForm("slime-stone", "Chunky", "stone", 0x8a5a38, 0xa09080),
                    SlimeForm("slime-iron", "Silvury", "iron", 0xb0b0bc, 0x606070),
                    SlimeForm("slime-venom", "Darpool", "venom", 0x6fb030, 0xb04fd0),
                    SlimeForm("slime-spirit", "Gooky", "spirit", 0xa070e0, 0xe0c8ff),
                } };
        }

        static Partner[] BuildRoster()
        {
            var all = new List<Partner>();
            foreach (Partner p in Starters)
            {
                p.Starter = true;
                all.Add(p);
            }
            // The newer drawn creatures come first on the select screen, then the placeholder enemy creatures.
            all.Add(Furrowkit);
            all.Add(Slime());
            all.Add(Sleach);
            all.Add(FromEnemy("skitter", "Scrapper", "A nimble pest that darts around throwing quick jabs.", "quick-jab", 1, 1, 95, 1, 0, 0, "kills", 500, "Get 500 knockouts in total."));
            all.Add(FromEnemy("spitter", "Sniper", "Sprays slowing bubbles from a safe distance.", "bubble-burst", 1, 1.1f, 90, 0, 1, 0, "runs", 3, "Play 3 runs."));
            all.Add(FromEnemy("sporeling", "Spreader", "Surrounds itself with a slowing cloud and heals as it goes.", "toxic-cloud", 1, 1, 100, 0, 1, 1, "bestLevel", 15, "Reach level 15 in a run."));
            all.Add(FromEnemy("wisp", "Curser", "Curses the ground under single targets for heavy damage.", "hex-smite", 1, 1.15f, 85, 0, 1, 0, "caught", 3, "Catch 3 wild creatures."));
            all.Add(FromEnemy("batling", "Screamer", "Fragile, but its wide scream stuns everything close.", "screech", 1, 1, 80, 0, 0, 0.5f, "bestLevel", 25, "Reach level 25 in a run."));
            all.Add(FromEnemy("ramhorn", "Bruiser", "Sweeps wide cones of flame at close range.", "fire-fan", 1, 1.15f, 115, 1, 1, 0, "guardians", 3, "Beat 3 guardians."));
            all.Add(FromEnemy("shockling", "Striker", "Hits everything in a straight line with lightning.", "arc-lance", 1, 1.1f, 90, 0, 1, 0, "evolutions", 6, "Evolve 6 times."));
            all.Add(FromEnemy("brute", "Juggernaut", "Enormous and slow to kill, lobbing explosive boulders.", "boulder-lob", 1.1f, 1, 170, 3, 1, 0, "wins", 1, "Clear the tower once."));
            return all.ToArray();
        }

        public static readonly Partner[] Partners = BuildRoster();

        // Creatures that only appear to be caught, so the director never sends them as regular enemies.
        static EnemyDef[] BuildWildOnly()
        {
            var all = new List<EnemyDef>();
            foreach (Partner p in Partners)
            {
                // Creatures made from a regular enemy already appear as one. Everything else only shows up to be caught.
                if (Array.Exists(Enemies, e => e.Id == p.Id)) continue;
                all.Add(new EnemyDef { Id = p.Id, Name = p.Name, Elem = p.Elem, Elem2 = p.Elem2, Hp = 25, Speed = 3.6f, Dmg = 8, Size = 1.6f, Xp = 8, Cost = 3, Art = p.Art });
            }
            return all.ToArray();
        }

        public static readonly EnemyDef[] WildOnly = BuildWildOnly();

        public static EnemyDef WildDef(string id)
        {
            foreach (EnemyDef e in Enemies) if (e.Id == id) return e;
            foreach (EnemyDef e in WildOnly) if (e.Id == id) return e;
            return null;
        }

        public static readonly Art MerchantArt = new Art { BodyX = 7, BodyY = 6, Head = 6, Ears = "round", Tail = true, Main = 0xe8c27a, Accent = 0xc75a3c, Belly = 0xfff0d0 };
        public static readonly Art DroneArt = new Art { BodyX = 4, BodyY = 4, Head = 5, Ears = "horn", Wings = true, Legs = 0, Main = 0x9a8f7a, Accent = 0x6cc4d8, Belly = 0xe8dfc8 };

        // Kind selects the behaviour in Moves.cs. Levels raise damage and offer random upgrades.
        public static readonly MoveDef[] MoveList =
        {
            new MoveDef { Id = "ember-shot", Name = "Ember Shot", Elem = "fire", Special = Spec, Kind = Kind.Bolt, Cd = 0.75f, Dmg = 9, Range = 19, Speed = 22, St = new Status(3, 0, 0), Desc = "Fires burning bolts at the nearest enemy." },
            new MoveDef { Id = "flare-ring", Name = "Flare Ring", Elem = "fire", Special = Spec, Kind = Kind.Nova, Cd = 3.2f, Dmg = 16, Radius = 6.5f, St = new Status(4, 0, 0), Knock = 7, Desc = "A blast of fire around you that throws enemies back." },
            // Added 2026-11-09 at the user's request (names and categories from the user, numbers from Claude).
            new MoveDef { Id = "flame-claw", Name = "Flame Claw", Elem = "fire", Special = Phys, Kind = Kind.Cone, Cd = 0.9f, Dmg = 15, Range = 5, Arc = 1.6f, St = new Status(2, 0, 0), Desc = "Rakes nearby enemies with burning claws." },
            new MoveDef { Id = "fire-fan", Name = "Fire Fan", Elem = "fire", Special = Spec, Kind = Kind.Cone, Cd = 1.6f, Dmg = 12, Range = 8, Arc = 1.1f, St = new Status(3, 0, 0), Desc = "Sweeps a wide cone of flame." },
            new MoveDef { Id = "droplet", Name = "Droplet", Elem = "water", Special = Spec, Kind = Kind.Bolt, Cd = 0.7f, Dmg = 8, Range = 19, Speed = 20, Pierce = 1, Desc = "Fast water shots that pass through one enemy." },
            new MoveDef { Id = "bubble-burst", Name = "Bubble Burst", Elem = "water", Special = Spec, Kind = Kind.Barrage, Cd = 2.4f, Dmg = 7, Range = 16, Speed = 13, Count = 4, Spread = 0.42f, St = new Status(0, 1.5f, 0), Desc = "A rapid spray of slowing bubbles." },
            new MoveDef { Id = "riptide", Name = "Riptide", Elem = "water", Special = Phys, Kind = Kind.Vortex, Cd = 6, Dmg = 6, Range = 17, Radius = 5, Dur = 2.8f, Desc = "A whirlpool that drags enemies in and grinds them down." },
            new MoveDef { Id = "razor-seed", Name = "Razor Seed", Elem = "leaf", Special = Phys, Kind = Kind.Boomerang, Cd = 1.15f, Dmg = 10, Range = 13, Speed = 15, Desc = "A spinning seed that flies out and comes back." },
            new MoveDef { Id = "thorn-orbit", Name = "Thorn Orbit", Elem = "leaf", Special = Phys, Kind = Kind.Orbit, Cd = 0.3f, Dmg = 6, Radius = 3.6f, Count = 2, Spin = 2.8f, Desc = "Thorns circle you and cut anything they touch." },
            new MoveDef { Id = "sun-shot", Name = "Sun Shot", Elem = "leaf", Special = Spec, Kind = Kind.Beam, Cd = 2.6f, Dmg = 24, Range = 22, Width = 1.2f, Desc = "Gathers sunlight and fires it as a long beam through every enemy in line." },
            new MoveDef { Id = "spore-cloud", Name = "Spore Cloud", Elem = "leaf", Special = Spec, Kind = Kind.Aura, Cd = 0.55f, Dmg = 4, Radius = 4.8f, St = new Status(0, 0.8f, 0), Desc = "A cloud around you that slows and poisons." },
            new MoveDef { Id = "arc-lance", Name = "Arc Lance", Elem = "volt", Special = Spec, Kind = Kind.Beam, Cd = 1.9f, Dmg = 18, Range = 18, Width = 1.1f, St = new Status(0, 0, 0.25f), Desc = "A straight bolt that hits everything in line." },
            new MoveDef { Id = "skyfall", Name = "Skyfall", Elem = "volt", Special = Spec, Kind = Kind.Smite, Cd = 2.3f, Dmg = 21, Range = 18, Radius = 2.5f, Count = 2, St = new Status(0, 0, 0.4f), Desc = "Lightning strikes random enemies after a short warning." },
            new MoveDef { Id = "zip-jab", Name = "Zip Jab", Elem = "volt", Special = Phys, Kind = Kind.Bolt, Cd = 0.5f, Dmg = 6, Range = 13, Speed = 32, St = new Status(0, 0, 0.12f), Desc = "Lightning-quick jabs at the nearest enemy that stun it for a moment." },
            // Dealeach's signature move, only offered at that stage: a timed dash in the facing direction that hits everything on the way and can't be hit.
            new MoveDef { Id = "lightning-charge", Name = "Lightning Charge", Elem = "volt", Special = Phys, Signature = "dealeach", Kind = Kind.Charge, Cd = 3.2f, Dmg = 34, Range = 8, Width = 1.4f, Speed = 34, Knock = 6, St = new Status(0, 0, 0.3f), Pool = new[] { "dmg", "cd", "area" }, Desc = "Dealeach's signature. Every few seconds, charges forward in the direction you face, striking everything in its path, and can't be hit while charging." },
            new MoveDef { Id = "boulder-lob", Name = "Boulder Lob", Elem = "stone", Special = Phys, Kind = Kind.Lob, Cd = 2.2f, Dmg = 26, Range = 16, Radius = 3.6f, Knock = 8, Desc = "Lobs a rock that explodes where it lands." },
            new MoveDef { Id = "shard-orbit", Name = "Shard Orbit", Elem = "stone", Special = Phys, Kind = Kind.Orbit, Cd = 0.4f, Dmg = 11, Radius = 5, Spin = 1.8f, Desc = "A heavy shard swings around you at long reach." },
            new MoveDef { Id = "hex-smite", Name = "Hex Smite", Elem = "spirit", Special = Spec, Kind = Kind.Smite, Cd = 3, Dmg = 30, Range = 20, Radius = 3.2f, Desc = "Curses the ground under one enemy for heavy damage." },
            new MoveDef { Id = "wail", Name = "Wail", Elem = "spirit", Special = Spec, Kind = Kind.Nova, Cd = 5, Dmg = 11, Radius = 9, St = new Status(0, 0, 0.7f), Desc = "A wide scream that stuns everything nearby." },
            new MoveDef { Id = "anvil-drop", Name = "Anvil Drop", Elem = "iron", Special = Phys, Kind = Kind.Lob, Cd = 2.2f, Dmg = 26, Range = 16, Radius = 3.6f, Knock = 8, Desc = "Lobs an anvil that explodes where it lands." },
            new MoveDef { Id = "blade-fan", Name = "Blade Fan", Elem = "iron", Special = Phys, Kind = Kind.Cone, Cd = 1.6f, Dmg = 13, Range = 8, Arc = 1.1f, Desc = "Sweeps a wide cone of blades." },
            // Sleach's starting move (user, 2026-11-05): a straight piercing line whose level-ups roll only damage,
            // attack area and attack speed (cooldown). Numbers proposed by Claude.
            new MoveDef { Id = "harpoon-lance", Name = "Harpoon Lance", Elem = "iron", Special = Phys, Kind = Kind.Lance, Cd = 1.7f, Dmg = 17, Range = 17, Speed = 30, Width = 0.9f, Pool = new[] { "dmg", "area", "cd" }, Desc = "Throws a long steel harpoon that flies straight ahead and pierces every enemy in its path." },
            new MoveDef { Id = "venom-spit", Name = "Venom Spit", Elem = "venom", Special = Spec, Kind = Kind.Bolt, Cd = 0.85f, Dmg = 8, Range = 18, Speed = 18, St = new Status(0, 1, 0), Desc = "Spits slowing venom at the nearest enemy." },
            new MoveDef { Id = "toxic-cloud", Name = "Toxic Cloud", Elem = "venom", Special = Spec, Kind = Kind.Aura, Cd = 0.55f, Dmg = 4, Radius = 4.8f, St = new Status(0, 0.8f, 0), Desc = "A toxic cloud around you that slows and poisons." },
            new MoveDef { Id = "quick-jab", Name = "Quick Jab", Elem = "basic", Special = Phys, Kind = Kind.Bolt, Cd = 0.6f, Dmg = 7, Range = 17, Speed = 22, Desc = "Quick plain shots at the nearest enemy." },
            new MoveDef { Id = "screech", Name = "Screech", Elem = "basic", Special = Spec, Kind = Kind.Nova, Cd = 5, Dmg = 11, Radius = 9, St = new Status(0, 0, 0.7f), Desc = "A piercing scream that stuns everything nearby." },
        };

        public static MoveDef Move(string id)
        {
            foreach (MoveDef m in MoveList) if (m.Id == id) return m;
            throw new ArgumentException(id);
        }

        public static readonly PassiveDef[] Passives =
        {
            new PassiveDef { Id = "might", Name = "Power Band", Stat = "might", Mul = true, Value = 0.12f, Unit = "%", Desc = "might (physical damage)" },
            new PassiveDef { Id = "focus", Name = "Focus Gem", Stat = "focus", Mul = true, Value = 0.12f, Unit = "%", Desc = "focus (special damage)" },
            new PassiveDef { Id = "haste", Name = "Quick Coil", Stat = "haste", Mul = true, Value = 0.1f, Unit = "%", Desc = "attack speed" },
            new PassiveDef { Id = "area", Name = "Wide Lens", Stat = "area", Mul = true, Value = 0.12f, Unit = "%", Desc = "attack area" },
            new PassiveDef { Id = "speed", Name = "Light Boots", Stat = "speed", Mul = true, Value = 0.07f, Unit = "%", Desc = "move speed" },
            new PassiveDef { Id = "vigor", Name = "Vigor Root", Stat = "maxHp", Value = 22, Unit = "", Desc = "max HP" },
            new PassiveDef { Id = "regen", Name = "Mending Moss", Stat = "regen", Value = 0.7f, Unit = "/s", Desc = "HP regen" },
            new PassiveDef { Id = "armor", Name = "Plate Shell", Stat = "armor", Value = 1.4f, Unit = "", Desc = "armor (physical defense)" },
            new PassiveDef { Id = "will", Name = "Calm Bell", Stat = "will", Value = 1.4f, Unit = "", Desc = "will (special defense)" },
            new PassiveDef { Id = "magnet", Name = "Lodestone", Stat = "magnet", Value = 1.8f, Unit = "m", Desc = "pickup range" },
            new PassiveDef { Id = "crit", Name = "Keen Eye", Stat = "crit", Value = 0.06f, Unit = "%", Desc = "crit chance" },
            new PassiveDef { Id = "xp", Name = "Study Charm", Stat = "xpGain", Mul = true, Value = 0.1f, Unit = "%", Desc = "XP gain" },
            new PassiveDef { Id = "jump", Name = "Spring Heel", Stat = "jumpHeight", Mul = true, Value = 0.22f, Unit = "%", Desc = "jump height" },
        };

        static StatChange S(string stat, float v) { return new StatChange(stat, false, v); }
        static StatChange X(string stat, float v) { return new StatChange(stat, true, v); }
        static ItemDef Item(string id, string name, string tier, int max, string desc, params StatChange[] stats)
        {
            return new ItemDef { Id = id, Name = name, Tier = tier, Max = max, Desc = desc, Stats = stats };
        }
        static ItemDef Charm(string id, string name, string elem)
        {
            return Item(id, name, "common", 10, "+30% " + Elements[elem].Name + " move damage and +3% all damage.", S("elem:" + elem, 0.3f), X("power", 0.03f));
        }

        // Items come from chests, shops and bosses rather than level-ups, and most of them stack.
        // Some have no stats: their effect is checked by id in Run.cs or Player.cs.
        public static readonly ItemDef[] Items =
        {
            Item("whetstone", "Whetstone", "common", 10, "+8% might (physical damage).", X("might", 0.08f)),
            Item("prism", "Prism Shard", "common", 10, "+8% focus (special damage).", X("focus", 0.08f)),
            Item("oil-flask", "Oil Flask", "common", 10, "+8% attack speed.", X("haste", 0.08f)),
            Item("field-lens", "Field Lens", "common", 10, "+8% attack area.", X("area", 0.08f)),
            Item("ration", "Trail Ration", "common", 10, "+15 max HP.", S("maxHp", 15)),
            Item("lucky-coin", "Lucky Coin", "common", 10, "+20% gold.", X("goldGain", 0.2f)),
            Item("feather", "Swift Feather", "common", 10, "+7% move speed.", X("speed", 0.07f)),
            Item("tonic", "Herb Tonic", "common", 10, "+0.6 HP regen per second.", S("regen", 0.6f)),
            Item("honeycomb", "Honeycomb", "common", 10, "+1.5 pickup range.", S("magnet", 1.5f)),
            Charm("ember-charm", "Ember Charm", "fire"),
            Charm("tide-charm", "Tide Charm", "water"),
            Charm("leaf-charm", "Bloom Charm", "leaf"),
            Charm("volt-charm", "Volt Charm", "volt"),
            Charm("stone-charm", "Stone Charm", "stone"),
            Charm("spirit-charm", "Spirit Charm", "spirit"),
            Charm("iron-charm", "Iron Charm", "iron"),
            Charm("venom-charm", "Venom Charm", "venom"),
            Charm("basic-charm", "Basic Charm", "basic"),
            Item("leech-tooth", "Leech Tooth", "rare", 5, "Recover 0.8% of the damage you deal.", S("lifesteal", 0.008f)),
            Item("thorn-mail", "Thorn Mail", "rare", 5, "Enemies that touch you take 25 damage.", S("thorns", 25)),
            Item("spring-boots", "Spring Boots", "rare", 3, "+1 air jump and +15% jump height.", S("extraJumps", 1), X("jumpHeight", 0.15f)),
            Item("keen-scope", "Keen Scope", "rare", 5, "+8% crit chance and +30% crit damage.", S("crit", 0.08f), S("critDmg", 0.3f)),
            Item("iron-boots", "Iron Boots", "rare", 2, "+6 armor, -5% speed. Landing from a jump sends out a shockwave that grows with your armor.", S("armor", 6), X("speed", -0.05f)),
            Item("slipshell", "Slipshell", "rare", 1, "You cannot be hit during most of a slide."),
            Item("ember-core", "Ember Core", "rare", 3, "Burning enemies have a 50% chance to explode when they fall."),
            Item("twin-barrel", "Twin Barrel", "epic", 2, "+1 projectile on all moves.", S("extraShots", 1)),
            Item("storm-coil", "Storm Coil", "epic", 3, "+4% crit chance. Crits call lightning onto another nearby enemy.", S("crit", 0.04f)),
            Item("bonk-stick", "Bonk Stick", "epic", 3, "Each hit has a 3% chance to deal 8x damage."),
            Item("war-drum", "War Drum", "epic", 5, "+15% damage and +15% area.", X("power", 0.15f), X("area", 0.15f)),
            Item("reaper-sash", "Reaper Sash", "epic", 1, "Every 100 knockouts in the run: +1% damage, up to +30%."),
            Item("second-heart", "Second Heart", "legend", 2, "When you fall, revive once with 60% HP.", S("revives", 1)),
            Item("rally-bell", "Rally Bell", "legend", 1, "Ultimate charges 25% faster, and casting it recharges all your moves.", X("ultRate", 0.25f)),
            Item("master-key", "Master Key", "legend", 1, "Moves can reach level 10.", S("moveCap", 2)),
            Item("glass-crown", "Glass Crown", "legend", 1, "+30% damage and +20% attack speed, but -10% max HP.", X("power", 0.3f), X("haste", 0.2f), X("maxHp", -0.1f)),
        };

        static UpgradeDef U(string id, string name, string desc, string stat, float per, bool mul, int max, int cost, float growth = 1.5f)
        {
            return new UpgradeDef { Id = id, Name = name, Desc = desc, Stat = stat, Per = per, Mul = mul, Max = max, BaseCost = cost, Growth = growth };
        }

        public static readonly UpgradeDef[] LabUpgrades =
        {
            U("might", "Training", "+4% might and focus per level", "power", 0.04f, true, 8, 20),
            U("vigor", "Stamina", "+8 max HP per level", "maxHp", 8, false, 6, 20),
            U("agility", "Agility", "+2.5% move speed per level", "speed", 0.025f, true, 6, 20),
            U("haste", "Swiftness", "+2% attack speed per level", "haste", 0.02f, true, 5, 30),
            U("area", "Expansion", "+3% attack area per level", "area", 0.03f, true, 5, 30),
            U("crit", "Precision", "+1% crit chance per level", "crit", 0.01f, false, 5, 30),
            U("armor", "Shell", "+1 armor and will per level", "guard", 1, false, 5, 30),
            U("regen", "Mending", "+0.2 HP regen per level", "regen", 0.2f, false, 5, 30),
            U("magnet", "Magnetism", "+0.6 pickup range per level", "magnet", 0.6f, false, 5, 15),
            U("study", "Study", "+5% experience per level", "xpGain", 0.05f, true, 6, 25),
            U("trading", "Trading", "+8% gold per level", "goldGain", 0.08f, true, 6, 15),
            U("fortune", "Fortune", "+10% better rarities per level", "luck", 0.1f, false, 5, 30),
            U("slot", "Fourth Move", "Unlocks your fourth move slot", "moveSlots", 1, false, 1, 120),
            U("vitamins", "Vitamin Slots", "+1 vitamin slot per level", "vitaminSlots", 1, false, 2, 80, 2),
            U("refresh", "Refresh", "+1 Refresh per run", "refreshes", 1, false, 3, 40),
            U("skip", "Skip", "+1 Skip per run", "skips", 1, false, 3, 30),
            U("banish", "Banish", "+1 Banish per run", "banishes", 1, false, 2, 50),
            U("options", "Fourth Option", "Level-ups offer 4 choices", "cardOptions", 1, false, 1, 300),
            U("revive", "Revive", "Start each run with one revive", "revives", 1, false, 1, 400),
        };

        public static readonly Difficulty[] Difficulties =
        {
            new Difficulty { Name = "Normal", Desc = "Shards x1", WinCounter = "winsNormal", Hp = 1, Dmg = 1, Credits = 1, Shards = 1 },
            new Difficulty { Name = "Hard", Desc = "Enemies +70%, shards x1.75", WinCounter = "winsHard", Hp = 1.7f, Dmg = 1.4f, Credits = 1.25f, Shards = 1.75f },
            new Difficulty { Name = "Nightmare", Desc = "Enemies +160%, shards x2.5", WinCounter = "winsNightmare", Hp = 2.6f, Dmg = 1.9f, Credits = 1.5f, Shards = 2.5f },
        };

        public static readonly Pact[] Pacts =
        {
            new Pact { Name = "Pact of Force", Desc = "+25% damage, but a 15% harder horde", Gain = X("power", 0.25f), Heat = 0.15f },
            new Pact { Name = "Pact of Haste", Desc = "+20% attack speed, but a 15% harder horde", Gain = X("haste", 0.2f), Heat = 0.15f },
            new Pact { Name = "Pact of Reach", Desc = "+25% attack area, but a 10% harder horde", Gain = X("area", 0.25f), Heat = 0.1f },
            new Pact { Name = "Pact of Flesh", Desc = "+60 max HP, but a 15% harder horde", Gain = S("maxHp", 60), Heat = 0.15f },
            new Pact { Name = "Pact of Greed", Desc = "+60% gold, but a 20% harder horde", Gain = X("goldGain", 0.6f), Heat = 0.2f },
        };

        public static readonly Rarity[] Rarities =
        {
            new Rarity { Id = "common", Label = "Common", Mult = 1, Weight = 64, Color = 0x9aa7c7 },
            new Rarity { Id = "rare", Label = "Rare", Mult = 1.4f, Weight = 26, Color = 0x49c8ff },
            new Rarity { Id = "epic", Label = "Epic", Mult = 1.9f, Weight = 8, Color = 0xc88cff },
            new Rarity { Id = "legend", Label = "Legendary", Mult = 2.6f, Weight = 2, Color = 0xffc53b },
        };

        public static int TierIndex(string tier)
        {
            for (int i = 0; i < Rarities.Length; i++) if (Rarities[i].Id == tier) return i;
            return 0;
        }

        static Trade T(StatChange up, string upText, StatChange down, string downText)
        {
            return new Trade { Up = up, UpText = upText, Down = down, DownText = downText };
        }

        // One swap per altar: one stat rises and another falls for the rest of the run.
        public static readonly Trade[] Trades =
        {
            T(X("haste", 0.25f), "+25% attack speed", X("duration", -0.2f), "-20% duration"),
            T(X("xpGain", 0.4f), "+40% experience", X("speed", -0.1f), "-10% move speed"),
            T(X("power", 0.25f), "+25% damage", X("maxHp", -0.15f), "-15% max HP"),
            T(X("area", 0.3f), "+30% area", X("haste", -0.1f), "-10% attack speed"),
            T(X("goldGain", 0.6f), "+60% gold", S("magnet", -1.5f), "-1.5 pickup range"),
            T(S("guard", 6), "+6 armor and will", X("speed", -0.08f), "-8% move speed"),
        };

        const bool Phys = false, Spec = true;

        // One part of a fusion. Each part picks physical or special like any other move.
        static MoveDef P(Kind kind, bool special, float dmg, float range = 0, float radius = 0, float speed = 0, int count = 1, int pierce = 0, float knock = 0, float arc = 0, float width = 0, float dur = 0, float spread = 0, string elem = null, Status st = default(Status), bool spreadAll = false)
        {
            return new MoveDef { Kind = kind, Special = special, Dmg = dmg, Range = range, Radius = radius, Speed = speed, Count = count, Pierce = pierce, Knock = knock, Arc = arc, Width = width, Dur = dur, Spread = spread, Elem = elem, St = st, SpreadAll = spreadAll };
        }

        static MoveDef F(string id, string name, string elem, float cd, string tag, string desc, params MoveDef[] parts)
        {
            foreach (MoveDef p in parts) if (p.Elem == null) p.Elem = elem;
            return new MoveDef { Id = id, Name = name, Elem = elem, Cd = cd, Tag = tag, Desc = desc, Kind = Kind.Fusion, Parts = parts };
        }

        // Two max-level moves fuse at a machine. The pair decides the result, checked in this order:
        // the partner's signature, then a reaction between two elements, then a shared element, then Glitch.
        public static readonly Dictionary<string, SignatureFusion> Signature = new Dictionary<string, SignatureFusion>
        {
            // Chichar line signature, approved by the user on 2026-11-05.
            { "chichar", new SignatureFusion { A = "skyfall", B = "arc-lance", Def = F("canopy-storm", "Canopy Storm", "volt", 2.3f, "Signature fusion", "Strikes fall on several enemies while a long bolt pierces the whole line.",
                P(Kind.Smite, Spec, 36, range: 22, radius: 3, count: 4, st: new Status(0, 0, 0.4f)), P(Kind.Beam, Spec, 34, range: 24, width: 1.5f, st: new Status(0, 0, 0.3f))) } },
            // Sleach line signature, proposed by Claude on 2026-11-05 and not yet approved.
            { "sleach", new SignatureFusion { A = "harpoon-lance", B = "arc-lance", Def = F("storm-harpoon", "Storm Harpoon", "volt", 2.2f, "Signature fusion", "A steel harpoon pierces the whole line while lightning strikes several enemies.",
                P(Kind.Lance, Phys, 40, range: 24, speed: 34, width: 1.3f, elem: "iron"), P(Kind.Smite, Spec, 30, range: 22, radius: 2.8f, count: 3, st: new Status(0, 0, 0.35f))) } },
            { "burrowkit", new SignatureFusion { A = "ember-shot", B = "flare-ring", Def = F("sunspire", "Sunspire", "fire", 2.4f, "Signature fusion", "A burst of flame, then a star of burning bolts in every direction.",
                P(Kind.Nova, Spec, 46, radius: 8, knock: 8, st: new Status(5, 0, 0)), P(Kind.Bolt, Spec, 28, range: 24, speed: 26, count: 6, pierce: 2, spreadAll: true, st: new Status(4, 0, 0))) } },
            { "fugiri", new SignatureFusion { A = "droplet", B = "bubble-burst", Def = F("tidal-jet", "Tidal Jet", "water", 2.2f, "Signature fusion", "A whirlpool under the enemy and a stream of piercing jets.",
                P(Kind.Vortex, Phys, 16, range: 22, radius: 7, dur: 2.4f), P(Kind.Barrage, Spec, 22, range: 22, speed: 22, count: 8, spread: 0.3f, pierce: 3)) } },
            { "golem-stage-i", new SignatureFusion { A = "razor-seed", B = "spore-cloud", Def = F("verdant-gale", "Verdant Gale", "leaf", 2.2f, "Signature fusion", "Three huge seeds fly out and return while thorns erupt around you.",
                P(Kind.Boomerang, Phys, 34, range: 18, speed: 18, count: 3), P(Kind.Nova, Phys, 28, radius: 7, st: new Status(0, 2, 0))) } },
        };

        public static readonly Dictionary<string, MoveDef> TypeFusions = new Dictionary<string, MoveDef>
        {
            { "fire", F("cinder-storm", "Cinder Storm", "fire", 2.8f, "Type fusion", "Fire falls on several enemies, and each strike burns the ground.",
                P(Kind.Smite, Spec, 42, range: 22, radius: 3.6f, count: 4, st: new Status(4, 0, 0)), P(Kind.Cone, Spec, 26, range: 11, arc: 1.5f, st: new Status(3, 0, 0))) },
            { "water", F("maelstrom", "Maelstrom", "water", 3, "Type fusion", "A wide whirlpool drags enemies in while a wave breaks around you.",
                P(Kind.Vortex, Phys, 18, range: 22, radius: 8, dur: 3), P(Kind.Nova, Spec, 34, radius: 9, knock: 5)) },
            { "leaf", F("bramble-rush", "Bramble Rush", "leaf", 2.4f, "Type fusion", "Seeds in a wide fan, then a ring of thorns.",
                P(Kind.Boomerang, Phys, 30, range: 16, speed: 16, count: 4), P(Kind.Nova, Phys, 24, radius: 7, st: new Status(0, 2, 0))) },
            { "volt", F("thunderhead", "Thunderhead", "volt", 2.4f, "Type fusion", "A long lance of lightning and strikes from above.",
                P(Kind.Beam, Spec, 40, range: 26, width: 1.6f, st: new Status(0, 0, 0.3f)), P(Kind.Smite, Spec, 34, range: 22, radius: 3, count: 4, st: new Status(0, 0, 0.4f))) },
            { "stone", F("landslide", "Landslide", "stone", 2.8f, "Type fusion", "A volley of boulders, each ending in a heavy blast.",
                P(Kind.Lob, Phys, 44, range: 22, radius: 4.6f, count: 4, knock: 8)) },
            { "spirit", F("soul-reap", "Soul Reap", "spirit", 3, "Type fusion", "Curses mark several enemies while a scream stuns everyone close.",
                P(Kind.Smite, Spec, 52, range: 24, radius: 3.4f, count: 3), P(Kind.Nova, Spec, 24, radius: 10, st: new Status(0, 0, 0.8f))) },
            { "iron", F("forge-fall", "Forge Fall", "iron", 2.8f, "Type fusion", "A volley of anvils and a storm of blades around you.",
                P(Kind.Lob, Phys, 40, range: 22, radius: 4.4f, count: 3, knock: 8), P(Kind.Cone, Phys, 28, range: 11, arc: 1.5f)) },
            { "venom", F("plague-tide", "Plague Tide", "venom", 2.6f, "Type fusion", "A spray of venom and a wide choking cloud.",
                P(Kind.Barrage, Spec, 20, range: 20, speed: 16, count: 8, spread: 0.6f, st: new Status(0, 1.5f, 0)), P(Kind.Nova, Spec, 30, radius: 9, st: new Status(0, 2, 0))) },
            { "basic", F("all-out", "All Out", "basic", 2.4f, "Type fusion", "A flurry of shots in every direction and a stunning shout.",
                P(Kind.Bolt, Phys, 26, range: 22, speed: 24, count: 6, pierce: 1, spreadAll: true), P(Kind.Nova, Spec, 26, radius: 9, st: new Status(0, 0, 0.6f))) },
        };

        // Keys are the two elements in alphabetical order.
        public static readonly Dictionary<string, MoveDef> ReactionFusions = new Dictionary<string, MoveDef>
        {
            { "fire+water", F("steam-burst", "Steam Burst", "water", 2.6f, "Reaction fusion", "Boiling bursts that stun, followed by a scalding spray.",
                P(Kind.Nova, Spec, 36, radius: 8, st: new Status(0, 0, 0.5f)), P(Kind.Barrage, Spec, 18, range: 18, speed: 16, count: 8, spread: 0.6f, elem: "fire", st: new Status(3, 0, 0))) },
            { "fire+leaf", F("wildfire", "Wildfire", "fire", 2.6f, "Reaction fusion", "Burning seeds that set everything they cross alight.",
                P(Kind.Boomerang, Spec, 32, range: 18, speed: 18, count: 4, st: new Status(5, 0, 0)), P(Kind.Cone, Spec, 24, range: 10, arc: 1.4f, st: new Status(4, 0, 0))) },
            { "volt+water", F("shock-tide", "Shock Tide", "volt", 2.8f, "Reaction fusion", "A charged whirlpool with lightning striking inside it.",
                P(Kind.Vortex, Phys, 16, range: 22, radius: 7, dur: 2.6f, elem: "water"), P(Kind.Smite, Spec, 34, range: 22, radius: 3, count: 4, st: new Status(0, 0, 0.4f))) },
            { "fire+stone", F("magma-rain", "Magma Rain", "fire", 3, "Reaction fusion", "Molten boulders that explode and burn.",
                P(Kind.Lob, Phys, 40, range: 22, radius: 4.4f, count: 4, knock: 6, st: new Status(5, 0, 0))) },
            { "leaf+water", F("bloom-surge", "Bloom Surge", "leaf", 2.6f, "Reaction fusion", "A wave of water, then seeds sprouting from it.",
                P(Kind.Nova, Spec, 30, radius: 8, knock: 5, elem: "water"), P(Kind.Boomerang, Phys, 28, range: 16, speed: 16, count: 3)) },
            { "spirit+volt", F("ghost-arc", "Ghost Arc", "spirit", 2.4f, "Reaction fusion", "A haunted lance, and a curse where it lands.",
                P(Kind.Beam, Spec, 38, range: 26, width: 1.5f, elem: "volt"), P(Kind.Smite, Spec, 44, range: 24, radius: 3.2f, count: 2)) },
        };

        // Any other pair: it works, but copies a random type fusion each time it fires.
        public static readonly MoveDef GlitchFusion = new MoveDef { Id = "glitch", Name = "Glitch", Elem = "spirit", Cd = 3, Tag = "Machine failure", Glitch = true, Kind = Kind.Fusion, Desc = "The machine failed. Each cast copies a random type fusion.", Parts = new MoveDef[0] };

        public static readonly Biome[] Biomes =
        {
            new Biome { Name = "Ashen Hollow", Sky = 0x1b1533, Fog = 0x241b44, FloorA = 0x4a3f7a, FloorB = 0x3b3166, Rim = 0x1d1838, Pillar = 0x37304f, Plinth = 0x54497c, Prop = 0x6a6280, Glow = 0xb98cff, HemiTop = 0xb7a8ff, HemiBot = 0x2a1f4a, Sun = 0xcab8ff, Sprite = 0xf2ecff },
            new Biome { Name = "Moss Vault", Sky = 0x10261f, Fog = 0x173428, FloorA = 0x3f7a4c, FloorB = 0x33663f, Rim = 0x14281c, Pillar = 0x35503a, Plinth = 0x4f7a55, Prop = 0x5e6e4a, Glow = 0x9dff6a, HemiTop = 0xbfeeb0, HemiBot = 0x1a3320, Sun = 0xeaffc8, Sprite = 0xf4fff0 },
            new Biome { Name = "Drowned Steps", Sky = 0x0d2233, Fog = 0x133147, FloorA = 0x2f6d8c, FloorB = 0x275c78, Rim = 0x0f2636, Pillar = 0x2a4c60, Plinth = 0x3f7a94, Prop = 0x4b6c7c, Glow = 0x5ae7ff, HemiTop = 0xa8e4ff, HemiBot = 0x133247, Sun = 0xd2f2ff, Sprite = 0xeef9ff },
            new Biome { Name = "Cinder Gate", Sky = 0x2a120f, Fog = 0x3a1a14, FloorA = 0x8a4a33, FloorB = 0x743c2a, Rim = 0x2a1410, Pillar = 0x58342a, Plinth = 0x8c5a3e, Prop = 0x6e4b3c, Glow = 0xffa53b, HemiTop = 0xffc9a0, HemiBot = 0x3a1a10, Sun = 0xffe0b8, Sprite = 0xfff4ea },
        };
    }
}


