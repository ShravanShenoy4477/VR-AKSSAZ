using System.Collections;
using UnityEngine;

/// <summary>
/// When the user opens the system menu (Meta / Oculus button), some builds leave odd input or time state.
/// Ensures time scale stays normal when returning to the app.
/// </summary>
public sealed class XRApplicationFocusStabilizer : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        var go = new GameObject("[XRApplicationFocusStabilizer]");
        DontDestroyOnLoad(go);
        go.AddComponent<XRApplicationFocusStabilizer>();
    }

    void OnApplicationPause(bool paused)
    {
        if (!paused)
            StartCoroutine(CoResume());
    }

    void OnApplicationFocus(bool focused)
    {
        if (focused)
            StartCoroutine(CoResume());
    }

    static IEnumerator CoResume()
    {
        yield return null;
        if (Time.timeScale <= 0f)
            Time.timeScale = 1f;
    }
}
