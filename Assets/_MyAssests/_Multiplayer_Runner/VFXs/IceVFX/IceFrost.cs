//using UnityEngine;

///// Ice "frost veins": straight, crystalline, 60-degree-branching lines that grow from the
///// core to the shell, hold, then fade. Replacement for PlasmaBolts (VR safe, LineRenderers).
///// Put this on the same empty child of your VFX object (position 0,0,0, scale 1,1,1).
//public class IceFrost : MonoBehaviour
//{
//    public Material lineMaterial;                       // can be the same material PlasmaBolts used
//    public Color iceColor = new Color(0.75f, 0.95f, 1f, 1f);

//    [Header("Shape (local units, shell = 1)")]
//    public float coreRadius = 0.30f;                    // where veins start (hidden behind snowflake)
//    public float shellRadius = 0.97f;
//    [Range(0.5f, 1f)] public float mainReach = 0.85f;   // main stem ends at this fraction of shell
//    public int veinCount = 8;
//    public int mainSegments = 6;                        // few segments = angular, crystal look
//    public float kink = 0.035f;                         // sideways angular wobble of the stem
//    public float width = 0.03f;

//    [Header("Branches (snowflake style)")]
//    public int branchPairs = 3;
//    public float branchLength = 0.24f;
//    [Range(20f, 80f)] public float branchAngle = 60f;   // 60 = real ice crystal angle
//    public float tipProngLength = 0.16f;

//    [Header("Timing")]
//    public Vector2 lifeRange = new Vector2(3f, 6f);
//    public float growTime = 0.8f;
//    public float dieTime = 0.9f;
//    public float shimmerSpeed = 4f;

//    class Vein
//    {
//        public LineRenderer main;
//        public LineRenderer[] fork;
//        public Vector3[] mainPts;
//        public Vector3[] fStart, fEnd;
//        public float[] fT;
//        public float age, life, seed;
//        public Vector3 dir, e1, e2;
//    }

//    Vein[] veins;
//    Vector3[] scratch;

//    void Start()
//    {
//        int forkCount = branchPairs * 2 + 2;
//        scratch = new Vector3[mainSegments + 2];
//        veins = new Vein[veinCount];

//        var mainCurve = new AnimationCurve(new Keyframe(0, 1.2f), new Keyframe(1, 0.35f));
//        var forkCurve = new AnimationCurve(new Keyframe(0, 0.8f), new Keyframe(1, 0f));

//        for (int i = 0; i < veinCount; i++)
//        {
//            var v = new Vein();
//            v.main = MakeLine("Vein" + i, mainCurve);
//            v.mainPts = new Vector3[mainSegments + 1];
//            v.fork = new LineRenderer[forkCount];
//            v.fStart = new Vector3[forkCount];
//            v.fEnd = new Vector3[forkCount];
//            v.fT = new float[forkCount];
//            for (int j = 0; j < forkCount; j++)
//                v.fork[j] = MakeLine("Vein" + i + "_Fork" + j, forkCurve);
//            veins[i] = v;
//            Respawn(v);
//            v.age = Random.value * v.life; // stagger
//        }
//    }

//    LineRenderer MakeLine(string n, AnimationCurve curve)
//    {
//        var go = new GameObject(n);
//        go.transform.SetParent(transform, false);
//        var lr = go.AddComponent<LineRenderer>();
//        lr.useWorldSpace = false;
//        lr.alignment = LineAlignment.View;
//        lr.textureMode = LineTextureMode.Stretch;
//        lr.numCapVertices = 0;
//        lr.numCornerVertices = 0;
//        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
//        lr.receiveShadows = false;
//        lr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
//        lr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
//        lr.sharedMaterial = lineMaterial;
//        lr.widthCurve = curve;
//        lr.widthMultiplier = width;
//        lr.positionCount = 0;
//        return lr;
//    }

