using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class SlotVisual : MonoBehaviour
{
    [Header("Size — match your card dimensions")]
    [SerializeField] float width = 0.16f;
    [SerializeField] float height = 0.13f;
    [SerializeField] float offsetFromBoard = 0.002f;

    [Header("Colors")]
    [SerializeField] Color emptyColor = new Color(0.2f, 1f, 0.3f, 1f);
    [SerializeField] Color nearColor = new Color(0f, 1f, 0.6f, 1f);
    [SerializeField] Color occupiedColor = new Color(1f, 0.15f, 0.15f, 1f);

    [Header("Pulse")]
    [SerializeField] float pulseSpeed = 1.5f;
    [SerializeField] float pulseIntensity = 0.3f;
    [SerializeField] float lineWidth = 0.004f;

    [Header("Snap Detection")]
    [SerializeField] float nearDistance = 1.5f;

    SlotBehaviour _slot;
    LineRenderer _line;
    Material _mat;

    void Awake()
    {
        _slot = GetComponent<SlotBehaviour>();
        _line = GetComponent<LineRenderer>();
        _mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));

        _line.loop = false;
        _line.positionCount = 5;
        _line.useWorldSpace = true;
        _line.startWidth = lineWidth;
        _line.endWidth = lineWidth;
        _line.material = _mat;
        _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    void Update()
    {
        Color final = GetTargetColor(); // move pulse logic only when not clear

        if (final == Color.clear)
        {
            _line.enabled = false;
            return;
        }

        _line.enabled = true;
        float pulse = (Mathf.Sin(Time.time * pulseSpeed) + 1f) / 2f;
        final = new Color(final.r, final.g, final.b, Mathf.Lerp(1f - pulseIntensity, 1f, pulse));
        _line.startColor = final;
        _line.endColor = final;
        _mat.SetColor("_BaseColor", final);
        UpdateCorners();
    }

    void UpdateCorners()
    {
        float hw = width / 2f;
        float hh = height / 2f;
        Vector3 normal = transform.up * offsetFromBoard;

        Vector3 tl = transform.position + transform.forward * hh - transform.right * hw + normal;
        Vector3 tr = transform.position + transform.forward * hh + transform.right * hw + normal;
        Vector3 br = transform.position - transform.forward * hh + transform.right * hw + normal;
        Vector3 bl = transform.position - transform.forward * hh - transform.right * hw + normal;

        _line.SetPosition(0, tl);
        _line.SetPosition(1, tr);
        _line.SetPosition(2, br);
        _line.SetPosition(3, bl);
        _line.SetPosition(4, tl);
    }

    Color GetTargetColor()
    {
        if (GameManager.Instance.CurrentState != GameState.BoardMode)
            return Color.clear;

        bool dragging = BoardDragSystem.Instance != null && BoardDragSystem.Instance.IsDragging;

        if (dragging && Vector3.Distance(transform.position, BoardDragSystem.Instance.DragPosition) < nearDistance)
            return _slot.IsOccupied ? occupiedColor : nearColor;

        return emptyColor;
    }
}