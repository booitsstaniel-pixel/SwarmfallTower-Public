using System;
using System.Runtime.InteropServices;

namespace Swarmfall
{
    // A growable-free vertex buffer for quads, pinned so OpenGL can read it directly.
    sealed class Batch
    {
        readonly float[] pos, uv;
        readonly byte[] col;
        readonly int capacity;
        readonly IntPtr posPtr, uvPtr, colPtr;
        int count;

        public Batch(int quads)
        {
            capacity = quads * 4;
            pos = new float[capacity * 3];
            uv = new float[capacity * 2];
            col = new byte[capacity * 4];
            posPtr = GCHandle.Alloc(pos, GCHandleType.Pinned).AddrOfPinnedObject();
            uvPtr = GCHandle.Alloc(uv, GCHandleType.Pinned).AddrOfPinnedObject();
            colPtr = GCHandle.Alloc(col, GCHandleType.Pinned).AddrOfPinnedObject();
        }

        public void Reset() { count = 0; }

        void V(float x, float y, float z, float u, float v, byte r, byte g, byte b, byte a)
        {
            int i = count++;
            pos[i * 3] = x;
            pos[i * 3 + 1] = y;
            pos[i * 3 + 2] = z;
            uv[i * 2] = u;
            uv[i * 2 + 1] = v;
            col[i * 4] = r;
            col[i * 4 + 1] = g;
            col[i * 4 + 2] = b;
            col[i * 4 + 3] = a;
        }

        public void Quad(float x0, float y0, float z0, float x1, float y1, float z1, float x2, float y2, float z2, float x3, float y3, float z3,
            float u0, float v0, float u1, float v1, float r, float g, float b, float a)
        {
            if (count + 4 > capacity) return;
            byte br = (byte)(M.Clamp(r, 0, 1) * 255), bg = (byte)(M.Clamp(g, 0, 1) * 255), bb = (byte)(M.Clamp(b, 0, 1) * 255), ba = (byte)(M.Clamp(a, 0, 1) * 255);
            V(x0, y0, z0, u0, v1, br, bg, bb, ba);
            V(x1, y1, z1, u1, v1, br, bg, bb, ba);
            V(x2, y2, z2, u1, v0, br, bg, bb, ba);
            V(x3, y3, z3, u0, v0, br, bg, bb, ba);
        }

        public void Draw()
        {
            if (count == 0) return;
            Gl.VertexPointer(3, Gl.FLOAT, 0, posPtr);
            Gl.TexCoordPointer(2, Gl.FLOAT, 0, uvPtr);
            Gl.ColorPointer(4, Gl.UNSIGNED_BYTE, 0, colPtr);
            Gl.DrawArrays(Gl.QUADS, 0, count);
        }
    }

    // Draws the world using plain OpenGL 1.1, which any Windows graphics driver supports.
    // Hit flashes and outlines use extra atlas layers instead of shaders, drawn as separate passes.
    sealed class Renderer
    {
        const int Layer1X = Atlas.LayerSize;
        const int Layer2Y = Atlas.LayerSize;
        const float CellUv = (float)Atlas.Cell / Atlas.Size;
        const float FogNear = 40, FogFar = 110, Fov = 58;
        const float CameraClearance = 0.4f, CameraMinClip = 0.3f;
        float cameraClip = 1;
        const float GhostAlpha = 0.3f;

        readonly Run run;
        readonly uint[] atlasTextures;
        uint tileTexture, groundList, solidList;
        int builtVersion;

        readonly Batch shadows = new Batch(3000);
        // One set per atlas page. Depth testing makes the draw order between pages safe.
        readonly Batch[] outlines, sprites, flashes, ghosts;
        readonly Batch particlesNorm = new Batch(2500);
        readonly Batch particlesAdd = new Batch(3500);
        readonly Batch rings = new Batch(9000);
        readonly Batch beams = new Batch(60);

        readonly float[] proj = new float[16];
        readonly float[] view = new float[16];
        float rightX, rightY, rightZ, upX, upY, upZ, camUpX, camUpY, camUpZ, eyeX, eyeY, eyeZ;
        public int Width = 1280, Height = 800;
        Col spriteLight = new Col(1, 1, 1);
        int shotFrame, chestFrame, cursedFrame;
        readonly int discFrame, gemFrame, coinFrame, heartFrame, portalFrame, tradeFrame, riskFrame, chargeFrame, podFrame, machineFrame, fossilFrame, stoneFrame, merchantFrame, droneFrame;
        readonly int[] enemyFrames;

