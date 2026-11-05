using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace Swarmfall
{
    // Holds every creature and icon, painted in code or loaded from authored sheets.
    // Each page has three layers with the same layout: normal sprites, white silhouettes (for hit
    // flashes and shadows) and white outlines (for elites and the current target).
    // Frame numbers run across pages, so one rich creature can never overflow the texture it is on.
    sealed class Atlas
    {
        public const int Cell = 64;
        public const int Columns = 16;
        public const int LayerSize = Cell * Columns;
        public const int Size = LayerSize * 2;
        public const int CellsPerPage = Columns * Columns;
        const int MaxPages = 32;

        // Authored sheets are 6 columns by 8 rows of 64 px cells. See assets/creatures/README.md.
        public const int SheetColumns = 6, SheetRows = 8;
        const int SheetCells = SheetColumns * SheetRows;
        const int PoseIdle = 0, PoseWalk = 1, WalkPoses = 4, PoseAir = 5;
        const int OutlineRadius = 2;

        // Legacy creatures are painted at half size and doubled, which keeps their chunky pixels.
        const int LegacyCell = Cell / 2;

        const int Outline = 1, Main = 2, Dark = 3, Light = 4, Belly = 5, Eye = 6, Accent = 7, White = 8;

        readonly List<byte[]> pages = new List<byte[]>();
        readonly Dictionary<string, int> frames = new Dictionary<string, int>();
        readonly HashSet<int> directional = new HashSet<int>();
        int next;

        public int PageCount { get { return pages.Count; } }
        public byte[] Page(int page) { return pages[page]; }
        public static int PageOf(int frame) { return frame / CellsPerPage; }
        public bool IsDirectional(string id) { return directional.Contains(frames[id]); }

        // Keeps a creature's frames together on one page.
        void Reserve(int cells)
        {
            int used = next % CellsPerPage;
            if (used + cells > CellsPerPage) next += CellsPerPage - used;
            if (PageOf(next + cells - 1) >= MaxPages) throw new InvalidDataException("Too many creatures for the sprite atlas (limit " + MaxPages + " pages).");
        }

        // Rows clockwise: front, front-right, right, back-right, back, back-left, left, front-left.
        // right and forward are the creature's facing relative to the camera. phase counts walk frames.
        public int CreatureFrame(int start, float right, float forward, float phase, bool moving, bool airborne, out bool flip)
        {
            if (directional.Contains(start))
            {
                int direction = ((int)Math.Round(Math.Atan2(right, -forward) * 4 / Math.PI) + 8) % 8;
                int pose = airborne ? PoseAir : moving ? PoseWalk + ((int)phase % WalkPoses + WalkPoses) % WalkPoses : PoseIdle;
                flip = false;
                return start + direction * SheetColumns + pose;
            }
            flip = right < 0;
            return start + (forward > 0.25f ? 2 : 0) + (moving ? (int)(phase * 0.6f) % 2 : 0);
        }

        bool LoadCreature(string id)
        {
            string name = id + ".png";
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "creatures", name);
            if (File.Exists(path))
            {
                using (var stream = File.OpenRead(path)) LoadSheet(id, stream, path);
                return true;
            }
            // Sheets are also built into the exe, so it keeps working when moved on its own.
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("creatures/" + name))
            {
                if (stream == null) return false;
                LoadSheet(id, stream, name);
            }
            return true;
        }

        public void LoadSheet(string id, Stream data, string source)
        {
            Bitmap sheet;
            try
            {
                sheet = new Bitmap(data);
            }
            catch (ArgumentException)
            {
                throw new InvalidDataException(source + " is not a readable image.");
            }
            using (sheet)
            {
                int wantW = Cell * SheetColumns, wantH = Cell * SheetRows;
                if (sheet.Width != wantW || sheet.Height != wantH)
                    throw new InvalidDataException(source + " is " + sheet.Width + " x " + sheet.Height + ". Creature sheets must be " + wantW + " x " + wantH + " (" + SheetColumns + " columns by " + SheetRows + " rows of " + Cell + " px cells).");
                Reserve(SheetCells);
                int start = next;
                for (int row = 0; row < SheetRows; row++)
                {
                    for (int col = 0; col < SheetColumns; col++)
                    {
                        int index = next++;
                        bool any = false;
                        for (int y = 0; y < Cell; y++)
                        {
                            for (int x = 0; x < Cell; x++)
                            {
                                Color c = sheet.GetPixel(col * Cell + x, row * Cell + y);
                                if (c.A < 128) continue;
                                any = true;
                                Plot(0, 0, index, x, y, (uint)(c.R << 16 | c.G << 8 | c.B));
                                Plot(LayerSize, 0, index, x, y, 0xffffff);
                                for (int dy = -OutlineRadius; dy <= OutlineRadius; dy++)
                                {
                                    for (int dx = -OutlineRadius; dx <= OutlineRadius; dx++)
                                    {
                                        if (Math.Abs(dx) + Math.Abs(dy) > OutlineRadius) continue;
                                        if (x + dx >= 0 && x + dx < Cell && y + dy >= 0 && y + dy < Cell) Plot(0, LayerSize, index, x + dx, y + dy, 0xffffff);
                                    }
                                }
                            }
                        }
                        if (!any) throw new InvalidDataException(source + " has an empty cell at row " + row + ", column " + col + ".");
                    }
                }
                frames[id] = start;
                directional.Add(start);
            }
        }

        sealed class Grid
        {
            public readonly byte[] P = new byte[LegacyCell * LegacyCell];
            public void Set(float x, float y, int v)
            {
                int ix = (int)Math.Round(x);
                int iy = (int)Math.Round(y);
                if (ix >= 1 && iy >= 1 && ix < LegacyCell - 1 && iy < LegacyCell - 1) P[iy * LegacyCell + ix] = (byte)v;
            }
            public int Get(int x, int y) { return x < 0 || y < 0 || x >= LegacyCell || y >= LegacyCell ? 0 : P[y * LegacyCell + x]; }
            // Mirrored around x = 15.5 so every creature stays symmetrical.
            public void Mirror(float x, float y, int v)
            {
                Set(15.5f - x - 0.5f, y, v);
                Set(15.5f + x + 0.5f, y, v);
            }
            public void Ellipse(float cx, float cy, float rx, float ry, Func<float, float, int> slot)
            {
                for (int y = (int)Math.Floor(cy - ry); y <= cy + ry; y++)
                {
                    for (int x = (int)Math.Floor(cx - rx); x <= cx + rx; x++)
                    {
                        float dx = (x + 0.5f - cx) / rx;
                        float dy = (y + 0.5f - cy) / ry;
                        if (dx * dx + dy * dy <= 1) Set(x, y, slot(dx, dy));
                    }
                }
            }
            public void AddOutline()
            {
                var q = (byte[])P.Clone();
                for (int y = 0; y < LegacyCell; y++)
                {
                    for (int x = 0; x < LegacyCell; x++)
                    {
                        if (q[y * LegacyCell + x] != 0) continue;
                        bool near = (x > 0 && q[y * LegacyCell + x - 1] != 0) || (x < LegacyCell - 1 && q[y * LegacyCell + x + 1] != 0)
                            || (y > 0 && q[(y - 1) * LegacyCell + x] != 0) || (y < LegacyCell - 1 && q[(y + 1) * LegacyCell + x] != 0);
                        if (near) P[y * LegacyCell + x] = Outline;
                    }
                }
            }
        }


