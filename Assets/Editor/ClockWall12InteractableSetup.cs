#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// One-shot wiring for Clock_Wall12: BoxCollider zone + <see cref="ClockProximityTilt"/> (grip while near).
/// </summary>
public static class ClockWall12InteractableSetup
{
    public const string ScenePath = "Assets/Scenes/Props.unity";

    [MenuItem("Tools/VR/Wire Clock_Wall12 Interactable")]
    public static void WireFromMenu()
    {
        WireClockWall12();
    }

    /// <summary>Called by batch mode to save the scene after wiring.</summary>
    public static void WireAndSaveBatch()
    {
        if (!WireClockWall12())
        {
            EditorApplication.Exit(1);
            return;
        }

        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
        EditorApplication.Exit(0);
    }

    static bool WireClockWall12()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        GameObject clock = null;
        foreach (var go in scene.GetRootGameObjects())
            clock = FindNamed(go.transform, "Clock_Wall12");
        if (clock == null)
        {
            Debug.LogError("Clock_Wall12: no GameObject named Clock_Wall12 in " + ScenePath);
            return false;
        }

        if (clock.GetComponent<Collider>() == null)
        {
            var box = clock.AddComponent<BoxCollider>();
            box.size = new Vector3(0.35f, 0.35f, 0.06f);
            box.center = Vector3.zero;
        }

        foreach (var grab in clock.GetComponents<XRGrabInteractable>())
            Object.DestroyImmediate(grab);

        var tilt = clock.GetComponent<ClockProximityTilt>();
        if (tilt == null)
            tilt = clock.AddComponent<ClockProximityTilt>();

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("Clock_Wall12: ClockProximityTilt + collider zone (grip near clock to tilt).");
        return true;
    }

    static GameObject FindNamed(Transform root, string name)
    {
        if (root.name == name)
            return root.gameObject;
        for (var i = 0; i < root.childCount; i++)
        {
            var found = FindNamed(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }
}
#endif