        public Renderer(Run run)
        {
            this.run = run;
            Atlas a = run.Atlas;
            int maxTexture;
            Gl.GetInteger(Gl.MAX_TEXTURE_SIZE, out maxTexture);
            if (maxTexture < Atlas.Size) throw new InvalidOperationException("This PC's graphics driver supports textures up to " + maxTexture + " px, but the game needs " + Atlas.Size + " px. Updating the graphics driver may fix this.");
            atlasTextures = new uint[a.PageCount];
            outlines = new Batch[a.PageCount];
            sprites = new Batch[a.PageCount];
            flashes = new Batch[a.PageCount];
            ghosts = new Batch[a.PageCount];
            for (int page = 0; page < a.PageCount; page++)
            {
                atlasTextures[page] = UploadTexture(a.Page(page), Atlas.Size, Atlas.Size, Gl.NEAREST, Gl.CLAMP);
                outlines[page] = new Batch(1500);
                sprites[page] = new Batch(4000);
                flashes[page] = new Batch(1500);
                ghosts[page] = new Batch(200);
            }

            discFrame = a.Frame("disc");
            shotFrame = a.Frame("orb");
            gemFrame = a.Frame("gem");
            coinFrame = a.Frame("coin");
            heartFrame = a.Frame("heart");
            chestFrame = a.Frame("chest");
            cursedFrame = a.Frame("cursed");
            portalFrame = a.Frame("portal");
            tradeFrame = a.Frame("altarTrade");
            riskFrame = a.Frame("altarRisk");
            chargeFrame = a.Frame("altarCharge");
            podFrame = a.Frame("pod");
            machineFrame = a.Frame("machine");
            fossilFrame = a.Frame("fossil");
            stoneFrame = a.Frame("stone");
            merchantFrame = a.Frame("merchant");
            droneFrame = a.Frame("drone");
            enemyFrames = new int[run.Enemies.Kinds.Count];
            for (int i = 0; i < enemyFrames.Length; i++) enemyFrames[i] = a.Frame(run.Enemies.Kinds[i].Id);
        }

        public uint TextureOf(int frame) { return atlasTextures[Atlas.PageOf(frame)]; }

        public static uint UploadTexture(byte[] rgba, int w, int h, int filter, int wrap)
        {
            uint id;
            Gl.GenTextures(1, out id);
            Gl.BindTexture(Gl.TEXTURE_2D, id);
            Gl.TexParameter(Gl.TEXTURE_2D, Gl.TEXTURE_MAG_FILTER, filter);
            Gl.TexParameter(Gl.TEXTURE_2D, Gl.TEXTURE_MIN_FILTER, filter);
            Gl.TexParameter(Gl.TEXTURE_2D, Gl.TEXTURE_WRAP_S, wrap);
            Gl.TexParameter(Gl.TEXTURE_2D, Gl.TEXTURE_WRAP_T, wrap);
            Gl.TexImage2D(Gl.TEXTURE_2D, 0, (int)Gl.RGBA, w, h, 0, Gl.RGBA, Gl.UNSIGNED_BYTE, rgba);
            return id;
        }

        static void SetColor(Col c) { Gl.Color4(c.R, c.G, c.B, 1); }

        // Lighting is baked into vertex colours once per triangle, which also gives the faceted look.
        static Col Light(Biome b, float nx, float ny, float nz)
        {
            Col top = new Col(b.HemiTop), bottom = new Col(b.HemiBot), sun = new Col(b.Sun);
            float h = ny * 0.5f + 0.5f;
            float d = M.Max(0, nx * 0.42f + ny * 0.84f + nz * 0.25f);
            return new Col(
                M.Lerp(bottom.R, top.R, h) * 0.72f + sun.R * d * 0.55f,
                M.Lerp(bottom.G, top.G, h) * 0.72f + sun.G * d * 0.55f,
                M.Lerp(bottom.B, top.B, h) * 0.72f + sun.B * d * 0.55f);
        }

        static Col Tint(Col c, Col light) { return new Col(M.Min(1, c.R * light.R), M.Min(1, c.G * light.G), M.Min(1, c.B * light.B)); }

        void BuildWorld()
        {
            Terrain t = run.Terrain;
            Biome b = t.Biome;
            builtVersion = run.TerrainVersion;
            if (groundList != 0)
            {
                Gl.DeleteLists(groundList, 1);
                Gl.DeleteLists(solidList, 1);
                Gl.DeleteTextures(1, ref tileTexture);
            }
            tileTexture = UploadTexture(FloorTile(b), 128, 128, Gl.NEAREST, Gl.REPEAT);

            groundList = Gl.GenLists(1);
            Gl.NewList(groundList, Gl.COMPILE);
            Gl.Begin(Gl.TRIANGLES);
            Col rim = new Col(b.Rim);
            const float repeat = 32;
            int n = Terrain.GridSize;
            for (int z = 0; z < n - 1; z++)
            {
                for (int x = 0; x < n - 1; x++)
                {
                    GroundTri(t, x, z, x, z + 1, x + 1, z, rim, repeat);
                    GroundTri(t, x + 1, z, x, z + 1, x + 1, z + 1, rim, repeat);
                }
            }
            Gl.End();
            Gl.EndList();

            solidList = Gl.GenLists(1);
            Gl.NewList(solidList, Gl.COMPILE);
            Gl.Begin(Gl.TRIANGLES);
            foreach (Collider o in t.Colliders) Column(o, o.Tall ? b.Pillar : b.Plinth, b);
            foreach (Prop p in t.Props) Rock(p, b);
            Gl.End();
            Gl.EndList();

            spriteLight = new Col(b.Sprite);
            Gl.Fog(Gl.FOG_MODE, Gl.FOG_LINEAR);
            Gl.Fog(Gl.FOG_START, FogNear);
            Gl.Fog(Gl.FOG_END, FogFar);
            Col fog = new Col(b.Fog);
            Gl.Fog(Gl.FOG_COLOR, new[] { fog.R, fog.G, fog.B, 1f });
        }

