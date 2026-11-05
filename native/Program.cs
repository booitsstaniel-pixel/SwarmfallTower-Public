using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace Swarmfall
{
    // The game window. It owns the OpenGL context and turns Windows events into game input.
    sealed class GameWindow : Form
    {
        const int PFD_DRAW_TO_WINDOW = 4, PFD_SUPPORT_OPENGL = 32, PFD_DOUBLEBUFFER = 1;
        IntPtr dc, rc;
        public Input Target;
        bool fullscreen;
        FormBorderStyle savedStyle;
        FormWindowState savedState;
        public bool Closed_;
        bool cursorHidden;

        public GameWindow()
        {
            Text = "Swarmfall Tower";
            ClientSize = new Size(1280, 760);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.Black;
            SetStyle(ControlStyles.Opaque | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
            KeyPreview = true;
            FormClosed += (s, e) => Closed_ = true;
        }

        public void InitGl()
        {
            dc = Native.GetDC(Handle);
            var pfd = new PixelFormatDescriptor
            {
                nSize = (ushort)Marshal.SizeOf(typeof(PixelFormatDescriptor)),
                nVersion = 1,
                dwFlags = PFD_DRAW_TO_WINDOW | PFD_SUPPORT_OPENGL | PFD_DOUBLEBUFFER,
                cColorBits = 32,
                cDepthBits = 24,
                cAlphaBits = 8,
            };
            int format = Native.ChoosePixelFormat(dc, ref pfd);
            if (format == 0 || !Native.SetPixelFormat(dc, format, ref pfd)) throw new InvalidOperationException("This PC's graphics driver did not offer a usable OpenGL mode.");
            rc = Gl.WglCreateContext(dc);
            if (rc == IntPtr.Zero || !Gl.WglMakeCurrent(dc, rc)) throw new InvalidOperationException("Could not start OpenGL.");
            IntPtr swap = Gl.WglGetProcAddress("wglSwapIntervalEXT");
            if (swap != IntPtr.Zero) ((Native.SwapInterval)Marshal.GetDelegateForFunctionPointer(swap, typeof(Native.SwapInterval)))(1);
        }

        public void Present() { Native.SwapBuffers(dc); }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.F11)
            {
                ToggleFullscreen();
            }
            else if (Target != null)
            {
                Target.KeyDown(e.KeyCode);
            }
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (Target != null) Target.KeyUp(e.KeyCode);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (Target == null) return;
            Target.MouseX = e.X;
            Target.MouseY = e.Y;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (Target == null || e.Button != MouseButtons.Left) return;
            Target.MouseX = e.X;
            Target.MouseY = e.Y;
            Target.Clicked = true;
        }

        protected override void OnDeactivate(EventArgs e)
        {
            if (Target != null) Target.ClearKeys();
            base.OnDeactivate(e);
        }

        void ToggleFullscreen()
        {
            fullscreen = !fullscreen;
            if (fullscreen)
            {
                savedStyle = FormBorderStyle;
                savedState = WindowState;
                WindowState = FormWindowState.Normal;
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Maximized;
            }
            else
            {
                FormBorderStyle = savedStyle;
                WindowState = savedState;
            }
        }

        // While playing, the cursor is hidden and held at the window centre so the mouse only turns the camera.
        public void UpdateMouseLook(Run run)
        {
            bool look = run.WantsMouseLook && Form.ActiveForm == this && WindowState != FormWindowState.Minimized;
            if (look != cursorHidden)
            {
                cursorHidden = look;
                if (look)
                {
                    Cursor.Hide();
                    Cursor.Position = PointToScreen(new Point(ClientSize.Width / 2, ClientSize.Height / 2));
                }
                else
                {
                    Cursor.Show();
                }
                return;
            }
            if (!look) return;
            Point center = PointToScreen(new Point(ClientSize.Width / 2, ClientSize.Height / 2));
            Point now = Cursor.Position;
            run.Input.LookX += now.X - center.X;
            run.Input.LookY += now.Y - center.Y;
            Cursor.Position = center;
        }
    }

    static class Program
    {
        public static bool Restart;

        [STAThread]
        static int Main(string[] args)
        {
            try
            {
                int test = Array.IndexOf(args, "--selftest");
                if (test >= 0) return SelfTest.Run_(args[test + 1]);
                int balance = Array.IndexOf(args, "--balance");
                if (balance >= 0) return SelfTest.RunBalance(args[balance + 1]);
                int sheet = Array.IndexOf(args, "--diagnostic-sheet");
                if (sheet >= 0)
                {
                    SelfTest.WriteDiagnosticSheet(args[sheet + 1]);
                    return 0;
                }
                PlayGame(args);
                return 0;
            }
            catch (Exception e)
            {
                string log = Path.Combine(Path.GetTempPath(), "swarmfall-crash.txt");
                File.WriteAllText(log, e.ToString());
                MessageBox.Show("Swarmfall Tower hit a problem and has to close.\n\n" + e.Message + "\n\nDetails were saved to:\n" + log, "Swarmfall Tower", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        static string Arg(string[] args, string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        static Partner PartnerArg(string[] args)
        {
            string id = Arg(args, "--partner");
            foreach (Partner p in Data.Partners) if (p.Id == id) return p;
            return Data.Partners[0];
        }

        // --shots and --turnaround are for testing: the game plays itself, saves images, then exits.
        static void PlayGame(string[] args)

        {
            Application.EnableVisualStyles();
            using (var window = new GameWindow())
            {
                window.Show();
                window.InitGl();
                var audio = new Audio();
                // Test scripts get a throwaway profile, so they can never change the player's real save.
                bool scripted = Arg(args, "--shots") != null || Arg(args, "--turnaround") != null || Arg(args, "--evolution-shots") != null || Arg(args, "--mode-shots") != null || Arg(args, "--starter-shots") != null;
                Profile profile = scripted ? Profile.InMemory() : Profile.Load(Arg(args, "--save") ?? Profile.DefaultPath());
                // Other scripts skip the first-launch window and can show any starter.
                if (scripted && Arg(args, "--starter-shots") == null)
                {
                    profile.ChooseStarter(Data.Partners[0]);
                    foreach (Partner p in Data.Partners) if (p.Starter) profile.AddToRoster(p);
                    profile.AddToRoster(PartnerArg(args));
                }
                do
                {
                    Restart = false;
                    PlayOnce(window, args, profile);
                } while (Restart && !window.Closed_);
                audio.Dispose();
            }
        }

        static void PlayOnce(GameWindow window, string[] args, Profile profile)
        {
            var run = new Run(profile);
            var renderer = new Renderer(run);
            run.Hud.InitGraphics(renderer);
            window.Target = run.Input;
            string shots = Arg(args, "--shots"), turnaround = Arg(args, "--turnaround"), evolution = Arg(args, "--evolution-shots");
            string modeShots = Arg(args, "--mode-shots"), starterShots = Arg(args, "--starter-shots");
            IFrameScript script = starterShots != null ? new StarterShotScript(run, starterShots)
                : modeShots != null ? (IFrameScript)new ModeShotScript(run, modeShots)
                : shots != null ? (IFrameScript)new ShotScript(run, shots, PartnerArg(args))
                : evolution != null ? (IFrameScript)new EvolutionShotScript(run, evolution, PartnerArg(args))
                : turnaround != null ? (IFrameScript)new TurnaroundScript(run, turnaround, PartnerArg(args), int.Parse(Arg(args, "--stage") ?? "0"))
                : null;
            if (script == null) run.Audio.Start();


            var clock = Stopwatch.StartNew();
            double last = 0;
            while (!window.Closed_ && !Restart)
            {
                Application.DoEvents();
                if (window.Closed_) break;
                if (window.WindowState == FormWindowState.Minimized)
                {
                    if (run.State == GameState.Play) run.Pause();
                    Thread.Sleep(30);
                    last = clock.Elapsed.TotalSeconds;
                    continue;
                }
                double now = clock.Elapsed.TotalSeconds;
                float dt = (float)(now - last);
                last = now;
                if (script != null)
                {
                    dt = 1 / 60f;
                    if (script.BeforeFrame()) window.Close();
                }
                else
                {
                    window.UpdateMouseLook(run);
                    if (Form.ActiveForm != window && run.State == GameState.Play) run.Pause();
                }

                run.Frame(dt);
                renderer.Width = window.ClientSize.Width;
                renderer.Height = window.ClientSize.Height;
                renderer.Render();
                run.Hud.Draw(renderer.Width, renderer.Height);
                if (script != null) script.AfterFrame(renderer.Width, renderer.Height);
                window.Present();
                run.Input.EndFrame();
            }
            run.Audio.Dispose();
            profile.Save();
            if (window.Visible) Cursor.Show();
        }
    }
}
