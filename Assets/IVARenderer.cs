using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
public class IVARenderer : MonoBehaviour
{
    [Range(0f, 1f)] public float face_width = 0.5f;
    [Range(0f, 1f)] public float face_height = 0.5f;
    [Range(3f, 100f)] public float face_sides = 100f;
    [Range(0f, 1f)] public float temple_width    = 0.5f;
    [Range(0f, 1f)] public float cheekbone_width = 0.5f;
    [Range(0f, 1f)] public float jaw_width       = 0.5f;
    [Range(0f, 1f)] public float eye_width = 0.15f;
    [Range(0f, 1f)] public float eye_height = 0.08f;
    [Range(0f, 1f)] public float eye_spacing = 0.3f;
    [Range(0f, 1f)] public float eye_position_y = 0.6f;
    [Range(0f, 1f)] public float eye_inner_curve = 0.5f;
    [Range(0f, 1f)] public float eye_outer_curve = 0.5f;
    [Range(0f, 1f)] public float eye_tilt = 0.5f;
    [Range(0f, 1f)] public float eye_roundness = 0f;
    [Range(0f, 1f)] public float pupil_size = 0f;
    [Range(0f, 1f)] public float mouth_width = 0.3f;
    [Range(0f, 1f)] public float mouth_height = 0.1f;
    [Range(0f, 1f)] public float mouth_curve = 0.5f;
    [Range(0f, 1f)] public float mouth_position_y = 0.3f;
    [Range(0f, 1f)] public float nose_width = 0.1f;
    [Range(0f, 1f)] public float nose_height = 0.15f;
    [Range(0f, 1f)] public float nose_curve = 0.5f;
    [Range(0f, 1f)] public float nose_position_y = 0.45f;
    [Range(0f, 1f)] public float ear_width = 0.1f;
    [Range(0f, 1f)] public float ear_height = 0.2f;
    [Range(0f, 1f)] public float ear_curve = 1f;
    [Range(0f, 1f)] public float ear_position_y = 0.5f;
    [Range(0f, 1f)] public float helmet_width  = 0f;
    [Range(0f, 1f)] public float helmet_height = 0.5f;
    [Range(3f, 100f)] public float helmet_sides = 100f;

    [Header("Style")]
    [Range(0f, 1f)] public float stroke_width   = 0.2f;
    [Range(0f, 1f)] public float wave_amplitude = 0f;
    [Range(0f, 3f)] public float wave_speed     = 1f;
    [Range(0f, 1f)] public float glow_intensity = 0f;

    [Header("Siri Colors")]
    // When on, every stroke is tinted with a flowing pink→purple→blue→teal
    // gradient that scrolls around the loop over time (the modern Siri palette),
    // instead of the single lineColor. This only affects COLOR — it does not gate
    // the wave rings below.
    public bool  siri_colors      = false;
    [Range(0f, 3f)] public float color_flow_speed = 1f;
    // Number of extra waveform loops stacked around the face (the layered "Siri
    // orb"). They only appear when wave_amplitude > 0 (the master wave switch);
    // with wave_amplitude = 0 the face is still and no rings show, whatever this is.
    [Range(0, 4)]   public int   wave_rings        = 0;
    [Range(0f, 1f)] public float ring_spacing      = 0.14f;

    [Header("Siri Wave (when form base is flat)")]
    // The authentic ios9 Siri waveform (faithful port of kopiro/siriwave), rendered
    // as additive colored mesh lobes. It is the form base's DEGENERATE state, not a
    // separate element: when the base outline collapses to a flat horizontal form
    // (face_height≈0) and still has a width (face_width>0), the renderer draws THIS
    // wave INSTEAD of the face — the two are mutually exclusive, never both at once.
    // Its horizontal span is read from face_width; energy/speed reuse the Style
    // fields wave_amplitude/wave_speed (amplitude floors so width alone shows a wave).
    [Range(0f, 2f)] public float wave_height      = 1f;   // peak-to-peak (world units)
    [Range(0f, 1f)] public float wave_layer_alpha = 0.7f; // per-lobe alpha (library default)

    [System.Serializable]
    public struct WaveCurveDef { public Color color; public bool supportLine; }

    // Library's default ios9 definition: white support line, then blue / red / green.
    public WaveCurveDef[] wave_definitions =
    {
        new WaveCurveDef { color = new Color(1f, 1f, 1f), supportLine = true },
        new WaveCurveDef { color = new Color(15f/255f,  82f/255f, 169f/255f) }, // blue
        new WaveCurveDef { color = new Color(173f/255f, 57f/255f,  76f/255f) }, // red
        new WaveCurveDef { color = new Color(48f/255f, 220f/255f, 155f/255f) }, // green
    };

    [Header("Rendering")]
    public int segments = 64;
    public Color lineColor = Color.cyan;

    // Modern Siri gradient stops, sampled cyclically so the flow loops seamlessly.
    static readonly Color[] SiriPalette =
    {
        new Color(1.00f, 0.29f, 0.58f), // pink / magenta
        new Color(0.61f, 0.35f, 0.98f), // purple
        new Color(0.26f, 0.55f, 1.00f), // blue
        new Color(0.20f, 0.92f, 0.86f), // teal
    };

    const int MaxRings = 4;
    readonly LineRenderer[] waveRingLines = new LineRenderer[MaxRings];

    // Reused each frame so the animated gradient doesn't allocate per line.
    readonly Gradient           _grad = new Gradient();
    readonly GradientColorKey[] _ck   = new GradientColorKey[8];
    readonly GradientAlphaKey[] _ak   = new GradientAlphaKey[2];

    LineRenderer faceLine;
    LineRenderer leftEyeLine;
    LineRenderer rightEyeLine;
    LineRenderer leftPupilLine;
    LineRenderer rightPupilLine;
    LineRenderer mouthLine;
    LineRenderer noseLine;
    LineRenderer leftEarLine;
    LineRenderer rightEarLine;
    LineRenderer helmetLine;

