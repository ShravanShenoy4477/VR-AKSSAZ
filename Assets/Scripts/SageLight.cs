using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Statue of the Sage: first interaction turns on highlight lights and leaves them on.
/// Uses the three assigned spots plus optional extras, plus runtime point + spot fills at clue/decoy props and random pockets.
/// </summary>
[RequireComponent(typeof(XRSimpleInteractable))]
public class SageLight : MonoBehaviour
{
    [Header("Primary spots (scene-assigned)")]
    public Light spotLight1;
    public Light spotLight2;
    public Light spotLight3;

    [Header("Optional extra lights (disabled in scene until Sage is used)")]
    public Light[] additionalHighlights;

    [Header("Runtime fill — clue / decoy props")]
    [Tooltip("Point lights added at these script locations (world + offset).")]
    public Vector3 clueLightWorldOffset = new Vector3(0f, 0.85f, 0f);
    [Tooltip("Intensity scale for auto-spawned point lights vs lightIntensity.")]
    [Range(0.02f, 0.5f)] public float runtimePointIntensityScale = 0.034f;
    [Tooltip("Range for auto-spawned point lights (meters).")]
    public float runtimePointRange = 5.5f;

    [Header("Runtime spot fills (aimed at clues / decoys / random)")]
    [Tooltip("Extra spot lights aimed down at each clue & decoy prop (in addition to point fills).")]
    public bool spawnSpotFillsOnInteractableTargets = true;
    [Tooltip("How many extra random spot pools in randomFillBounds (2–3 typical).")]
    [Range(0, 8)] public int randomSpotFillCount = 3;
    [Tooltip("Height above aim point to place the spot (meters).")]
    public float spotRigHeightMeters = 1.45f;
    [Tooltip("Horizontal jitter for spot rig position (meters, seeded).")]
    public float spotRigJitterMeters = 0.38f;
    [Tooltip("World offset added to aim target (e.g. prop center).")]
    public Vector3 spotAimWorldOffset = new Vector3(0f, 0.12f, 0f);
    [Range(20f, 85f)] public float runtimeSpotAngle = 52f;
    public float runtimeSpotRange = 9.5f;
    [Tooltip("Intensity scale for runtime spots vs lightIntensity.")]
    [Range(0.05f, 1.2f)] public float runtimeSpotIntensityScale = 0.52f;

    [Header("Runtime fill — random search pockets")]
    public int randomFillCount = 3;
    public Bounds randomFillBounds = new Bounds(new Vector3(4.5f, -0.4f, -2f), new Vector3(10f, 3.5f, 12f));
    public int randomSeed = 538;

    [Header("Look — focused highlights")]
    [Tooltip("Intensity for assigned spot/additional lights and base for runtime fill points.")]
    public float lightIntensity = 4.1f;
    public Color runtimePointColor = new Color(1f, 0.92f, 0.78f, 1f);

    [Header("Global dimming (once, when Sage first activates)")]
    [Tooltip("Multiply RenderSettings ambient sky/equator/ground RGB (alpha unchanged).")]
    [Range(0.15f, 1f)] public float ambientColorScaleAfterSage = 0.52f;
    [Tooltip("Multiply RenderSettings.ambientIntensity.")]
    [Range(0f, 1f)] public float ambientIntensityScaleAfterSage = 0.55f;
    [Tooltip("Also scale RenderSettings.ambientLight (flat ambient).")]
    [Range(0.15f, 1f)] public float ambientFlatColorScaleAfterSage = 0.52f;
    [Tooltip("Multiply intensity of every directional light in loaded scenes.")]
    public bool dimDirectionalLightsAfterSage = true;
    [Range(0.15f, 1f)] public float directionalIntensityScaleAfterSage = 0.58f;

    [Header("After Sage — soften non–Sage-trio lights")]
    [Tooltip("Multiply scene lights that are NOT the three Sage spots, NOT additionalHighlights, and NOT runtime Sage fills.")]
    [Range(0.02f, 0.5f)] public float postSageNonSpotLightScale = 0.22f;

    [Header("Statue physics")]
    [Tooltip("XR / player collisions won't shove the statue when this is on (Rigidbody becomes kinematic).")]
    [SerializeField] bool lockStatueRigidbody = true;

    [Tooltip("Log once in Awake when rigidbody is locked (paste Unity Console if the statue still moves).")]
    [SerializeField] bool debugStatuePhysics;

    XRSimpleInteractable _interactable;
    bool _activated;
    readonly List<Light> _runtimeLights = new List<Light>(48);
    Transform _runtimeRoot;

