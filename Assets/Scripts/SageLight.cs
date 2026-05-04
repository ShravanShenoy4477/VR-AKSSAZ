using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[RequireComponent(typeof(XRSimpleInteractable))]
public class SageLight : MonoBehaviour
{
    [Header("Lights")]
    public Light spotLight1;
    public Light spotLight2;
    public Light spotLight3;

    [Header("Settings")]
    public float lightIntensity = 3f;
    public float duration = 3f;

    private XRSimpleInteractable _interactable;
    private bool _isRunning = false;

    private void Awake()
    {
        if (!TryGetComponent(out _interactable))
        {
            Debug.LogError($"[SageLight] Missing XRSimpleInteractable on {gameObject.name}. Add it to the same GameObject.");
            enabled = false;
            return;
        }

        Debug.Log($"[SageLight] Found XRSimpleInteractable on {gameObject.name}");
        _interactable.selectEntered.AddListener(OnSelected);
        _interactable.selectExited.AddListener(OnDeselected);

        SetLightsActive(false, 0f);
        LogLightState("Initialized");
    }

    private void OnDestroy()
    {
        if (_interactable == null) return;
        _interactable.selectEntered.RemoveListener(OnSelected);
        _interactable.selectExited.RemoveListener(OnDeselected);
    }

    private void OnSelected(SelectEnterEventArgs args)
    {
        if (_isRunning)
        {
            Debug.Log("[SageLight] Already running, ignoring select");
            return;
        }

        Debug.Log($"[SageLight] OnSelected detected on {gameObject.name} by {args.interactorObject?.transform?.name ?? "unknown interactor"}");
        StartCoroutine(FadeSequence());
    }

    private void OnDeselected(SelectExitEventArgs args)
    {
        Debug.Log($"[SageLight] OnDeselected detected on {gameObject.name} by {args.interactorObject?.transform?.name ?? "unknown interactor"}");
    }

    private IEnumerator FadeSequence()
    {
        _isRunning = true;
        Debug.Log("[SageLight] Fade sequence started");

        LogLightState("Before activate");
        SetLightsActive(true, lightIntensity);
        Debug.Log($"[SageLight] Lights activated at intensity {lightIntensity}");
        LogLightState("After activate");

        float elapsed = 0f;
        float safeDuration = Mathf.Max(0.01f, duration);
        while (elapsed < safeDuration)
        {
            float t = elapsed / safeDuration;
            float intensity = Mathf.Lerp(lightIntensity, 0f, t);
            SetLightsIntensity(intensity);
            elapsed += Time.deltaTime;
            yield return null;
        }

        SetLightsActive(false, 0f);
        Debug.Log("[SageLight] Lights deactivated, fade complete");
        LogLightState("After deactivate");
        _isRunning = false;
    }

    private void SetLightsActive(bool active, float intensity)
    {
        SetLightState(spotLight1, active, intensity);
        SetLightState(spotLight2, active, intensity);
        SetLightState(spotLight3, active, intensity);
    }

    private void SetLightsIntensity(float intensity)
    {
        SetLightIntensity(spotLight1, intensity);
        SetLightIntensity(spotLight2, intensity);
        SetLightIntensity(spotLight3, intensity);
    }

    private static void SetLightState(Light light, bool active, float intensity)
    {
        if (light == null) return;

        if (light.gameObject != null)
            light.gameObject.SetActive(active);

        light.enabled = active;
        light.intensity = intensity;
    }

    private static void SetLightIntensity(Light light, float intensity)
    {
        if (light == null) return;
        light.intensity = intensity;
    }

    private void LogLightState(string prefix)
    {
        Debug.Log($"[SageLight] {prefix} | " +
                  $"L1={(spotLight1 ? spotLight1.name : "null")} activeSelf={(spotLight1 ? spotLight1.gameObject.activeSelf.ToString() : "null")} activeInHierarchy={(spotLight1 ? spotLight1.gameObject.activeInHierarchy.ToString() : "null")} enabled={(spotLight1 ? spotLight1.enabled.ToString() : "null")} intensity={(spotLight1 ? spotLight1.intensity.ToString("0.00") : "null")} range={(spotLight1 ? spotLight1.range.ToString("0.00") : "null")} | " +
                  $"L2={(spotLight2 ? spotLight2.name : "null")} activeSelf={(spotLight2 ? spotLight2.gameObject.activeSelf.ToString() : "null")} activeInHierarchy={(spotLight2 ? spotLight2.gameObject.activeInHierarchy.ToString() : "null")} enabled={(spotLight2 ? spotLight2.enabled.ToString() : "null")} intensity={(spotLight2 ? spotLight2.intensity.ToString("0.00") : "null")} range={(spotLight2 ? spotLight2.range.ToString("0.00") : "null")} | " +
                  $"L3={(spotLight3 ? spotLight3.name : "null")} activeSelf={(spotLight3 ? spotLight3.gameObject.activeSelf.ToString() : "null")} activeInHierarchy={(spotLight3 ? spotLight3.gameObject.activeInHierarchy.ToString() : "null")} enabled={(spotLight3 ? spotLight3.enabled.ToString() : "null")} intensity={(spotLight3 ? spotLight3.intensity.ToString("0.00") : "null")} range={(spotLight3 ? spotLight3.range.ToString("0.00") : "null")}");
    }
}
