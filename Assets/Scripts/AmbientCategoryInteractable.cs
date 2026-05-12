using System.Collections;
using UnityEngine;
using UnityEngine.XR;

[DisallowMultipleComponent]
public class AmbientCategoryInteractable : MonoBehaviour
{
    static float s_GlobalInteractCooldownUntil;

    public enum MotionKind { Slide, Spin, Tilt, Nudge }

    [SerializeField] MotionKind motionKind = MotionKind.Nudge;
    [SerializeField] float interactRadius = 0.38f;
    [SerializeField] float motionDuration = 0.28f;
    [SerializeField] float slideDistance = 0.28f;
    [SerializeField] float angleDegrees = 24f;
    [SerializeField] Vector3 localAxis = Vector3.up;

    Transform _right;
    Transform _left;
    bool _prevR;
    bool _prevL;
    bool _busy;
    Vector3 _basePos;
    Quaternion _baseRot;
    Coroutine _routine;
    Collider[] _colliders;
    bool[] _colliderTriggerDefaults;
    Rigidbody _rb;
    bool _rbKinematicDefault;
    bool _rbDetectCollisionsDefault;
    Bounds _interactionBounds;
    bool _hasInteractionBounds;

    void Awake()
    {
        _basePos = transform.localPosition;
        _baseRot = transform.localRotation;

        var r = GameObject.Find("RightHandController");
        if (r != null) _right = r.transform;
        var l = GameObject.Find("LeftHandController");
        if (l != null) _left = l.transform;

        EnsureCollider();
        CachePhysicsState();
        CacheInteractionBounds();
    }

    void EnsureCollider()
    {
        if (GetComponent<Collider>() != null) return;
        var rend = GetComponentInChildren<Renderer>();
        if (rend == null) return;
        var box = gameObject.AddComponent<BoxCollider>();
        var b = rend.bounds;
        var c = transform.InverseTransformPoint(b.center);
        box.center = c;
        box.size = new Vector3(
            Mathf.Max(0.06f, b.size.x),
            Mathf.Max(0.06f, b.size.y),
            Mathf.Max(0.06f, b.size.z));
    }

    void CachePhysicsState()
    {
        _colliders = GetComponentsInChildren<Collider>(true);
        _colliderTriggerDefaults = new bool[_colliders.Length];
        for (int i = 0; i < _colliders.Length; i++)
            _colliderTriggerDefaults[i] = _colliders[i] != null && _colliders[i].isTrigger;

        _rb = GetComponent<Rigidbody>();
        if (_rb == null) _rb = GetComponentInChildren<Rigidbody>();
        if (_rb != null)
        {
            _rbKinematicDefault = _rb.isKinematic;
            _rbDetectCollisionsDefault = _rb.detectCollisions;
        }
    }

    void CacheInteractionBounds()
    {
        var renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            _hasInteractionBounds = false;
            return;
        }

        bool found = false;
        Bounds b = default;
        foreach (var r in renderers)
        {
            if (r == null) continue;
            if (!found) { b = r.bounds; found = true; }
            else b.Encapsulate(r.bounds);
        }

