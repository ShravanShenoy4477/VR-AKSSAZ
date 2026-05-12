using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Slightly dims the room at startup (still moody, but readable), adds a soft rim on the Sage statue, and scales ambient.
/// Sage spot trio is left at authored values (usually off until <see cref="SageLight"/> runs).
/// </summary>
[DefaultExecutionOrder(-400)]
public sealed class RoomLightingAtmosphere : MonoBehaviour
{
    [Header("Startup dim (applied in Start)")]
    [Tooltip("Multiply intensity of most lights (point/spot/area). Very low = dark room.")]
    [SerializeField] float initialNonSageLightScale = 0.14f;

    [Tooltip("Multiply directional light intensity at startup.")]
    [SerializeField] float initialDirectionalScale = 0.26f;

    [Tooltip("Multiply RenderSettings ambient sky/equator/ground RGB at startup.")]
    [SerializeField] float initialAmbientColorScale = 0.72f;

    [Tooltip("Multiply RenderSettings.ambientIntensity at startup.")]
    [SerializeField] float initialAmbientIntensityScale = 0.75f;

    [Header("Statue idle fill")]
    [SerializeField] float statueFillIntensity = 0.78f;
    [SerializeField] float statueFillRange = 2.8f;
    [SerializeField] Vector3 statueFillLocalOffset = new Vector3(0f, 1.15f, 0.15f);
    [SerializeField] Color statueFillColor = new Color(1f, 0.93f, 0.82f, 1f);

    [Header("Debug")]
    [SerializeField] bool debugLogs;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (FindObjectsByType<RoomLightingAtmosphere>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0)
            return;
        var go = new GameObject("[RoomLightingAtmosphere]");
        go.AddComponent<RoomLightingAtmosphere>();
    }

    void Start()
    {
        ApplyStartupDarkness();
        TryAddStatueIdleFill();
    }

    void ApplyStartupDarkness()
    {
        var sky = RenderSettings.ambientSkyColor;
        var eq = RenderSettings.ambientEquatorColor;
        var gr = RenderSettings.ambientGroundColor;
        RenderSettings.ambientSkyColor = ScaleRgb(sky, initialAmbientColorScale);
        RenderSettings.ambientEquatorColor = ScaleRgb(eq, initialAmbientColorScale);
        RenderSettings.ambientGroundColor = ScaleRgb(gr, initialAmbientColorScale);
        RenderSettings.ambientIntensity =
            Mathf.Max(0f, RenderSettings.ambientIntensity * initialAmbientIntensityScale);
        RenderSettings.ambientLight = ScaleRgb(RenderSettings.ambientLight, initialAmbientColorScale);

        var sageSpots = new HashSet<Light>();
        foreach (var sage in FindObjectsByType<SageLight>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (sage.spotLight1 != null) sageSpots.Add(sage.spotLight1);
            if (sage.spotLight2 != null) sageSpots.Add(sage.spotLight2);
            if (sage.spotLight3 != null) sageSpots.Add(sage.spotLight3);
        }

        foreach (var L in FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (L == null || sageSpots.Contains(L)) continue;
            if (L.gameObject.GetComponent<RoomAtmosphereStatueFill>() != null) continue;

            if (L.type == LightType.Directional)
                L.intensity = Mathf.Max(0f, L.intensity * initialDirectionalScale);
            else
                L.intensity = Mathf.Max(0f, L.intensity * initialNonSageLightScale);
        }

        if (debugLogs)
            Debug.Log($"[RoomLightingAtmosphere] Startup dim applied. Sage spot count excluded={sageSpots.Count}.");
    }

    void TryAddStatueIdleFill()
    {
        var sage = FindFirstObjectByType<SageLight>();
        if (sage == null)
        {
            if (debugLogs)
                Debug.Log("[RoomLightingAtmosphere] No SageLight in scene — skip statue idle fill.");
            return;
        }

        var go = new GameObject("Atmosphere_StatueIdleFill");
        go.transform.SetParent(sage.transform, false);
        go.transform.localPosition = statueFillLocalOffset;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        go.AddComponent<RoomAtmosphereStatueFill>();

        var L = go.AddComponent<Light>();
        L.type = LightType.Point;
        L.range = statueFillRange;
        L.color = statueFillColor;
        L.intensity = statueFillIntensity;
        L.shadows = LightShadows.None;
        L.enabled = true;

        if (debugLogs)
            Debug.Log($"[RoomLightingAtmosphere] Statue idle fill on '{sage.name}' intensity={statueFillIntensity}.");
    }

    static Color ScaleRgb(Color c, float s)
    {
        return new Color(c.r * s, c.g * s, c.b * s, c.a);
    }
}