        static byte[] FloorTile(Biome b)
        {
            const int size = 128, tile = 32;
            var rng = new SeededRandom(b.FloorA);
            var px = new byte[size * size * 4];
            Col a = new Col(b.FloorA), c = new Col(b.FloorB);
            for (int y = 0; y < size; y += 4)
            {
                for (int x = 0; x < size; x += 4)
                {
                    int checker = x / tile + y / tile;
                    Col col = (checker % 2 != 0 ? a : c).Mul(0.92f + rng.Next() * 0.16f);
                    if (x % tile == 0 || y % tile == 0) col = col.Mul(0.55f);
                    for (int j = 0; j < 4; j++)
                    {
                        for (int i = 0; i < 4; i++)
                        {
                            int o = ((y + j) * size + x + i) * 4;
                            px[o] = (byte)(col.R * 255);
                            px[o + 1] = (byte)(col.G * 255);
                            px[o + 2] = (byte)(col.B * 255);
                            px[o + 3] = 255;
                        }
                    }
                }
            }
            return px;
        }

        static void GroundVertex(Terrain t, int gx, int gz, float repeat)
        {
            int span = Terrain.Half * 2;
            float wx = gx * Terrain.Step - Terrain.Half;
            float wz = gz * Terrain.Step - Terrain.Half;
            Gl.TexCoord((float)gx * Terrain.Step / span * repeat, (float)gz * Terrain.Step / span * repeat);
            Gl.Vertex(wx, t.Heights[gz * Terrain.GridSize + gx], wz);
        }

        static void GroundTri(Terrain t, int ax, int az, int bx, int bz, int cx, int cz, Col rim, float repeat)
        {
            int n = Terrain.GridSize;
            float s = Terrain.Step;
            float ay = t.Heights[az * n + ax], by = t.Heights[bz * n + bx], cy = t.Heights[cz * n + cx];
            float ux = (bx - ax) * s, uy = by - ay, uz = (bz - az) * s;
            float vx = (cx - ax) * s, vy = cy - ay, vz = (cz - az) * s;
            float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            float len = M.Hypot(nx, ny, nz);
            if (len < 0.0001f) len = 1;
            nx /= len; ny /= len; nz /= len;
            if (ny < 0) { nx = -nx; ny = -ny; nz = -nz; }
            float wx = (ax + bx + cx) / 3f * s - Terrain.Half;
            float wz = (az + bz + cz) / 3f * s - Terrain.Half;
            float r = M.Hypot(wx, wz);
            float shade = t.Shades[az * n + ax];
            Col baseCol = new Col(shade, shade, shade).Lerp(rim, M.Smoothstep(Terrain.ArenaRadius - 3, Terrain.ArenaRadius + 2, r));
            Col lit = Tint(baseCol, Light(t.Biome, nx, ny, nz)).Mul(1.25f);
            SetColor(lit);
            GroundVertex(t, ax, az, repeat);
            GroundVertex(t, bx, bz, repeat);
            GroundVertex(t, cx, cz, repeat);
        }

        static void Column(Collider o, uint color, Biome b)
        {
            const int sides = 10;
            Col c = new Col(color);
            float bottom = o.Base - 3;
            float r0 = o.R * 1.06f, r1 = o.R;
            for (int k = 0; k < sides; k++)
            {
                float a0 = (float)k / sides * M.Tau, a1 = (float)(k + 1) / sides * M.Tau;
                float mid = (a0 + a1) / 2;
                SetColor(Tint(c, Light(b, M.Cos(mid), 0, M.Sin(mid))).Mul(1.25f));
                float c0 = M.Cos(a0), s0 = M.Sin(a0), c1 = M.Cos(a1), s1 = M.Sin(a1);
                Gl.Vertex(o.X + c0 * r0, bottom, o.Z + s0 * r0);
                Gl.Vertex(o.X + c1 * r0, bottom, o.Z + s1 * r0);
                Gl.Vertex(o.X + c1 * r1, o.Top, o.Z + s1 * r1);
                Gl.Vertex(o.X + c0 * r0, bottom, o.Z + s0 * r0);
                Gl.Vertex(o.X + c1 * r1, o.Top, o.Z + s1 * r1);
                Gl.Vertex(o.X + c0 * r1, o.Top, o.Z + s0 * r1);
                SetColor(Tint(c, Light(b, 0, 1, 0)).Mul(1.25f));
                Gl.Vertex(o.X, o.Top, o.Z);
                Gl.Vertex(o.X + c0 * r1, o.Top, o.Z + s0 * r1);
                Gl.Vertex(o.X + c1 * r1, o.Top, o.Z + s1 * r1);
            }
        }