    // ── ios9 Siri-wave state (integrated; driven by the wave_* fields) ────────
    // Constants and ranges are verbatim from kopiro/siriwave (src/ios9-curve.ts).
    const float WAVE_GRAPH_X      = 25f;
    const float WAVE_AMP_FACTOR   = 0.8f;
    const float WAVE_SPEED_FACTOR = 1f;
    const float WAVE_DEAD_PX      = 2f;
    const float WAVE_ATT_FACTOR   = 4f;
    const float WAVE_DESPAWN      = 0.02f;
    const float WAVE_PIXEL_STEP   = 0.1f;
    const float WAVE_FLAT_EPS     = 0.02f; // face_height at/below this = "form base is flat"
    const float WAVE_OUTLINE_AMP  = 0.22f; // face-outline ripple strength at wave_amplitude=1
    const int   GLOW_LAYERS       = 3;     // concentric bloom copies for a soft glow halo

    static readonly Vector2 WAVE_NOOF       = new Vector2(2f, 5f);
    static readonly Vector2 WAVE_AMP        = new Vector2(0.3f, 1f);
    static readonly Vector2 WAVE_OFF        = new Vector2(-3f, 3f);
    static readonly Vector2 WAVE_WID        = new Vector2(1f, 3f);
    static readonly Vector2 WAVE_SPD        = new Vector2(0.5f, 1f);
    static readonly Vector2 WAVE_DESPAWN_MS = new Vector2(500f, 2000f);

    class WaveGroup
    {
        public int   noOfCurves;
        public float spawnAt, prevMaxY;
        public float[] phases, amplitudes, finalAmplitudes, offsets, speeds, widths, verses, despawn;
        public Mesh  mesh;
    }

    WaveGroup[] waveGroups;
    float _lastWaveTime;
    readonly List<Mesh>     _waveMeshes    = new List<Mesh>();
    readonly List<Material> _waveMaterials = new List<Material>();
    Material _lineMaterial; // one shared vertex-color material for every stroke + glow layer
    readonly List<Vector3>  _wv = new List<Vector3>();
    readonly List<Color>    _wc = new List<Color>();
    readonly List<int>      _wt = new List<int>();

    // Wave energy/speed reuse the Style fields so one "voice" drives it. Amplitude
    // floors at 0.6 so a flat form with only a width already shows a live wave.
    float WaveHeightMax  => wave_height * 0.5f;
    float WaveAmp        => 0.6f + wave_amplitude * 2.4f;   // 0.6 .. 3.0
    float WavePhaseSpeed => wave_speed * 0.2f;              // wave_speed 1 ≈ lib default 0.2
    float WaveSpan       => 2f * face_width;                // matches the flat outline extent

    // Wave mode ⟺ the form base has flattened to a horizontal line but still has a
    // width. In this state every facial feature is already collapsed (their heights
    // scale with face_height), so face_height≈0 is a sufficient, robust trigger.
    bool IsWaveMode() => face_width > 0f && face_height <= WAVE_FLAT_EPS;

    // ── Blink / smile expression animation ─────────────────────────────────────
    // Every public field above is a legitimate Bayesian-optimization design
    // parameter — the BO backend may be reading/writing any of them as part of the
    // current proposed design. So this layer NEVER writes into eye_height,
    // mouth_curve, or any other public field; it only nudges the value used for
    // THIS frame's draw call, then relaxes back. The BO-assigned baseline stays
    // exactly what BO set it to, no matter what the face is doing on screen.
    static readonly Vector2 BLINK_INTERVAL = new Vector2(2.5f, 6f);  // seconds between blinks
    const float BLINK_DURATION = 0.5f;                               // seconds, full close-and-open (slow enough to read the arc)
    static readonly Vector2 SMILE_INTERVAL = new Vector2(8f, 18f);   // seconds between idle smiles
    const float SMILE_DURATION = 1.6f;                               // seconds, rise-hold-fall

    float _nextBlinkAt = -1f, _blinkStartedAt = -10f;
    float _nextSmileAt = -1f, _smileStartedAt = -10f;
    float _blinkAmount, _smileAmount; // 0..1, recomputed every Redraw()

    // Triangular pulse eased on both slopes: 0 at start/end, 1 at the midpoint.
    static float Pulse01(float elapsed, float duration)
    {
        if (elapsed < 0f || elapsed > duration) return 0f;
        float t = elapsed / duration;
        float x = t < 0.5f ? t * 2f : (1f - t) * 2f;
        return Mathf.SmoothStep(0f, 1f, x);
    }

    void UpdateExpression()
    {
        float t = WaveTime();
        if (_nextBlinkAt < 0f) _nextBlinkAt = t + Random.Range(BLINK_INTERVAL.x, BLINK_INTERVAL.y);
        if (_nextSmileAt < 0f) _nextSmileAt = t + Random.Range(SMILE_INTERVAL.x, SMILE_INTERVAL.y);

        if (t >= _nextBlinkAt)
        {
            _blinkStartedAt = t;
            _nextBlinkAt = t + Random.Range(BLINK_INTERVAL.x, BLINK_INTERVAL.y);
        }
        if (t >= _nextSmileAt)
        {
            _smileStartedAt = t;
            _nextSmileAt = t + Random.Range(SMILE_INTERVAL.x, SMILE_INTERVAL.y);
        }

        _blinkAmount = Pulse01(t - _blinkStartedAt, BLINK_DURATION);
        _smileAmount = Pulse01(t - _smileStartedAt, SMILE_DURATION);
    }

#if UNITY_EDITOR
    void OnEnable()
    {
        InitLines();
        UnityEditor.EditorApplication.update += EditorTick;
    }

    void OnDisable()
    {
        UnityEditor.EditorApplication.update -= EditorTick;
        Cleanup();
    }

    void OnValidate()
    {
        face_sides   = Mathf.Round(face_sides);
        helmet_sides = Mathf.Round(helmet_sides);
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this == null) return;
            InitLines();
            Redraw();
        };
    }

    // Keeps the animated waveform (and the always-on blink/smile idle motion)
    // ticking while the editor is not in Play mode. (During Play mode the normal
    // player-loop Update handles it.)
    void EditorTick()
    {
        if (this == null || Application.isPlaying) return;
        Redraw();
        UnityEditor.SceneView.RepaintAll();
    }
#else
    void OnEnable() => InitLines();
    void OnDisable() => Cleanup();
#endif

    void OnDestroy() => Cleanup();

    void Update() => Redraw();

    // Clock that advances both in Play mode and while previewing in the editor,
    // so the Siri waveform animates in either context.
    float WaveTime()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
            return (float)UnityEditor.EditorApplication.timeSinceStartup;
