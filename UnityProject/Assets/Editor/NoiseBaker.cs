using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

// Bruit 3D périodique pour les volumes : généré hors ligne, embarqué dans le bundle.
// r = Perlin-Worley (Perlin fBm remappé par le Worley inversé, forme des « choux-fleurs »)
// g = Worley fBm basse fréquence (4, 8, 16 cellules)   → cellules de colonnes, lobes du front
// b = Worley fBm haute fréquence (8, 16, 32 cellules)  → érosion des bords
// a = Perlin fBm indépendant                            → déformation de domaine
// Tous les octaves sont périodiques sur la texture : aucun joint visible au pavage.
public static class NoiseBaker
{
    public static void Create(string path, int size)
    {
        var pixels = new Color32[size * size * size];
        Parallel.For(0, size, z =>
        {
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size, v = (y + 0.5f) / size, w = (z + 0.5f) / size;
                    float perlin = PerlinFbm(u, v, w, 4, 4, 11);
                    float wLow = WorleyFbm(u, v, w, 4, 21);
                    float wHigh = WorleyFbm(u, v, w, 8, 37);
                    float pw = Mathf.Clamp01(Remap(perlin, wLow - 1f, 1f, 0f, 1f));
                    float warp = PerlinFbm(u, v, w, 3, 3, 53);
                    pixels[x + size * (y + size * z)] = new Color32(B(pw), B(wLow), B(wHigh), B(warp));
                }
        });
        var texture = new Texture3D(size, size, size, TextureFormat.RGBA32, true)
        {
            name = "GroundBlastFx Perlin-Worley 3D noise",
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Trilinear,
            anisoLevel = 0
        };
        texture.SetPixels32(pixels);
        texture.Apply(true, true);
        AssetDatabase.CreateAsset(texture, path);
        AssetDatabase.SaveAssets();
    }

    private static byte B(float v) { return (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255); }

    private static float Remap(float v, float a, float b, float c, float d) { return c + (v - a) / Math.Max(b - a, 1e-4f) * (d - c); }

    // --- Worley (distance à la graine la plus proche), périodique ---
    private static float WorleyFbm(float u, float v, float w, int baseCells, int seed)
    {
        float a = 1f - Worley(u, v, w, baseCells, seed);
        float b = 1f - Worley(u, v, w, baseCells * 2, seed + 7);
        float c = 1f - Worley(u, v, w, baseCells * 4, seed + 13);
        return a * 0.625f + b * 0.25f + c * 0.125f;
    }

    private static float Worley(float u, float v, float w, int cells, int seed)
    {
        float px = u * cells, py = v * cells, pz = w * cells;
        int ix = (int)Math.Floor(px), iy = (int)Math.Floor(py), iz = (int)Math.Floor(pz);
        float best = float.MaxValue;
        for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = ix + dx, cy = iy + dy, cz = iz + dz;
                    int wx = Mod(cx, cells), wy = Mod(cy, cells), wz = Mod(cz, cells);
                    float fx = cx + Hash01(wx, wy, wz, seed);
                    float fy = cy + Hash01(wx, wy, wz, seed + 1);
                    float fz = cz + Hash01(wx, wy, wz, seed + 2);
                    float ddx = fx - px, ddy = fy - py, ddz = fz - pz;
                    float d = ddx * ddx + ddy * ddy + ddz * ddz;
                    if (d < best) best = d;
                }
        return Mathf.Clamp01((float)Math.Sqrt(best) / 0.9f);
    }

    // --- Perlin (gradients), périodique ---
    private static float PerlinFbm(float u, float v, float w, int baseCells, int octaves, int seed)
    {
        float sum = 0, amp = 0.5f, norm = 0;
        int cells = baseCells;
        for (int o = 0; o < octaves; o++)
        {
            sum += amp * Perlin(u * cells, v * cells, w * cells, cells, seed + o * 17);
            norm += amp;
            amp *= 0.5f;
            cells *= 2;
        }
        return Mathf.Clamp01(sum / norm * 0.5f + 0.5f);
    }

    private static float Perlin(float x, float y, float z, int period, int seed)
    {
        int ix = (int)Math.Floor(x), iy = (int)Math.Floor(y), iz = (int)Math.Floor(z);
        float fx = x - ix, fy = y - iy, fz = z - iz;
        float ux = Fade(fx), uy = Fade(fy), uz = Fade(fz);
        float n000 = Grad(ix, iy, iz, period, seed, fx, fy, fz);
        float n100 = Grad(ix + 1, iy, iz, period, seed, fx - 1, fy, fz);
        float n010 = Grad(ix, iy + 1, iz, period, seed, fx, fy - 1, fz);
        float n110 = Grad(ix + 1, iy + 1, iz, period, seed, fx - 1, fy - 1, fz);
        float n001 = Grad(ix, iy, iz + 1, period, seed, fx, fy, fz - 1);
        float n101 = Grad(ix + 1, iy, iz + 1, period, seed, fx - 1, fy, fz - 1);
        float n011 = Grad(ix, iy + 1, iz + 1, period, seed, fx, fy - 1, fz - 1);
        float n111 = Grad(ix + 1, iy + 1, iz + 1, period, seed, fx - 1, fy - 1, fz - 1);
        float x00 = Lerp(n000, n100, ux), x10 = Lerp(n010, n110, ux), x01 = Lerp(n001, n101, ux), x11 = Lerp(n011, n111, ux);
        return Lerp(Lerp(x00, x10, uy), Lerp(x01, x11, uy), uz) * 1.15f;
    }

    private static float Grad(int x, int y, int z, int period, int seed, float dx, float dy, float dz)
    {
        uint h = Hash(Mod(x, period), Mod(y, period), Mod(z, period), seed);
        switch (h % 12)
        {
            case 0: return dx + dy;
            case 1: return -dx + dy;
            case 2: return dx - dy;
            case 3: return -dx - dy;
            case 4: return dx + dz;
            case 5: return -dx + dz;
            case 6: return dx - dz;
            case 7: return -dx - dz;
            case 8: return dy + dz;
            case 9: return -dy + dz;
            case 10: return dy - dz;
            default: return -dy - dz;
        }
    }

    private static float Fade(float t) { return t * t * t * (t * (t * 6 - 15) + 10); }
    private static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
    private static int Mod(int a, int m) { int r = a % m; return r < 0 ? r + m : r; }

    private static float Hash01(int x, int y, int z, int seed) { return (Hash(x, y, z, seed) & 0xffffff) / 16777215f; }

    private static uint Hash(int x, int y, int z, int seed)
    {
        uint h = (uint)(x * 73856093 ^ y * 19349663 ^ z * 83492791 ^ seed * 104729);
        h ^= h >> 16; h *= 0x7feb352d; h ^= h >> 15; h *= 0x846ca68b; h ^= h >> 16;
        return h;
    }
}
