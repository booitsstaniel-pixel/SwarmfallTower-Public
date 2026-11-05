using System;
using System.Runtime.InteropServices;

namespace Swarmfall
{
    // The parts of OpenGL 1.1 and WGL this game uses. opengl32.dll ships with Windows.
    static class Gl
    {
        const string L = "opengl32.dll";
        public const uint COLOR_BUFFER_BIT = 0x4000, DEPTH_BUFFER_BIT = 0x100;
        public const uint TRIANGLES = 4, QUADS = 7;
        public const uint DEPTH_TEST = 0x0B71, BLEND = 0x0BE2, ALPHA_TEST = 0x0BC0, TEXTURE_2D = 0x0DE1, FOG = 0x0B60, CULL_FACE = 0x0B44;
        public const uint SRC_ALPHA = 0x0302, ONE_MINUS_SRC_ALPHA = 0x0303, ONE = 1;
        public const uint LEQUAL = 0x0203, GREATER = 0x0204;
        public const uint MODELVIEW = 0x1700, PROJECTION = 0x1701;
        public const uint RGBA = 0x1908, UNSIGNED_BYTE = 0x1401, FLOAT = 0x1406;
        public const uint TEXTURE_MAG_FILTER = 0x2800, TEXTURE_MIN_FILTER = 0x2801, TEXTURE_WRAP_S = 0x2802, TEXTURE_WRAP_T = 0x2803;
        public const int NEAREST = 0x2600, LINEAR = 0x2601, REPEAT = 0x2901, CLAMP = 0x2900;
        public const uint VERTEX_ARRAY = 0x8074, COLOR_ARRAY = 0x8076, TEXTURE_COORD_ARRAY = 0x8078;
        public const uint FOG_MODE = 0x0B65, FOG_START = 0x0B63, FOG_END = 0x0B64, FOG_COLOR = 0x0B66;
        public const int FOG_LINEAR = 0x2601;
        public const uint COMPILE = 0x1300;
        public const uint SMOOTH = 0x1D01;
        public const uint MAX_TEXTURE_SIZE = 0x0D33;