//    // direction farthest from every other living vein -> even coverage of the whole sphere
//    Vector3 PickDirection(Vein self)
//    {
//        Vector3 best = Random.onUnitSphere;
//        float bestScore = float.MaxValue;
//        for (int c = 0; c < 10; c++)
//        {
//            Vector3 cand = Random.onUnitSphere;
//            float maxDot = -1f;
//            for (int k = 0; k < veins.Length; k++)
//            {
//                var o = veins[k];
//                if (o == null || o == self || o.dir == Vector3.zero) continue;
//                float d = Vector3.Dot(cand, o.dir);
//                if (d > maxDot) maxDot = d;
//            }
//            if (maxDot < bestScore) { bestScore = maxDot; best = cand; }
//        }
//        return best;
//    }

//    Vector3 ClampShell(Vector3 p)
//    {
//        float m = p.magnitude;
//        return m > shellRadius ? p * (shellRadius / m) : p;
//    }

//    static Vector3 PointOn(Vector3[] pts, float t)
//    {
//        float f = Mathf.Clamp01(t) * (pts.Length - 1);
//        int i = Mathf.Min(Mathf.FloorToInt(f), pts.Length - 2);
//        return Vector3.Lerp(pts[i], pts[i + 1], f - i);
//    }

//    // geometry is built ONCE per respawn (static, hard-edged = ice), Update only reveals it
//    void Respawn(Vein v)
//    {
//        v.dir = PickDirection(v);
//        v.e1 = Vector3.Cross(v.dir, Mathf.Abs(v.dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
//        v.e2 = Vector3.Cross(v.dir, v.e1);
//        v.life = Random.Range(lifeRange.x, lifeRange.y);
//        v.age = 0f;
//        v.seed = Random.value * 100f;

//        // main stem: few straight segments, each kinked in one of 6 hex directions
//        float end = shellRadius * mainReach;
//        Vector3 lateral = Vector3.zero;
//        for (int k = 0; k <= mainSegments; k++)
//        {
//            float t = k / (float)mainSegments;
//            if (k > 0)
//            {
//                float a = Random.Range(0, 6) * Mathf.PI / 3f;
//                lateral = lateral * 0.6f + (v.e1 * Mathf.Cos(a) + v.e2 * Mathf.Sin(a)) * kink;
//            }
//            v.mainPts[k] = ClampShell(v.dir * Mathf.Lerp(coreRadius, end, t) + lateral);
//        }

//        // branches: symmetric pairs at branchAngle from the stem, all in one plane
//        float phi = Random.value * Mathf.PI * 2f;
//        Vector3 side = v.e1 * Mathf.Cos(phi) + v.e2 * Mathf.Sin(phi);
//        float ang = branchAngle * Mathf.Deg2Rad;
//        float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);

//        for (int i = 0; i < branchPairs; i++)
//        {
//            float t = Mathf.Lerp(0.25f, 0.7f, (i + 0.5f) / branchPairs);
//            float len = branchLength * (1f - 0.18f * i) * Random.Range(0.85f, 1.15f);
//            Vector3 s0 = PointOn(v.mainPts, t);
//            for (int sg = 0; sg < 2; sg++)
//            {
//                int j = i * 2 + sg;
//                float sign = sg == 0 ? 1f : -1f;
//                Vector3 d = (v.dir * ca + side * (sign * sa)).normalized;
//                v.fStart[j] = s0;
//                v.fEnd[j] = ClampShell(s0 + d * len);
//                v.fT[j] = t;
//            }
//        }

//        // tip: the stem splits into two prongs (like a snowflake arm tip)
//        Vector3 tip = v.mainPts[mainSegments];
//        float ta = ang * 0.6f;
//        for (int n = 0; n < 2; n++)
//        {
//            int j = branchPairs * 2 + n;
//            float sign = n == 0 ? 1f : -1f;
//            Vector3 d = (v.dir * Mathf.Cos(ta) + side * (sign * Mathf.Sin(ta))).normalized;
//            v.fStart[j] = tip;
//            v.fEnd[j] = ClampShell(tip + d * tipProngLength);
//            v.fT[j] = 1f;
//        }
//    }

//    void Update()
//    {
//        if (veins == null || lineMaterial == null) return;
//        float time = Time.time;

