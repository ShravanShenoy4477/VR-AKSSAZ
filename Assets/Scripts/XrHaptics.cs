using UnityEngine;
using UnityEngine.XR;

/// <summary>
/// Lightweight haptic pulse for successful grabs / UI actions (Quest-friendly).
/// </summary>
public static class XrHaptics
{
    public static void PulseRight(float amplitude = 0.35f, float durationSeconds = 0.08f)
    {
        var dev = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (!dev.isValid) return;
        dev.SendHapticImpulse(0u, Mathf.Clamp01(amplitude), Mathf.Max(0.005f, durationSeconds));
    }

    public static void PulseLeft(float amplitude = 0.35f, float durationSeconds = 0.08f)
    {
        var dev = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        if (!dev.isValid) return;
        dev.SendHapticImpulse(0u, Mathf.Clamp01(amplitude), Mathf.Max(0.005f, durationSeconds));
    }
}
