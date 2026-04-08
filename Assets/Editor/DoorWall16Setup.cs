#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Creates a simple hinged door rig on "Wall (16)" and wires it:
/// door opens/closes when TableProp_Keys overlaps the knob trigger.
/// </summary>
public static class DoorWall16Setup
{
    const string ScenePath = "Assets/Scenes/Props.unity";

    [MenuItem("Tools/VR/Wire Wall (16) Door Proximity")]
    public static void WireDoorWall16()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var wall = FindNamed(scene.GetRootGameObjects(), "Wall (16)");
        if (wall == null)
        {
            Debug.LogError($"DoorWall16Setup: Could not find GameObject named 'Wall (16)' in {ScenePath}");
            return;
        }

        var keys = GameObject.Find("TableProp_Keys");
        if (keys == null)
        {
            Debug.LogError("DoorWall16Setup: Could not find GameObject named 'TableProp_Keys'.");
            return;
        }

        // Hide the original wall so the door visually replaces it.
        foreach (var renderer in wall.GetComponents<Renderer>())
            renderer.enabled = false;

        foreach (var collider in wall.GetComponents<Collider>())
            collider.enabled = false;

        var wallCollider = wall.GetComponent<Collider>();
        if (wallCollider == null)
        {
            Debug.LogError("DoorWall16Setup: Wall (16) is missing a collider, cannot derive door dimensions.");
            return;
        }

        var wallBox = wallCollider as BoxCollider;
        if (wallBox == null)
        {
            Debug.LogError("DoorWall16Setup: Wall (16) collider is not a BoxCollider.");
            return;
        }

        // Derive the door from the wall collider so it fits the wall panel.
        Vector3 wallCenter = wallBox.center;
        Vector3 wallSize = wallBox.size;

        float doorWidth = wallSize.x * 0.995f;
        float doorHeight = wallSize.y * 0.995f;
        float doorThickness = Mathf.Min(wallSize.z * 0.8f, 0.06f);

        // DoorRoot is placed on the hinge line at the left edge of the door.
        Vector3 doorRootLocalPos = new Vector3(
            wallCenter.x - doorWidth * 0.5f,
            0f,
            wallCenter.z);

        // Mesh center relative to hinge root.
        Vector3 doorMeshLocalPos = new Vector3(
            doorWidth * 0.5f,
            wallCenter.y,
            0f);

        // Knob trigger sits near the free edge of the door.
        Vector3 knobLocalPos = new Vector3(
            doorWidth * 0.82f,
            wallCenter.y,
            doorThickness * 0.6f);
        float knobRadius = Mathf.Clamp(doorWidth * 0.05f, 0.05f, 0.09f);

        // ---- Create DoorRoot ----
        var doorRoot = GameObject.Find("Door_Wall16_Root");
        if (doorRoot == null)
        {
            doorRoot = new GameObject("Door_Wall16_Root");
        }
        doorRoot.transform.SetParent(wall.transform, false);
        doorRoot.transform.localPosition = doorRootLocalPos;
        doorRoot.transform.localRotation = Quaternion.identity;

        // ---- Create HingePivot ----
        var hingePivot = GameObject.Find("Door_Wall16_HingePivot");
        if (hingePivot == null)
        {
            hingePivot = new GameObject("Door_Wall16_HingePivot");
            hingePivot.transform.SetParent(doorRoot.transform, false);
        }
        hingePivot.transform.localPosition = Vector3.zero; // hinge pivot is the DoorRoot origin
        hingePivot.transform.localRotation = Quaternion.identity;

        // ---- Create DoorMesh (a simple cube as the door) ----
        var doorMesh = GameObject.Find("Door_Wall16_Mesh");
        if (doorMesh == null)
        {
            doorMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
            doorMesh.name = "Door_Wall16_Mesh";
            Object.DestroyImmediate(doorMesh.GetComponent<Collider>()); // door mesh collision not needed; proximity handled by knob trigger.
            var mr = doorMesh.GetComponent<MeshRenderer>();
            if (mr != null) mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        doorMesh.transform.SetParent(hingePivot.transform, false);
        doorMesh.transform.localRotation = Quaternion.identity;
        doorMesh.transform.localPosition = doorMeshLocalPos;
        doorMesh.transform.localScale = new Vector3(doorWidth, doorHeight, doorThickness);

        // ---- Create Knob trigger ----
        var knob = GameObject.Find("Door_Wall16_KnobTrigger");
        if (knob == null)
        {
            knob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            knob.name = "Door_Wall16_KnobTrigger";
        }
        knob.transform.SetParent(doorRoot.transform, false);
        knob.transform.localPosition = knobLocalPos;
        knob.transform.localRotation = Quaternion.identity;
        knob.transform.localScale = Vector3.one * (knobRadius * 2f);

        var knobCollider = knob.GetComponent<Collider>();
        if (knobCollider == null)
            knobCollider = knob.AddComponent<SphereCollider>();

        knobCollider.isTrigger = true;

        var knobVisual = GameObject.Find("Door_Wall16_KnobVisual");
        if (knobVisual != null)
            Object.DestroyImmediate(knobVisual);

        var knobRenderer = knob.GetComponent<MeshRenderer>();
        if (knobRenderer != null)
            Object.DestroyImmediate(knobRenderer);

        // ---- Add/Configure DoorProximityHinge ----
        var hinge = knob.GetComponent<DoorProximityHinge>();
        if (hinge == null)
            hinge = knob.AddComponent<DoorProximityHinge>();

        var hingeSo = new SerializedObject(hinge);
        hingeSo.FindProperty("m_HingePivot").objectReferenceValue = hingePivot.transform;
        hingeSo.FindProperty("m_KeyObject").objectReferenceValue = keys;
        hingeSo.FindProperty("m_LocalSwingAxis").vector3Value = Vector3.up;
        hingeSo.FindProperty("m_OpenAngleDegrees").floatValue = 90f;
        hingeSo.FindProperty("m_AnimationDuration").floatValue = 0.8f;
        hingeSo.FindProperty("m_CloseOnExit").boolValue = true;
        hingeSo.FindProperty("m_OpenOnEnter").boolValue = true;
        hingeSo.FindProperty("m_StayOpenAfterUnlock").boolValue = true;
        hingeSo.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("DoorWall16Setup: Door rig wired on Wall (16).");
    }

    static GameObject FindNamed(GameObject[] roots, string name)
    {
        foreach (var r in roots)
        {
            var found = FindNamed(r.transform, name);
            if (found != null) return found;
        }

        return null;
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

