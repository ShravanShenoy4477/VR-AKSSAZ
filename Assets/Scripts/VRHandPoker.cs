using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attach this to your VR controller or hand tip GameObject.
/// Each frame it casts a small sphere from the controller position to find
/// any UI Button colliders nearby and pokes them — no Rigidbody or Collider
/// is required on the controller itself.
/// </summary>
public class VRHandPoker : MonoBehaviour
{
    [Tooltip("Radius of the poke sphere in metres. 0.04 = 4 cm (fingertip size).")]
    public float pokeRadius = 0.04f;

    [Tooltip("Which layers to scan. Make sure your Canvas/buttons layer is included.")]
    public LayerMask layerMask = ~0; // Everything by default

    // ── Constants ────────────────────────────────────────────────────────────
    private const float ClickCooldown = 0.60f; // min seconds between repeated pokes
    private static readonly Color HoverColor = new Color(1.00f, 0.90f, 0.30f, 1f);

    // ── Per-collider state ───────────────────────────────────────────────────
    private readonly HashSet<Collider>           _inside      = new HashSet<Collider>();
    private readonly Dictionary<Collider, float> _lastClickAt = new Dictionary<Collider, float>();
    private readonly Dictionary<Collider, Color> _savedColors = new Dictionary<Collider, Color>();

    // Reused buffer — avoids per-frame GC allocations
    private readonly Collider[] _overlapBuffer = new Collider[32];

    private void Update()
    {
        int count = Physics.OverlapSphereNonAlloc(
            transform.position, pokeRadius, _overlapBuffer,
            layerMask, QueryTriggerInteraction.Collide);

        // Build current-frame hit set
        var currentHits = new HashSet<Collider>();
        for (int i = 0; i < count; i++)
            currentHits.Add(_overlapBuffer[i]);

        // ── Enter ─────────────────────────────────────────────────────────
        foreach (var col in currentHits)
        {
            if (_inside.Contains(col)) continue; // already inside, skip
            _inside.Add(col);

            var btn = col.GetComponent<Button>();
            if (btn == null || !btn.interactable) continue;

            // Highlight the button
            var img = col.GetComponent<Image>();
            if (img != null)
            {
                if (!_savedColors.ContainsKey(col))
                    _savedColors[col] = img.color;
                img.color = HoverColor;
            }

            // Click — respect cooldown so a resting hand doesn't spam.
            // Use unscaledTime so the button still works when timeScale = 0
            // (e.g. the Time's Up screen freezes the game but Play Again must fire).
            _lastClickAt.TryGetValue(col, out float last);
            if (Time.unscaledTime - last >= ClickCooldown)
            {
                _lastClickAt[col] = Time.unscaledTime;
                btn.onClick.Invoke();
            }
        }

        // ── Exit ──────────────────────────────────────────────────────────
        var toRemove = new List<Collider>();
        foreach (var col in _inside)
        {
            if (currentHits.Contains(col)) continue;
            toRemove.Add(col);

            // Restore the button's original colour
            var img = col.GetComponent<Image>();
            if (img != null && _savedColors.TryGetValue(col, out Color saved))
                img.color = saved;
        }
        foreach (var col in toRemove)
            _inside.Remove(col);
    }

    // Draws the poke sphere in the Scene view so you can size it easily
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 1f, 0.35f);
        Gizmos.DrawSphere(transform.position, pokeRadius);
        Gizmos.color = new Color(0f, 1f, 1f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, pokeRadius);
    }
}
