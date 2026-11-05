using System;
using System.Collections.Generic;

namespace Swarmfall
{
    enum EncKind { Chest, Cursed, TradeAltar, Risk, Charge, Fossil, Pod, Merchant, Machine, Stone }

    sealed class Encounter
    {
        public EncKind Type;
        public float X, Y, Z, ChargeT, Markup = 1;
        public int Cost;
        public bool Used;
        public Trade[] Trades;
    }

    // Everything worth walking to on the map. Each zone scatters a fresh set.
    sealed class Encounters
    {
        const float Reach = 3.2f;
        const float ChargeRadius = 5;
        const float ChargeTime = 7;
        const float PodRadius = 13;
        readonly Run run;
        public readonly List<Encounter> Items = new List<Encounter>();

        public Encounters(Run run) { this.run = run; }

        Encounter Place(EncKind type, SeededRandom rng, float inner = 14, float outer = Terrain.ArenaRadius - 8)
        {
            for (int tries = 0; tries < 40; tries++)
            {
                float a = rng.Next() * M.Tau;
                float d = inner + M.Sqrt(rng.Next()) * (outer - inner);
                float x = M.Cos(a) * d;
                float z = M.Sin(a) * d;
                run.Terrain.PushOut(ref x, ref z, 2);
                bool blocked = false;
                foreach (Encounter o in Items) if (M.Hypot(o.X - x, o.Z - z) < 12) blocked = true;
                if (blocked) continue;
                var e = new Encounter { Type = type, X = x, Z = z, Y = run.Terrain.HeightAt(x, z) };
                Items.Add(e);
                return e;
            }
            return null;
        }

        public void Populate(int zone, uint seed)
        {
            var rng = new SeededRandom(seed ^ 0x9e37);
            Items.Clear();
            for (int k = 0; k < 6; k++)
            {
                Encounter c = Place(EncKind.Chest, rng);
                if (c != null) c.Cost = (int)Math.Round((12 + zone * 6) * (1 + rng.Next() * 0.3f));
            }
            for (int k = 0; k < 3; k++) Place(EncKind.Cursed, rng);
            for (int k = 0; k < 2; k++)
            {
                Encounter t = Place(EncKind.TradeAltar, rng);
                if (t != null) t.Trades = new[] { M.Pick(Data.Trades), M.Pick(Data.Trades), M.Pick(Data.Trades) };
            }
            Place(EncKind.Risk, rng);
            for (int k = 0; k < 3; k++) Place(EncKind.Charge, rng);
            Place(EncKind.Fossil, rng);
            for (int k = 0; k < 5; k++) Place(EncKind.Pod, rng);
            Place(EncKind.Merchant, rng, 18, 60);
            Place(EncKind.Machine, rng, 18, 60);
            Place(EncKind.Stone, rng, 45, Terrain.ArenaRadius - 10);
            // One free chest close by, so the first thing you find pays off.
            Place(EncKind.Chest, rng, 8, 14);
        }

        public void MarkStoneUsed()
        {
            foreach (Encounter o in Items) if (o.Type == EncKind.Stone) o.Used = true;
        }

        public void DropChest(float x, float z)
        {
            Items.Add(new Encounter { Type = EncKind.Chest, X = x, Z = z, Y = run.Terrain.HeightAt(x, z) });
        }

        public Encounter Nearest()
        {
            Player p = run.Player;
            Encounter best = null;
            float bestD = Reach;
            foreach (Encounter o in Items)
            {
                if (o.Used || o.Type == EncKind.Charge || o.Type == EncKind.Pod) continue;
                float d = M.Hypot(o.X - p.X, o.Z - p.Z);
                if (d < bestD)
                {
                    bestD = d;
                    best = o;
                }
            }
            return best;
        }

        public string Prompt()
        {
            Player p = run.Player;
            foreach (Encounter o in Items)
            {
                if (o.Type == EncKind.Charge && !o.Used && M.Hypot(o.X - p.X, o.Z - p.Z) < ChargeRadius)
                    return "Stay in the circle: " + (int)Math.Round(o.ChargeT * 100) + "%";
            }
            Encounter n = Nearest();
            if (n == null) return "";
            switch (n.Type)
            {
                case EncKind.Chest: return n.Cost > 0 ? "E: open chest (" + n.Cost + " gold, you have " + run.Money + ")" : "E: open chest (free)";
                case EncKind.Cursed: return "E: open cursed chest (free... are you sure?)";
                case EncKind.TradeAltar: return "E: trade one stat for another";
                case EncKind.Risk: return "E: raise the difficulty for more XP and loot";
                case EncKind.Fossil: return "E: revive a fossil for max HP";
                case EncKind.Merchant: return "E: browse the shop";
                case EncKind.Machine: return "E: fuse two max-level moves";
                case EncKind.Stone: return "E: challenge the guardian now";
                default: return "";
            }
        }