static int Shaded(float dx, float dy) { return dy + dx * 0.3f < -0.45f ? Light : dy + dx * 0.3f > 0.5f ? Dark : Main; }

        static Grid PaintCreature(Art art, int frame, bool back)
        {
            var g = new Grid();
            bool step = frame == 1;
            int legs = art.Legs;
            float brx = art.BodyX, bry = art.BodyY;
            const float feet = 28;
            float bodyCy = feet - (legs > 0 ? 2 : 3) - bry + (art.Wings && step ? -1 : 0);
            float headR = art.Head;
            float headCy = M.Max(headR + 3, bodyCy - bry - headR * 0.45f);

            if (art.Tail)
            {
                float dir = back ? 0 : 1;
                for (int i = 0; i < 6; i++)
                {
                    int part = i;
                    float tx = 15.5f + (brx + i * 0.9f) * dir;
                    float ty = bodyCy + 2 - i * 0.8f + (step ? 1 : 0);
                    float r = 1.8f - i * 0.12f;
                    g.Ellipse(back ? 15.5f : tx, ty - (back ? i * 0.5f : 0), r, r, (a, b) => part > 3 ? Accent : Main);
                }
            }
            if (legs > 0)
            {
                float lift = step ? 1 : 0;
                g.Ellipse(15.5f - brx * 0.55f, feet - 1 - lift, 2.2f, 2, (a, b) => Dark);
                g.Ellipse(15.5f + brx * 0.55f, feet - 1 - (step ? 0 : 1), 2.2f, 2, (a, b) => Dark);
            }
            if (art.Wings)
            {
                float flap = step ? -2 : 1;
                for (int i = 0; i < 7; i++)
                {
                    for (int j = 0; j < 6 - i * 0.6f; j++) g.Mirror(brx + i, bodyCy - 2 + j + flap * (i / 5f), i > 4 ? Dark : Accent);
                }
            }
            g.Ellipse(15.5f, bodyCy, brx, bry, (dx, dy) => !back && M.Abs(dx) < 0.55f && dy > -0.3f ? Belly : Shaded(dx, dy));

            string ear = art.Ears;
            if (ear == "point" || ear == "horn")
            {
                int slot = ear == "horn" ? Accent : Main;
                int span = ear == "horn" ? 3 : 5;
                for (int i = 0; i < span; i++)
                {
                    for (int w = 0; w < span - i; w++) g.Mirror(headR * 0.42f + w * 0.7f, headCy - headR + 1 - i, i > span - 3 ? Accent : slot);
                }
            }
            else if (ear == "round")
            {
                g.Ellipse(15.5f - headR * 0.75f, headCy - headR * 0.8f, 2.6f, 2.6f, (a, b) => Dark);
                g.Ellipse(15.5f + headR * 0.75f, headCy - headR * 0.8f, 2.6f, 2.6f, (a, b) => Dark);
            }
            else if (ear == "fin")
            {
                for (int i = 0; i < 6; i++)
                {
                    g.Set(15.5f, headCy - headR - i + 1, Accent);
                    g.Set(14.5f, headCy - headR - i + 2, Accent);
                    g.Set(16.5f, headCy - headR - i + 2, Accent);
                }
            }
            else if (ear == "leaf")
            {
                g.Ellipse(15.5f, headCy - headR - 1, 3.2f, 2.2f, (a, b) => Accent);
            }

            g.Ellipse(15.5f, headCy, headR, headR * 0.92f, Shaded);

            if (!back)
            {
                float eyeX = headR * 0.42f;
                float eyeY = headCy + 0.5f;
                if (art.Angry)
                {
                    g.Mirror(eyeX, eyeY, Eye);
                    g.Mirror(eyeX + 1, eyeY, Eye);
                    g.Mirror(eyeX, eyeY + 1, Eye);
                    g.Mirror(eyeX + 1, eyeY - 1, Outline);
                    g.Mirror(eyeX, eyeY - 1, Outline);
                }
                else
                {
                    g.Mirror(eyeX, eyeY, Eye);
                    g.Mirror(eyeX, eyeY + 1, Eye);
                    g.Mirror(eyeX + 1, eyeY, Eye);
                    g.Mirror(eyeX + 1, eyeY + 1, Eye);
                    g.Mirror(eyeX, eyeY, White);
                }
                g.Mirror(0, headCy + headR * 0.55f, art.Angry ? Outline : Dark);
            }
            g.AddOutline();
            return g;
        }

        static void PaintIcon(string shape, Grid g)
        {
            switch (shape)
            {
                case "disc":
                    g.Ellipse(15.5f, 15.5f, 14, 14, (a, b) => Main);
                    return;
                case "orb":
                    g.Ellipse(15.5f, 15.5f, 8, 8, (dx, dy) => dx * dx + dy * dy < 0.4f ? White : Main);
                    break;
                case "gem":
                    for (int y = 0; y < 14; y++)
                    {
                        float w = y < 5 ? y * 1.4f : (13 - y) * 0.9f;
                        for (float x = -w; x <= w; x++) g.Set(15.5f + x, 9 + y, x < 0 ? Light : y < 5 ? Main : Dark);
                    }
                    break;
                case "coin":
                    g.Ellipse(15.5f, 15.5f, 7, 7, (dx, dy) => dx * dx + dy * dy < 0.34f ? (dx + dy < 0 ? Light : Main) : dx + dy < 0 ? Main : Dark);
                    break;
                case "heart":
                    g.Ellipse(12, 13, 4, 4, (a, b) => Main);
                    g.Ellipse(19, 13, 4, 4, (a, b) => Main);
                    for (int y = 0; y < 8; y++) for (int x = -7 + y; x <= 7 - y; x++) g.Set(15.5f + x, 15 + y, Main);
                    g.Set(11, 12, White);
                    break;
                case "chest":
                    for (int y = 11; y < 26; y++)
                    {
                        for (int x = 5; x < 27; x++)
                        {
                            bool band = y == 17 || x == 5 || x == 26;
                            g.Set(x, y, y < 17 ? (band ? Dark : Light) : band ? Dark : Main);
                        }
                    }
                    for (int y = 15; y < 20; y++) for (int x = 14; x < 18; x++) g.Set(x, y, Accent);
                    break;
                case "altar":
                    for (int y = 18; y < 28; y++) for (int x = 8; x < 24; x++) g.Set(x, y, y < 20 ? Light : x % 5 == 0 ? Dark : Main);
                    for (int y = 9; y < 18; y++) for (int x = 12; x < 20; x++) g.Set(x, y, x == 12 ? Light : Main);
                    g.Ellipse(16, 8, 4, 4, (a, b) => Accent);
                    g.Set(15, 7, White);
                    break;
                case "pod":
                    g.Ellipse(15.5f, 17, 9, 9, (dx, dy) => dy < -0.1f ? Main : dy < 0.15f ? White : Accent);
                    g.Ellipse(12, 13, 2, 2, (a, b) => White);
                    break;
                case "machine":
                    for (int y = 8; y < 28; y++) for (int x = 7; x < 25; x++) g.Set(x, y, x == 7 || y == 8 ? Light : y > 24 ? Dark : Main);
                    g.Ellipse(12, 15, 3.5f, 3.5f, (a, b) => Accent);
                    g.Ellipse(20, 15, 3.5f, 3.5f, (a, b) => Accent);
                    for (int x = 10; x < 22; x++) g.Set(x, 22, White);
                    break;
                case "fossil":
                    g.Ellipse(15.5f, 17, 10, 8, (dx, dy) => M.Abs(M.Sin(dx * 7 + dy * 2)) > 0.72f ? Dark : dx + dy < 0 ? Light : Main);
                    break;
                case "stone":
                    for (int y = 4; y < 28; y++)
                    {
                        float w = y < 10 ? y - 3 : 6 + (y - 10) * 0.2f;
                        for (float x = -w; x <= w; x++) g.Set(15.5f + x, y, x < 0 ? Light : y % 6 == 0 ? Dark : Main);
                    }
                    g.Ellipse(15.5f, 13, 3, 3, (a, b) => Accent);
                    break;
                case "portal":
                    g.Ellipse(15.5f, 15.5f, 13, 13, (dx, dy) =>
                    {
                        float d = M.Hypot(dx, dy);
                        return d > 0.8f ? Accent : d > 0.55f ? Main : d > 0.3f ? Light : White;
                    });
                    break;
            }
            g.AddOutline();
        }

        void Plot(int layerX, int layerY, int index, int x, int y, uint rgb)
        {
            int page = PageOf(index);
            while (pages.Count <= page) pages.Add(new byte[Size * Size * 4]);
            byte[] pixels = pages[page];
            int local = index % CellsPerPage;
            int px = layerX + (local % Columns) * Cell + x;
            int py = layerY + (local / Columns) * Cell + y;
            int o = (py * Size + px) * 4;
            pixels[o] = (byte)(rgb >> 16);
            pixels[o + 1] = (byte)(rgb >> 8);
            pixels[o + 2] = (byte)rgb;
            pixels[o + 3] = 255;
        }

        void PlotBlock(int layerX, int layerY, int index, int x, int y, uint rgb)
        {
            Plot(layerX, layerY, index, x * 2, y * 2, rgb);
            Plot(layerX, layerY, index, x * 2 + 1, y * 2, rgb);
            Plot(layerX, layerY, index, x * 2, y * 2 + 1, rgb);
            Plot(layerX, layerY, index, x * 2 + 1, y * 2 + 1, rgb);
        }

        int Blit(Grid g, uint[] palette)
        {
            Reserve(1);
            int index = next++;
            for (int y = 0; y < LegacyCell; y++)
            {
                for (int x = 0; x < LegacyCell; x++)
                {
                    int v = g.Get(x, y);
                    if (v != 0)
                    {
                        PlotBlock(0, 0, index, x, y, palette[v]);
                        PlotBlock(LayerSize, 0, index, x, y, 0xffffff);
                    }
                    else if (g.Get(x - 1, y) != 0 || g.Get(x + 1, y) != 0 || g.Get(x, y - 1) != 0 || g.Get(x, y + 1) != 0)
                    {
                        PlotBlock(0, LayerSize, index, x, y, 0xffffff);
                    }
                }
            }
            return index;
        }

        // Four cells per creature: front step A, front step B, back step A, back step B.
        public void AddCreature(string id, Art art)
        {
            if (frames.ContainsKey(id)) return;
            if (LoadCreature(id)) return;
            Reserve(4);
            uint main = art.Main;
            uint belly = art.Belly != 0 ? art.Belly : Col.Shade(main, 1.5f);
            uint[] palette = { 0, 0x17102a, main, Col.Shade(main, 0.68f), Col.Shade(main, 1.3f), belly, art.Angry ? 0xff3355u : 0x17102au, art.Accent, 0xffffff };
            int start = Blit(PaintCreature(art, 0, false), palette);
            Blit(PaintCreature(art, 1, false), palette);
            Blit(PaintCreature(art, 0, true), palette);
            Blit(PaintCreature(art, 1, true), palette);
            frames[id] = start;
        }

        public void AddIcon(string id, string shape, uint main, uint accent)
        {
            var g = new Grid();
            PaintIcon(shape, g);
            uint[] palette = { 0, 0x17102a, main, Col.Shade(main, 0.65f), Col.Shade(main, 1.35f), main, 0x17102a, accent, 0xffffff };
            frames[id] = Blit(g, palette);
        }

        public int Frame(string id) { return frames[id]; }
    }
}