        static void Rock(Prop p, Biome b)
        {
            Col c = new Col(p.Glow ? b.Glow : b.Prop);
            float s = p.S * 0.5f;
            float top = p.Y + p.H * 0.5f, low = p.Y - p.H * 0.2f;
            for (int k = 0; k < 5; k++)
            {
                float a0 = p.Yaw + k / 5f * M.Tau, a1 = p.Yaw + (k + 1) / 5f * M.Tau;
                float mid = (a0 + a1) / 2;
                SetColor(p.Glow ? c : Tint(c, Light(b, M.Cos(mid) * 0.7f, 0.7f, M.Sin(mid) * 0.7f)).Mul(1.25f));
                Gl.Vertex(p.X, top, p.Z);
                Gl.Vertex(p.X + M.Cos(a0) * s, low, p.Z + M.Sin(a0) * s);
                Gl.Vertex(p.X + M.Cos(a1) * s, low, p.Z + M.Sin(a1) * s);
            }
        }

        // Pulls the camera in when a tall pillar stands between it and the player, then eases back out.
        void UpdateCameraClip(float tx, float ty, float tz, float dx, float dy, float dz)
        {
            float want = 1;
            float a = dx * dx + dz * dz;
            foreach (Collider o in run.Terrain.Colliders)
            {
                if (!o.Tall) continue;
                float fx = tx - o.X, fz = tz - o.Z;
                float r = o.R + CameraClearance;
                float b = fx * dx + fz * dz;
                float c = fx * fx + fz * fz - r * r;
                float disc = b * b - a * c;
                if (c <= 0 || disc <= 0 || a < 0.0001f) continue;
                float s = (-b - M.Sqrt(disc)) / a;
                if (s <= 0 || s >= want || ty + dy * s > o.Top) continue;
                want = s;
            }
            want = M.Max(want, CameraMinClip);
            cameraClip = want < cameraClip ? want : M.Lerp(cameraClip, want, 0.06f);
        }

        void SetupCamera()
        {
            float aspect = (float)Width / Math.Max(1, Height);
            float f = 1 / (float)Math.Tan(Fov * Math.PI / 360);
            const float near = 0.1f, far = 400;
            Array.Clear(proj, 0, 16);
            proj[0] = f / aspect;
            proj[5] = f;
            proj[10] = (far + near) / (near - far);
            proj[11] = -1;
            proj[14] = 2 * far * near / (near - far);

            float cp = M.Cos(run.CamPitch), sp = M.Sin(run.CamPitch);
            float tx = run.CamTx, ty = run.CamTy + 1, tz = run.CamTz;
            UpdateCameraClip(run.CamTx, ty, run.CamTz, M.Sin(run.CamYaw) * cp * run.CamDist, sp * run.CamDist, M.Cos(run.CamYaw) * cp * run.CamDist);
            float dist = run.CamDist * cameraClip;
            eyeX = run.CamTx + M.Sin(run.CamYaw) * cp * dist + run.ShakeX;
            eyeZ = run.CamTz + M.Cos(run.CamYaw) * cp * dist + run.ShakeX;
            eyeY = run.CamTy + 1 + sp * dist + run.ShakeY;

            // Lift the camera over any hill or plinth between it and the player, so neither can hide the player.
            for (int k = 1; k <= 8; k++)
            {
                float t = k / 8f;
                float ground = run.Terrain.CameraFloor(tx + (eyeX - tx) * t, tz + (eyeZ - tz) * t) + 0.8f;
                eyeY = M.Max(eyeY, ty + (ground - ty) / t);
            }

            float fx = tx - eyeX, fy = ty - eyeY, fz = tz - eyeZ;
            float fl = M.Hypot(fx, fy, fz);
            fx /= fl; fy /= fl; fz /= fl;
            float rx = -fz, ry = 0, rz = fx;
            float rl = M.Hypot(rx, rz);
            if (rl < 0.0001f) rl = 1;
            rx /= rl; rz /= rl;
            float ux = ry * fz - rz * fy, uy = rz * fx - rx * fz, uz = rx * fy - ry * fx;

            rightX = rx; rightY = ry; rightZ = rz;
            camUpX = ux; camUpY = uy; camUpZ = uz;
            // Sprites stay upright but lean halfway to the camera, so they read well from above.
            float lx = ux, ly = uy + 1, lz = uz;
            float ll = M.Hypot(lx, ly, lz);
            upX = lx / ll; upY = ly / ll; upZ = lz / ll;

            view[0] = rx; view[1] = ux; view[2] = -fx; view[3] = 0;
            view[4] = ry; view[5] = uy; view[6] = -fy; view[7] = 0;
            view[8] = rz; view[9] = uz; view[10] = -fz; view[11] = 0;
            view[12] = -(rx * eyeX + ry * eyeY + rz * eyeZ);
            view[13] = -(ux * eyeX + uy * eyeY + uz * eyeZ);
            view[14] = fx * eyeX + fy * eyeY + fz * eyeZ;
            view[15] = 1;
        }

