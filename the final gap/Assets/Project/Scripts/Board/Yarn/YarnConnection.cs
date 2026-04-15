using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class YarnConnection : MonoBehaviour
{
    [SerializeField] float sagAmount = 0.005f;
    [SerializeField] int segments = 12;
    [SerializeField] float boardOffset = 0.001f;

    LineRenderer _line;
    PinBehaviour _from;
    PinBehaviour _to;
    Transform _boardTransform;

    public PinBehaviour From => _from;
    public PinBehaviour To => _to;

    void Awake()
    {
        _line = GetComponent<LineRenderer>();
        _line.positionCount = segments;
        _line.useWorldSpace = true;
        _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
    }

    public void Init(PinBehaviour from, PinBehaviour to, Material mat, float width, Transform boardTransform)
    {
        _from = from;
        _to = to;
        _boardTransform = boardTransform;
        _line.material = mat;
        _line.startWidth = width;
        _line.endWidth = width;
    }

    void Update()
    {
        if (_from == null || _to == null) return;
        UpdateLine();
    }

    void UpdateLine()
    {
        Vector3 start = _from.transform.position;
        Vector3 end = _to.transform.position;
        Vector3 outward = _boardTransform.forward.normalized * boardOffset;

        for (int i = 0; i < segments; i++)
        {
            float t = i / (float)(segments - 1);
            Vector3 point = Vector3.Lerp(start, end, t);
            point += outward;
            point += outward * Mathf.Sin(t * Mathf.PI) * sagAmount;
            _line.SetPosition(i, point);
        }
    }

    public bool Involves(PinBehaviour pin) => _from == pin || _to == pin;
    public bool Involves(PinBehaviour a, PinBehaviour b) =>
        (_from == a && _to == b) || (_from == b && _to == a);
}