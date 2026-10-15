using UnityEngine;

/// Real 3D plasma-ball lightning built from LineRenderers (VR safe).
/// Put this on an empty child of your VFX object (position 0,0,0, scale 1,1,1).
public class PlasmaBolts : MonoBehaviour
{
    public Material boltMaterial;

    [Header("Shape (local units, shell = 1)")]
    public float coreRadius = 0.36f;
    public float shellRadius = 0.97f;
    public int boltCount = 12;
    public int segments = 36;
    public float jaggedness = 0.07f;
    public float width = 0.035f;

    [Header("Timing")]
    public Vector2 lifeRange = new Vector2(1.0f, 2.5f);
    public float wanderSpeed = 1.5f;
    public float growTime = 0.08f;
    public float dieTime = 0.15f;

    [Header("Side Branches")]
    [Range(0, 1)] public float branchChance = 0.8f;
    public int branchesPerBolt = 2;
    public int branchSegments = 12;

    [Header("Split Tips (bolt ends break into prongs)")]
    public int tipForks = 3;
    public float tipSpread = 0.9f;

    class Bolt
    {
        public LineRenderer main;
        public LineRenderer[] br;
        public float age, life, seed;
        public Vector3 dir, e1, e2;
        public bool[] bOn;
        public float[] bT, bSlope, bLen, bAngle;
    }

    Bolt[] bolts;
    Vector3[] mainPts, brPts;
    int totalForks;

    void Start()
    {
        totalForks = branchesPerBolt + tipForks;
        mainPts = new Vector3[segments + 1];
        brPts = new Vector3[branchSegments + 1];
        bolts = new Bolt[boltCount];

        var mainCurve = new AnimationCurve(new Keyframe(0, 1.4f), new Keyframe(1, 0.7f));
        var brCurve = new AnimationCurve(new Keyframe(0, 0.7f), new Keyframe(1, 0f));

        for (int i = 0; i < boltCount; i++)
        {
            var b = new Bolt();
            b.main = MakeLine("Bolt" + i, mainCurve);
            b.br = new LineRenderer[totalForks];
            b.bOn = new bool[totalForks];
            b.bT = new float[totalForks];
            b.bSlope = new float[totalForks];
            b.bLen = new float[totalForks];
            b.bAngle = new float[totalForks];
            for (int j = 0; j < totalForks; j++)
                b.br[j] = MakeLine("Bolt" + i + "_Fork" + j, brCurve);
            bolts[i] = b;
            Respawn(b);
            b.age = Random.value * b.life; // stagger
        }
    }