        // World point to pixel coordinates. Returns false when it is behind the camera.
        public bool Project(float x, float y, float z, out float sx, out float sy)
        {
            float vx = view[0] * x + view[4] * y + view[8] * z + view[12];
            float vy = view[1] * x + view[5] * y + view[9] * z + view[13];
            float vz = view[2] * x + view[6] * y + view[10] * z + view[14];
            float cx = proj[0] * vx, cy = proj[5] * vy, cw = -vz;
            sx = sy = 0;
            if (cw <= 0.01f) return false;
            sx = (cx / cw * 0.5f + 0.5f) * Width;
            sy = (-cy / cw * 0.5f + 0.5f) * Height;
            return true;
        }

        static void CellUvs(int frame, int layerX, int layerY, bool flip, out float u0, out float v0, out float u1, out float v1)
        {
            int local = frame % Atlas.CellsPerPage;
            float px = (layerX + (local % Atlas.Columns) * Atlas.Cell) / (float)Atlas.Size;
            float py = (layerY + (local / Atlas.Columns) * Atlas.Cell) / (float)Atlas.Size;
            u0 = flip ? px + CellUv : px;
            u1 = flip ? px : px + CellUv;
            v0 = py;
            v1 = py + CellUv;
        }

        void Billboard(Batch batch, float x, float y, float z, float w, float h, int frame, int layerX, int layerY, bool flip, float r, float g, float b, float a)
        {
            float u0, v0, u1, v1;
            CellUvs(frame, layerX, layerY, flip, out u0, out v0, out u1, out v1);
            float hx = rightX * w * 0.5f, hy = rightY * w * 0.5f, hz = rightZ * w * 0.5f;
            float tx = upX * h, ty = upY * h, tz = upZ * h;
            batch.Quad(x - hx, y - hy, z - hz, x + hx, y + hy, z + hz, x + hx + tx, y + hy + ty, z + hz + tz, x - hx + tx, y - hy + ty, z - hz + tz, u0, v0, u1, v1, r, g, b, a);
        }

        void Sprite(float x, float y, float z, float w, float h, int frame, bool flip, float flash, int outline, float tr = 1, float tg = 1, float tb = 1)
        {
            int page = Atlas.PageOf(frame);
            if (outline > 0)
            {
                if (outline > 1) Billboard(outlines[page], x, y, z, w, h, frame, 0, Layer2Y, flip, 1, 0.72f, 0.1f, 1);
                else Billboard(outlines[page], x, y, z, w, h, frame, 0, Layer2Y, flip, 1, 1, 1, 1);
            }
            Billboard(sprites[page], x, y, z, w, h, frame, 0, 0, flip, spriteLight.R * tr, spriteLight.G * tg, spriteLight.B * tb, 1);
            if (flash > 0.01f) Billboard(flashes[page], x, y, z, w, h, frame, Layer1X, 0, flip, 1, 1, 1, flash);
        }

        void Shadow(float x, float y, float z, float r)
        {
            float u0, v0, u1, v1;
            CellUvs(discFrame, Layer1X, 0, false, out u0, out v0, out u1, out v1);
            y += 0.06f;
            shadows.Quad(x - r, y, z + r, x + r, y, z + r, x + r, y, z - r, x - r, y, z - r, u0, v0, u1, v1, 0, 0, 0, 0.32f);
        }

        void CollectEnemies()
        {
            Enemies e = run.Enemies;
            float fwdX = -view[2], fwdZ = -view[10];
            for (int i = 0; i < e.Count; i++)
            {
                if (e.Dead[i] || (e.Flags[i] & Flag.Captive) != 0) continue;
                float dx = e.X[i] - eyeX, dz = e.Z[i] - eyeZ;
                if (dx * dx + dz * dz > 6400) continue;
                bool flip;
                float h = e.Hgt[i] * 1.25f;
                int f = e.Flags[i];
                int outline = (f & (Flag.Elite | Flag.Boss | Flag.Wild)) != 0 ? 2 : e.Uid[i] == run.TargetUid ? 1 : 0;
                Sprite(e.X[i], e.Y[i] - h * 0.08f, e.Z[i], h, h, run.Atlas.CreatureFrame(enemyFrames[e.Kind[i]], e.Fx[i] * rightX + e.Fz[i] * rightZ, e.Fx[i] * fwdX + e.Fz[i] * fwdZ, e.Anim[i] * 2, true, false, out flip), flip, e.Flash[i], outline, e.WetT[i] > 0 ? 0.62f : 1, e.WetT[i] > 0 ? 0.8f : e.BurnT[i] > 0 ? 0.72f : 1, e.BurnT[i] > 0 ? 0.55f : 1);
                Shadow(e.X[i], e.Y[i] - e.Hover[i], e.Z[i], e.Radius[i] * 1.2f);
            }
        }

