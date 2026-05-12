using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// Makes heavy furniture (couch, table, etc.) grabbable so the player can
/// slide it to reveal objects underneath.
///
/// Behaviour:
///   - Player grabs and pushes/pulls the piece — it slides on the floor.
///   - Cannot be lifted (Y locked), cannot tip over (rotation locked).
///   - Feels heavy: high damping + capped slide speed.
///
/// Setup in Unity:
///   1. Select the couch in the Hierarchy.
///   2. Add Component → Rigidbody  (leave defaults — script configures it).
///   3. Make sure the couch has at least one Collider.
///   4. Add Component → GrabbableFurniture  (auto-adds XRGrabInteractable).
/// </summary>
[RequireComponent(typeof(XRGrabInteractable))]
[RequireComponent(typeof(Rigidbody))]
public class GrabbableFurniture : MonoBehaviour
{
    [Header("Feel")]
    [Tooltip("Rigidbody mass. Higher = harder to start moving.")]
    public float mass = 50f;

    [Tooltip("Linear drag. Higher = stops faster when released.")]
    public float linearDamping = 12f;

    [Tooltip("Velocity cap while being dragged (m/s). Prevents the couch flying.")]
    public float maxSlideSpeed = 0.6f;

    // ── Private ───────────────────────────────────────────────────────────────
    private XRGrabInteractable _grab;
    private Rigidbody          _rb;
    private float              _floorY;        // Y locked to spawn position
    private bool               _isGrabbed;

    // ── Awake ─────────────────────────────────────────────────────────────────
    private void Awake()
    {
        _rb   = GetComponent<Rigidbody>();
        _grab = GetComponent<XRGrabInteractable>();

        // ── Rigidbody ──────────────────────────────────────────────────────
        _rb.mass            = mass;
        _rb.linearDamping   = linearDamping;
        _rb.angularDamping  = 20f;
        _rb.useGravity      = true;
        // Freeze Y (can't lift) and all rotation (can't tip)
        _rb.constraints     = RigidbodyConstraints.FreezePositionY
                            | RigidbodyConstraints.FreezeRotation;

        // ── XRGrabInteractable ─────────────────────────────────────────────
        // VelocityTracking: XRIT sets rb.velocity each frame to chase the
        // controller. Our RigidbodyConstraints clamp Y automatically.
        _grab.movementType  = XRBaseInteractable.MovementType.VelocityTracking;
        _grab.throwOnDetach = false;       // don't launch the couch when released
        _grab.trackRotation = false;       // don't rotate with the controller

        _grab.selectEntered.AddListener(_ => OnGrabbed());
        _grab.selectExited .AddListener(_ => OnReleased());
    }

    private void Start()
    {
        // Snap initial Y — used to correct any drift
        _floorY = transform.position.y;
    }

    // ── Callbacks ─────────────────────────────────────────────────────────────
    private void OnGrabbed() => _isGrabbed = true;

    private void OnReleased()
    {
        _isGrabbed = false;
        // Kill any residual velocity so the couch doesn't keep sliding
        _rb.linearVelocity  = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
    }

    // ── FixedUpdate ───────────────────────────────────────────────────────────
    private void FixedUpdate()
    {
        if (!_isGrabbed) return;

        // Clamp horizontal velocity so the player can't fling the couch
        Vector3 v = _rb.linearVelocity;
        float   h = new Vector2(v.x, v.z).magnitude;
        if (h > maxSlideSpeed)
        {
            float scale = maxSlideSpeed / h;
            _rb.linearVelocity = new Vector3(v.x * scale, 0f, v.z * scale);
        }
    }

    // ── LateUpdate ────────────────────────────────────────────────────────────
    private void LateUpdate()
    {
        // Hard-lock Y in case of any physics drift
        Vector3 p = transform.position;
        if (p.y != _floorY)
        {
            p.y = _floorY;
            transform.position = p;
            Vector3 v = _rb.linearVelocity;
            _rb.linearVelocity = new Vector3(v.x, 0f, v.z);
        }
    }

    private void OnDestroy()
    {
        if (_grab == null) return;
        _grab.selectEntered.RemoveAllListeners();
        _grab.selectExited .RemoveAllListeners();
    }
}