        [DllImport(L, EntryPoint = "glClear")] public static extern void Clear(uint mask);
        [DllImport(L, EntryPoint = "glClearColor")] public static extern void ClearColor(float r, float g, float b, float a);
        [DllImport(L, EntryPoint = "glEnable")] public static extern void Enable(uint cap);
        [DllImport(L, EntryPoint = "glDisable")] public static extern void Disable(uint cap);
        [DllImport(L, EntryPoint = "glBlendFunc")] public static extern void BlendFunc(uint s, uint d);
        [DllImport(L, EntryPoint = "glDepthFunc")] public static extern void DepthFunc(uint f);
        [DllImport(L, EntryPoint = "glDepthMask")] public static extern void DepthMask(byte flag);
        [DllImport(L, EntryPoint = "glAlphaFunc")] public static extern void AlphaFunc(uint f, float r);
        [DllImport(L, EntryPoint = "glViewport")] public static extern void Viewport(int x, int y, int w, int h);
        [DllImport(L, EntryPoint = "glMatrixMode")] public static extern void MatrixMode(uint m);
        [DllImport(L, EntryPoint = "glLoadMatrixf")] public static extern void LoadMatrix(float[] m);
        [DllImport(L, EntryPoint = "glLoadIdentity")] public static extern void LoadIdentity();
        [DllImport(L, EntryPoint = "glOrtho")] public static extern void Ortho(double l, double r, double b, double t, double n, double f);
        [DllImport(L, EntryPoint = "glGenTextures")] public static extern void GenTextures(int n, out uint id);
        [DllImport(L, EntryPoint = "glBindTexture")] public static extern void BindTexture(uint target, uint id);
        [DllImport(L, EntryPoint = "glDeleteTextures")] public static extern void DeleteTextures(int n, ref uint id);
        [DllImport(L, EntryPoint = "glTexParameteri")] public static extern void TexParameter(uint target, uint name, int value);
        [DllImport(L, EntryPoint = "glTexImage2D")] public static extern void TexImage2D(uint target, int level, int internalFormat, int w, int h, int border, uint format, uint type, byte[] data);
        [DllImport(L, EntryPoint = "glColor4f")] public static extern void Color4(float r, float g, float b, float a);
        [DllImport(L, EntryPoint = "glTexCoord2f")] public static extern void TexCoord(float u, float v);
        [DllImport(L, EntryPoint = "glVertex3f")] public static extern void Vertex(float x, float y, float z);
        [DllImport(L, EntryPoint = "glBegin")] public static extern void Begin(uint mode);
        [DllImport(L, EntryPoint = "glEnd")] public static extern void End();
        [DllImport(L, EntryPoint = "glGenLists")] public static extern uint GenLists(int n);
        [DllImport(L, EntryPoint = "glNewList")] public static extern void NewList(uint list, uint mode);
        [DllImport(L, EntryPoint = "glEndList")] public static extern void EndList();
        [DllImport(L, EntryPoint = "glCallList")] public static extern void CallList(uint list);
        [DllImport(L, EntryPoint = "glDeleteLists")] public static extern void DeleteLists(uint list, int range);
        [DllImport(L, EntryPoint = "glEnableClientState")] public static extern void EnableClientState(uint cap);
        [DllImport(L, EntryPoint = "glDisableClientState")] public static extern void DisableClientState(uint cap);
        [DllImport(L, EntryPoint = "glVertexPointer")] public static extern void VertexPointer(int size, uint type, int stride, IntPtr p);
        [DllImport(L, EntryPoint = "glTexCoordPointer")] public static extern void TexCoordPointer(int size, uint type, int stride, IntPtr p);
        [DllImport(L, EntryPoint = "glColorPointer")] public static extern void ColorPointer(int size, uint type, int stride, IntPtr p);
        [DllImport(L, EntryPoint = "glDrawArrays")] public static extern void DrawArrays(uint mode, int first, int count);
        [DllImport(L, EntryPoint = "glFogi")] public static extern void Fog(uint name, int value);
        [DllImport(L, EntryPoint = "glFogf")] public static extern void Fog(uint name, float value);
        [DllImport(L, EntryPoint = "glFogfv")] public static extern void Fog(uint name, float[] value);
        [DllImport(L, EntryPoint = "glReadPixels")] public static extern void ReadPixels(int x, int y, int w, int h, uint format, uint type, byte[] data);
        [DllImport(L, EntryPoint = "glGetIntegerv")] public static extern void GetInteger(uint name, out int value);
        [DllImport(L, EntryPoint = "glGetString")] public static extern IntPtr GetString(uint name);
        [DllImport(L, EntryPoint = "glFinish")] public static extern void Finish();

        [DllImport(L, EntryPoint = "wglCreateContext")] public static extern IntPtr WglCreateContext(IntPtr dc);
        [DllImport(L, EntryPoint = "wglMakeCurrent")] public static extern bool WglMakeCurrent(IntPtr dc, IntPtr rc);
        [DllImport(L, EntryPoint = "wglDeleteContext")] public static extern bool WglDeleteContext(IntPtr rc);
        [DllImport(L, EntryPoint = "wglGetProcAddress")] public static extern IntPtr WglGetProcAddress(string name);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PixelFormatDescriptor
    {
        public ushort nSize, nVersion;
        public uint dwFlags;
        public byte iPixelType, cColorBits, cRedBits, cRedShift, cGreenBits, cGreenShift, cBlueBits, cBlueShift, cAlphaBits, cAlphaShift;
        public byte cAccumBits, cAccumRedBits, cAccumGreenBits, cAccumBlueBits, cAccumAlphaBits, cDepthBits, cStencilBits, cAuxBuffers, iLayerType, bReserved;
        public uint dwLayerMask, dwVisibleMask, dwDamageMask;
    }

    static class Native
    {
        [DllImport("gdi32.dll")] public static extern int ChoosePixelFormat(IntPtr dc, ref PixelFormatDescriptor pfd);
        [DllImport("gdi32.dll")] public static extern bool SetPixelFormat(IntPtr dc, int format, ref PixelFormatDescriptor pfd);
        [DllImport("gdi32.dll")] public static extern bool SwapBuffers(IntPtr dc);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] public delegate bool SwapInterval(int interval);
    }
}
