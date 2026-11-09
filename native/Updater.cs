using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Swarmfall
{
    enum UpdateState { Idle, Checking, UpToDate, Available, Downloading, Ready, Failed }

    // Updates Swarmfall.exe from the public release repository. Only goes online when the player asks.
    // The repository holds latest.txt (release number, size and SHA-256 of Swarmfall.exe), written by
    // tools/publish-public.ps1. A running exe can't be overwritten but can be renamed, so installing renames
    // this exe to Swarmfall.exe.old, moves the download into its place and starts it. Saves live in
    // %APPDATA% and are never touched.
    static class Updater
    {
        // Raise this by one for every public release.
        public const int Release = 7;
        public const string DefaultSource = "https://raw.githubusercontent.com/booitsstaniel-pixel/SwarmfallTower-Public/main/";

        public static string Source = DefaultSource;
        public static volatile UpdateState State = UpdateState.Idle;
        public static volatile float Progress;
        public static volatile int LatestRelease;
        public static volatile string Error;
        public static bool JustUpdated;
        static string latestSha;
        static long latestSize;

        static string ExePath { get { return Process.GetCurrentProcess().MainModule.FileName; } }
        static string DownloadPath { get { return ExePath + ".download"; } }
        static string OldPath { get { return ExePath + ".old"; } }

        public static bool Busy { get { return State == UpdateState.Checking || State == UpdateState.Downloading; } }

        // latest.txt is "key=value" lines: release, size and sha256 are required.
        public static bool TryParse(string text, out int release, out long size, out string sha)
        {
            release = 0;
            size = 0;
            sha = null;
            if (text == null) return false;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim(), value = line.Substring(eq + 1).Trim();
                if (key == "release") int.TryParse(value, out release);
                else if (key == "size") long.TryParse(value, out size);
                else if (key == "sha256") sha = value.ToLowerInvariant();
            }
            return release > 0 && size > 0 && sha != null && sha.Length == 64;
        }

        public static void CheckAsync()
        {
            if (Busy) return;
            State = UpdateState.Checking;
            Error = null;
            Start(() =>
            {
                Check();
            });
        }

        public static void DownloadAsync()
        {
            if (Busy || State != UpdateState.Available) return;
            State = UpdateState.Downloading;
            Progress = 0;
            Start(() =>
            {
                Download();
            });
        }

        static void Start(Action work)
        {
            var thread = new Thread(() =>
            {
                try { work(); }
                catch (Exception e) { Fail(e); }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        static void Fail(Exception e)
        {
            if (e is UnauthorizedAccessException) Error = "Can't write to the game's folder. Move Swarmfall.exe to a folder you own, such as Documents, and try again.";
            else if (e is WebException) Error = "Couldn't reach GitHub. Check your internet connection and try again.";
            else Error = e.Message;
            State = UpdateState.Failed;
            try { if (File.Exists(DownloadPath)) File.Delete(DownloadPath); } catch { }
        }

        static void Check()
        {
            int release;
            long size;
            string sha;
            if (!TryParse(Encoding.UTF8.GetString(Fetch(Url("latest.txt"), null)), out release, out size, out sha))
                throw new InvalidDataException("The update information on GitHub couldn't be read.");
            LatestRelease = release;
            latestSize = size;
            latestSha = sha;
            State = release > Release ? UpdateState.Available : UpdateState.UpToDate;
        }

        static void Download()
        {
            byte[] data = Fetch(Url("Swarmfall.exe"), latestSize);
            if (data.Length != latestSize || Sha256(data) != latestSha)
                throw new InvalidDataException("The downloaded game didn't match its checksum, so it wasn't installed. Try again later.");
            File.WriteAllBytes(DownloadPath, data);
            State = UpdateState.Ready;
        }

        // Swaps the downloaded exe in and starts it. Call from the UI thread; the caller should exit afterwards.
        public static void InstallAndRestart(bool restart)
        {
            if (State != UpdateState.Ready) return;
            string exe = ExePath;
            if (File.Exists(OldPath)) File.Delete(OldPath);
            File.Move(exe, OldPath);
            try { File.Move(DownloadPath, exe); }
            catch
            {
                File.Move(OldPath, exe);
                throw;
            }
            if (restart) Process.Start(new ProcessStartInfo(exe, "--updated") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) });
        }

        // The previous exe is left behind as Swarmfall.exe.old; remove it once that process has exited.
        public static void CleanUpAfterUpdate()
        {
            Start(() =>
            {
                for (int i = 0; i < 20 && File.Exists(OldPath); i++)
                {
                    try { File.Delete(OldPath); }
                    catch (IOException) { Thread.Sleep(500); }
                    catch (UnauthorizedAccessException) { Thread.Sleep(500); }
                }
            });
        }

        // Headless update for `Swarmfall.exe --update`. Returns 0 when updated, 2 when already up to date, 1 on failure.
        public static int RunHeadless(bool restart)
        {
            try
            {
                State = UpdateState.Checking;
                Check();
                if (State == UpdateState.UpToDate) return 2;
                State = UpdateState.Downloading;
                Download();
                InstallAndRestart(restart);
                return 0;
            }
            catch (Exception e)
            {
                Fail(e);
                return 1;
            }
        }

        static string Url(string file)
        {
            string url = Source + file;
            // Ask GitHub's cache for a fresh copy.
            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url += "?t=" + DateTime.UtcNow.Ticks;
            return url;
        }

        static byte[] Fetch(string url, long? expected)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2, which GitHub requires.
            WebRequest request = WebRequest.Create(url);
            request.Timeout = 30000;
            var http = request as HttpWebRequest;
            if (http != null) http.UserAgent = "SwarmfallTower-Updater";
            using (WebResponse response = request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (var memory = new MemoryStream())
            {
                var buffer = new byte[65536];
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    memory.Write(buffer, 0, read);
                    if (expected.HasValue && expected.Value > 0) Progress = Math.Min(1f, (float)memory.Length / expected.Value);
                    if (memory.Length > 64L * 1024 * 1024) throw new InvalidDataException("The download was far larger than expected, so it was stopped.");
                }
                return memory.ToArray();
            }
        }

        public static string Sha256(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                var text = new StringBuilder();
                foreach (byte b in sha.ComputeHash(data)) text.Append(b.ToString("x2"));
                return text.ToString();
            }
        }
    }
}
