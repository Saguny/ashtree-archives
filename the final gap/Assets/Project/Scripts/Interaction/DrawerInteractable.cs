using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Universal cabinet script — handles both sliding drawers and hinged doors.
///
/// ── DRAWER setup ──────────────────────────────────────────────────────────
///   1. Set cabinetType to Drawer.
///   2. Set openAxis (local axis the drawer slides along, usually Z or X).
///   3. Set openDistance (how far in local units the drawer travels).
///
/// ── DOOR setup ────────────────────────────────────────────────────────────
///   1. Set cabinetType to Door.
///   2. IMPORTANT: the door mesh pivot MUST be at the hinge edge (set in Blender/Maya).
///      If the pivot is centered, the door will swing around its middle.
///   3. Set hingeAxis (local axis to rotate around, usually Y = vertical hinge).
///   4. Set openAngle (degrees the door swings open, e.g. 110).
///      Positive = counter-clockwise when viewed from above. Negate if it swings the wrong way.
///
/// ── CONTENTS (both types) ─────────────────────────────────────────────────
///   • Static: drag scene objects into contentObjects[].
///   • Dynamic (ItemPlacementSystem): place ItemSpawnPoint children inside and
///     assign this DrawerInteractable as their drawerOwner.
/// </summary>
public class DrawerInteractable : Interactable
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    public enum CabinetType { Drawer, Door }

    [Header("Cabinet Type")]
    [Tooltip("Drawer = slides along an axis. Door = rotates around a hinge.")]
    [SerializeField] CabinetType cabinetType = CabinetType.Drawer;

    [Tooltip("Speed of the slide / swing animation (lerp factor).")]
    [SerializeField] float animSpeed = 6f;

    // ── Drawer-specific ───────────────────────────────────────────────────────

    [Header("Drawer Settings")]
    [Tooltip("Local axis the drawer slides along. Usually Vector3.forward (Z) or Vector3.right (X).")]
    [SerializeField] Vector3 openAxis = Vector3.forward;

    [Tooltip("How many units the drawer travels when fully open.")]
    [SerializeField] float openDistance = 0.35f;

    // ── Door-specific ─────────────────────────────────────────────────────────

    [Header("Door Settings")]
    [Tooltip("Local axis to rotate around. Usually Vector3.up (Y) for a vertical hinge.")]
    [SerializeField] Vector3 hingeAxis = Vector3.up;

    [Tooltip("Degrees the door swings open. Negate the value if it swings the wrong way.")]
    [SerializeField] float openAngle = 110f;

    // ── Contents ──────────────────────────────────────────────────────────────

    [Header("Contents")]
    [Tooltip("Objects inside this cabinet. Hidden when closed, shown when open. " +
             "Make them children so they move / rotate with the cabinet piece.")]
    [SerializeField] GameObject[] contentObjects = new GameObject[0];

    // ── Audio ─────────────────────────────────────────────────────────────────

    [Header("Audio (optional)")]
    [SerializeField] AudioSource audioSource;
    [SerializeField] AudioClip   openClip;
    [SerializeField] AudioClip   closeClip;

    // ── State ─────────────────────────────────────────────────────────────────

    bool       _isOpen;
    bool       _isAnimating;

    // Drawer
    Vector3    _closedLocalPos;
    Vector3    _openLocalPos;

    // Door
    Quaternion _closedLocalRot;
    Quaternion _openLocalRot;

    // Items registered at runtime by ItemPlacementSystem via ItemSpawnPoint
    readonly List<GameObject> _dynamicItems = new List<GameObject>();

    // ── Unity ─────────────────────────────────────────────────────────────────

    void Awake()
    {
        _closedLocalPos = transform.localPosition;
        _openLocalPos   = _closedLocalPos + openAxis.normalized * openDistance;

        _closedLocalRot = transform.localRotation;
        _openLocalRot   = _closedLocalRot * Quaternion.AngleAxis(openAngle, hingeAxis.normalized);

        SetContentsActive(false);

        promptText = OpenLabel;
    }

    // ── Interactable ──────────────────────────────────────────────────────────

    public override void OnInteract()
    {
        if (_isAnimating) return;

        _isOpen   = !_isOpen;
        promptText = _isOpen ? CloseLabel : OpenLabel;
        GameEvents.FocusInteractable(this); // force tooltip refresh immediately

        PlaySound(_isOpen ? openClip : closeClip);

        // Enable contents immediately on open — they're hidden behind the cabinet
        // geometry, so no pop-in. On close, we wait until animation ends.
        if (_isOpen)
            SetContentsActive(true);

        StopAllCoroutines();

        if (cabinetType == CabinetType.Drawer)
            StartCoroutine(AnimateDrawer(_isOpen ? _openLocalPos : _closedLocalPos));
        else
            StartCoroutine(AnimateDoor(_isOpen ? _openLocalRot : _closedLocalRot));
    }

    // ── Animation coroutines ──────────────────────────────────────────────────

    IEnumerator AnimateDrawer(Vector3 target)
    {
        _isAnimating = true;

        while (Vector3.Distance(transform.localPosition, target) > 0.0005f)
        {
            transform.localPosition = Vector3.Lerp(
                transform.localPosition, target, animSpeed * Time.deltaTime);
            yield return null;
        }

        transform.localPosition = target;
        FinishAnimation();
    }

    IEnumerator AnimateDoor(Quaternion target)
    {
        _isAnimating = true;

        while (Quaternion.Angle(transform.localRotation, target) > 0.05f)
        {
            transform.localRotation = Quaternion.Lerp(
                transform.localRotation, target, animSpeed * Time.deltaTime);
            yield return null;
        }

        transform.localRotation = target;
        FinishAnimation();
    }

    void FinishAnimation()
    {
        _isAnimating = false;

        // Only act on close — disable contents once fully shut
        if (!_isOpen)
            SetContentsActive(false);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    string OpenLabel  => cabinetType == CabinetType.Door ? "Open cabinet"  : "Open drawer";
    string CloseLabel => cabinetType == CabinetType.Door ? "Close cabinet" : "Close drawer";

    void SetContentsActive(bool active)
    {
        foreach (var obj in contentObjects)
            if (obj != null) obj.SetActive(active);

        foreach (var obj in _dynamicItems)
            if (obj != null) obj.SetActive(active);
    }

    void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Snap to open or closed without animation (useful for scene init).</summary>
    public void SetOpenInstant(bool open)
    {
        StopAllCoroutines();
        _isOpen      = open;
        _isAnimating = false;
        promptText   = open ? CloseLabel : OpenLabel;

        if (cabinetType == CabinetType.Drawer)
            transform.localPosition = open ? _openLocalPos : _closedLocalPos;
        else
            transform.localRotation = open ? _openLocalRot : _closedLocalRot;

        SetContentsActive(open);
    }

    public bool IsOpen => _isOpen;

    /// <summary>
    /// Called by ItemSpawnPoint when the ItemPlacementSystem places an item inside.
    /// The item starts inactive and is revealed when the cabinet opens.
    /// </summary>
    public void RegisterSpawnedItem(GameObject item)
    {
        if (item == null) return;
        _dynamicItems.Add(item);
        item.SetActive(false);
    }
}