        _hasInteractionBounds = found;
        if (!found) return;
        b.Expand(0.16f);
        b.center += Vector3.up * (b.extents.y * 0.15f);
        _interactionBounds = b;
    }

    void SetInteractionCollisionMode(bool interactionActive)
    {
        if (_colliders != null && _colliderTriggerDefaults != null)
        {
            for (int i = 0; i < _colliders.Length; i++)
            {
                var c = _colliders[i];
                if (c == null) continue;
                c.isTrigger = interactionActive ? true : _colliderTriggerDefaults[i];
            }
        }

        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic = interactionActive ? true : _rbKinematicDefault;
            _rb.detectCollisions = interactionActive ? false : _rbDetectCollisionsDefault;
        }
    }

    void Update()
    {
        if (_busy) return;
        if (Time.unscaledTime < s_GlobalInteractCooldownUntil) return;
        bool edgeR = ReadEdge(XRNode.RightHand, ref _prevR);
        bool edgeL = ReadEdge(XRNode.LeftHand, ref _prevL);
        if (!edgeR && !edgeL) return;

        bool nearR = _right != null && DistanceToInteractable(_right.position) <= interactRadius;
        bool nearL = _left != null && DistanceToInteractable(_left.position) <= interactRadius;
        bool tryR = edgeR && nearR;
        bool tryL = edgeL && nearL;
        if (!(tryR || tryL)) return;
        if (tryR && _right != null && !IsNearestForPoint(_right.position)) return;
        if (tryL && _left != null && !IsNearestForPoint(_left.position)) return;
        s_GlobalInteractCooldownUntil = Time.unscaledTime + 0.08f;

        if (tryR) XrHaptics.PulseRight(0.16f, 0.03f);
        if (tryL) XrHaptics.PulseLeft(0.16f, 0.03f);
        InteractableHapticFeedback.ShowWrongOrderCue(transform);

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(AnimatePreview());
    }

    bool IsNearestForPoint(Vector3 handPos)
    {
        float mine = DistanceToInteractable(handPos);
        if (mine > interactRadius) return false;

        foreach (var other in FindObjectsByType<AmbientCategoryInteractable>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (other == null || other == this || !other.isActiveAndEnabled) continue;
            float d = other.DistanceToInteractable(handPos);
            if (d + 0.01f < mine && d <= interactRadius)
                return false;
        }
        return true;
    }

    float DistanceToInteractable(Vector3 point)
    {
        if (_hasInteractionBounds)
            return Vector3.Distance(_interactionBounds.ClosestPoint(point), point);
        return Vector3.Distance(point, transform.position);
    }

    bool ReadEdge(XRNode node, ref bool prev)
    {
        var d = InputDevices.GetDeviceAtXRNode(node);
        if (!d.isValid)
        {
            prev = false;
            return false;
        }

        bool pressed = false;
        d.TryGetFeatureValue(CommonUsages.gripButton, out pressed);
        bool edge = pressed && !prev;
        prev = pressed;
        return edge;
    }

    IEnumerator AnimatePreview()
    {
        _busy = true;
        SetInteractionCollisionMode(true);
        float dur = Mathf.Max(0.12f, motionDuration);

        Vector3 fromPos = transform.localPosition;
        Quaternion fromRot = transform.localRotation;
        Vector3 peakPos = _basePos;
        Quaternion peakRot = _baseRot;

        switch (motionKind)
        {
            case MotionKind.Slide:
                peakPos = _basePos + localAxis.normalized * slideDistance;
                break;
            case MotionKind.Spin:
                peakRot = _baseRot * Quaternion.AngleAxis(angleDegrees, localAxis.normalized);
                break;
            case MotionKind.Tilt:
                peakRot = _baseRot * Quaternion.AngleAxis(angleDegrees, localAxis.normalized);
                break;
            default:
                peakRot = _baseRot * Quaternion.AngleAxis(10f, localAxis.normalized);
                break;
        }

        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / dur);
            u = u * u * (3f - 2f * u);
            transform.localPosition = Vector3.Lerp(fromPos, peakPos, u);
            transform.localRotation = Quaternion.Slerp(fromRot, peakRot, u);
            yield return null;
        }

        t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / dur);
            u = u * u * (3f - 2f * u);
            transform.localPosition = Vector3.Lerp(peakPos, _basePos, u);
            transform.localRotation = Quaternion.Slerp(peakRot, _baseRot, u);
            yield return null;
        }

        transform.localPosition = _basePos;
        transform.localRotation = _baseRot;
        SetInteractionCollisionMode(false);
        _busy = false;
        _routine = null;
    }

    public void Configure(MotionKind kind, Vector3 axis, float angle, float distance)
    {
        motionKind = kind;
        localAxis = axis.sqrMagnitude > 0.0001f ? axis.normalized : Vector3.up;
        angleDegrees = angle;
        slideDistance = distance;
    }

    public void ConfigureInteraction(float radius, float duration)
    {
        interactRadius = Mathf.Max(0.2f, radius);
        motionDuration = Mathf.Max(0.12f, duration);
    }
}