    void Awake()
    {
        if (!TryGetComponent(out _interactable))
        {
            enabled = false;
            return;
        }

        _interactable.selectEntered.AddListener(OnSelected);
        SetSpotsActive(false, 0f);
        if (additionalHighlights != null)
        {
            foreach (var L in additionalHighlights)
                SetLightState(L, false, 0f);
        }

        if (lockStatueRigidbody)
        {
            var rb = GetComponent<Rigidbody>();
            if (rb == null)
                rb = GetComponentInChildren<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.useGravity = false;
                rb.mass = Mathf.Max(rb.mass, 800f);
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.constraints = RigidbodyConstraints.FreezeAll;
                if (debugStatuePhysics)
                    Debug.Log($"[SageLight] Locked Rigidbody on '{rb.gameObject.name}': kinematic={rb.isKinematic}, mass={rb.mass}.");
            }
            else if (debugStatuePhysics)
            {
                Debug.Log($"[SageLight] No Rigidbody on '{name}' or children — add one on the statue root if physics should be locked.");
            }
        }
    }

    void OnDestroy()
    {
        if (_interactable != null)
            _interactable.selectEntered.RemoveListener(OnSelected);
        if (_runtimeRoot != null)
            Destroy(_runtimeRoot.gameObject);
    }

    void OnSelected(SelectEnterEventArgs args)
    {
        if (_activated) return;
        _activated = true;
        ActivatePermanent();
        XrHaptics.PulseRight(0.4f, 0.08f);
    }

    void ActivatePermanent()
    {
        SetSpotsActive(true, lightIntensity);
        if (additionalHighlights != null)
        {
            foreach (var L in additionalHighlights)
                SetLightState(L, true, lightIntensity);
        }

        ApplyGlobalDimmingOnce();
        EnsureRuntimeRoot();
        var rng = new System.Random(randomSeed);
        SpawnLightsAtInteractableTargets(rng);
        SpawnRandomFillLights(rng);
        SpawnRandomSpotFills(rng);
        DimAllLightsExceptSageHighlights(postSageNonSpotLightScale);
    }

    void DimAllLightsExceptSageHighlights(float mult)
    {
        var keep = new HashSet<Light>();
        if (spotLight1 != null) keep.Add(spotLight1);
        if (spotLight2 != null) keep.Add(spotLight2);
        if (spotLight3 != null) keep.Add(spotLight3);
        if (additionalHighlights != null)
        {
            foreach (var L in additionalHighlights)
                if (L != null) keep.Add(L);
        }

        foreach (var L in _runtimeLights)
            if (L != null) keep.Add(L);

        foreach (var L in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (L == null || keep.Contains(L)) continue;
            L.intensity = Mathf.Max(0.0001f, L.intensity * mult);
        }
    }

    void ApplyGlobalDimmingOnce()
    {
        var c = ambientColorScaleAfterSage;
        RenderSettings.ambientSkyColor = ScaleRgb(RenderSettings.ambientSkyColor, c);
        RenderSettings.ambientEquatorColor = ScaleRgb(RenderSettings.ambientEquatorColor, c);
        RenderSettings.ambientGroundColor = ScaleRgb(RenderSettings.ambientGroundColor, c);
        RenderSettings.ambientLight = ScaleRgb(RenderSettings.ambientLight, ambientFlatColorScaleAfterSage);
        RenderSettings.ambientIntensity = Mathf.Max(0f, RenderSettings.ambientIntensity * ambientIntensityScaleAfterSage);

        if (!dimDirectionalLightsAfterSage) return;
        var d = directionalIntensityScaleAfterSage;
        foreach (var L in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (L == null || L.type != LightType.Directional) continue;
            L.intensity = Mathf.Max(0f, L.intensity * d);
        }
    }

    static Color ScaleRgb(Color color, float scale)
    {
        return new Color(color.r * scale, color.g * scale, color.b * scale, color.a);
    }

    void EnsureRuntimeRoot()
    {
        if (_runtimeRoot != null) return;
        var go = new GameObject("SageRuntimeHighlights");
        var parent = GameObject.Find("Others");
        go.transform.SetParent(parent != null ? parent.transform : null);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        _runtimeRoot = go.transform;
    }

    void SpawnLightsAtInteractableTargets(System.Random rng)
    {
        foreach (var c in UnityEngine.Object.FindObjectsByType<ClueNote>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var p = c.transform.position + clueLightWorldOffset;
            AddRuntimePoint(p);
            if (spawnSpotFillsOnInteractableTargets)
                AddRuntimeSpotAimedAt(rng, p + spotAimWorldOffset);
        }

        foreach (var p in UnityEngine.Object.FindObjectsByType<ProximityClueNote>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var pos = p.transform.position + clueLightWorldOffset;
            AddRuntimePoint(pos);
            if (spawnSpotFillsOnInteractableTargets)
                AddRuntimeSpotAimedAt(rng, pos + spotAimWorldOffset);
        }

        foreach (var g in UnityEngine.Object.FindObjectsByType<GlobeClueReveal>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var pos = g.transform.position + clueLightWorldOffset;
            AddRuntimePoint(pos);
            if (spawnSpotFillsOnInteractableTargets)
                AddRuntimeSpotAimedAt(rng, pos + spotAimWorldOffset);
        }

        foreach (var d in UnityEngine.Object.FindObjectsByType<DecoyNote>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var pos = d.transform.position + clueLightWorldOffset;
            AddRuntimePoint(pos);
            if (spawnSpotFillsOnInteractableTargets)
                AddRuntimeSpotAimedAt(rng, pos + spotAimWorldOffset);
        }

        foreach (var h in UnityEngine.Object.FindObjectsByType<DecoyGramophoneHandle>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var pos = h.transform.position + clueLightWorldOffset;
            AddRuntimePoint(pos);
            if (spawnSpotFillsOnInteractableTargets)
                AddRuntimeSpotAimedAt(rng, pos + spotAimWorldOffset);
        }

        foreach (var cd in UnityEngine.Object.FindObjectsByType<DecoyGramophoneCD>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var pos = cd.transform.position + clueLightWorldOffset;
            AddRuntimePoint(pos);
            if (spawnSpotFillsOnInteractableTargets)
                AddRuntimeSpotAimedAt(rng, pos + spotAimWorldOffset);
        }
    }

    void SpawnRandomFillLights(System.Random rng)
    {
        for (int i = 0; i < randomFillCount; i++)
        {
            var c = randomFillBounds.center + new Vector3(
                (float)(rng.NextDouble() * 2 - 1) * randomFillBounds.extents.x,
                (float)(rng.NextDouble() * 2 - 1) * randomFillBounds.extents.y,
                (float)(rng.NextDouble() * 2 - 1) * randomFillBounds.extents.z);
            AddRuntimePoint(c);
        }
    }

    void SpawnRandomSpotFills(System.Random rng)
    {
        if (!spawnSpotFillsOnInteractableTargets || randomSpotFillCount <= 0) return;
        for (int i = 0; i < randomSpotFillCount; i++)
        {
            var aim = randomFillBounds.center + new Vector3(
                (float)(rng.NextDouble() * 2 - 1) * randomFillBounds.extents.x,
                (float)(rng.NextDouble() * 2 - 1) * randomFillBounds.extents.y,
                (float)(rng.NextDouble() * 2 - 1) * randomFillBounds.extents.z);
            AddRuntimeSpotAimedAt(rng, aim + spotAimWorldOffset);
        }
    }

    void AddRuntimePoint(Vector3 worldPos)
    {
        var go = new GameObject("SageFillPoint");
        go.transform.SetParent(_runtimeRoot, false);
        go.transform.position = worldPos;
        var L = go.AddComponent<Light>();
        L.type = LightType.Point;
        L.range = runtimePointRange;
        L.color = runtimePointColor;
        L.intensity = lightIntensity * runtimePointIntensityScale;
        L.shadows = LightShadows.None;
        L.enabled = true;
        _runtimeLights.Add(L);
    }

    void AddRuntimeSpotAimedAt(System.Random rng, Vector3 aimWorld)
    {
        float jx = (float)(rng.NextDouble() * 2 - 1) * spotRigJitterMeters;
        float jz = (float)(rng.NextDouble() * 2 - 1) * spotRigJitterMeters;
        Vector3 from = aimWorld + new Vector3(jx, spotRigHeightMeters, jz);
        var dir = aimWorld - from;
        if (dir.sqrMagnitude < 1e-8f)
            dir = Vector3.down;
        else
            dir.Normalize();

        var go = new GameObject("SageFillSpot");
        go.transform.SetParent(_runtimeRoot, false);
        go.transform.position = from;
        go.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

        var L = go.AddComponent<Light>();
        L.type = LightType.Spot;
        L.spotAngle = runtimeSpotAngle;
        L.innerSpotAngle = Mathf.Max(1f, runtimeSpotAngle * 0.66f);
        L.range = runtimeSpotRange;
        L.color = runtimePointColor;
        L.intensity = lightIntensity * runtimeSpotIntensityScale;
        L.shadows = LightShadows.None;
        L.enabled = true;
        _runtimeLights.Add(L);
    }

    void SetSpotsActive(bool active, float intensity)
    {
        SetLightState(spotLight1, active, intensity);
        SetLightState(spotLight2, active, intensity);
        SetLightState(spotLight3, active, intensity);
    }

    static void SetLightState(Light light, bool active, float intensity)
    {
        if (light == null) return;
        if (active)
        {
            light.gameObject.SetActive(true);
            light.enabled = true;
            light.intensity = intensity;
        }
        else
        {
            light.intensity = 0f;
            light.enabled = false;
            light.gameObject.SetActive(false);
        }
    }
}
