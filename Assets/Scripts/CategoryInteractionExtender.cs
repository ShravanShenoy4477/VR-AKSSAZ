using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Ensures category-wide baseline interactions (preview-only) exist for
/// same-type props that are not part of core puzzle progression scripts.
/// </summary>
[DefaultExecutionOrder(-150)]
public class CategoryInteractionExtender : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoCreate()
    {
        if (FindObjectsByType<CategoryInteractionExtender>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length > 0)
            return;
        var go = new GameObject("[CategoryInteractionExtender]");
        DontDestroyOnLoad(go);
        go.AddComponent<CategoryInteractionExtender>();
    }

    void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        ApplyToScene();
    }

    void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    void OnSceneLoaded(Scene _, LoadSceneMode __)
    {
        ApplyToScene();
    }

    void ApplyToScene()
    {
        var all = FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var t in all)
        {
            if (t == null) continue;
            var go = t.gameObject;
            var n = go.name.ToLowerInvariant();

            if (TryApplyExplicitIrrelevantClutter(go, n))
                continue;

            if (n.Contains("sofa") || n.Contains("couch") || n.Contains("armchair"))
            {
                if (go.GetComponent<AmbientCategoryInteractable>() != null)
                    Destroy(go.GetComponent<AmbientCategoryInteractable>());

                if (!IsTopmostCategoryObject(t, "sofa", "couch", "armchair"))
                    continue;

                var slide = go.GetComponent<SofaProximitySlide>();
                if (slide == null)
                    slide = go.AddComponent<SofaProximitySlide>();
                slide.ApplyReferenceTemplate();
                continue;
            }

            if (n.Contains("globe") && IsTopmostCategoryObject(t, "globe"))
            {
                var ambient = go.GetComponent<AmbientCategoryInteractable>();
                if (ambient != null) Destroy(ambient);

                bool isActualGlobeHierarchy =
                    go.GetComponent<GlobeClueReveal>() != null
                    || go.GetComponentInParent<GlobeClueReveal>() != null
                    || go.GetComponentInChildren<GlobeClueReveal>(true) != null;
                if (isActualGlobeHierarchy)
                {
                    var strayDecoy = go.GetComponent<DecoyGlobeReveal>();
                    if (strayDecoy != null) Destroy(strayDecoy);
                    foreach (var strayChildDecoy in go.GetComponentsInChildren<DecoyGlobeReveal>(true))
                    {
                        if (strayChildDecoy != null)
                            Destroy(strayChildDecoy);
                    }
                    continue;
                }

                var decoy = go.GetComponent<DecoyGlobeReveal>();
                if (decoy == null)
                    decoy = go.AddComponent<DecoyGlobeReveal>();
                continue;
            }

            if (n.Contains("telescope") && IsTopmostCategoryObject(t, "telescope"))
            {
                var ambient = go.GetComponent<AmbientCategoryInteractable>();
                if (ambient != null) Destroy(ambient);
                if (go.GetComponent<DecoyTelescopeReveal>() == null)
                    go.AddComponent<DecoyTelescopeReveal>();
                continue;
            }

            if (HasCorePuzzleInteraction(go))
                continue;

            if (go.GetComponent<AmbientCategoryInteractable>() != null)
                continue;

            if (!IsTopmostCategoryObject(t, "globe", "clock", "book", "note", "journal", "key", "telescope"))
                continue;

            if (n.Contains("globe"))
            {
                var c = go.AddComponent<AmbientCategoryInteractable>();
                c.Configure(AmbientCategoryInteractable.MotionKind.Spin, Vector3.up, 28f, 0f);
                continue;
            }

            if (n.Contains("clock"))
            {
                var ambient = go.GetComponent<AmbientCategoryInteractable>();
                if (ambient != null) Destroy(ambient);
                var clock = go.GetComponent<ClockProximityTilt>();
                if (clock == null)
                    clock = go.AddComponent<ClockProximityTilt>();
                clock.ConfigureAsNonKeyClock();
                continue;
            }

            if ((n.Contains("book") || n.Contains("note") || n.Contains("journal")) &&
                go.GetComponentInChildren<XRGrabInteractable>(true) == null)
            {
                var c = go.AddComponent<AmbientCategoryInteractable>();
                c.Configure(AmbientCategoryInteractable.MotionKind.Nudge, Vector3.up, 0f, 0f);
                continue;
            }

            if (n.Contains("key") && go.GetComponentInChildren<XRGrabInteractable>(true) == null)
            {
                var c = go.AddComponent<AmbientCategoryInteractable>();
                c.Configure(AmbientCategoryInteractable.MotionKind.Nudge, Vector3.up, 0f, 0f);
            }
        }
    }

    static bool HasCorePuzzleInteraction(GameObject go)
    {
        return go.GetComponentInParent<ClueNote>() != null
            || go.GetComponentInParent<ProximityClueNote>() != null
            || go.GetComponentInParent<GlobeClueReveal>() != null
            || go.GetComponentInParent<ClockProximityTilt>() != null
            || go.GetComponentInParent<SofaProximitySlide>() != null
            || go.GetComponentInParent<DecoyNote>() != null
            || go.GetComponentInParent<DecoyGramophoneCD>() != null
            || go.GetComponentInParent<DecoyGramophoneHandle>() != null
            || go.GetComponentInParent<DecoyGlobeReveal>() != null
            || go.GetComponentInParent<DecoyTelescopeReveal>() != null
            || go.GetComponentInParent<DoorProximityHinge>() != null
            || go.GetComponentInParent<SageLight>() != null;
    }

    static bool TryApplyExplicitIrrelevantClutter(GameObject go, string lowerName)
    {
        if (go == null || string.IsNullOrEmpty(lowerName)) return false;

        bool match =
            lowerName.Equals("telescope (1)", StringComparison.Ordinal)
            || lowerName.Equals("cornertable_w11_w17 (1)", StringComparison.Ordinal)
            || lowerName.Equals("globe (1)", StringComparison.Ordinal)
            || lowerName.Equals("armchair_wall9_right (1)", StringComparison.Ordinal)
            || lowerName.Equals("coat hanger (1)", StringComparison.Ordinal);
        if (!match) return false;

        // Force these explicit clutter instances to stay irrelevant:
        // no clue/decoy scripts, no progression hooks.
        RemoveIfPresent<DecoyGlobeReveal>(go);
        RemoveIfPresent<DecoyTelescopeReveal>(go);
        RemoveIfPresent<GlobeClueReveal>(go);
        RemoveIfPresent<ProximityClueNote>(go);
        RemoveIfPresent<ClueNote>(go);
        RemoveIfPresent<SofaProximitySlide>(go);

        var ambient = go.GetComponent<AmbientCategoryInteractable>();
        if (ambient == null)
            ambient = go.AddComponent<AmbientCategoryInteractable>();

        if (lowerName.Equals("globe (1)", StringComparison.Ordinal))
        {
            ambient.Configure(AmbientCategoryInteractable.MotionKind.Spin, Vector3.up, 30f, 0f);
            ambient.ConfigureInteraction(0.68f, 0.34f);
        }
        else if (lowerName.Equals("telescope (1)", StringComparison.Ordinal))
        {
            ambient.Configure(AmbientCategoryInteractable.MotionKind.Tilt, Vector3.right, 16f, 0f);
            ambient.ConfigureInteraction(0.72f, 0.34f);
        }
        else if (lowerName.Equals("armchair_wall9_right (1)", StringComparison.Ordinal))
        {
            Destroy(ambient);
            var slide = go.GetComponent<SofaProximitySlide>();
            if (slide == null)
                slide = go.AddComponent<SofaProximitySlide>();
            slide.ApplyReferenceTemplate();
        }
        else if (lowerName.Equals("cornertable_w11_w17 (1)", StringComparison.Ordinal))
        {
            ambient.Configure(AmbientCategoryInteractable.MotionKind.Nudge, Vector3.up, 0f, 0f);
            ambient.ConfigureInteraction(0.60f, 0.26f);
        }
        else // coat hanger (1)
        {
            ambient.Configure(AmbientCategoryInteractable.MotionKind.Tilt, Vector3.forward, 10f, 0f);
            ambient.ConfigureInteraction(0.62f, 0.28f);
        }

        return true;
    }

    static void RemoveIfPresent<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c != null) Destroy(c);
    }

    static bool IsTopmostCategoryObject(Transform t, params string[] tokens)
    {
        if (t == null) return false;
        string name = t.name.ToLowerInvariant();
        bool selfMatch = false;
        foreach (var token in tokens)
        {
            if (name.Contains(token))
            {
                selfMatch = true;
                break;
            }
        }
        if (!selfMatch) return false;

        var p = t.parent;
        while (p != null)
        {
            string pn = p.name.ToLowerInvariant();
            foreach (var token in tokens)
            {
                if (pn.Contains(token))
                    return false;
            }
            p = p.parent;
        }
        return true;
    }
}