//        for (int i = 0; i < veins.Length; i++)
//        {
//            var v = veins[i];
//            v.age += Time.deltaTime;
//            if (v.age >= v.life) Respawn(v);

//            // ease-out growth: shoots out fast, slows down at the end like crystal growth
//            float g = Mathf.Clamp01(v.age / growTime);
//            float head = 1f - (1f - g) * (1f - g) * (1f - g);
//            float die = Mathf.Clamp01((v.life - v.age) / dieTime);
//            float shimmer = 0.8f + 0.2f * Mathf.Sin(time * shimmerSpeed + v.seed);
//            float bright = die * shimmer;

//            // main stem
//            float vis = head * mainSegments;
//            int full = Mathf.Min(Mathf.FloorToInt(vis), mainSegments);
//            float frac = vis - full;
//            int count;
//            for (int k = 0; k <= full; k++) scratch[k] = v.mainPts[k];
//            if (full < mainSegments)
//            {
//                scratch[full + 1] = Vector3.Lerp(v.mainPts[full], v.mainPts[full + 1], frac);
//                count = full + 2;
//            }
//            else count = mainSegments + 1;

//            v.main.positionCount = count;
//            v.main.SetPositions(scratch);
//            var c = new Color(iceColor.r, iceColor.g, iceColor.b, bright);
//            v.main.startColor = c;
//            v.main.endColor = c;

//            // branches + tip prongs
//            var fc = new Color(iceColor.r, iceColor.g, iceColor.b, bright * 0.85f);
//            for (int j = 0; j < v.fork.Length; j++)
//            {
//                var lr = v.fork[j];
//                float f = Mathf.Clamp01((head - v.fT[j] * 0.85f) * 3f);
//                if (f <= 0f) { lr.positionCount = 0; continue; }
//                lr.positionCount = 2;
//                lr.SetPosition(0, v.fStart[j]);
//                lr.SetPosition(1, Vector3.Lerp(v.fStart[j], v.fEnd[j], f));
//                lr.startColor = fc;
//                lr.endColor = fc;
//            }
//        }
//    }
//}


using UnityEngine;

/// Ice "frost veins": straight, crystalline, 60-degree-branching lines that grow from the
/// core to the shell, hold, then fade. Replacement for PlasmaBolts (VR safe, LineRenderers).
/// Put this on the same empty child of your VFX object (position 0,0,0, scale 1,1,1).
public class IceFrost : MonoBehaviour
{
    public Material lineMaterial;                       // can be the same material PlasmaBolts used
    public Color iceColor = new Color(0.75f, 0.95f, 1f, 1f);

    [Header("Shape (local units, shell = 1)")]
    public float coreRadius = 0.30f;                    // where veins start (hidden behind snowflake)
    public float shellRadius = 0.97f;
    [Range(0.5f, 1f)] public float mainReach = 0.85f;   // main stem ends at this fraction of shell
    public int veinCount = 8;
    public int mainSegments = 6;                        // few segments = angular, crystal look
    public float kink = 0.035f;                         // sideways angular wobble of the stem
    public float width = 0.03f;

    [Header("Scale (stripe thickness)")]
    [Tooltip("ON: stripe width shrinks/grows with this object's world scale, so a small ball gets thin stripes")]
    public bool followScale = true;
    [Tooltip("Extra manual multiplier for the stripe width")]
    public float widthScale = 1f;
    [Tooltip("Stripes never get thinner than this (world units). 0 = no limit")]
    public float minWidth = 0f;

    [Header("Branches (snowflake style)")]
    public int branchPairs = 3;
    public float branchLength = 0.24f;
    [Range(20f, 80f)] public float branchAngle = 60f;   // 60 = real ice crystal angle
    public float tipProngLength = 0.16f;

    [Header("Timing")]
    public Vector2 lifeRange = new Vector2(3f, 6f);
    public float growTime = 0.8f;
    public float dieTime = 0.9f;
    public float shimmerSpeed = 4f;