        public void Interact()
        {
            Encounter o = Nearest();
            if (o == null) return;
            Hud hud = run.Hud;
            Audio audio = run.Audio;
            Vfx vfx = run.Vfx;
            switch (o.Type)
            {
                case EncKind.Chest:
                    if (run.Money < o.Cost)
                    {
                        hud.Toast("You need " + o.Cost + " gold to open this");
                        audio.Denied();
                        return;
                    }
                    run.Money -= o.Cost;
                    o.Used = true;
                    vfx.Burst(o.X, o.Y + 1, o.Z, 0xffd24a, 26, 7);
                    audio.Coin();
                    QueueItemChoice("You found items", "Pick one", 0);
                    break;
                case EncKind.Cursed:
                    o.Used = true;
                    vfx.Burst(o.X, o.Y + 1, o.Z, 0xb98cff, 30, 8);
                    hud.Toast("It was a trap");
                    audio.Alarm();
                    run.Director.SummonAdds(o.X, o.Z, 8 + run.Zone * 4);
                    QueueItemChoice("The curse pays out", "Pick one", 1);
                    break;
                case EncKind.TradeAltar:
                    OpenTrade(o);
                    break;
                case EncKind.Risk:
                    o.Used = true;
                    run.Heat += 0.2f;
                    run.Player.Stats.XpGain *= 1.25f;
                    run.LootMult *= 1.25f;
                    hud.Banner("The zone gets harder", "+20% difficulty, +25% XP and loot");
                    audio.Alarm();
                    vfx.RingFx(o.X, o.Y, o.Z, 8, 0xff4a5e, 0.6f);
                    break;
                case EncKind.Fossil:
                    OpenFossil(o);
                    break;
                case EncKind.Merchant:
                    OpenShop(o);
                    break;
                case EncKind.Machine:
                    OpenMachine();
                    break;
                case EncKind.Stone:
                    run.Director.SummonGuardian(true);
                    break;
            }
        }

        public void QueueItemChoice(string title, string sub, int minTier)
        {
            int sell = 12 + run.Zone * 6;
            run.QueueChoice(() => new Choice
            {
                Title = title,
                Sub = sub,
                Cards = Upgrades.RollItemCards(run.Player, 3, minTier),
                CloseLabel = "Take gold instead (+" + sell + ")",
                OnClose = () => run.Money += sell,
            });
        }

        void OpenTrade(Encounter o)
        {
            run.QueueChoice(() =>
            {
                var cards = new List<Card>();
                foreach (Trade t in o.Trades)
                {
                    Trade trade = t;
                    cards.Add(new Card
                    {
                        Key = "trade",
                        Title = trade.UpText,
                        Tag = "Trade",
                        Color = 0x49e0c8,
                        Desc = trade.DownText,
                        Run = () =>
                        {
                            o.Used = true;
                            run.Player.AddStat(trade.Up.Stat, trade.Up.Mul, trade.Up.Value);
                            run.Player.AddStat(trade.Down.Stat, trade.Down.Mul, trade.Down.Value);
                        },
                    });
                }
                return new Choice { Title = "Trade altar", Sub = "One stat rises and another falls for the rest of the run. It answers only once.", CloseLabel = "Not now", Cards = cards };
            });
        }

        void OpenFossil(Encounter o)
        {
            Player player = run.Player;
            Func<float, Action, Action> cost = (share, then) => () =>
            {
                o.Used = true;
                player.AddStat("maxHp", true, -share);
                then();
            };
            run.QueueChoice(() => new Choice
            {
                Title = "Fossil shrine",
                Sub = "Revive a fossil. It gives you power, but costs max HP for the rest of the run.",
                CloseLabel = "Not now",
                Cards = new List<Card>
                {
                    new Card { Key = "f1", Title = "Spiral Fossil", Tag = "Item", Color = 0xc88cff, Desc = "Choose between 2 epic or better items. -15% max HP.",
                        Run = cost(0.15f, () => run.QueueChoice(() => new Choice { Title = "Fossil gift", Sub = "Pick one", Cards = Upgrades.RollItemCards(player, 2, 2) })) },
                    new Card { Key = "f2", Title = "Dome Fossil", Tag = "Blessing", Color = 0xffc53b, Desc = "Choose a strong blessing. -20% max HP.", Run = cost(0.2f, () => QueueBlessing(2)) },
                    new Card { Key = "f3", Title = "Amber Fossil", Tag = "Partner", Color = 0x49c8ff, Desc = "An ancient flyer fights beside you for the rest of the run. -25% max HP.", Run = cost(0.25f, player.AddDrone) },
                },
            });
        }