        void CollectPickups()
        {
            foreach (Drop p in run.Pickups.Items)
            {
                float bob = p.Pulled ? 0 : M.Sin(run.Time * 4 + p.X) * 0.08f;
                int frame = p.Kind == Pickups.Gem ? gemFrame : p.Kind == Pickups.Coin ? coinFrame : heartFrame;
                float s = p.Kind == Pickups.Gem ? 0.7f + M.Min(0.6f, p.Value * 0.03f) : 0.75f;
                Sprite(p.X, p.Y + bob - s * 0.5f, p.Z, s, s, frame, false, 0, 0);
            }
            Pickups q = run.Pickups;
            if (q.HasPortal)
            {
                float s = 4.6f + M.Sin(q.PortalT * 3) * 0.25f;
                Sprite(q.PortalX, q.PortalY + 0.1f, q.PortalZ, s, s, portalFrame, false, 0.1f + M.Sin(q.PortalT * 5) * 0.08f, 0);
            }
        }

        bool BlocksView(float x, float z, float size)
        {
            float dx = run.CamTx - eyeX, dz = run.CamTz - eyeZ;
            float len2 = dx * dx + dz * dz;
            if (len2 < 0.01f) return false;
            float t = ((x - eyeX) * dx + (z - eyeZ) * dz) / len2;
            if (t < 0.02f || t > 0.9f) return false;
            float px = eyeX + dx * t - x, pz = eyeZ + dz * t - z;
            return px * px + pz * pz < size * size * 0.3f;
        }

        void CollectEncounters()
        {
            bool unusedFlip;
            foreach (Encounter o in run.Encounters.Items)
            {
                if (o.Used) continue;
                int frame;
                float size = 2.4f;
                switch (o.Type)
                {
                    case EncKind.Chest: frame = chestFrame; size = 2.1f; break;
                    case EncKind.Cursed: frame = cursedFrame; size = 2.1f; break;
                    case EncKind.TradeAltar: frame = tradeFrame; size = 3.2f; break;
                    case EncKind.Risk: frame = riskFrame; size = 3.2f; break;
                    case EncKind.Charge: frame = chargeFrame; size = 3.2f; break;
                    case EncKind.Fossil: frame = fossilFrame; size = 2.8f; break;
                    case EncKind.Pod: frame = podFrame; size = 1.7f; break;
                    case EncKind.Merchant: frame = run.Atlas.CreatureFrame(merchantFrame, 0, -1, run.Time * 3.3f, true, false, out unusedFlip); size = 2.6f; break;
                    case EncKind.Machine: frame = machineFrame; size = 3.4f; break;
                    default: frame = stoneFrame; size = 4.4f; break;
                }
                // Anything standing between the camera and the player turns see-through.
                if (BlocksView(o.X, o.Z, size)) Billboard(ghosts[Atlas.PageOf(frame)], o.X, o.Y - size * 0.1f, o.Z, size, size, frame, 0, 0, false, spriteLight.R, spriteLight.G, spriteLight.B, GhostAlpha);
                else Sprite(o.X, o.Y - size * 0.1f, o.Z, size, size, frame, false, 0, o.Type == EncKind.Chest && o.Cost == 0 ? 1 : 0);
                Shadow(o.X, o.Y, o.Z, size * 0.4f);
                if (o.Type == EncKind.Charge) RingBand(o.X, o.Y, o.Z, Encounters.ChargeRingRadius, 0.05f + o.ChargeT * 0.2f, new Col(0xffd24a), 0.7f, 0);
            }
        }

        void CollectShots()
        {
            foreach (Shot p in run.Projectiles.Items)
            {
                float s = p.Size * 1.25f;
                Col c = new Col(p.Color);
                Sprite(p.X, p.Y - s * 0.5f, p.Z, s, s, shotFrame, false, 0.15f, 0, c.R * 1.6f, c.G * 1.6f, c.B * 1.6f);
            }
        }

