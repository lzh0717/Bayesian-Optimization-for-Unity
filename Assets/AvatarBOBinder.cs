using System.Reflection;
using UnityEngine;
using BOforUnity;

// Drives an IVARenderer from the Bayesian-Optimization parameter list, BY NAME,
// with ZERO per-parameter code.
//
// Declare a design parameter in the BoForUnityManager inspector whose KEY exactly
// matches a public IVARenderer field (e.g. "mouth_curve", "eye_width",
// "wave_amplitude"), set its lower/upper bounds to that field's meaningful range
// (0..1 for most; 3..100 for *_sides), and this component writes the BO-proposed
// value into the matching field every frame during a run.
//
// Add / remove / rename parameters in the inspector anytime — no code change here.
// This is the INTENDED write path (BO → field); the avatar's blink/smile/wave
// animation never writes these fields, so the two layers don't fight.
//
// Portable: this file + IVARenderer.cs drop into any project that has BoForUnity.
// Only the inspector parameter list and questionnaire are re-created per project.
//
// Setup: put this on the same GameObject as IVARenderer (it auto-finds both the
// renderer and the manager), press Play, run the BO loop.
public class AvatarBOBinder : MonoBehaviour
{
    [Tooltip("The face to drive. Auto-filled from this GameObject if left empty.")]
    public IVARenderer avatar;

    [Tooltip("The BO manager. Auto-found by tag/scene if left empty.")]
    public BoForUnityManager bo;

    [Tooltip("Clamp each written value into the target field's [Range] as a safety net.")]
    public bool clampToFieldRange = true;

    const BindingFlags PublicInstance = BindingFlags.Public | BindingFlags.Instance;

    void Reset()    => AutoWire();
    void OnEnable() => AutoWire();

    void AutoWire()
    {
        if (avatar == null) avatar = GetComponent<IVARenderer>();
        if (bo == null)
        {
            var tagged = GameObject.FindWithTag("BOforUnityManager");
            if (tagged != null) bo = tagged.GetComponent<BoForUnityManager>();
        }
        if (bo == null) bo = FindObjectOfType<BoForUnityManager>();
    }

    // Runs only in Play mode: in the editor you design the resting face by hand;
    // during a run BO owns these fields.
    void Update()
    {
        if (!Application.isPlaying) return;
        Apply();
    }

    // Reads every BO parameter and writes it into the matching IVARenderer field.
    // Parameters whose key isn't a public IVARenderer field are skipped silently
    // (so the same manager can also carry non-avatar parameters).
    public void Apply()
    {
        if (avatar == null || bo == null || bo.parameters == null) return;

        for (int i = 0; i < bo.parameters.Count; i++)
        {
            var p = bo.parameters[i];
            if (p == null || p.value == null || string.IsNullOrWhiteSpace(p.key)) continue;

            FieldInfo f = typeof(IVARenderer).GetField(p.key, PublicInstance);
            if (f == null) continue;

            float v = p.value.Value;
            if (clampToFieldRange) v = ClampToRange(f, v);

            if (f.FieldType == typeof(float))     f.SetValue(avatar, v);
            else if (f.FieldType == typeof(int))  f.SetValue(avatar, Mathf.RoundToInt(v));
            else if (f.FieldType == typeof(bool)) f.SetValue(avatar, v >= 0.5f);
            // other field types (Color, arrays) are intentionally not BO-driven
        }
    }

    static float ClampToRange(FieldInfo f, float v)
    {
        var r = (RangeAttribute)System.Attribute.GetCustomAttribute(f, typeof(RangeAttribute));
        return r == null ? v : Mathf.Clamp(v, r.min, r.max);
    }

#if UNITY_EDITOR
    // Right-click the component → "Log BO ↔ Avatar mapping" to see which parameter
    // keys map to a field and which are typos/unmapped. Handy when wiring up.
    [ContextMenu("Log BO ↔ Avatar mapping")]
    void LogMapping()
    {
        AutoWire();
        if (bo == null || bo.parameters == null) { Debug.LogWarning("AvatarBOBinder: no BoForUnityManager found."); return; }
        foreach (var p in bo.parameters)
        {
            if (p == null || string.IsNullOrWhiteSpace(p.key)) continue;
            var f = typeof(IVARenderer).GetField(p.key, PublicInstance);
            Debug.Log(f != null
                ? $"✓ '{p.key}' → IVARenderer.{p.key} ({f.FieldType.Name})"
                : $"✗ '{p.key}' → no IVARenderer field (typo? not an avatar param?)");
        }
    }
#endif
}