#endif
        return Time.time;
    }

    void InitLines()
    {
        faceLine       = GetOrCreateLine("Face");
        leftEyeLine    = GetOrCreateLine("LeftEye");
        rightEyeLine   = GetOrCreateLine("RightEye");
        leftPupilLine  = GetOrCreateLine("LeftPupil");
        rightPupilLine = GetOrCreateLine("RightPupil");
        mouthLine      = GetOrCreateLine("Mouth");
        noseLine       = GetOrCreateLine("Nose");
        leftEarLine    = GetOrCreateLine("LeftEar");  leftEarLine.loop  = false;
        rightEarLine   = GetOrCreateLine("RightEar"); rightEarLine.loop = false;
        helmetLine     = GetOrCreateLine("Helmet");
        for (int i = 0; i < MaxRings; i++)
            waveRingLines[i] = GetOrCreateLine("WaveRing" + i);
    }

    void Redraw()
    {
        if (faceLine == null || leftEyeLine == null || rightEyeLine == null ||
            leftPupilLine == null || rightPupilLine == null ||
            mouthLine == null || noseLine == null ||
            leftEarLine == null || rightEarLine == null || helmetLine == null)
            InitLines();

        // Siri-wave mode: the form base has collapsed to a flat horizontal outline
        // with a width — draw the authentic ios9 wave INSTEAD of the face. Mutually
        // exclusive: never the face and the wave at the same time.
        if (IsWaveMode())
        {
            HideFaceLines();
            UpdateSiriWave();
            return;
        }
        ClearWaveMeshes();
        UpdateExpression();

        DrawFace(faceLine, face_width, face_height, Mathf.RoundToInt(face_sides));
        DrawHelmet(helmetLine, Mathf.RoundToInt(helmet_sides));

        float eyeX = eye_spacing * face_width * 0.5f;
        float eyeY = (eye_position_y - 0.5f) * face_height;
        float rx   = eye_width  * face_width;
        // Blink shrinks only the drawn eye radius, never the eye_height field
        // itself — at ry=0 the eye bezier collapses to a flat closed-lid line.
        float ry   = eye_height * face_height * (1f - _blinkAmount);

        // Left eye: inner corner is on the right (toward nose), mirrored = false
        // Right eye: inner corner is on the left (toward nose), mirrored = true
        DrawEye(leftEyeLine,  new Vector3(-eyeX, eyeY, 0f), rx, ry, false);
        DrawEye(rightEyeLine, new Vector3( eyeX, eyeY, 0f), rx, ry, true);

        float pr = pupil_size * Mathf.Min(rx, ry);
        DrawEllipse(leftPupilLine,  new Vector3(-eyeX, eyeY, 0f), pr, pr);
        DrawEllipse(rightPupilLine, new Vector3( eyeX, eyeY, 0f), pr, pr);

        float mouthY = (mouth_position_y - 0.5f) * face_height;
        // Smile pulse nudges the drawn curve upward from whatever baseline BO set
        // (additive, not a lerp-to-max, so it reads as a boost at any baseline and
        // never overwrites mouth_curve itself).
        float smileCurve = Mathf.Clamp01(mouth_curve + _smileAmount * 0.35f);
        DrawMouth(mouthLine, new Vector3(0f, mouthY, 0f), mouth_width * face_width, mouth_height * face_height, smileCurve);

        float noseY = (nose_position_y - 0.5f) * face_height;
        DrawNose(noseLine, new Vector3(0f, noseY, 0f), nose_width * face_width, nose_height * face_height, nose_curve);

        float leftCenterDeg = Mathf.Lerp(180f, 90f, ear_position_y);
        float halfSpreadDeg = ear_width * 45f;
        DrawEar(leftEarLine,  leftCenterDeg, halfSpreadDeg, false);
        DrawEar(rightEarLine, leftCenterDeg, halfSpreadDeg, true);

        // Extra waveform loops around the face (the layered "Siri orb"). Activated by
        // wave_amplitude — the master "wave is alive" switch — so wave_amplitude=0
        // means no rings, and wave_rings then chooses how many stack up. siri_colors
        // only tints them (rainbow gradient vs the single lineColor); it no longer
        // gates whether they appear.
        int ringSides = Mathf.RoundToInt(face_sides);
        for (int i = 0; i < MaxRings; i++)
        {
            if (wave_amplitude > 0f && i < wave_rings) DrawWaveRing(waveRingLines[i], i, ringSides);
            else                                       waveRingLines[i].positionCount = 0;
        }

        // Color: flowing Siri gradient, or the single lineColor.
        ApplyLineColor(faceLine,       0.00f);
        ApplyLineColor(leftEyeLine,    0.05f);
        ApplyLineColor(rightEyeLine,   0.05f);
        ApplyLineColor(leftPupilLine,  0.10f);
        ApplyLineColor(rightPupilLine, 0.10f);
        ApplyLineColor(mouthLine,      0.15f);
        ApplyLineColor(noseLine,       0.15f);
        ApplyLineColor(leftEarLine,    0.20f);
        ApplyLineColor(rightEarLine,   0.20f);
        ApplyLineColor(helmetLine,     0.00f);
        for (int i = 0; i < MaxRings; i++)
            ApplyLineColor(waveRingLines[i], 0.18f * (i + 1));

        ApplyGlow(faceLine);
        ApplyGlow(leftEyeLine);
        ApplyGlow(rightEyeLine);
        ApplyGlow(leftPupilLine);
        ApplyGlow(rightPupilLine);
        ApplyGlow(mouthLine);
        ApplyGlow(noseLine);
        ApplyGlow(leftEarLine);
        ApplyGlow(rightEarLine);
        ApplyGlow(helmetLine);
        for (int i = 0; i < MaxRings; i++)
            ApplyGlow(waveRingLines[i]);
    }

    // Zeroes every face/feature stroke (and its glow) so only the Siri wave shows.
    void HideFaceLines()
    {
        LineRenderer[] all =
        {
            faceLine, leftEyeLine, rightEyeLine, leftPupilLine, rightPupilLine,
            mouthLine, noseLine, leftEarLine, rightEarLine, helmetLine,
        };
        foreach (var lr in all)
        {
            if (lr == null) continue;
            lr.positionCount = 0;
            ApplyGlow(lr); // clears the glow child now that the source is empty
        }
        for (int i = 0; i < MaxRings; i++)
            if (waveRingLines[i] != null)
            {
                waveRingLines[i].positionCount = 0;
                ApplyGlow(waveRingLines[i]); // also clear the ring's glow child (no ghost loops)
            }
    }

    // ── Siri color flow ──────────────────────────────────────────────────────

    // Cyclic sample of the palette so 0 and 1 wrap to the same color — this lets
    // the gradient loop seamlessly around a closed stroke and scroll over time.
    Color ColorAt(float u)
    {
        u = Mathf.Repeat(u, 1f);
        float f = u * SiriPalette.Length;
        int   i = Mathf.FloorToInt(f);
        float frac = f - i;
        Color a = SiriPalette[i % SiriPalette.Length];
        Color b = SiriPalette[(i + 1) % SiriPalette.Length];
        return Color.Lerp(a, b, frac);
    }

    // Fills the reused _grad with 8 palette keys (LineRenderer's max) scrolled by
    // phase. Returns the shared instance — assigning it to a LineRenderer's
    // colorGradient copies the keys, so reuse across lines is safe.
    Gradient SiriGradient(float phase, float alpha)
    {
        const int K = 7; // 8 keys: j = 0..7
        for (int j = 0; j <= K; j++)
        {
            float pos = j / (float)K;
            _ck[j] = new GradientColorKey(ColorAt(pos + phase), pos);
        }
        _ak[0] = new GradientAlphaKey(alpha, 0f);
        _ak[1] = new GradientAlphaKey(alpha, 1f);
        _grad.SetKeys(_ck, _ak);
        return _grad;
    }

    float ColorPhase() => WaveTime() * color_flow_speed * 0.12f;

    // Colors shimmer through the Siri palette whenever there is a wave — or when
    // siri_colors forces it on with no wave. So "there's a wave" ⇒ "it flows colors".
    bool ColorsFlow => siri_colors || wave_amplitude > 0f;

    void ApplyLineColor(LineRenderer lr, float phaseOffset)
    {
        if (lr == null) return;
        if (ColorsFlow)
        {
            lr.colorGradient = SiriGradient(ColorPhase() + phaseOffset, 1f);
        }
        else
        {
            lr.startColor = lineColor;
            lr.endColor   = lineColor;
        }
    }

    // Radial wave shared by BOTH the face outline (layer 0) and the stacked orb
    // rings (layer 1, 2, 3…). One formula → the outline and the rings ripple as one
    // system. Returns a >= 0 factor (breathe · half-cos), so applied along the
    // radial direction it only bulges outward and never self-intersects. Each layer
    // travels at its own frequency/direction/phase for an organic, non-repeating look.
    float WaveLayerOffset(float angle, int layer, float t)
    {
        float dir     = (layer % 2 == 0) ? 1f : -1f;
        float freq    = 6f + layer * 3f;
        float breathe = 0.8f - 0.2f * Mathf.Cos(t * 1.4f + layer);
        float h       = 0.5f * (1f - Mathf.Cos(angle * freq - dir * t * (1.6f + 0.4f * layer) + layer * 1.3f));
        return breathe * h;
    }

    void DrawWaveRing(LineRenderer lr, int idx, int sides)
    {
        // Size the ring from the face's ACTUAL extent so it tracks the outline as
        // the face widens (temple/cheekbone/jaw scale x up to 2×).
        float regionMax = (sides == 100)
            ? Mathf.Max(temple_width, Mathf.Max(cheekbone_width, jaw_width)) * 2f
            : 1f;
        float faceX = face_width * Mathf.Max(1f, regionMax);
        float faceY = face_height;

        // Rings OVERLAP rather than spread out: ring idx 0 sits right on the face
        // outline's radius, and each next ring is only ring_spacing further out AND
        // layered a hair in front — so every ring overlaps the one before it (and
        // the outline itself): the layered "Siri orb". Its wave frequency still
        // steps up one layer per ring so the overlapping loops don't move in lockstep.
        int   layer = idx + 1;                 // layer 0 is the face outline itself
        float scl   = 1f + ring_spacing * idx; // idx 0 → 1.0 → coincides with the outline
        float rx    = faceX * scl;
        float ry    = faceY * scl;
        float t     = WaveTime() * wave_speed;
        float amp   = Mathf.Max(wave_amplitude, 0.4f); // rings stay visibly wavy

        // Stack each ring a hair in front of the previous, so overlapping
        // semi-transparent loops composite cleanly instead of z-fighting.
        lr.transform.localPosition = new Vector3(0f, 0f, -0.0006f * layer);

        lr.loop = true;
        lr.positionCount = sides;
        for (int i = 0; i < sides; i++)
        {
            float angle = 2f * Mathf.PI * i / sides;
            float nx = Mathf.Cos(angle);
            float ny = Mathf.Sin(angle);
            float x  = nx * rx;
            float y  = ny * ry;

            float off = amp * 0.14f * WaveLayerOffset(angle, layer, t);
            x += off * nx;
            y += off * ny;

            lr.SetPosition(i, new Vector3(x, y, 0f));
        }
    }

    // ── Glow ───────────────────────────────────────────────────────────────

    void ApplyGlow(LineRenderer mainLine)
    {
        Transform parent = mainLine.transform.parent;
        bool needGlow = glow_intensity > 0f && mainLine.positionCount > 1;

        // Retire the old single-layer glow child from earlier versions, if present,
        // so it can't linger as a stale fat outline behind the new layered halo.
        Transform legacy = parent.Find(mainLine.gameObject.name + "_Glow");
        if (legacy != null) SafeDestroy(legacy.gameObject);

        int count = needGlow ? mainLine.positionCount : 0;
        Vector3[] pts = null;
        if (needGlow)
        {
            pts = new Vector3[count];
            mainLine.GetPositions(pts);
        }

        // Several concentric copies, each wider and fainter than the last, so the
        // falloff reads as a soft halo instead of one fat flat outline: near the
        // stroke every layer overlaps (bright core), while only the wide faint
        // layers reach the outer edge (soft glow). Colors flow with the wave.
        for (int j = 0; j < GLOW_LAYERS; j++)
        {
            string glowName = mainLine.gameObject.name + "_Glow" + j;
            Transform glowT = parent.Find(glowName);

            if (!needGlow)
            {
                if (glowT != null)
                {
                    LineRenderer off = glowT.GetComponent<LineRenderer>();
                    if (off != null) off.positionCount = 0;
                }
                continue;
            }

            if (glowT == null)
            {
                glowT = new GameObject(glowName).transform;
                glowT.SetParent(parent, false);
                glowT.gameObject.hideFlags = HideFlags.DontSave; // derived, rebuilt each load
            }

            LineRenderer glow = glowT.GetComponent<LineRenderer>();
            if (glow == null) glow = glowT.gameObject.AddComponent<LineRenderer>();

            glow.useWorldSpace     = mainLine.useWorldSpace;
            glow.loop              = mainLine.loop;
            glow.numCornerVertices = mainLine.numCornerVertices;
            glow.numCapVertices    = mainLine.numCapVertices;

            // Layer j widens and fades as j grows → smooth outward falloff.
            float u          = (j + 1f) / GLOW_LAYERS;                       // 0<..1
            float widthMul   = 1f + glow_intensity * Mathf.Lerp(3f, 16f, u);
            float layerAlpha = glow_intensity * 0.33f * Mathf.Pow(0.5f, j);  // 0.33, 0.165, 0.08

            float w = mainLine.startWidth * widthMul;
            glow.startWidth = w;
            glow.endWidth   = w;

            if (ColorsFlow)
            {
                glow.colorGradient = SiriGradient(ColorPhase(), layerAlpha);
            }
            else
            {
                Color c = lineColor;
                c.a = layerAlpha;
                glow.startColor = c;
                glow.endColor   = c;
            }

            AssignLineMaterial(glow);

            glow.positionCount = count;
            glow.SetPositions(pts);

            // Widest/faintest layer furthest back; all behind the crisp main stroke.
            glowT.localPosition = new Vector3(0f, 0f, 0.001f * (j + 2));
        }
    }

    // Assigns ONE shared vertex-color material to every stroke — all face lines and
    // all glow layers. They differ only in per-LineRenderer color/gradient/width,
    // never in material state, so a single instance is correct; and it means we
    // allocate & free exactly one Material instead of one per line (Unity never GCs
    // Materials, so per-line `new Material` would leak). Created lazily; freed in
    // Cleanup. The shader multiplies by vertex color so the flowing Siri gradient
    // and the single lineColor both render.
    void AssignLineMaterial(LineRenderer lr)
    {
        if (_lineMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default")
                         ?? Shader.Find("Universal Render Pipeline/Particles/Unlit")
                         ?? Shader.Find("Universal Render Pipeline/Unlit")
                         ?? Shader.Find("Unlit/Color");
            if (shader == null) return;
            _lineMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
        }
        if (lr.sharedMaterial != _lineMaterial) lr.sharedMaterial = _lineMaterial;
    }

    // ── Face ───────────────────────────────────────────────────────────────

    void DrawFace(LineRenderer lr, float rx, float ry, int sides)
    {
        // One vertex per side, NO duplicated closing vertex — loop=true joins the
        // last point back to the first, so there's no overlapping seam "blob".
        lr.loop = true;
        lr.positionCount = sides;
        for (int i = 0; i < sides; i++)
        {
            float angle = 2f * Mathf.PI * i / sides;
            float nx    = Mathf.Cos(angle); // -1..1
            float ny    = Mathf.Sin(angle); // -1..1 (1=top, -1=bottom)

            float x = nx * rx;
            float y = ny * ry;

            if (sides == 100)
            {
                // Region weights with smooth transitions
                // top 20%: ny > 0.6, middle 40%: -0.2..0.6, bottom 30%: ny < -0.2
                float wTop = Mathf.SmoothStep(0.5f, 0.7f, ny);
                float wBot = Mathf.SmoothStep(-0.1f, -0.3f, ny);
                float wMid = 1f - wTop - wBot;

                // param * 2 so that default 0.5 → scale 1.0 (no change)
                float scaleX = wTop * (temple_width    * 2f)
                             + wMid * (cheekbone_width * 2f)
                             + wBot * (jaw_width       * 2f);
                x *= scaleX;
            }

            // ── Siri-style animated waveform ─────────────────────────────────
            // The outline IS wave-layer 0 — the exact travelling ripple the stacked
            // orb rings use (see WaveLayerOffset), so the outline and the rings move
            // as one coherent system. The offset is pushed along the point's own
            // radial direction (nx, ny) and kept >= 0, so the outline always bulges
            // outward and can never self-cross as wave_amplitude grows.
            // wave_amplitude=0 gives exactly zero offset (a perfectly still face).
            if (wave_amplitude > 0f)
            {
                float t = WaveTime() * wave_speed;
                float waveOffset = wave_amplitude * WAVE_OUTLINE_AMP * WaveLayerOffset(angle, 0, t);
                x += waveOffset * nx;
                y += waveOffset * ny;
            }

            lr.SetPosition(i, new Vector3(x, y, 0f));
        }
    }

    void DrawHelmet(LineRenderer lr, int sides)
    {
        if (helmet_width <= 0f)
        {
            lr.positionCount = 0;
            return;
        }

        // Always larger than the face outline: scales outward from the face size.
        float rx = face_width  * (1f + helmet_width);
        float ry = face_height * (1f + helmet_height);

        lr.loop = true;
        lr.positionCount = sides;
        for (int i = 0; i < sides; i++)
        {
            float angle = 2f * Mathf.PI * i / sides;
            lr.SetPosition(i, new Vector3(Mathf.Cos(angle) * rx, Mathf.Sin(angle) * ry, 0f));
        }
    }

    // ── Eyes ───────────────────────────────────────────────────────────────

    void DrawEye(LineRenderer lr, Vector3 center, float rx, float ry, bool mirror)
    {
        // Tilt: outer corner rises/drops. inner corner goes opposite direction.
        float tiltOff = (eye_tilt - 0.5f) * ry;

        // Inner/outer curve: independently shift each corner vertically
        float icOff = (eye_inner_curve - 0.5f) * ry;
        float ocOff = (eye_outer_curve - 0.5f) * ry;

        // For left eye: inner = left (-rx), outer = right (+rx)
        Vector2 inner = new Vector2(-rx, -tiltOff + icOff);
        Vector2 outer = new Vector2( rx,  tiltOff + ocOff);

        // Cubic bezier control points for top and bottom halves
        // Top bulges upward by ry, bottom bulges downward by ry
        Vector2 tc1 = new Vector2(inner.x + rx * 0.5f, inner.y + ry);
        Vector2 tc2 = new Vector2(outer.x - rx * 0.5f, outer.y + ry);
        Vector2 bc1 = new Vector2(outer.x - rx * 0.5f, outer.y - ry);
        Vector2 bc2 = new Vector2(inner.x + rx * 0.5f, inner.y - ry);

        int half = Mathf.Max(4, segments / 2);
        int total = half * 2;
        lr.positionCount = total;
        lr.loop = true;

        for (int i = 0; i < half; i++)
        {
            float t = (float)i / half;
            Vector2 bezierP = CubicBezier(inner, tc1, tc2, outer, t);

            float ellipseAngle = Mathf.Lerp(Mathf.PI, 0f, t);
            Vector2 ellipseP = new Vector2(Mathf.Cos(ellipseAngle) * rx, Mathf.Sin(ellipseAngle) * ry);

            Vector2 p = Vector2.Lerp(bezierP, ellipseP, eye_roundness);
            if (mirror) p.x = -p.x;
            lr.SetPosition(i, center + new Vector3(p.x, p.y, 0f));
        }
        for (int i = 0; i < half; i++)
        {
            float t = (float)i / half;
            Vector2 bezierP = CubicBezier(outer, bc1, bc2, inner, t);

            float ellipseAngle = Mathf.Lerp(0f, -Mathf.PI, t);
            Vector2 ellipseP = new Vector2(Mathf.Cos(ellipseAngle) * rx, Mathf.Sin(ellipseAngle) * ry);

            Vector2 p = Vector2.Lerp(bezierP, ellipseP, eye_roundness);
            if (mirror) p.x = -p.x;
            lr.SetPosition(half + i, center + new Vector3(p.x, p.y, 0f));
        }
    }

    Vector2 CubicBezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        float u = 1f - t;
        return u*u*u*p0 + 3*u*u*t*p1 + 3*u*t*t*p2 + t*t*t*p3;
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    void DrawEllipse(LineRenderer lr, Vector3 center, float rx, float ry)
    {
        lr.loop = true;
        lr.positionCount = segments;
        for (int i = 0; i < segments; i++)
        {
            float angle = 2f * Mathf.PI * i / segments;
            lr.SetPosition(i, center + new Vector3(Mathf.Cos(angle) * rx, Mathf.Sin(angle) * ry, 0f));
        }
    }

    void DrawMouth(LineRenderer lr, Vector3 center, float rx, float ry, float curve)
    {
        // Open arc — loop=false so no straight chord is drawn across the smile.
        lr.loop = false;
        lr.positionCount = segments + 1;
        float curvature = (curve - 0.5f) * 2f;
        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float x = Mathf.Lerp(-rx, rx, t);
            float normalizedX = (t - 0.5f) * 2f;
            float y = curvature * ry * (1f - normalizedX * normalizedX);
            lr.SetPosition(i, center + new Vector3(x, y, 0f));
        }
    }

    void DrawNose(LineRenderer lr, Vector3 center, float rx, float ry, float curve)
    {
        if (nose_width <= 0f) { lr.positionCount = 1; lr.SetPosition(0, center); return; }

        lr.loop = false;
        lr.positionCount = segments + 1;
        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            float x, y;
            if (t <= 0.5f)
            {
                float s = t * 2f;
                float linearX = Mathf.Lerp(-rx, 0f, s);
                float linearY = Mathf.Lerp(-ry, ry, s);
                float angle = Mathf.Lerp(Mathf.PI, 0f, s);
                x = Mathf.Lerp(linearX, Mathf.Cos(angle) * rx, curve);
                y = Mathf.Lerp(linearY, Mathf.Sin(angle) * ry, curve);
            }
            else
            {
                float s = (t - 0.5f) * 2f;
                float linearX = Mathf.Lerp(0f, rx, s);
                float linearY = Mathf.Lerp(ry, -ry, s);
                float angle = Mathf.Lerp(0f, -Mathf.PI, s);
                x = Mathf.Lerp(linearX, Mathf.Cos(angle) * rx, curve);
                y = Mathf.Lerp(linearY, Mathf.Sin(angle) * ry, curve);
            }
            lr.SetPosition(i, center + new Vector3(x, y, 0f));
        }
    }

    void DrawEar(LineRenderer lr, float centerDeg, float halfSpreadDeg, bool mirror)
    {
        if (ear_width <= 0f) { lr.positionCount = 0; return; }

        float a1 = (centerDeg - halfSpreadDeg) * Mathf.Deg2Rad;
        float a2 = (centerDeg + halfSpreadDeg) * Mathf.Deg2Rad;
        float aC = centerDeg * Mathf.Deg2Rad;

        Vector2 p1     = new Vector2(face_width * Mathf.Cos(a1), face_height * Mathf.Sin(a1));
        Vector2 p2     = new Vector2(face_width * Mathf.Cos(a2), face_height * Mathf.Sin(a2));
        Vector2 mid    = (p1 + p2) * 0.5f;
        Vector2 outDir = new Vector2(Mathf.Cos(aC), Mathf.Sin(aC)).normalized;
        float   earH   = ear_height * Mathf.Max(face_width, face_height);
        Vector2 tip    = mid + outDir * earH;
        Vector2 ctrl   = mid + outDir * earH * 2f;

        lr.positionCount = segments + 1;
        for (int i = 0; i <= segments; i++)
        {
            float t = (float)i / segments;
            Vector2 pointed = t <= 0.5f
                ? Vector2.Lerp(p1, tip, t * 2f)
                : Vector2.Lerp(tip, p2, (t - 0.5f) * 2f);
            float   u     = 1f - t;
            Vector2 round = u * u * p1 + 2f * u * t * ctrl + t * t * p2;
            Vector2 pos   = Vector2.Lerp(pointed, round, ear_curve);
            if (mirror) pos.x = -pos.x;
            lr.SetPosition(i, new Vector3(pos.x, pos.y, 0f));
        }
    }

    // ── Line setup ─────────────────────────────────────────────────────────

    LineRenderer GetOrCreateLine(string childName)
    {
        Transform child = transform.Find(childName);
        if (child == null)
            child = new GameObject(childName).transform;

        child.SetParent(transform, false);

        LineRenderer lr = child.GetComponent<LineRenderer>();
        if (lr == null)
            lr = child.gameObject.AddComponent<LineRenderer>();

        float width = Mathf.Lerp(0.003f, 0.04f, stroke_width); // thin minimal → thick cartoon

        lr.useWorldSpace = false;
        lr.loop          = true;
        lr.startWidth    = width;
        lr.endWidth      = width;
        lr.startColor    = lineColor;
        lr.endColor      = lineColor;
        lr.numCornerVertices  = 24;
        lr.numCapVertices     = 24;

        AssignLineMaterial(lr);

        return lr;
    }

    // ── ios9 Siri wave (integrated port of kopiro/siriwave) ──────────────────

    // Called only in wave mode. Ticks the spawn/despawn state and rebuilds each
    // colored lobe. The library's per-frame (~60fps) rates are made framerate-
    // independent via frames = dt*60.
    void UpdateSiriWave()
    {
        if (wave_definitions == null || wave_definitions.Length == 0)
        {
            ClearWaveMeshes();
            return;
        }

        EnsureWaveObjects();

        float t  = WaveTime();
        float dt = Mathf.Clamp(t - _lastWaveTime, 0f, 0.1f);
        _lastWaveTime = t;
        float frames = dt * 60f;

        for (int g = 0; g < waveGroups.Length; g++)
        {
            var def = wave_definitions[g];
            var grp = waveGroups[g];
            if (def.supportLine) { BuildWaveSupport(grp, def); continue; }

            if (grp.spawnAt == 0f) SpawnWave(grp, t);
            StepWave(grp, t, frames);
            BuildWaveCurve(grp, def);
        }
    }

    // Lazily create one additive mesh child per definition; rebuild if the
    // definition count changed. Children are transient (rebuilt on load).
    void EnsureWaveObjects()
    {
        int n = wave_definitions.Length;
        if (waveGroups != null && waveGroups.Length == n) return;

        DestroyOwnedWave(); // free meshes/materials from the previous set

        waveGroups = new WaveGroup[n];
        var expected = new HashSet<string>();
        for (int g = 0; g < n; g++)
        {
            var def = wave_definitions[g];
            string childName = def.supportLine ? "SiriWaveSupport" : "SiriWaveCurve" + g;
            expected.Add(childName);

            Transform child = transform.Find(childName);
            if (child == null)
            {
                child = new GameObject(childName).transform;
                child.SetParent(transform, false);
                child.gameObject.hideFlags = HideFlags.DontSave;
            }

            var mf = child.GetComponent<MeshFilter>();   if (mf == null) mf = child.gameObject.AddComponent<MeshFilter>();
            var mr = child.GetComponent<MeshRenderer>();  if (mr == null) mr = child.gameObject.AddComponent<MeshRenderer>();

            var mesh = new Mesh { name = childName + "_Mesh", hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();
            mf.sharedMesh = mesh;
            _waveMeshes.Add(mesh);

            var mat = MakeAdditiveMaterial();
            mat.hideFlags = HideFlags.DontSave;
            mr.sharedMaterial = mat;
            _waveMaterials.Add(mat);

            waveGroups[g] = new WaveGroup { spawnAt = 0f, mesh = mesh };
        }

        PruneWaveChildren(expected);
        _lastWaveTime = WaveTime();
    }

    void PruneWaveChildren(HashSet<string> expected)
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform c = transform.GetChild(i);
            if (c.name.StartsWith("SiriWave") && !expected.Contains(c.name))
                SafeDestroy(c.gameObject);
        }
    }

    void ClearWaveMeshes()
    {
        if (waveGroups == null) return;
        for (int g = 0; g < waveGroups.Length; g++)
            if (waveGroups[g] != null && waveGroups[g].mesh != null) waveGroups[g].mesh.Clear();
    }

    // ── Spawn / step (ports spawn(), spawnSingle(), the per-frame update) ─────
    static float RandW(Vector2 r) => Random.Range(r.x, r.y);

    void SpawnWave(WaveGroup grp, float t)
    {
        grp.spawnAt  = t;
        grp.prevMaxY = 0f;
        int n = Mathf.Max(1, Mathf.FloorToInt(RandW(WAVE_NOOF)));
        grp.noOfCurves = n;

        grp.phases          = new float[n];
        grp.amplitudes      = new float[n];
        grp.finalAmplitudes = new float[n];
        grp.offsets         = new float[n];
        grp.speeds          = new float[n];
        grp.widths          = new float[n];
        grp.verses          = new float[n];
        grp.despawn         = new float[n];

        for (int ci = 0; ci < n; ci++)
        {
            grp.phases[ci]          = 0f;
            grp.amplitudes[ci]      = 0f;
            grp.despawn[ci]         = RandW(WAVE_DESPAWN_MS) / 1000f; // seconds
            grp.offsets[ci]         = RandW(WAVE_OFF);
            grp.speeds[ci]          = RandW(WAVE_SPD);
            grp.finalAmplitudes[ci] = RandW(WAVE_AMP);
            grp.widths[ci]          = RandW(WAVE_WID);
            grp.verses[ci]          = RandW(new Vector2(-1f, 1f));
        }
    }

    void StepWave(WaveGroup grp, float t, float frames)
    {
        for (int ci = 0; ci < grp.noOfCurves; ci++)
        {
            if (grp.spawnAt + grp.despawn[ci] <= t)
                grp.amplitudes[ci] -= WAVE_DESPAWN * frames;
            else
                grp.amplitudes[ci] += WAVE_DESPAWN * frames;

            grp.amplitudes[ci] = Mathf.Clamp(grp.amplitudes[ci], 0f, grp.finalAmplitudes[ci]);
            grp.phases[ci] = Mathf.Repeat(
                grp.phases[ci] + WavePhaseSpeed * grp.speeds[ci] * WAVE_SPEED_FACTOR * frames, 2f * Mathf.PI);
        }
    }

    // ── Math (globalAttFn, yRelativePos, yPos, xPos — verbatim) ───────────────
    static float WaveAtt(float x) => Mathf.Pow(WAVE_ATT_FACTOR / (WAVE_ATT_FACTOR + x * x), WAVE_ATT_FACTOR);

    float WaveYRel(WaveGroup grp, float i)
    {
        float y = 0f;
        int denom = Mathf.Max(1, grp.noOfCurves - 1);
        for (int ci = 0; ci < grp.noOfCurves; ci++)
        {
            float tt = 4f * (-1f + ((float)ci / denom) * 2f) + grp.offsets[ci];
            float k  = 1f / grp.widths[ci];
            float x  = i * k - tt;
            y += Mathf.Abs(grp.amplitudes[ci] * Mathf.Sin(grp.verses[ci] * x - grp.phases[ci]) * WaveAtt(x));
        }
        return y / grp.noOfCurves;
    }

    float WaveYPos(WaveGroup grp, float i) =>
        WAVE_AMP_FACTOR * WaveHeightMax * WaveAmp * WaveYRel(grp, i) * WaveAtt((i / WAVE_GRAPH_X) * 2f);

    // Library xPos maps i∈[-25,25] to [0,span]; we center it on the transform.
    float WaveXPos(float i) => WaveSpan * ((i + WAVE_GRAPH_X) / (WAVE_GRAPH_X * 2f)) - WaveSpan * 0.5f;

    // ── Mesh building ─────────────────────────────────────────────────────────
    void BuildWaveCurve(WaveGroup grp, WaveCurveDef def)
    {
        _wv.Clear(); _wc.Clear(); _wt.Clear();
        Color col = def.color; col.a = wave_layer_alpha;

        float maxY = float.NegativeInfinity;

        // Two mirrored lobes (sign = +1 top, -1 bottom), each filled between the
        // curve and the center line — matching the library's two-pass fill.
        for (int s = 0; s < 2; s++)
        {
            float sign = s == 0 ? 1f : -1f;
            int start = _wv.Count;
            int cols = 0;
            for (float i = -WAVE_GRAPH_X; i <= WAVE_GRAPH_X + 1e-4f; i += WAVE_PIXEL_STEP)
            {
                float x = WaveXPos(i);
                float y = WaveYPos(grp, i);
                if (y > maxY) maxY = y;
                _wv.Add(new Vector3(x, 0f, 0f));         // baseline (center)
                _wv.Add(new Vector3(x, sign * y, 0f));   // curve
                _wc.Add(col); _wc.Add(col);
                cols++;
            }
            for (int cIdx = 0; cIdx < cols - 1; cIdx++)
            {
                int b0 = start + cIdx * 2, c0 = b0 + 1;
                int b1 = start + (cIdx + 1) * 2, c1 = b1 + 1;
                _wt.Add(b0); _wt.Add(c0); _wt.Add(c1);
                _wt.Add(b0); _wt.Add(c1); _wt.Add(b1);
            }
        }

        CommitWave(grp.mesh);

        // Respawn when the group has decayed to nothing (library's DEAD_PX check).
        if (maxY < WAVE_DEAD_PX * (wave_height / 200f) && grp.prevMaxY > maxY) grp.spawnAt = 0f;
        grp.prevMaxY = maxY;
    }

    void BuildWaveSupport(WaveGroup grp, WaveCurveDef def)
    {
        _wv.Clear(); _wc.Clear(); _wt.Clear();

        float thick = Mathf.Max(0.004f, wave_height * 0.012f);
        float x0 = -WaveSpan * 0.5f, x1 = WaveSpan * 0.5f;
        // Alpha gradient along x: transparent → 0.5 → 0.5 → transparent.
        float[] stops = { 0f, 0.1f, 0.8f, 1f };
        float[] al    = { 0f, 0.5f, 0.5f, 0f };
        for (int k = 0; k < stops.Length; k++)
        {
            float x = Mathf.Lerp(x0, x1, stops[k]);
            Color c = def.color; c.a = al[k] * wave_layer_alpha;
            _wv.Add(new Vector3(x, -thick * 0.5f, 0f));
            _wv.Add(new Vector3(x,  thick * 0.5f, 0f));
            _wc.Add(c); _wc.Add(c);
        }
        for (int k = 0; k < stops.Length - 1; k++)
        {
            int b0 = k * 2, t0 = b0 + 1, b1 = (k + 1) * 2, t1 = b1 + 1;
            _wt.Add(b0); _wt.Add(t0); _wt.Add(t1);
            _wt.Add(b0); _wt.Add(t1); _wt.Add(b1);
        }
        CommitWave(grp.mesh);
    }

    void CommitWave(Mesh m)
    {
        m.Clear();
        if (_wv.Count == 0) return;
        m.SetVertices(_wv);
        m.SetColors(_wc);
        m.SetTriangles(_wt, 0);
        m.RecalculateBounds();
    }

    // Additive, vertex-colored, unlit material (the "lighter" compositing that
    // makes overlapping blue/red/green lobes add to the signature Siri white).
    static Material MakeAdditiveMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                     ?? Shader.Find("Particles/Standard Unlit")
                     ?? Shader.Find("Legacy Shaders/Particles/Additive")
                     ?? Shader.Find("Sprites/Default");
        var m = new Material(shader);

        m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        m.SetInt("_ZWrite", 0);
        m.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        if (shader != null && shader.name.Contains("Universal Render Pipeline"))
        {
            m.SetFloat("_Surface", 1f); // Transparent
            m.SetFloat("_Blend", 2f);   // Additive
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
        }
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        return m;
    }

    // ── Teardown ─────────────────────────────────────────────────────────────
    // Unity does not GC meshes/materials, and [ExecuteAlways] re-inits often, so
    // the wave meshes/materials we allocate are destroyed explicitly.
    void Cleanup()
    {
        DestroyOwnedWave();
        SafeDestroy(_lineMaterial); // freed only on real teardown, not on wave rebuild
        _lineMaterial = null;
    }

    void DestroyOwnedWave()
    {
        for (int i = 0; i < _waveMeshes.Count; i++)    SafeDestroy(_waveMeshes[i]);
        for (int i = 0; i < _waveMaterials.Count; i++) SafeDestroy(_waveMaterials[i]);
        _waveMeshes.Clear();
        _waveMaterials.Clear();
        waveGroups = null;
    }

    static void SafeDestroy(Object o)
    {
        if (o == null) return;
#if UNITY_EDITOR
        if (!Application.isPlaying) { DestroyImmediate(o); return; }
#endif
        Destroy(o);
    }
}
