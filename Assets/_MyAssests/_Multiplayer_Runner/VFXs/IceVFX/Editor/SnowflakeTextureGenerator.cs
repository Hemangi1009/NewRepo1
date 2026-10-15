using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// EDITOR SCRIPT - must live in a folder named "Editor" (Assets/IceVFX/Editor/).
/// Menu: Tools > Ice VFX > Generate Snowflake Textures
/// Creates two white-on-transparent 6-fold snowflake PNGs:
///   Assets/IceVFX/Textures/Snowflake_Core.png   (512, detailed, for the center)
///   Assets/IceVFX/Textures/Snowflake_Small.png  (256, simpler, for floating particles)
public static class SnowflakeTextureGenerator
{
    struct Seg
    {
        public Vector2 a, b;
        public float r;
        public Seg(Vector2 a, Vector2 b, float r) { this.a = a; this.b = b; this.r = r; }
    }

    [MenuItem("Tools/Ice VFX/Generate Snowflake Textures")]
    public static void Generate()
    {
        string dir = "Assets/IceVFX/Textures";
        Directory.CreateDirectory(dir);

        string core = dir + "/Snowflake_Core.png";
        string small = dir + "/Snowflake_Small.png";
        Save(Build(true, 512), core);
        Save(Build(false, 256), small);
        AssetDatabase.Refresh();

        foreach (string p in new[] { core, small })
        {
            var imp = AssetImporter.GetAtPath(p) as TextureImporter;
            if (imp == null) continue;
            imp.textureType = TextureImporterType.Default;
            imp.alphaSource = TextureImporterAlphaSource.FromInput;
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.maxTextureSize = 512;
            imp.SaveAndReimport();
        }
        Debug.Log("Snowflake textures created in " + dir);
    }

    static float CapDist(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 pa = p - a, ba = b - a;
        float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
        return (pa - ba * h).magnitude;
    }

    static Color[] Build(bool detailed, int size)
    {
        // one arm along +x, y >= 0 only; the arm is mirrored (abs y) and rotated 6x below
        const float R = 0.42f;
        const float c60 = 0.5f, s60 = 0.8660254f;
        var segs = new List<Seg>();

        segs.Add(new Seg(Vector2.zero, new Vector2(R, 0f), detailed ? 0.016f : 0.022f));

        int pairs = detailed ? 4 : 2;
        for (int i = 0; i < pairs; i++)
        {
            float x = detailed ? R * (0.26f + 0.19f * i) : R * (0.40f + 0.25f * i);
            float L = R * (0.36f - 0.06f * i);
            Vector2 a = new Vector2(x, 0f);
            Vector2 b = a + new Vector2(c60, s60) * L;
            segs.Add(new Seg(a, b, detailed ? 0.012f : 0.018f));
            if (detailed)
            {
                Vector2 m = Vector2.Lerp(a, b, 0.55f);
                segs.Add(new Seg(m, m + new Vector2(L * 0.45f, 0f), 0.008f));
                segs.Add(new Seg(b, b + new Vector2(L * 0.30f, 0f), 0.008f));
            }
        }

        // arrow-shaped tip
        segs.Add(new Seg(new Vector2(R * 0.80f, 0f), new Vector2(R * 0.98f, R * 0.13f), detailed ? 0.010f : 0.016f));

        // center hexagon(s)
        float r0 = R * 0.22f;
        segs.Add(new Seg(new Vector2(r0, 0f), new Vector2(r0 * c60, r0 * s60), detailed ? 0.012f : 0.018f));
        if (detailed)
        {
            float r1 = R * 0.11f;
            segs.Add(new Seg(new Vector2(r1, 0f), new Vector2(r1 * c60, r1 * s60), 0.008f));
        }

        float[] cs = new float[6], sn = new float[6];
        for (int k = 0; k < 6; k++)
        {
            cs[k] = Mathf.Cos(k * Mathf.PI / 3f);
            sn[k] = Mathf.Sin(k * Mathf.PI / 3f);
        }

        var px = new Color[size * size];
        float soft = 1.5f / size;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2((x + 0.5f) / size - 0.5f, (y + 0.5f) / size - 0.5f);
                float best = 1e9f;
                for (int k = 0; k < 6; k++)
                {
                    Vector2 q = new Vector2(p.x * cs[k] + p.y * sn[k], -p.x * sn[k] + p.y * cs[k]);
                    q.y = Mathf.Abs(q.y);
                    for (int s = 0; s < segs.Count; s++)
                    {
                        float d = CapDist(q, segs[s].a, segs[s].b) - segs[s].r;
                        if (d < best) best = d;
                    }
                }

                float alpha = Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(-soft, soft, best));
                float depth = Mathf.Clamp01(-best / 0.012f);
                float a = alpha * (0.7f + 0.3f * depth);
                a += 0.18f * Mathf.Exp(-Mathf.Max(best, 0f) * 60f);   // soft glow around the arms
                px[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
            }
        }
        return px;
    }

    static void Save(Color[] px, string path)
    {
        int size = Mathf.RoundToInt(Mathf.Sqrt(px.Length));
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.SetPixels(px);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
    }
}