        void CollectPlayer()
        {
            Player p = run.Player;
            float e8 = -view[2], e10 = -view[10];
            bool flip;
            // Blinking signals recovery from a hit. A slide is also invulnerable, but stays solid so its pose reads.
            bool blink = p.Iframes > 0 && p.SlideT <= 0 && (int)(run.Time * 22) % 2 == 0;
            float size = 2.15f * p.Stage.Scale;
            float sq = p.Squash + (p.SlideT > 0 ? 0.4f : 0);
            int baseFrame = run.Atlas.Frame(p.Stage.Id);
            if (!blink || p.FlashT > 0.1f) Sprite(p.X, p.Y - size * 0.08f, p.Z, size * (1 + sq * 0.45f), size * (1 - sq * 0.45f), run.Atlas.CreatureFrame(baseFrame, p.FacingX * rightX + p.FacingZ * rightZ, p.FacingX * e8 + p.FacingZ * e10, p.Anim, p.Moving && p.Grounded, !p.Grounded, out flip), flip, p.FlashT, 0);
            Shadow(p.X, run.Terrain.Surface(p.X, p.Z, p.Y), p.Z, 0.75f * p.Stage.Scale);
            foreach (Drone d in p.Drones)
            {
                float fwdX = -view[2], fwdZ = -view[10];
                Sprite(d.X, d.Y - (d.Flying ? 0 : d.Size * 0.08f), d.Z, d.Size, d.Size, run.Atlas.CreatureFrame(run.Atlas.Frame(d.FrameId), d.Fx * rightX + d.Fz * rightZ, d.Fx * fwdX + d.Fz * fwdZ, d.Anim, true, false, out flip), flip, 0, 0);
                if (!d.Flying) Shadow(d.X, d.Y, d.Z, d.Size * 0.32f);
            }
            if (run.Capture.OrbVisible) Sprite(run.Capture.OrbX, run.Capture.OrbY, run.Capture.OrbZ, 0.9f, 0.9f, shotFrame, false, 0, 1, 0.35f, 1, 0.85f);
        }

        void CollectMenu()
        {
            bool menuFlip;
            for (int k = 0; k < Data.Partners.Length; k++)
            {
                float a = (float)k / Data.Partners.Length * M.Tau + 0.6f;
                float x = M.Cos(a) * 3, z = M.Sin(a) * 3;
                float y = run.Terrain.HeightAt(x, z);
                Sprite(x, y, z, 2.6f, 2.6f, run.Atlas.CreatureFrame(run.Atlas.Frame(Data.Partners[k].Id), 0, -1, (run.MenuT * 3 + k) / 0.6f, true, false, out menuFlip), false, 0, 0);
                Shadow(x, y, z, 0.9f);
            }
        }

        void CollectParticles(ParticleLayer l, Batch batch)
        {
            batch.Reset();
            for (int i = 0; i < l.Count; i++)
            {
                float s = l.Size[i] * 0.5f;
                float hx = rightX * s, hy = rightY * s, hz = rightZ * s;
                float tx = camUpX * s, ty = camUpY * s, tz = camUpZ * s;
                float x = l.X[i], y = l.Y[i], z = l.Z[i];
                batch.Quad(x - hx - tx, y - hy - ty, z - hz - tz, x + hx - tx, y + hy - ty, z + hz - tz, x + hx + tx, y + hy + ty, z + hz + tz, x - hx + tx, y - hy + ty, z - hz + tz, 0, 0, 0, 0, l.R[i], l.G[i], l.B[i], l.Alpha[i]);
            }
        }

        void RingBand(float x, float y, float z, float radius, float thick, Col c, float alpha, float fillAlpha)
        {
            const int segments = 36;
            y += 0.15f;
            float inner = radius * (1 - thick);
            for (int k = 0; k < segments; k++)
            {
                float a0 = (float)k / segments * M.Tau, a1 = (float)(k + 1) / segments * M.Tau;
                float c0 = M.Cos(a0), s0 = M.Sin(a0), c1 = M.Cos(a1), s1 = M.Sin(a1);
                rings.Quad(x + c0 * inner, y, z + s0 * inner, x + c1 * inner, y, z + s1 * inner, x + c1 * radius, y, z + s1 * radius, x + c0 * radius, y, z + s0 * radius, 0, 0, 0, 0, c.R, c.G, c.B, alpha);
                if (fillAlpha > 0) rings.Quad(x, y, z, x, y, z, x + c1 * inner, y, z + s1 * inner, x + c0 * inner, y, z + s0 * inner, 0, 0, 0, 0, c.R, c.G, c.B, alpha * fillAlpha);
            }
        }

