using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace Swarmfall
{
    // A texture of glyphs drawn once with Windows' own font engine.
    sealed class UiFont
    {
        readonly int Size;
        const string Chars = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~·×★";
        readonly Dictionary<char, RectangleF> glyphs = new Dictionary<char, RectangleF>();
        readonly Dictionary<char, float> advance = new Dictionary<char, float>();
        public readonly uint Texture;
        public readonly float LineHeight;

        public UiFont(string family, float px, FontStyle style)
        {
            Size = px > 50 ? 1024 : 512;
            using (var bmp = new Bitmap(Size, Size, PixelFormat.Format32bppArgb))
            using (var g = Graphics.FromImage(bmp))
            using (var font = new System.Drawing.Font(family, px, style, GraphicsUnit.Pixel))
            using (var format = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.Clear(Color.Transparent);
                LineHeight = font.GetHeight(g);
                float x = 2, y = 2;
                float cellH = (float)Math.Ceiling(LineHeight) + 1;
                // Rows are spaced wider than the glyph rectangles so one row cannot bleed into the next.
                float rowStep = cellH + 6 + px * 0.2f;
                foreach (char c in Chars)
                {
                    string s = c.ToString();
                    float w = g.MeasureString(s, font, PointF.Empty, format).Width;
                    float cellW = (float)Math.Ceiling(w) + 3;
                    if (x + cellW + 4 >= Size)
                    {
                        x = 2;
                        y += rowStep;
                    }
                    g.DrawString(s, font, Brushes.White, x, y, format);
                    glyphs[c] = new RectangleF(x, y, cellW, cellH);
                    advance[c] = w;
                    x += cellW + 4;
                }
                var data = bmp.LockBits(new Rectangle(0, 0, Size, Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                var raw = new byte[Size * Size * 4];
                Marshal.Copy(data.Scan0, raw, 0, raw.Length);
                bmp.UnlockBits(data);
                for (int i = 0; i < raw.Length; i += 4)
                {
                    byte a = raw[i + 3];
                    raw[i] = raw[i + 1] = raw[i + 2] = 255;
                    raw[i + 3] = a;
                }
                Texture = Renderer.UploadTexture(raw, Size, Size, Gl.LINEAR, Gl.CLAMP);
            }
        }

        public float Measure(string text)
        {
            float w = 0;
            foreach (char c in text)
            {
                float a;
                if (advance.TryGetValue(c, out a)) w += a;
            }
            return w;
        }

        public void Draw(string text, float x, float y)
        {
            Gl.BindTexture(Gl.TEXTURE_2D, Texture);
            Gl.Begin(Gl.QUADS);
            foreach (char c in text)
            {
                RectangleF r;
                if (!glyphs.TryGetValue(c, out r)) continue;
                float u0 = r.X / Size, v0 = r.Y / Size, u1 = r.Right / Size, v1 = r.Bottom / Size;
                Gl.TexCoord(u0, v0); Gl.Vertex(x, y, 0);
                Gl.TexCoord(u1, v0); Gl.Vertex(x + r.Width, y, 0);
                Gl.TexCoord(u1, v1); Gl.Vertex(x + r.Width, y + r.Height, 0);
                Gl.TexCoord(u0, v1); Gl.Vertex(x, y + r.Height, 0);
                x += advance[c];
            }
            Gl.End();
        }
    }

    sealed class FloatText
    {
        public float X, Y, Z, Life;
        public string Value;
        public int Cls;
    }

    // Game-facing state (banners, cards, floating text) plus all the interface drawing.
    // The state part works without a window, for the --selftest mode.
    sealed class Hud
    {
        public const int TextNormal = 0, TextCrit = 1, TextStrong = 2, TextWeak = 3, TextHurt = 4, TextHeal = 5;
        const int TextPool = 48;
        const float TextLife = 0.7f;
        const float BannerLife = 2.6f;
        const float ToastLife = 1.8f;
        const float DesignW = 1280, DesignH = 760;

        const uint Night = 0x150f2b, Panel = 0x221a40, Line = 0x453a74, TextCol = 0xf4efff, Muted = 0xb3a9d6, Gold = 0xffd24a, Ember = 0xff7a3c, Mint = 0x49e0c8, Danger = 0xff4a5e, Ink = 0x17102a;

        readonly Run run;
        readonly FloatText[] texts = new FloatText[TextPool];
        int nextText;
        string bannerTitle = "", bannerSub = "", toastText = "", bossName;
        float bannerT, toastT, hitT;
        public Choice Opts;
        public bool Banishing;
        public bool Hidden;

        Renderer renderer;
        UiFont small, medium, large, huge;
        float w, h, scale = 1, mx, my;

        public Hud(Run run)
        {
            this.run = run;
            for (int i = 0; i < TextPool; i++) texts[i] = new FloatText();
        }

        public void Text(float x, float y, float z, string value, int cls)
        {
            FloatText t = texts[nextText];
            nextText = (nextText + 1) % TextPool;
            t.X = x + M.Range(-0.35f, 0.35f);
            t.Y = y;
            t.Z = z + M.Range(-0.35f, 0.35f);
            t.Life = TextLife;
            t.Value = value;
            t.Cls = cls;
        }

        public void Banner(string title, string sub)
        {
            bannerTitle = title;
            bannerSub = sub;
            bannerT = BannerLife;
        }

        public void Toast(string msg)
        {
            toastText = msg;
            toastT = ToastLife;
        }

        public void FlashDamage() { hitT = 0.45f; }
        public void ShowBoss(string name) { bossName = name; }
        public void HideBoss() { bossName = null; }

        public void ShowCards(Choice opts)
        {
            Opts = opts;
            Banishing = false;
        }

        public void ToggleBanish()
        {
            if (run.Player.Banishes > 0) Banishing = !Banishing;
        }

        public void ChooseCard(int k)
        {
            if (Opts == null || k >= Opts.Cards.Count) return;
            Card card = Opts.Cards[k];
            if (Banishing)
            {
                run.UseBanish(card);
                return;
            }
            if (card.Disabled)
            {
                run.Audio.Denied();
                return;
            }
            card.Run();
            run.CloseChoice();
        }

        public void CloseButton()
        {
            if (Opts == null || Opts.CloseLabel == null) return;
            if (Opts.OnClose != null) Opts.OnClose();
            run.CloseChoice();
        }

        public void Update(float dt)
        {
            bannerT -= dt;
            toastT -= dt;
            hitT -= dt;
            foreach (FloatText t in texts)
            {
                if (t.Life <= 0) continue;
                t.Life -= dt;
                t.Y += dt * 1.6f;
            }
        }

        // ---- Drawing ----

        public void InitGraphics(Renderer r)
        {
            renderer = r;
            small = new UiFont("Segoe UI", 15, FontStyle.Bold);
            medium = new UiFont("Segoe UI", 22, FontStyle.Bold);
            large = new UiFont("Segoe UI Black", 40, FontStyle.Regular);
            huge = new UiFont("Segoe UI Black", 76, FontStyle.Regular);
        }

        static void SetColor(uint hex, float a = 1)
        {
            var c = new Col(hex);
            Gl.Color4(c.R, c.G, c.B, a);
        }

        void Rect(float x, float y, float rw, float rh, uint color, float a = 1)
        {
            SetColor(color, a);
            Gl.Begin(Gl.QUADS);
            Gl.Vertex(x, y, 0);
            Gl.Vertex(x + rw, y, 0);
            Gl.Vertex(x + rw, y + rh, 0);
            Gl.Vertex(x, y + rh, 0);
            Gl.End();
        }

        void Border(float x, float y, float rw, float rh, float t, uint color, float a = 1)
        {
            Rect(x, y, rw, t, color, a);
            Rect(x, y + rh - t, rw, t, color, a);
            Rect(x, y, t, rh, color, a);
            Rect(x + rw - t, y, t, rh, color, a);
        }

        void Disc(float cx, float cy, float r, uint color, float a = 1, float sweep = 1)
        {
            const int segs = 40;
            SetColor(color, a);
            Gl.Begin(Gl.TRIANGLES);
            int n = (int)(segs * sweep);
            for (int k = 0; k < n; k++)
            {
                float a0 = (float)k / segs * M.Tau - M.Pi / 2, a1 = (float)(k + 1) / segs * M.Tau - M.Pi / 2;
                Gl.Vertex(cx, cy, 0);
                Gl.Vertex(cx + M.Cos(a0) * r, cy + M.Sin(a0) * r, 0);
                Gl.Vertex(cx + M.Cos(a1) * r, cy + M.Sin(a1) * r, 0);
            }
            Gl.End();
        }

        // align: 0 left, 1 centre, 2 right. Draws with a hard drop shadow so it stays readable over anything.
        void Label(UiFont font, string s, float x, float y, uint color, int align = 0, float shadow = 2, float alpha = 1, uint shadowColor = Ink)
        {
            if (string.IsNullOrEmpty(s)) return;
            float tw = font.Measure(s);
            if (align == 1) x -= tw / 2;
            else if (align == 2) x -= tw;
            Gl.Enable(Gl.TEXTURE_2D);
            if (shadow > 0)
            {
                SetColor(shadowColor, alpha);
                font.Draw(s, x, y + shadow);
            }
            SetColor(color, alpha);
            font.Draw(s, x, y);
            Gl.Disable(Gl.TEXTURE_2D);
        }

        // Breaks text to fit a width. Returns the height used.
        float Wrapped(UiFont font, string s, float x, float y, float maxW, uint color, int align = 0)
        {
            string line = "";
            float lh = font.LineHeight;
            float top = y;
            foreach (string word in s.Split(' '))
            {
                string trial = line.Length == 0 ? word : line + " " + word;
                if (font.Measure(trial) > maxW && line.Length > 0)
                {
                    Label(font, line, x, y, color, align, 1);
                    y += lh;
                    line = word;
                }
                else
                {
                    line = trial;
                }
            }
            Label(font, line, x, y, color, align, 1);
            return y + lh - top;
        }

        void Icon(int frame, float x, float y, float size)
        {
            float cell = (float)Atlas.Cell / Atlas.Size;
            int local = frame % Atlas.CellsPerPage;
            float u = (local % Atlas.Columns) * cell, v = (local / Atlas.Columns) * cell;
            Gl.Enable(Gl.TEXTURE_2D);
            Gl.BindTexture(Gl.TEXTURE_2D, renderer.TextureOf(frame));
            Gl.Color4(1, 1, 1, 1);
            Gl.Begin(Gl.QUADS);
            Gl.TexCoord(u, v); Gl.Vertex(x, y, 0);
            Gl.TexCoord(u + cell, v); Gl.Vertex(x + size, y, 0);
            Gl.TexCoord(u + cell, v + cell); Gl.Vertex(x + size, y + size, 0);
            Gl.TexCoord(u, v + cell); Gl.Vertex(x, y + size, 0);
            Gl.End();
            Gl.Disable(Gl.TEXTURE_2D);
        }

        bool Hover(float x, float y, float rw, float rh) { return mx >= x && my >= y && mx < x + rw && my < y + rh; }
        bool Clicked(float x, float y, float rw, float rh) { return run.Input.Clicked && Hover(x, y, rw, rh); }

        bool Button(string label, float x, float y, float rw, float rh, bool primary, bool enabled = true)
        {
            bool over = enabled && Hover(x, y, rw, rh);
            if (primary)
            {
                Rect(x, y + 4, rw, rh, 0xa5741d);
                Rect(x, y + (over ? 1 : 0), rw, rh, over ? 0xffe07a : Gold);
                Label(medium, label, x + rw / 2, y + (rh - medium.LineHeight) / 2, Ink, 1, 0);
            }
            else
            {
                Rect(x, y, rw, rh, over ? Line : Night, enabled ? 0.85f : 0.4f);
                Border(x, y, rw, rh, 2, Line, enabled ? 1 : 0.4f);
                Label(small, label, x + rw / 2, y + (rh - small.LineHeight) / 2, TextCol, 1, 1, enabled ? 1 : 0.45f);
            }
            return enabled && Clicked(x, y, rw, rh);
        }

        public void Draw(int pxW, int pxH)
        {
            if (Hidden) return;
            scale = M.Clamp(M.Min(pxW / DesignW, pxH / DesignH), 0.55f, 2);
            w = pxW / scale;
            h = pxH / scale;
            mx = run.Input.MouseX / scale;
            my = run.Input.MouseY / scale;

            Gl.MatrixMode(Gl.PROJECTION);
            Gl.LoadIdentity();
            Gl.Ortho(0, w, h, 0, -1, 1);
            Gl.MatrixMode(Gl.MODELVIEW);
            Gl.LoadIdentity();
            Gl.Disable(Gl.DEPTH_TEST);
            Gl.Disable(Gl.FOG);
            Gl.Disable(Gl.TEXTURE_2D);
            Gl.Enable(Gl.BLEND);
            Gl.BlendFunc(Gl.SRC_ALPHA, Gl.ONE_MINUS_SRC_ALPHA);

            switch (run.State)
            {
                case GameState.Menu:
                    DrawMenu();
                    return;
                default:
                    DrawGameHud();
                    break;
            }
            if (run.State == GameState.Choice) DrawChoice();
            else if (run.State == GameState.Paused) DrawPause();
            else if (run.State == GameState.Dead || run.State == GameState.Won) DrawEnd();
        }

        void Dim(float a = 0.62f) { Rect(0, 0, w, h, 0x0d081c, a); }

        // ---- Roster menu ----

        const int RosterColumns = 6;
        const float RosterW = 196, RosterH = 112, RosterGap = 10, RosterTop = 150;
        Partner selected;

        Partner Selected()
        {
            if (selected != null) return selected;
            foreach (Partner p in Data.Partners) if (p.Id == run.Profile.LastPartner && run.Profile.Owns(p)) selected = p;
            if (selected == null) foreach (Partner p in Data.Partners) if (selected == null && run.Profile.Owns(p)) selected = p;
            if (selected == null) selected = Data.Partners[0];
            return selected;
        }

        float RosterX(int i) { return w / 2 - (RosterColumns * RosterW + (RosterColumns - 1) * RosterGap) / 2 + (i % RosterColumns) * (RosterW + RosterGap); }
        float RosterY(int i) { return RosterTop + (i / RosterColumns) * (RosterH + RosterGap); }
        float DetailY { get { return RosterY(Data.Partners.Length - 1) + RosterH + 14; } }

        // Pixel positions of menu controls, for the screenshot script to click. Layout uses the last drawn size.
        public void RosterPoint(int i, out int px, out int py)
        {
            px = (int)((RosterX(i) + RosterW / 2) * scale);
            py = (int)((RosterY(i) + RosterH / 2) * scale);
        }

        public void StartPoint(out int px, out int py)
        {
            px = (int)((StartX + StartW / 2) * scale);
            py = (int)((DetailY + 130) * scale);
        }

        const float StartW = 280;
        float StartX { get { return w / 2 + RosterColumns * (RosterW + RosterGap) / 2 - RosterGap - StartW - 16; } }

        void IconSilhouette(int frame, float x, float y, float size, uint color)
        {
            float cell = (float)Atlas.Cell / Atlas.Size;
            int local = frame % Atlas.CellsPerPage;
            float u = (float)Atlas.LayerSize / Atlas.Size + (local % Atlas.Columns) * cell, v = (local / Atlas.Columns) * cell;
            Gl.Enable(Gl.TEXTURE_2D);
            Gl.BindTexture(Gl.TEXTURE_2D, renderer.TextureOf(frame));
            Gl.Enable(Gl.ALPHA_TEST);
            Gl.AlphaFunc(Gl.GREATER, 0.5f);
            SetColor(color);
            Gl.Begin(Gl.QUADS);
            Gl.TexCoord(u, v); Gl.Vertex(x, y, 0);
            Gl.TexCoord(u + cell, v); Gl.Vertex(x + size, y, 0);
            Gl.TexCoord(u + cell, v + cell); Gl.Vertex(x + size, y + size, 0);
            Gl.TexCoord(u, v + cell); Gl.Vertex(x, y + size, 0);
            Gl.End();
            Gl.Disable(Gl.ALPHA_TEST);
            Gl.Disable(Gl.TEXTURE_2D);
        }

        void RosterCard(Partner p, int i)
        {
            Profile profile = run.Profile;
            float x = RosterX(i), y = RosterY(i);
            bool owned = profile.Owns(p), ready = profile.Ready(p), isSelected = Selected() == p;
            uint tone = owned ? Data.ElemColor(p.Elem) : ready ? Gold : Line;
            bool over = Hover(x, y, RosterW, RosterH);
            Rect(x, y, RosterW, RosterH, over ? Line : Panel, 0.95f);
            Border(x, y, RosterW, RosterH, isSelected ? 4 : 2, isSelected ? Gold : tone);
            int frame = run.Atlas.Frame(p.Id);
            if (owned)
            {
                Icon(frame, x + 6, y + 8, 80);
                Label(small, p.Name, x + 86, y + 14, TextCol, 0, 1);
                Wrapped(small, p.Role, x + 86, y + 36, RosterW - 92, Muted);
                Label(small, Data.Elements[p.Elem].Name, x + 86, y + 80, tone, 0, 0);
            }
            else
            {
                IconSilhouette(frame, x + 6, y + 8, 80, ready ? 0x6a5a1au : 0x0d081cu);
                Label(small, ready ? p.Name : "???", x + 86, y + 14, ready ? Gold : Muted, 0, 1);
                if (ready)
                {
                    Wrapped(small, "Find it in the wild", x + 86, y + 36, RosterW - 92, TextCol);
                }
                else
                {
                    float fill = (float)profile.Get(p.ChallengeCounter) / p.ChallengeTarget;
                    Bar(x + 86, y + 42, RosterW - 96, 12, fill, Mint);
                    Label(small, "Locked", x + 86, y + 62, Muted, 0, 0);
                }
            }
            if (Clicked(x, y, RosterW, RosterH)) selected = p;
        }

        void RosterDetail(Partner p, float x, float y, float width)
        {
            Profile profile = run.Profile;
            bool owned = profile.Owns(p), ready = profile.Ready(p);
            const float height = 196;
            Rect(x, y, width, height, Panel, 0.95f);
            Border(x, y, width, height, 2, owned ? Data.ElemColor(p.Elem) : ready ? Gold : Line);
            int frame = run.Atlas.Frame(p.Id);
            if (owned) Icon(frame, x + 12, y + 18, 160);
            else IconSilhouette(frame, x + 12, y + 18, 160, ready ? 0x6a5a1au : 0x0d081cu);
            float tx = x + 186, tw = width - 186 - StartW - 40;
            if (owned || ready)
            {
                Label(medium, p.Name, tx, y + 12, TextCol);
                Label(small, p.Role.ToUpper() + " · " + p.TypeLine(), tx, y + 44, Data.ElemColor(p.Elem), 0, 0);
            }
            else
            {
                Label(medium, "???", tx, y + 12, Muted);
                Label(small, "NOT ON YOUR ROSTER YET", tx, y + 44, Muted, 0, 0);
            }
            if (owned)
            {
                float used = Wrapped(small, p.Blurb, tx, y + 68, tw, TextCol);
                string line = "HP " + (int)p.MaxHp + " · Might x" + p.Might.ToString("0.00") + " · Focus x" + p.Focus.ToString("0.00") + " · Armor " + (int)p.Armor + " · Will " + (int)p.Will + " · Starts with " + Data.Move(p.Move).Name + (Data.Move(p.Move).Special ? " (special)" : " (physical)");
                float more = Wrapped(small, line, tx, y + 72 + used, tw, Muted);
                if (p.Forms != null) Label(small, "Changes form at Lv " + p.FormLevel, tx, y + 76 + used + more, Gold, 0, 1);
                else if (p.Evolutions.Length == 1) Label(small, "Evolves at Lv " + p.Evolutions[0].Level, tx, y + 76 + used + more, Gold, 0, 0);
                else if (p.Evolutions.Length > 0) Label(small, "Evolves at Lv " + p.Evolutions[0].Level + " and Lv " + p.Evolutions[p.Evolutions.Length - 1].Level, tx, y + 76 + used + more, Gold, 0, 0);
                string modeText = run.Mode == GameMode.Survival ? "Mode: Survival (endless)" : "Mode: Classic (3 zones)";
                if (Button(modeText, StartX, y + 14, StartW, 36, false))
                {
                    run.Mode = run.Mode == GameMode.Survival ? GameMode.Classic : GameMode.Survival;
                    profile.Set("lastMode", run.Mode == GameMode.Survival ? 1 : 0);
                }
                if (Button(run.Diff.Name + ": " + run.Diff.Desc, StartX, y + 58, StartW, 36, false))
                {
                    run.DifficultyIndex = (run.DifficultyIndex + 1) % Data.Difficulties.Length;
                    profile.Set("lastDifficulty", run.DifficultyIndex);
                }
                if (Button("Start with " + p.Name, StartX, y + 106, StartW, 48, true)) run.Start(p);
            }
            else
            {
                string step1 = (ready ? "Done: " : "1. ") + p.ChallengeText;
                Label(small, step1, tx, y + 72, ready ? Mint : TextCol, 0, 1);
                if (!ready)
                {
                    long have = Math.Min(profile.Get(p.ChallengeCounter), p.ChallengeTarget);
                    Bar(tx, y + 94, M.Min(tw, 300), 14, (float)have / p.ChallengeTarget, Mint);
                    Label(small, have + " / " + p.ChallengeTarget, tx + M.Min(tw, 300) + 10, y + 91, Muted, 0, 0);
                }
                Label(small, "2. It then appears in the wild during a run.", tx, y + 118, ready ? Gold : Muted, 0, 1);
                Label(small, "3. Weaken it and catch it to add it to your roster.", tx, y + 140, Muted, 0, 1);
                Button("Not on your roster yet", StartX, y + 106, StartW, 48, false, false);
            }
        }

        // ---- Lab ----

        const float TabY = 104, TabW = 110, TabH = 32;
        const int LabColumns = 4;
        const float LabW = 300, LabH = 80, LabGap = 8, LabTop = 150;
        bool menuLab;

        float LabX(int i) { return w / 2 - (LabColumns * LabW + (LabColumns - 1) * LabGap) / 2 + (i % LabColumns) * (LabW + LabGap); }
        float LabY(int i) { return LabTop + (i / LabColumns) * (LabH + LabGap); }

        // Pixel positions for the screenshot script to click.
        public void LabTabPoint(bool lab, out int px, out int py)
        {
            px = (int)((RosterX(0) + (lab ? TabW + 8 : 0) + TabW / 2) * scale);
            py = (int)((TabY + TabH / 2) * scale);
        }

        public void LabCardPoint(int i, out int px, out int py)
        {
            px = (int)((LabX(i) + LabW / 2) * scale);
            py = (int)((LabY(i) + LabH / 2) * scale);
        }

        void LabCard(UpgradeDef u, int i)
        {
            Profile profile = run.Profile;
            float x = LabX(i), y = LabY(i);
            int level = profile.Level(u);
            bool maxed = level >= u.Max, affordable = profile.CanBuy(u);
            bool over = Hover(x, y, LabW, LabH);
            Rect(x, y, LabW, LabH, over && affordable ? Line : Panel, 0.95f);
            Border(x, y, LabW, LabH, 2, maxed ? Mint : affordable ? Gold : Line);
            Label(small, u.Name, x + 12, y + 8, TextCol, 0, 1);
            Label(small, u.Desc, x + 12, y + 30, Muted, 0, 0);
            for (int k = 0; k < u.Max; k++) Rect(x + 12 + k * 15, y + 58, 12, 10, k < level ? Mint : Night);
            if (maxed) Label(small, "Max", x + LabW - 12, y + 52, Mint, 2, 0);
            else Label(small, profile.Cost(u) + " shards", x + LabW - 12, y + 52, affordable ? Gold : Muted, 2, 0);
            if (!Clicked(x, y, LabW, LabH) || maxed) return;
            if (profile.Buy(u)) run.Audio.Coin();
            else run.Audio.Denied();
        }

        void DrawLab()
        {
            for (int i = 0; i < Data.LabUpgrades.Length; i++) LabCard(Data.LabUpgrades[i], i);
            int rows = (Data.LabUpgrades.Length + LabColumns - 1) / LabColumns;
            Label(small, "Upgrades apply to every partner, from the start of your next run. You earn shards from knockouts, catches, bosses and how far you get.", w / 2, LabTop + rows * (LabH + LabGap) + 8, Muted, 1);
        }

        // ---- First-launch starter window ----

        const float StarterW = 380, StarterH = 430, StarterGap = 16, StarterTop = 130;
        Partner starterPick;

        Partner[] StarterOptions() { return Array.FindAll(Data.Partners, p => p.Starter); }

        float StarterX(int i, int n) { return w / 2 - (n * StarterW + (n - 1) * StarterGap) / 2 + i * (StarterW + StarterGap); }
        float StarterConfirmY { get { return StarterTop + StarterH + 18; } }
        const float StarterConfirmW = 360, StarterConfirmH = 52;

        // Pixel positions for the screenshot script to click.
        public void StarterPoint(int i, out int px, out int py)
        {
            px = (int)((StarterX(i, StarterOptions().Length) + StarterW / 2) * scale);
            py = (int)((StarterTop + StarterH / 2) * scale);
        }

        public void StarterConfirmPoint(out int px, out int py)
        {
            px = (int)(w / 2 * scale);
            py = (int)((StarterConfirmY + StarterConfirmH / 2) * scale);
        }

        void StarterCard(Partner p, float x)
        {
            bool picked = starterPick == p;
            uint tone = Data.ElemColor(p.Elem);
            bool over = Hover(x, StarterTop, StarterW, StarterH);
            Rect(x, StarterTop, StarterW, StarterH, over ? Line : Panel, 0.97f);
            Border(x, StarterTop, StarterW, StarterH, picked ? 4 : 2, picked ? Gold : tone);
            Icon(run.Atlas.Frame(p.Id), x + StarterW / 2 - 90, StarterTop + 8, 180);
            float tx = x + 20, tw = StarterW - 40, y = StarterTop + 192;
            Label(medium, p.Name, x + StarterW / 2, y, TextCol, 1);
            y += 32;
            Label(small, p.Role.ToUpper() + " · " + p.TypeLine(), x + StarterW / 2, y, tone, 1, 0);
            y += 30;
            y += Wrapped(small, p.Blurb, tx, y, tw, TextCol) + 8;
            y += Wrapped(small, "HP " + (int)p.MaxHp + " · Might x" + p.Might.ToString("0.00") + " · Focus x" + p.Focus.ToString("0.00") + " · Armor " + (int)p.Armor + " · Will " + (int)p.Will + " · Starts with " + Data.Move(p.Move).Name + (Data.Move(p.Move).Special ? " (special)" : " (physical)"), tx, y, tw, Muted) + 8;
            var names = new List<string>();
            foreach (Stage st in p.Stages()) names.Add(st.Name);
            Wrapped(small, "Evolves: " + string.Join(", then ", names.ToArray()), tx, y, tw, Gold);
            if (Clicked(x, StarterTop, StarterW, StarterH))
            {
                starterPick = p;
                run.Audio.Coin();
            }
        }

        void DrawStarterChoice()
        {
            Profile profile = run.Profile;
            float cx = w / 2;
            Label(medium, "Choose your first creature", cx, 72, TextCol, 1);
            Label(small, "It will be the only one unlocked at the start. You can earn the others later.", cx, 102, Muted, 1);
            Partner[] options = StarterOptions();
            for (int i = 0; i < options.Length; i++) StarterCard(options[i], StarterX(i, options.Length));
            string label = starterPick != null ? "Choose " + starterPick.Name : "Pick a creature above";
            if (Button(label, cx - StarterConfirmW / 2, StarterConfirmY, StarterConfirmW, StarterConfirmH, starterPick != null, starterPick != null) && profile.ChooseStarter(starterPick))
            {
                selected = starterPick;
                run.Audio.LevelUp();
            }
            if (profile.Warning != null) Wrapped(small, profile.Warning, cx, StarterConfirmY + 60, w - 80, Danger, 1);
        }

        void DrawMenu()
        {
            Profile profile = run.Profile;
            Dim(0.6f);
            float cx = w / 2;
            Label(large, "Swarmfall Tower", cx, 22, Gold, 1, 4, 1, Ember);
            if (profile.NeedsStarter)
            {
                DrawStarterChoice();
                return;
            }
            Label(small, (run.Mode == GameMode.Survival ? "One map, a boss every 5 minutes, and a horde that never stops. How long can you last?" : "Cross three open zones, each with a guardian to beat.") + " Your moves fire on their own, so your job is to explore, build, and stay alive.", cx, 78, TextCol, 1);
            Label(small, "Shards " + profile.Get("shards") + "  ·  Roster " + profile.OwnedCount() + " of " + Data.Partners.Length + "  ·  Runs " + profile.Get("runs") + "  ·  Clears " + profile.Get("wins") + "  ·  Knockouts " + profile.Get("kills") + "  ·  Best level " + profile.Get("bestLevel") + (profile.Get("survivalBest") > 0 ? "  ·  Best Survival " + Clock(profile.Get("survivalBest")) : ""), cx, 108, Gold, 1);
            float tabX = RosterX(0);
            if (Button(menuLab ? "Roster" : "Roster (open)", tabX, TabY, TabW, TabH, false)) menuLab = false;
            if (Button(menuLab ? "Lab (open)" : "Lab", tabX + TabW + 8, TabY, TabW, TabH, false)) menuLab = true;
            UpdateButton();
            if (menuLab)
            {
                DrawLab();
                if (profile.Warning != null) Wrapped(small, profile.Warning, cx, h - 40, w - 80, Danger, 1);
                return;
            }
            for (int i = 0; i < Data.Partners.Length; i++) RosterCard(Data.Partners[i], i);
            float left = RosterX(0);
            float width = RosterColumns * RosterW + (RosterColumns - 1) * RosterGap;
            RosterDetail(Selected(), left, DetailY, width);
            float y = DetailY + 196 + 14;
            Label(small, "WASD move  ·  Mouse camera  ·  Space jump  ·  Shift slide, or dive in the air  ·  R ultimate  ·  E interact  ·  Esc pause  ·  M mute  ·  F11 fullscreen", cx, y, Muted, 1);
            if (profile.Warning != null) Wrapped(small, profile.Warning, cx, y + 26, w - 80, Danger, 1);
        }

        // Top-right of the menu. Checks GitHub only when clicked, then offers the newer release.
        const float UpdateW = 230;
        void UpdateButton()
        {
            float x = RosterX(RosterColumns - 1) + RosterW - UpdateW, y = TabY;
            string label;
            bool enabled = true, primary = false;
            switch (Updater.State)
            {
                case UpdateState.Checking: label = "Checking for updates..."; enabled = false; break;
                case UpdateState.UpToDate: label = "Up to date · Release " + Updater.Release; break;
                case UpdateState.Available: label = "Update to Release " + Updater.LatestRelease; primary = true; break;
                case UpdateState.Downloading: label = "Downloading... " + (int)(Updater.Progress * 100) + "%"; enabled = false; break;
                case UpdateState.Ready: label = "Restart to finish update"; primary = true; break;
                case UpdateState.Failed: label = "Update failed · Try again"; break;
                default: label = Updater.JustUpdated ? "Updated to Release " + Updater.Release : "Check for updates · R" + Updater.Release; break;
            }
            if (Button(label, x, y, UpdateW, TabH, false, enabled))
            {
                run.Audio.Coin();
                if (Updater.State == UpdateState.Available) Updater.DownloadAsync();
                else if (Updater.State == UpdateState.Ready)
                {
                    try
                    {
                        Updater.InstallAndRestart(true);
                        Environment.Exit(0);
                    }
                    catch (Exception e)
                    {
                        Updater.Error = "The update couldn't be installed: " + e.Message;
                        Updater.State = UpdateState.Failed;
                    }
                }
                else Updater.CheckAsync();
            }
            if (primary) Border(x, y, UpdateW, TabH, 2, Gold);
            string note = Updater.State == UpdateState.Failed ? Updater.Error
                : Updater.State == UpdateState.Available ? "A newer version is on GitHub. Click to download it. Your save is kept."
                : Updater.State == UpdateState.Ready ? "Downloaded and checked. Click to swap it in and restart. Your save is kept."
                : null;
            if (note != null && Hover(x, y, UpdateW, TabH))
            {
                float nw = 320, nx = x + UpdateW - nw, ny = y + TabH + 6;
                Rect(nx, ny, nw, 74, Night, 0.97f);
                Border(nx, ny, nw, 74, 2, Updater.State == UpdateState.Failed ? Danger : Gold);
                Wrapped(small, note, nx + 10, ny + 8, nw - 20, TextCol);
            }
        }

        static string Clock(float t)
        {
            int s = (int)t;
            return (s / 60) + ":" + (s % 60).ToString("00");
        }

        void Bar(float x, float y, float bw, float bh, float fill, uint color)
        {
            Rect(x, y, bw, bh, Ink);
            Rect(x + 2, y + 2, (bw - 4) * M.Clamp(fill, 0, 1), bh - 4, color);
        }

        void DrawFloatingText()
        {
            foreach (FloatText t in texts)
            {
                if (t.Life <= 0) continue;
                float sx, sy;
                if (!renderer.Project(t.X, t.Y, t.Z, out sx, out sy)) continue;
                sx /= scale;
                sy /= scale;
                UiFont font = t.Cls == TextCrit || t.Cls == TextHurt ? medium : small;
                uint color = t.Cls == TextCrit ? Gold : t.Cls == TextStrong ? 0xffb86b : t.Cls == TextWeak ? Muted : t.Cls == TextHurt ? Danger : t.Cls == TextHeal ? Mint : TextCol;
                Label(font, t.Value, sx, sy, color, 1, 2, M.Min(1, t.Life * 3));
            }
        }

        void DrawMinimap(float x, float y, float size)
        {
            float c = size / 2;
            float k = c / (Terrain.ArenaRadius + 3);
            float cx = x + c, cy = y + c;
            Disc(cx, cy, c, Gold);
            Disc(cx, cy, c - 2, 0x140e26, 0.92f);
            foreach (Encounter o in run.Encounters.Items)
            {
                uint color;
                switch (o.Type)
                {
                    case EncKind.Chest: color = 0xffd24a; break;
                    case EncKind.Cursed: color = 0xb98cff; break;
                    case EncKind.TradeAltar: color = 0x49e0c8; break;
                    case EncKind.Risk: color = 0xff4a5e; break;
                    case EncKind.Charge: color = 0xffe9a0; break;
                    case EncKind.Fossil: color = 0xc9a06a; break;
                    case EncKind.Merchant: color = 0xff9a3c; break;
                    case EncKind.Machine: color = 0xffffff; break;
                    case EncKind.Stone: color = 0xff5da2; break;
                    default: continue;
                }
                if (o.Used) continue;
                float s = o.Type == EncKind.Stone || o.Type == EncKind.Machine ? 6 : 4;
                Rect(cx + o.X * k - s / 2, cy + o.Z * k - s / 2, s, s, color);
            }
            float wx, wz;
            if (run.Capture.Locate(out wx, out wz)) Rect(cx + wx * k - 4, cy + wz * k - 4, 8, 8, Mint);
            if (run.Pickups.HasPortal) Rect(cx + run.Pickups.PortalX * k - 4, cy + run.Pickups.PortalZ * k - 4, 8, 8, Mint);
            Enemies e = run.Enemies;
            SetColor(0xff5a6a);
            Gl.Begin(Gl.QUADS);
            for (int i = 0; i < e.Count; i++)
            {
                if (e.Dead[i]) continue;
                float ex = cx + e.X[i] * k, ey = cy + e.Z[i] * k;
                Gl.Vertex(ex - 1, ey - 1, 0);
                Gl.Vertex(ex + 1, ey - 1, 0);
                Gl.Vertex(ex + 1, ey + 1, 0);
                Gl.Vertex(ex - 1, ey + 1, 0);
            }
            Gl.End();
            // Player arrow, rotated with the camera.
            float px = cx + run.Player.X * k, py = cy + run.Player.Z * k;
            float yaw = -run.CamYaw;
            float sn = M.Sin(yaw), cs = M.Cos(yaw);
            SetColor(0xffffff);
            Gl.Begin(Gl.TRIANGLES);
            Gl.Vertex(px + 6 * sn, py - 6 * cs, 0);
            Gl.Vertex(px + 4 * cs - 4 * sn, py + 4 * sn + 4 * cs, 0);
            Gl.Vertex(px - 4 * cs - 4 * sn, py - 4 * sn + 4 * cs, 0);
            Gl.End();
        }

        void DrawGameHud()
        {
            Player p = run.Player;
            Stats s = p.Stats;
            Director d = run.Director;
            DrawFloatingText();

            // Portrait and bars.
            Disc(52, 52, 38, Gold);
            Disc(52, 52, 35, Panel);
            Icon(run.Atlas.Frame(p.Stage.Id), 22, 22, 60);
            float bx = 100, bw = 300;
            Label(medium, p.Stage.Name, bx, 12, TextCol);
            Label(medium, "Lv " + p.Level, bx + bw, 12, Gold, 2);
            Bar(bx, 44, bw, 20, p.Hp / s.MaxHp, 0x3fd27c);
            Label(small, (int)Math.Ceiling(p.Hp) + " / " + (int)Math.Ceiling(s.MaxHp), bx + bw / 2, 44, TextCol, 1, 1);
            Bar(bx, 66, bw, 9, p.Xp / p.XpNext, Mint);
            if (p.StageIndex + 1 < p.Stages.Length) Label(small, "Evolves at Lv " + p.Stages[p.StageIndex + 1].Level, bx, 77, Muted, 0, 1);
            else if (p.Partner.Forms != null && p.StageIndex == 0) Label(small, "Changes form at Lv " + p.Partner.FormLevel, bx, 77, Muted, 0, 1);
            else if (p.SecondElem != null) Label(small, Data.Elements[p.Elem].Name + " / " + Data.Elements[p.SecondElem].Name, bx, 77, Muted, 0, 1);
            List<Drone> allies = p.Allies();
            for (int k = 0; k < allies.Count; k++)
            {
                float ax = 100 + k * 150;
                Icon(run.Atlas.Frame(allies[k].FrameId), ax, 96, 32);
                Label(small, allies[k].Name, ax + 34, 98, TextCol, 0, 1);
            }

            // Timer and zone.
            float cx = w / 2;
            string clock = Clock(d.Phase == Phase.Zone ? M.Max(0, d.T) : run.Time);
            Label(large, clock, cx, 6, TextCol, 1, 3);
            string diffLabel = run.DifficultyIndex > 0 ? " · " + run.Diff.Name : "";
            Label(small, (run.Mode == GameMode.Survival ? "Survival" : "Zone " + run.Zone + "/" + Data.ZoneCount) + " · " + run.Terrain.Biome.Name + diffLabel, cx, 60, Muted, 1);
            string wave = d.Phase == Phase.Survive ? "Next boss in " + Clock(d.NextBossT)
                : run.Mode == GameMode.Survival ? "Defeat the milestone boss"
                : d.Phase == Phase.Zone ? "Guardian at 0:00, or challenge it early"
                : d.Phase == Phase.Guardian ? "Defeat the guardian"
                : d.Phase == Phase.Final ? "Final battle"
                : d.Phase == Phase.Swarm ? "Final swarm: reach the portal and press E"
                : "Zone clear";
            Label(small, wave, cx, 80, Gold, 1);
            float wildDist = run.Capture.State == CaptureState.Roaming ? run.Capture.Distance() : -1;
            if (wildDist >= 0) Label(small, "Wild " + run.Capture.WildName + ": " + (int)wildDist + " m", cx, bossName != null ? 156 : 100, Mint, 1);

            // Map and counters.
            const float mapSize = 160;
            DrawMinimap(w - mapSize - 16, 14, mapSize);
            Label(medium, run.Money + " gold · " + run.Kills + " KOs", w - 16, mapSize + 22, TextCol, 2);
            if (run.Streak >= 5) Label(large, "x" + run.Streak, w - 16, mapSize + 52, TextCol, 2, 3, 1, Ember);

            // Move slots with cooldown wipes.
            const float slot = 76, gap = 6;
            float sy = h - slot - 14;
            for (int k = 0; k < 4; k++)
            {
                float sx = 16 + k * (slot + gap);
                if (k >= p.Moves.Count)
                {
                    bool locked = k >= s.MoveSlots;
                    Rect(sx, sy, slot, slot, locked ? Night : Panel, locked ? 0.35f : 0.6f);
                    Border(sx, sy, slot, slot, 2, Line, locked ? 0.4f : 1);
                    if (locked) Label(small, "Lab", sx + slot / 2, sy + (slot - small.LineHeight) / 2, Muted, 1, 0, 0.7f);
                    continue;
                }
                Move m = p.Moves[k];
                Rect(sx, sy, slot, slot, Ink);
                Rect(sx + 3, sy + 3, slot - 6, slot - 6, Data.ElemColor(m.Def.Elem));
                float cd = m.MaxCd > 0 ? M.Clamp(m.CdLeft / m.MaxCd, 0, 1) : 0;
                Rect(sx + 3, sy + 3, slot - 6, (slot - 6) * cd, 0x0f081e, 0.62f);
                Wrapped(small, m.Def.Name, sx + 6, sy + slot - 44, slot - 10, Ink);
                string lv = (m.Def.Kind == Kind.Fusion ? "★" : "") + m.Level;
                Label(small, lv, sx + slot - 7, sy + 4, Ink, 2, 0);
            }
            float ux = 16 + 4 * (slot + gap) + 36, uy = sy + slot / 2;
            Disc(ux, uy, 34, Ink);
            if (p.Ult >= 1) Disc(ux, uy, 34, Mint, 0.35f + M.Sin(run.Time * 6) * 0.15f);
            Disc(ux, uy, 31, Mint, 1, p.Ult);
            Disc(ux, uy, 24, Panel);
            Label(medium, "R", ux, uy - medium.LineHeight / 2, TextCol, 1, 0);

            // Boss bar.
            if (bossName != null)
            {
                float bossW = M.Min(520, w * 0.7f);
                Label(medium, bossName, cx, 102, TextCol, 1);
                int bi = d.BossIndex();
                float fill = bi >= 0 ? run.Enemies.Hp[bi] / run.Enemies.MaxHp[bi] : 0;
                Bar(cx - bossW / 2, 134, bossW, 18, fill, Danger);
            }

            // Prompts and messages.
            string prompt = run.Capture.Prompt();
            if (prompt.Length == 0 && run.Pickups.NearPortal(run.Player.X, run.Player.Z)) prompt = "E: enter the portal to zone " + (run.Zone + 1);
            if (prompt.Length == 0) prompt = run.Encounters.Prompt();
            if (prompt.Length > 0) Pill(prompt, cx, h - 120, TextCol);
            if (toastT > 0) Pill(toastText, cx, h - 160, Gold);
            if (bannerT > 0 && run.State != GameState.Dead && run.State != GameState.Won)
            {
                float t = BannerLife - bannerT;
                float a = t < 0.26f ? t / 0.26f : bannerT < 0.5f ? bannerT / 0.5f : 1;
                float by = h * 0.3f + (t < 0.26f ? (t / 0.26f - 1) * 14 : 0);
                Label(large, bannerTitle, cx, by, TextCol, 1, 4, a);
                Label(small, bannerSub, cx, by + large.LineHeight, Gold, 1, 2, a);
            }

            // Red screen edges when hit.
            if (hitT > 0)
            {
                float a = hitT / 0.45f * 0.75f;
                float band = M.Min(w, h) * 0.18f;
                Edge(0, 0, w, band, a, true, false);
                Edge(0, h - band, w, band, a, true, true);
                Edge(0, 0, band, h, a, false, false);
                Edge(w - band, 0, band, h, a, false, true);
            }
        }

        void Pill(string text, float cx, float y, uint color)
        {
            float tw = small.Measure(text);
            Rect(cx - tw / 2 - 14, y, tw + 28, small.LineHeight + 12, Night, 0.88f);
            Border(cx - tw / 2 - 14, y, tw + 28, small.LineHeight + 12, 2, Line);
            Label(small, text, cx, y + 6, color, 1, 0);
        }

        // A gradient strip that fades from the screen edge inward.
        void Edge(float x, float y, float ew, float eh, float a, bool horizontal, bool flip)
        {
            var c = new Col(Danger);
            Gl.Begin(Gl.QUADS);
            float a0 = flip ? 0 : a, a1 = flip ? a : 0;
            Gl.Color4(c.R, c.G, c.B, a0); Gl.Vertex(x, y, 0);
            Gl.Color4(c.R, c.G, c.B, horizontal ? a0 : a1); Gl.Vertex(x + ew, y, 0);
            Gl.Color4(c.R, c.G, c.B, a1); Gl.Vertex(x + ew, y + eh, 0);
            Gl.Color4(c.R, c.G, c.B, horizontal ? a1 : a0); Gl.Vertex(x, y + eh, 0);
            Gl.End();
        }

        void DrawCard(Card card, int k, float x, float y, float cw, float ch)
        {
            bool over = !card.Disabled && Hover(x, y, cw, ch);
            float lift = over ? -3 : 0;
            float alpha = card.Disabled ? 0.55f : 1;
            Rect(x, y + 5, cw, ch, 0x0d081c, alpha);
            Rect(x, y + lift, cw, ch, Panel, alpha);
            Border(x, y + lift, cw, ch, 3, Banishing ? Danger : card.Color, alpha);
            Label(small, card.Tag.ToUpper(), x + 14, y + lift + 12, card.Color, 0, 0, alpha);
            Label(small, (k + 1).ToString(), x + cw - 12, y + lift + 12, Muted, 2, 0);
            Wrapped(medium, card.Title, x + 14, y + lift + 34, cw - 28, TextCol);
            Wrapped(small, card.Disabled ? card.Desc + " (not enough gold)" : card.Desc, x + 14, y + lift + 90, cw - 28, Muted);
            if (Clicked(x, y, cw, ch)) ChooseCard(k);
        }

        void DrawChoice()
        {
            Choice o = Opts;
            if (o == null) return;
            Dim();
            float cx = w / 2;
            float y = h * 0.14f;
            Label(large, o.Title, cx, y, TextCol, 1, 3);
            y += large.LineHeight + 2;
            Label(small, Banishing ? "Choose a card to remove for the rest of the run" : o.Sub, cx, y, TextCol, 1);
            y += small.LineHeight + 20;
            const float cw = 280, ch = 190, gap = 14;
            int n = o.Cards.Count;
            float x = cx - (n * cw + (n - 1) * gap) / 2;
            // Snapshot: choosing a card can close the choice or replace the list.
            List<Card> cards = o.Cards;
            for (int i = 0; i < n && i < cards.Count; i++) DrawCard(cards[i], i, x + i * (cw + gap), y, cw, ch);
            y += ch + 28;
            if (run.State != GameState.Choice || Opts == null) return;
            if (o.Tools)
            {
                Player p = run.Player;
                const float bw = 190, bh = 38;
                float bx = cx - (3 * bw + 2 * 10) / 2;
                if (Button("Refresh (" + p.Refreshes + ") · R", bx, y, bw, bh, false, p.Refreshes > 0)) run.UseRefresh();
                else if (Button("Skip for gold (" + p.Skips + ") · X", bx + bw + 10, y, bw, bh, false, p.Skips > 0)) run.UseSkip();
                else if (Button("Banish (" + p.Banishes + ") · B", bx + (bw + 10) * 2, y, bw, bh, false, p.Banishes > 0)) ToggleBanish();
            }
            else if (o.CloseLabel != null)
            {
                float tw = small.Measure(o.CloseLabel) + 40;
                if (Button(o.CloseLabel, cx - tw / 2, y, tw, 38, false)) CloseButton();
            }
        }

        void BuildColumn(string title, List<string> rows, float x, float y)
        {
            Label(medium, title, x, y, Gold);
            y += medium.LineHeight + 2;
            if (rows.Count == 0) rows.Add("None yet");
            foreach (string row in rows)
            {
                Label(small, row, x, y, TextCol, 0, 1);
                y += small.LineHeight;
            }
        }

        // The pause screen doubles as a build sheet: moves, vitamins and items.
        void DrawPause()
        {
            Dim(0.72f);
            Player p = run.Player;
            Stats s = p.Stats;
            float cx = w / 2;
            float y = 112;
            Label(large, "Paused", cx, y, TextCol, 1, 3);
            y += large.LineHeight + 4;
            string heat = run.Heat > 0 ? " · Difficulty +" + Math.Round(run.Heat * 100) + "%" : "";
            Label(small, "Might x" + s.Might.ToString("0.00") + " · Focus x" + s.Focus.ToString("0.00") + " · Armor " + Math.Round(s.Armor) + " · Will " + Math.Round(s.Will) + " · Attack speed x" + s.Haste.ToString("0.00") + " · Area x" + s.Area.ToString("0.00") + " · Crit " + Math.Round(s.Crit * 100) + "%" + heat, cx, y, TextCol, 1);
            y += small.LineHeight + 22;

            var moves = new List<string>();
            foreach (Move m in p.Moves) moves.Add(m.Def.Name + (m.Def.Kind == Kind.Fusion ? " Star " : " Lv ") + m.Level + " · " + (m.Def.Cd * m.Mods.Cd / s.Haste).ToString("0.00") + "s");
            var vitamins = new List<string>();
            foreach (PassiveDef d in Data.Passives)
            {
                int n;
                if (p.Picks.TryGetValue(d.Id, out n)) vitamins.Add(d.Name + " " + n);
            }
            var items = new List<string>();
            foreach (ItemDef d in Data.Items)
            {
                int n = p.ItemCount(d.Id);
                if (n > 0) items.Add(d.Name + " x" + n);
            }
            const float colW = 270;
            float x = cx - colW * 1.5f;
            BuildColumn("Moves", moves, x, y);
            BuildColumn("Vitamins", vitamins, x + colW, y);
            BuildColumn("Items", items, x + colW * 2, y);
            var allyRows = new List<string>();
            foreach (Drone a in p.Allies()) allyRows.Add(a.Name + " · " + a.BonusText);
            BuildColumn("Allies", allyRows, x + colW, y + 200);

            float by = h - 90;
            if (Button("Resume", cx - 170, by, 160, 46, true)) run.Resume();
            if (Button("Quit to menu", cx + 10, by, 160, 46, false)) Program.Restart = true;
        }

        void DrawEnd()
        {
            Dim();
            float cx = w / 2;
            float y = h * 0.34f;
            Label(large, run.Mode == GameMode.Survival ? "You survived " + Clock(run.Time) : run.State == GameState.Won ? "You cleared the tower" : "You fell", cx, y, TextCol, 1, 3);
            y += large.LineHeight + 8;
            string where = run.Mode == GameMode.Survival ? "Best " + Clock(run.Profile.Get("survivalBest")) : "Zone " + run.Zone + " of " + Data.ZoneCount;
            Label(small, where + " · " + run.Diff.Name + ", level " + run.Player.Level + ", " + run.Kills + " knockouts in " + Clock(run.Time) + ".", cx, y, TextCol, 1);
            y += small.LineHeight + 8;
            Label(small, "+" + run.RunShards + " shards, for " + run.Profile.Get("shards") + " in total. Spend them in the Lab.", cx, y, Mint, 1);
            y += small.LineHeight + 12;
            foreach (string news in run.RunNews)
            {
                Label(small, news, cx, y, Gold, 1);
                y += small.LineHeight + 2;
            }
            y += 14;
            if (Button("Back to roster", cx - 100, y, 200, 46, true)) Program.Restart = true;
        }
    }
}