        void QueueBlessing(float strength)
        {
            run.QueueChoice(() => new Choice { Title = "Blessing", Sub = "Choose one for this run", Cards = Upgrades.RollBlessings(run.Player, strength) });
        }

        void OpenShop(Encounter o)
        {
            run.QueueChoice(() =>
            {
                List<Card> cards = Upgrades.RollItemCards(run.Player, 3, 0);
                foreach (Card card in cards)
                {
                    Card c = card;
                    int price = (int)Math.Round(run.ShopPrice(c.Tier) * o.Markup);
                    Action give = c.Run;
                    c.Tag += " - " + price + " gold";
                    c.Disabled = run.Money < price;
                    c.Run = () =>
                    {
                        run.Money -= price;
                        o.Markup *= 1.2f;
                        run.Audio.Coin();
                        give();
                    };
                }
                return new Choice { Title = "Merchant", Sub = "You have " + run.Money + " gold", Cards = cards, CloseLabel = "Leave" };
            });
        }

        void OpenMachine()
        {
            List<FusionOption> options = Moves.FusionOptions(run.Player);
            if (options.Count == 0)
            {
                run.Hud.Toast("You need two moves at max level");
                run.Audio.Denied();
                return;
            }
            run.QueueChoice(() =>
            {
                var cards = new List<Card>();
                foreach (FusionOption option in options)
                {
                    FusionOption opt = option;
                    if (cards.Count >= 4) break;
                    cards.Add(new Card
                    {
                        Key = "fuse",
                        Title = opt.Def.Name,
                        Tag = opt.Def.Tag,
                        Color = 0xffc53b,
                        Desc = opt.A.Def.Name + " + " + opt.B.Def.Name + ". " + opt.Def.Desc,
                        Run = () =>
                        {
                            Moves.Fuse(run.Player, opt);
                            run.Hud.Banner(opt.Def.Name, opt.Def.Tag);
                            run.Audio.LevelUp();
                            run.AddTrauma(0.3f);
                        },
                    });
                }
                return new Choice { Title = "Fusion machine", Sub = "Both moves are replaced by one, which frees a slot.", CloseLabel = "Not now", Cards = cards };
            });
        }

        public void Update(float dt)
        {
            Player p = run.Player;
            // Interacting can add a chest while this runs, so loop by index.
            for (int k = 0; k < Items.Count; k++)
            {
                Encounter o = Items[k];
                if (o.Used) continue;
                float d = M.Hypot(o.X - p.X, o.Z - p.Z);
                if (o.Type == EncKind.Pod && d < 2.2f)
                {
                    o.Used = true;
                    float dmg = 40 * (1 + run.Zone) * p.Stats.Might;
                    run.Blast(o.X, o.Y + 0.5f, o.Z, PodRadius, dmg, "volt", 0xffd93b, 14, new Status(0, 0, 0.6f), 0);
                    run.AddTrauma(0.4f);
                    run.Hud.Toast("Pod detonated");
                }
                else if (o.Type == EncKind.Charge)
                {
                    if (d < ChargeRadius)
                    {
                        o.ChargeT += dt / ChargeTime;
                        if (M.Rand() < dt * 14) run.Vfx.Trail(o.X + M.Range(-1.5f, 1.5f), o.Y + M.Range(0, 3), o.Z + M.Range(-1.5f, 1.5f), 0xffd24a, 0.5f, 0.5f);
                        if (o.ChargeT >= 1)
                        {
                            o.Used = true;
                            run.Vfx.RingFx(o.X, o.Y, o.Z, ChargeRadius * 2, 0xffd24a, 0.6f);
                            run.Audio.LevelUp();
                            QueueBlessing(1);
                        }
                    }
                    else
                    {
                        o.ChargeT = M.Max(0, o.ChargeT - dt / 14);
                    }
                }
            }
        }

        public static float ChargeRingRadius { get { return ChargeRadius; } }
    }
}