    LineRenderer MakeLine(string n, AnimationCurve curve)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        var lr = go.AddComponent<LineRenderer>();
        lr.useWorldSpace = false;
        lr.alignment = LineAlignment.View;
        lr.textureMode = LineTextureMode.Stretch;
        lr.numCapVertices = 0;
        lr.numCornerVertices = 0;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        lr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        lr.sharedMaterial = boltMaterial;
        lr.widthCurve = curve;
        lr.widthMultiplier = width;
        lr.positionCount = 0;
        return lr;
    }

    // pick the direction farthest from every other living bolt -> even coverage of the WHOLE sphere
    Vector3 PickDirection(Bolt self)
    {
        Vector3 best = Random.onUnitSphere;
        float bestScore = float.MaxValue;
        for (int c = 0; c < 10; c++)
        {
            Vector3 cand = Random.onUnitSphere;
            float maxDot = -1f;
            for (int k = 0; k < bolts.Length; k++)
            {
                var o = bolts[k];
                if (o == null || o == self || o.dir == Vector3.zero) continue;
                float d = Vector3.Dot(cand, o.dir);
                if (d > maxDot) maxDot = d;
            }
            if (maxDot < bestScore) { bestScore = maxDot; best = cand; }
        }
        return best;
    }

    void Respawn(Bolt b)
    {
        b.dir = PickDirection(b);
        b.e1 = Vector3.Cross(b.dir, Mathf.Abs(b.dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        b.e2 = Vector3.Cross(b.dir, b.e1);
        b.life = Random.Range(lifeRange.x, lifeRange.y);
        b.age = 0f;
        b.seed = Random.value * 100f;

        // side branches along the bolt
        for (int j = 0; j < branchesPerBolt; j++)
        {
            b.bOn[j] = Random.value < branchChance;
            b.bT[j] = Random.Range(0.2f, 0.65f);
            b.bSlope[j] = Random.Range(0.15f, 0.55f);
            b.bLen[j] = Random.Range(0.15f, 0.35f);
            b.bAngle[j] = Random.value * Mathf.PI * 2f;
        }

        // tip forks: the end of the bolt splits into 2-3 prongs fanning out
        float baseAngle = Random.value * Mathf.PI * 2f;
        float tipStart = Random.Range(0.8f, 0.9f);
        for (int j = branchesPerBolt; j < totalForks; j++)
        {
            int n = j - branchesPerBolt;
            b.bOn[j] = true;
            b.bT[j] = tipStart + Random.Range(-0.03f, 0.03f);
            b.bSlope[j] = tipSpread * Random.Range(0.7f, 1.3f);
            b.bLen[j] = (1f - b.bT[j]) * (shellRadius - coreRadius) * Random.Range(1.0f, 1.6f) + 0.04f;
            b.bAngle[j] = baseAngle + n * (Mathf.PI * 2f / Mathf.Max(1, tipForks))
                          + Random.Range(-0.4f, 0.4f);
        }
    }

    // layered smooth noise: big curves + fine crackle, animated in time
    float Noise(float t, float time, float s)
    {
        float v = 0f, amp = 1f, f = 3f;
        for (int o = 0; o < 4; o++)
        {
            float n = Mathf.PerlinNoise(t * f + s * 13.1f + o * 7.7f, time * (0.6f + o * 0.7f) + s * 3.3f);
            v += (n * 2f - 1f) * amp;
            f *= 2.6f;
            amp *= 0.45f;
        }
        return v;
    }

    Vector3 ClampShell(Vector3 p)
    {
        float m = p.magnitude;
        return m > shellRadius ? p * (shellRadius / m) : p;
    }

    Vector3 MainPoint(Bolt b, float t, float time)
    {
        float pin = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.12f))
                  * (1f - 0.5f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - 0.88f) / 0.12f)));
        float a = Noise(t, time, b.seed) * jaggedness * 2.2f * pin;
        float c = Noise(t, time, b.seed + 41f) * jaggedness * 2.2f * pin;
        return ClampShell(b.dir * Mathf.Lerp(coreRadius, shellRadius, t) + b.e1 * a + b.e2 * c);
    }

    void Update()
    {
        if (bolts == null || boltMaterial == null) return;
        float time = Time.time * wanderSpeed;

        for (int i = 0; i < bolts.Length; i++)
        {
            var b = bolts[i];
            b.age += Time.deltaTime;
            if (b.age >= b.life) Respawn(b);

            float head = Mathf.Clamp01(b.age / growTime);
            float die = Mathf.Clamp01((b.life - b.age) / dieTime);
            float flick = 0.85f + 0.15f * Mathf.PerlinNoise(Time.time * 30f, b.seed);
            float bright = die * flick;
            var col = new Color(1f, 1f, 1f, bright);

            // main bolt
            int count = Mathf.Max(2, Mathf.CeilToInt(head * segments) + 1);
            for (int k = 0; k < count; k++)
            {
                float t = Mathf.Min(k / (float)segments, head);
                mainPts[k] = MainPoint(b, t, time);
            }
            b.main.positionCount = count;
            b.main.SetPositions(mainPts);
            b.main.startColor = col;
            b.main.endColor = col;

            // side branches + split tips
            for (int j = 0; j < totalForks; j++)
            {
                var lr = b.br[j];
                float bh = Mathf.Clamp01((b.age - growTime * b.bT[j]) / (growTime * 2f));
                if (!b.bOn[j] || bh <= 0f) { lr.positionCount = 0; continue; }

                Vector3 start = MainPoint(b, Mathf.Min(b.bT[j], head), time);
                Vector3 side = b.e1 * Mathf.Cos(b.bAngle[j]) + b.e2 * Mathf.Sin(b.bAngle[j]);
                int bc = Mathf.Max(2, Mathf.CeilToInt(bh * branchSegments) + 1);
                for (int m = 0; m < bc; m++)
                {
                    float s = Mathf.Min(m / (float)branchSegments, bh);
                    float fade = Mathf.Min(1f, s * 8f);
                    float wa = Noise(s, time, b.seed + 17f * (j + 1)) * jaggedness * 1.4f * fade;
                    float wb = Noise(s, time, b.seed + 53f * (j + 1)) * jaggedness * 1.4f * fade;
                    brPts[m] = ClampShell(start
                             + b.dir * (b.bLen[j] * s)
                             + side * (b.bSlope[j] * b.bLen[j] * s)
                             + b.e1 * wa + b.e2 * wb);
                }
                lr.positionCount = bc;
                lr.SetPositions(brPts);
                var bcCol = new Color(1f, 1f, 1f, bright * 0.8f);
                lr.startColor = bcCol;
                lr.endColor = bcCol;
            }
        }
    }
}
