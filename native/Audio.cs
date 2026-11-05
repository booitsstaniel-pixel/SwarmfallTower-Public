using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Swarmfall
{
    // Every sound is synthesised and mixed on a background thread through waveOut, so there are no audio files.
    sealed class Audio : IDisposable
    {
        const int Rate = 22050;
        const int BufferSamples = 882;
        const int BufferCount = 4;
        const uint WHDR_DONE = 1;
        const int HeaderSize = 48;
        const int FlagsOffset = 24;

        enum Wave { Square, Saw, Triangle }

        sealed class Voice
        {
            public Wave Type;
            public float From, To, Dur, Vol, Delay, T, Phase;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WaveHdr
        {
            public IntPtr lpData;
            public uint dwBufferLength, dwBytesRecorded;
            public IntPtr dwUser;
            public uint dwFlags, dwLoops;
            public IntPtr lpNext, reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WaveFormat
        {
            public ushort wFormatTag, nChannels;
            public uint nSamplesPerSec, nAvgBytesPerSec;
            public ushort nBlockAlign, wBitsPerSample, cbSize;
        }

        [DllImport("winmm.dll")] static extern int waveOutOpen(out IntPtr h, uint device, ref WaveFormat f, IntPtr cb, IntPtr inst, uint flags);
        [DllImport("winmm.dll")] static extern int waveOutPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutWrite(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveOutReset(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveOutClose(IntPtr h);

        readonly List<Voice> voices = new List<Voice>();
        readonly object gate = new object();
        IntPtr device = IntPtr.Zero;
        Thread thread;
        volatile bool running;
        DateTime lastHit = DateTime.MinValue;
        public bool Muted;

        // Starts playback. If the machine has no audio device the game simply stays silent.
        public void Start()
        {
            var format = new WaveFormat { wFormatTag = 1, nChannels = 1, nSamplesPerSec = Rate, nAvgBytesPerSec = Rate * 2, nBlockAlign = 2, wBitsPerSample = 16 };
            try
            {
                if (waveOutOpen(out device, 0xFFFFFFFF, ref format, IntPtr.Zero, IntPtr.Zero, 0) != 0) return;
            }
            catch (DllNotFoundException)
            {
                return;
            }
            running = true;
            thread = new Thread(MixLoop);
            thread.IsBackground = true;
            thread.Start();
        }

        void MixLoop()
        {
            var headers = new IntPtr[BufferCount];
            var data = new IntPtr[BufferCount];
            var samples = new short[BufferSamples];
            for (int i = 0; i < BufferCount; i++)
            {
                data[i] = Marshal.AllocHGlobal(BufferSamples * 2);
                headers[i] = Marshal.AllocHGlobal(HeaderSize);
                Marshal.StructureToPtr(new WaveHdr { lpData = data[i], dwBufferLength = BufferSamples * 2 }, headers[i], false);
                waveOutPrepareHeader(device, headers[i], HeaderSize);
                Mix(samples);
                Marshal.Copy(samples, 0, data[i], BufferSamples);
                waveOutWrite(device, headers[i], HeaderSize);
            }
            while (running)
            {
                bool wrote = false;
                for (int i = 0; i < BufferCount; i++)
                {
                    if (((uint)Marshal.ReadInt32(headers[i], FlagsOffset) & WHDR_DONE) == 0) continue;
                    Mix(samples);
                    Marshal.Copy(samples, 0, data[i], BufferSamples);
                    waveOutWrite(device, headers[i], HeaderSize);
                    wrote = true;
                }
                if (!wrote) Thread.Sleep(4);
            }
        }

        void Mix(short[] output)
        {
            lock (gate)
            {
                for (int n = 0; n < output.Length; n++)
                {
                    float sum = 0;
                    for (int v = 0; v < voices.Count; v++)
                    {
                        Voice x = voices[v];
                        if (x.Delay > 0)
                        {
                            x.Delay -= 1f / Rate;
                            continue;
                        }
                        float t = x.T / x.Dur;
                        float freq = x.From * M.Pow(x.To / x.From, t);
                        x.Phase += freq / Rate;
                        x.Phase -= (float)Math.Floor(x.Phase);
                        float wave = x.Type == Wave.Square ? (x.Phase < 0.5f ? 1 : -1)
                            : x.Type == Wave.Saw ? x.Phase * 2 - 1
                            : 1 - 4 * M.Abs(x.Phase - 0.5f);
                        sum += wave * x.Vol * M.Pow(0.001f, t);
                        x.T += 1f / Rate;
                    }
                    output[n] = (short)(M.Clamp(sum * 0.28f, -1, 1) * 32000);
                }
                voices.RemoveAll(x => x.T >= x.Dur);
            }
        }

        void Tone(float from, float dur, Wave type, float to, float vol, float delay = 0)
        {
            if (!running || Muted) return;
            lock (gate)
            {
                if (voices.Count < 24) voices.Add(new Voice { From = from, To = Math.Max(20, to), Dur = dur, Type = type, Vol = vol, Delay = delay });
            }
        }

        public void Shoot() { Tone(620, 0.08f, Wave.Square, 280, 0.09f); }
        public void Hit()
        {
            if ((DateTime.UtcNow - lastHit).TotalMilliseconds < 45) return;
            lastHit = DateTime.UtcNow;
            Tone(180, 0.06f, Wave.Saw, 70, 0.14f);
        }
        public void Jump(float pitch = 1) { Tone(300 * pitch, 0.14f, Wave.Triangle, 620 * pitch, 0.14f); }
        public void Whoosh() { Tone(900, 0.2f, Wave.Saw, 160, 0.1f); }
        public void Pickup() { Tone(980, 0.06f, Wave.Triangle, 1400, 0.07f); }
        public void Coin() { Tone(1200, 0.05f, Wave.Square, 1200, 0.07f); Tone(1600, 0.09f, Wave.Square, 1600, 0.07f, 0.05f); }
        public void Hurt() { Tone(240, 0.2f, Wave.Saw, 60, 0.28f); }
        public void Boom(float size = 1) { Tone(130, 0.35f * size, Wave.Saw, 30, 0.32f); }
        public void Zap() { Tone(1100, 0.12f, Wave.Saw, 240, 0.14f); }
        public void LevelUp()
        {
            float[] notes = { 523, 659, 784, 1047 };
            for (int i = 0; i < notes.Length; i++) Tone(notes[i], 0.16f, Wave.Triangle, notes[i], 0.16f, i * 0.07f);
        }
        public void Alarm() { Tone(330, 0.22f, Wave.Square, 330, 0.14f); Tone(330, 0.22f, Wave.Square, 330, 0.14f, 0.28f); }
        public void Denied() { Tone(150, 0.14f, Wave.Square, 150, 0.14f); }

        public void Dispose()
        {
            if (device == IntPtr.Zero) return;
            running = false;
            if (thread != null) thread.Join(200);
            waveOutReset(device);
            waveOutClose(device);
            device = IntPtr.Zero;
        }
    }
}
