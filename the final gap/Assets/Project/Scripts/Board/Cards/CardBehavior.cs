using UnityEngine;

public class CardBehaviour : PickableObject, IPinnable
{
    [Header("Card Data")]
    public string cardTitle = "Evidence";
    [TextArea] public string cardDescription = "";

    [Header("Tag")]
    [Tooltip("Invisible category used by the tag connection system. Set this in the Inspector for every card.")]
    [SerializeField] CardTag _cardTag = CardTag.None;
    /// <summary>The invisible tag category used to evaluate corkboard connections.</summary>
    public CardTag CardTag => _cardTag;

    [Header("Snap Settings")]
    [SerializeField] float snapDistance = 1.5f;

    [Header("Pin")]
    [SerializeField] GameObject pinPrefab;
    [SerializeField] Vector3 pinOffset = new Vector3(0f, 0.06f, 0f);

    GameObject _spawnedPin;
    public bool IsPinned { get; private set; }
    public SlotBehaviour CurrentSlot { get; private set; }

    Vector3 _originPosition;
    Quaternion _originRotation;

    protected override void Awake()
    {
        base.Awake();
        promptText = "Pick Up";
    }

    void Start()
    {
        _originPosition = transform.position;
        _originRotation = transform.rotation;
    }

    public override void OnInteract()
    {
        if (IsPinned) return;
        base.OnInteract();
    }

    public override void OnDropped(Vector3 throwVelocity)
    {
        if (GameManager.Instance.CurrentState == GameState.BoardMode)
        {
            SlotBehaviour nearest = FindNearestSlot();
            if (nearest != null && !nearest.IsOccupied)
            {
                nearest.TryOccupy(this);
                return;
            }
        }
        base.OnDropped(throwVelocity);
    }

    public void OnPinned(SlotBehaviour slot)
    {
        IsPinned = true;
        CurrentSlot = slot;
        Rigidbody.isKinematic = false;
        Rigidbody.linearVelocity = Vector3.zero;
        Rigidbody.angularVelocity = Vector3.zero;
        Rigidbody.isKinematic = true;

        transform.position = slot.transform.position + slot.transform.TransformDirection(slot.CardPositionOffset);
        transform.rotation = slot.transform.rotation * slot.CardRotationOffset;

        if (pinPrefab != null)
        {
            _spawnedPin = Instantiate(pinPrefab);
            _spawnedPin.transform.position = transform.position + transform.TransformDirection(pinOffset);
            _spawnedPin.transform.rotation = transform.rotation;

            PinBehaviour pin = _spawnedPin.GetComponentInChildren<PinBehaviour>();
            if (pin != null) pin.Init(this);

            foreach (Transform child in _spawnedPin.GetComponentsInChildren<Transform>())
                child.gameObject.layer = LayerMask.NameToLayer("Interactable");
        }

        GameEvents.CardPinned(this, slot);
    }

    public void OnUnpinned()
    {
        IsPinned = false;
        CurrentSlot = null;
        Rigidbody.isKinematic = false;

        YarnSystem.Instance.RemoveConnectionsForCard(this);

        if (_spawnedPin != null)
        {
            Destroy(_spawnedPin);
            _spawnedPin = null;
        }

        GameEvents.CardUnpinned(this);
    }

    public void ReturnToOrigin()
    {
        Rigidbody.linearVelocity = Vector3.zero;
        Rigidbody.angularVelocity = Vector3.zero;
        transform.position = _originPosition;
        transform.rotation = _originRotation;
    }

    SlotBehaviour FindNearestSlot()
    {
        SlotBehaviour nearest = null;
        float nearestDist = snapDistance;

        foreach (var slot in FindObjectsByType<SlotBehaviour>(FindObjectsSortMode.None))
        {
            float dist = Vector3.Distance(transform.position, slot.transform.position);
            if (dist < nearestDist)
            {
                nearestDist = dist;
                nearest = slot;
            }
        }
        return nearest;
    }
}