    class Vein
    {
        public LineRenderer main;
        public LineRenderer[] fork;
        public Vector3[] mainPts;
        public Vector3[] fStart, fEnd;
        public float[] fT;
        public float age, life, seed;
        public Vector3 dir, e1, e2;
    }

    Vein[] veins;
    Vector3[] scratch;
    float lastWidth = -1f;

    void Start()
    {
        int forkCount = branchPairs * 2 + 2;
        scratch = new Vector3[mainSegments + 2];
        veins = new Vein[veinCount];

        var mainCurve = new AnimationCurve(new Keyframe(0, 1.2f), new Keyframe(1, 0.35f));
        var forkCurve = new AnimationCurve(new Keyframe(0, 0.8f), new Keyframe(1, 0f));

        for (int i = 0; i < veinCount; i++)
        {
            var v = new Vein();
            v.main = MakeLine("Vein" + i, mainCurve);
            v.mainPts = new Vector3[mainSegments + 1];
            v.fork = new LineRenderer[forkCount];
            v.fStart = new Vector3[forkCount];
            v.fEnd = new Vector3[forkCount];
            v.fT = new float[forkCount];
            for (int j = 0; j < forkCount; j++)
                v.fork[j] = MakeLine("Vein" + i + "_Fork" + j, forkCurve);
            veins[i] = v;
            Respawn(v);
            v.age = Random.value * v.life; // stagger
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
        lr.sharedMaterial = lineMaterial;
        lr.widthCurve = curve;
        lr.widthMultiplier = width;
        lr.positionCount = 0;
        return lr;
    }

    // direction farthest from every other living vein -> even coverage of the whole sphere
    Vector3 PickDirection(Vein self)
    {
        Vector3 best = Random.onUnitSphere;
        float bestScore = float.MaxValue;
        for (int c = 0; c < 10; c++)
        {
            Vector3 cand = Random.onUnitSphere;
            float maxDot = -1f;
            for (int k = 0; k < veins.Length; k++)
            {
                var o = veins[k];
                if (o == null || o == self || o.dir == Vector3.zero) continue;
                float d = Vector3.Dot(cand, o.dir);
                if (d > maxDot) maxDot = d;
            }
            if (maxDot < bestScore) { bestScore = maxDot; best = cand; }
        }
        return best;
    }

    Vector3 ClampShell(Vector3 p)
    {
        float m = p.magnitude;
        return m > shellRadius ? p * (shellRadius / m) : p;
    }

    static Vector3 PointOn(Vector3[] pts, float t)
    {
        float f = Mathf.Clamp01(t) * (pts.Length - 1);
        int i = Mathf.Min(Mathf.FloorToInt(f), pts.Length - 2);
        return Vector3.Lerp(pts[i], pts[i + 1], f - i);
    }

    // geometry is built ONCE per respawn (static, hard-edged = ice), Update only reveals it
    void Respawn(Vein v)
    {
        v.dir = PickDirection(v);
        v.e1 = Vector3.Cross(v.dir, Mathf.Abs(v.dir.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
        v.e2 = Vector3.Cross(v.dir, v.e1);
        v.life = Random.Range(lifeRange.x, lifeRange.y);
        v.age = 0f;
        v.seed = Random.value * 100f;

        // main stem: few straight segments, each kinked in one of 6 hex directions
        float end = shellRadius * mainReach;
        Vector3 lateral = Vector3.zero;
        for (int k = 0; k <= mainSegments; k++)
        {
            float t = k / (float)mainSegments;
            if (k > 0)
            {
                float a = Random.Range(0, 6) * Mathf.PI / 3f;
                lateral = lateral * 0.6f + (v.e1 * Mathf.Cos(a) + v.e2 * Mathf.Sin(a)) * kink;
            }
            v.mainPts[k] = ClampShell(v.dir * Mathf.Lerp(coreRadius, end, t) + lateral);
        }

        // branches: symmetric pairs at branchAngle from the stem, all in one plane
        float phi = Random.value * Mathf.PI * 2f;
        Vector3 side = v.e1 * Mathf.Cos(phi) + v.e2 * Mathf.Sin(phi);
        float ang = branchAngle * Mathf.Deg2Rad;
        float ca = Mathf.Cos(ang), sa = Mathf.Sin(ang);

        for (int i = 0; i < branchPairs; i++)
        {
            float t = Mathf.Lerp(0.25f, 0.7f, (i + 0.5f) / branchPairs);
            float len = branchLength * (1f - 0.18f * i) * Random.Range(0.85f, 1.15f);
            Vector3 s0 = PointOn(v.mainPts, t);
            for (int sg = 0; sg < 2; sg++)
            {
                int j = i * 2 + sg;
                float sign = sg == 0 ? 1f : -1f;
                Vector3 d = (v.dir * ca + side * (sign * sa)).normalized;
                v.fStart[j] = s0;
                v.fEnd[j] = ClampShell(s0 + d * len);
                v.fT[j] = t;
            }
        }

        // tip: the stem splits into two prongs (like a snowflake arm tip)
        Vector3 tip = v.mainPts[mainSegments];
        float ta = ang * 0.6f;
        for (int n = 0; n < 2; n++)
        {
            int j = branchPairs * 2 + n;
            float sign = n == 0 ? 1f : -1f;
            Vector3 d = (v.dir * Mathf.Cos(ta) + side * (sign * Mathf.Sin(ta))).normalized;
            v.fStart[j] = tip;
            v.fEnd[j] = ClampShell(tip + d * tipProngLength);
            v.fT[j] = 1f;
        }
    }

    void Update()
    {
        if (veins == null || lineMaterial == null) return;
        float time = Time.time;

        // stripe width follows the ball's scale (LineRenderer width is NOT scaled by transforms)
        float w = width * widthScale * (followScale ? transform.lossyScale.x : 1f);
        if (minWidth > 0f) w = Mathf.Max(w, minWidth);
        bool widthChanged = !Mathf.Approximately(w, lastWidth);
        lastWidth = w;

        for (int i = 0; i < veins.Length; i++)
        {
            var v = veins[i];
            if (widthChanged)
            {
                v.main.widthMultiplier = w;
                for (int j = 0; j < v.fork.Length; j++) v.fork[j].widthMultiplier = w;
            }
            v.age += Time.deltaTime;
            if (v.age >= v.life) Respawn(v);

            // ease-out growth: shoots out fast, slows down at the end like crystal growth
            float g = Mathf.Clamp01(v.age / growTime);
            float head = 1f - (1f - g) * (1f - g) * (1f - g);
            float die = Mathf.Clamp01((v.life - v.age) / dieTime);
            float shimmer = 0.8f + 0.2f * Mathf.Sin(time * shimmerSpeed + v.seed);
            float bright = die * shimmer;

            // main stem
            float vis = head * mainSegments;
            int full = Mathf.Min(Mathf.FloorToInt(vis), mainSegments);
            float frac = vis - full;
            int count;
            for (int k = 0; k <= full; k++) scratch[k] = v.mainPts[k];
            if (full < mainSegments)
            {
                scratch[full + 1] = Vector3.Lerp(v.mainPts[full], v.mainPts[full + 1], frac);
                count = full + 2;
            }
            else count = mainSegments + 1;

            v.main.positionCount = count;
            v.main.SetPositions(scratch);
            var c = new Color(iceColor.r, iceColor.g, iceColor.b, bright);
            v.main.startColor = c;
            v.main.endColor = c;

            // branches + tip prongs
            var fc = new Color(iceColor.r, iceColor.g, iceColor.b, bright * 0.85f);
            for (int j = 0; j < v.fork.Length; j++)
            {
                var lr = v.fork[j];
                float f = Mathf.Clamp01((head - v.fT[j] * 0.85f) * 3f);
                if (f <= 0f) { lr.positionCount = 0; continue; }
                lr.positionCount = 2;
                lr.SetPosition(0, v.fStart[j]);
                lr.SetPosition(1, Vector3.Lerp(v.fStart[j], v.fEnd[j], f));
                lr.startColor = fc;
                lr.endColor = fc;
            }
        }
    }
}