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
