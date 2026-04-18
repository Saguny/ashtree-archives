using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Runtime fly camera that matches Unity's Scene View controls:
///   RMB held          → look around; enables WASD / Q / E movement
///   Shift (while RMB) → fast mode
///   Scroll wheel      → zoom along view axis (Shift = faster)
///   MMB drag          → pan
///   Alt + LMB drag    → orbit around a point ahead of the camera
///
/// No Input Action assets required — reads devices directly.
/// </summary>
public class FlyCamera : MonoBehaviour
{
    [Header("Fly Movement")]
    public float moveSpeed           = 10f;
    public float fastSpeedMultiplier = 3f;

    [Header("Look")]
    public float lookSensitivity = 0.15f;

    [Header("Zoom")]
    public float scrollSpeed = 2f;

    [Header("Pan (MMB)")]
    public float panSpeed = 0.3f;

    [Header("Orbit (Alt + LMB)")]
    public float orbitDistance = 10f;

    // ── private state ────────────────────────────────────────────────────────
    float _yaw;
    float _pitch;

    // ── lifecycle ────────────────────────────────────────────────────────────

    void Start()
    {
        Vector3 e = transform.eulerAngles;
        _yaw   = e.y;
        _pitch = e.x;
    }

    void Update()
    {
        var mouse    = Mouse.current;
        var keyboard = Keyboard.current;
        if (mouse == null || keyboard == null) return;

        bool rmb = mouse.rightButton.isPressed;
        bool mmb = mouse.middleButton.isPressed;
        bool lmb = mouse.leftButton.isPressed;
        bool alt = keyboard.leftAltKey.isPressed || keyboard.rightAltKey.isPressed;
        bool shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;

        Vector2 delta = mouse.delta.ReadValue();

        // ── Cursor lock while RMB is held ────────────────────────────────────
        if (mouse.rightButton.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }
        if (mouse.rightButton.wasReleasedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }

        // ── Look + Fly (RMB held) ─────────────────────────────────────────────
        if (rmb)
        {
            // Look
            _yaw   += delta.x * lookSensitivity;
            _pitch -= delta.y * lookSensitivity;
            _pitch  = Mathf.Clamp(_pitch, -89f, 89f);
            transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);

            // Fly — WASD + Q/E
            float speed = shift ? moveSpeed * fastSpeedMultiplier : moveSpeed;

            Vector3 dir = Vector3.zero;
            if (keyboard.wKey.isPressed) dir += transform.forward;
            if (keyboard.sKey.isPressed) dir -= transform.forward;
            if (keyboard.dKey.isPressed) dir += transform.right;
            if (keyboard.aKey.isPressed) dir -= transform.right;
            if (keyboard.eKey.isPressed) dir += Vector3.up;
            if (keyboard.qKey.isPressed) dir -= Vector3.up;

            if (dir.sqrMagnitude > 0f)
                transform.position += dir.normalized * speed * Time.deltaTime;
        }

        // ── Scroll zoom ───────────────────────────────────────────────────────
        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            float boost = shift ? fastSpeedMultiplier : 1f;
            transform.position += transform.forward * scroll * scrollSpeed * boost;
        }

        // ── Pan (MMB drag) — ignored while Alt is held ───────────────────────
        if (mmb && !alt)
        {
            transform.position -= transform.right * delta.x * panSpeed;
            transform.position -= transform.up    * delta.y * panSpeed;
        }

        // ── Orbit (Alt + LMB drag) ────────────────────────────────────────────
        // Orbits around a pivot point 'orbitDistance' units in front of the camera.
        if (alt && lmb)
        {
            Vector3 pivot = transform.position + transform.forward * orbitDistance;

            _yaw   += delta.x * lookSensitivity;
            _pitch -= delta.y * lookSensitivity;
            _pitch  = Mathf.Clamp(_pitch, -89f, 89f);

            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            transform.position = pivot - rot * Vector3.forward * orbitDistance;
            transform.rotation = rot;
        }
    }
}