        void CollectBeams()
        {
            beams.Reset();
            foreach (Beam b in run.Vfx.Beams)
            {
                float dx = b.Bx - b.Ax, dy = b.By - b.Ay, dz = b.Bz - b.Az;
                float mx = (b.Ax + b.Bx) / 2 - eyeX, my = (b.Ay + b.By) / 2 - eyeY, mz = (b.Az + b.Bz) / 2 - eyeZ;
                float sx = dy * mz - dz * my, sy = dz * mx - dx * mz, sz = dx * my - dy * mx;
                float sl = M.Hypot(sx, sy, sz);
                if (sl < 0.001f) continue;
                float w = b.Width * (b.Life / b.MaxLife) / sl;
                sx *= w; sy *= w; sz *= w;
                beams.Quad(b.Ax - sx, b.Ay - sy, b.Az - sz, b.Ax + sx, b.Ay + sy, b.Az + sz, b.Bx + sx, b.By + sy, b.Bz + sz, b.Bx - sx, b.By - sy, b.Bz - sz, 0, 0, 0, 0, b.Color.R, b.Color.G, b.Color.B, 1);
            }
        }

        void DrawPages(Batch[] batches)
        {
            for (int page = 0; page < batches.Length; page++)
            {
                Gl.BindTexture(Gl.TEXTURE_2D, atlasTextures[page]);
                batches[page].Draw();
            }
        }

        public void Render()
        {
            if (builtVersion != run.TerrainVersion) BuildWorld();
            Biome biome = run.Terrain.Biome;
            SetupCamera();

            Col sky = new Col(biome.Sky);
            Gl.Viewport(0, 0, Width, Height);
            Gl.ClearColor(sky.R, sky.G, sky.B, 1);
            Gl.DepthMask(1);
            Gl.Clear(Gl.COLOR_BUFFER_BIT | Gl.DEPTH_BUFFER_BIT);
            Gl.MatrixMode(Gl.PROJECTION);
            Gl.LoadMatrix(proj);
            Gl.MatrixMode(Gl.MODELVIEW);
            Gl.LoadMatrix(view);

            Gl.Enable(Gl.DEPTH_TEST);
            Gl.DepthFunc(Gl.LEQUAL);
            Gl.Enable(Gl.FOG);
            Gl.Disable(Gl.BLEND);
            Gl.Disable(Gl.ALPHA_TEST);

            Gl.Enable(Gl.TEXTURE_2D);
            Gl.BindTexture(Gl.TEXTURE_2D, tileTexture);
            Gl.CallList(groundList);
            Gl.Disable(Gl.TEXTURE_2D);
            Gl.CallList(solidList);

            shadows.Reset();
            for (int page = 0; page < sprites.Length; page++)
            {
                outlines[page].Reset();
                sprites[page].Reset();
                flashes[page].Reset();
                ghosts[page].Reset();
            }
            rings.Reset();
            if (run.Player != null && run.State != GameState.Menu)
            {
                CollectEnemies();
                CollectPickups();
                CollectEncounters();
                CollectShots();
                CollectPlayer();
            }
            else
            {
                CollectMenu();
            }

            Gl.EnableClientState(Gl.VERTEX_ARRAY);
            Gl.EnableClientState(Gl.COLOR_ARRAY);
            Gl.EnableClientState(Gl.TEXTURE_COORD_ARRAY);

            Gl.Enable(Gl.TEXTURE_2D);
            Gl.BindTexture(Gl.TEXTURE_2D, atlasTextures[0]);
            Gl.Enable(Gl.BLEND);
            Gl.BlendFunc(Gl.SRC_ALPHA, Gl.ONE_MINUS_SRC_ALPHA);
            Gl.DepthMask(0);
            Gl.Enable(Gl.ALPHA_TEST);
            Gl.AlphaFunc(Gl.GREATER, 0.01f);
            shadows.Draw();

            Gl.Disable(Gl.BLEND);
            Gl.DepthMask(1);
            Gl.AlphaFunc(Gl.GREATER, 0.5f);
            DrawPages(outlines);
            DrawPages(sprites);

            Gl.Enable(Gl.BLEND);
            Gl.DepthMask(0);
            Gl.AlphaFunc(Gl.GREATER, 0.01f);
            DrawPages(flashes);
            DrawPages(ghosts);
            Gl.Disable(Gl.ALPHA_TEST);
            Gl.Disable(Gl.TEXTURE_2D);
            Gl.Disable(Gl.FOG);

            CollectParticles(run.Vfx.Norm, particlesNorm);
            particlesNorm.Draw();
            Gl.BlendFunc(Gl.SRC_ALPHA, Gl.ONE);
            CollectParticles(run.Vfx.Add, particlesAdd);
            particlesAdd.Draw();
            CollectBeams();
            beams.Draw();
            Gl.BlendFunc(Gl.SRC_ALPHA, Gl.ONE_MINUS_SRC_ALPHA);

            foreach (Ring r in run.Vfx.Rings) RingBand(r.X, r.Y, r.Z, r.Radius, r.Thick, r.Color, r.Alpha, r.FillAlpha);
            Gl.Disable(Gl.DEPTH_TEST);
            rings.Draw();

            Gl.DisableClientState(Gl.VERTEX_ARRAY);
            Gl.DisableClientState(Gl.COLOR_ARRAY);
            Gl.DisableClientState(Gl.TEXTURE_COORD_ARRAY);
        }
    }
}
