using System.Collections;
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

    [Header("Pin")]
    [SerializeField] GameObject pinPrefab;
    [SerializeField] Vector3 pinOffset = new Vector3(0f, 0.06f, 0f);
    [SerializeField] Vector3 pinRotationOffset = Vector3.zero;

    GameObject _spawnedPin;
    public bool IsPinned { get; private set; }

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
            // BoardDragSystem and CardGhostSystem call OnPinned directly.
            // This is a safety fallback in case a hotbar card is dropped in board mode
            // without going through CardGhostSystem.
            OnPinned(transform.position, transform.rotation);
            return;
        }
        base.OnDropped(throwVelocity);
    }

    public void OnPinned(Vector3 worldPosition, Quaternion worldRotation)
    {
        IsPinned = true;

        if (!Rigidbody.isKinematic)
        {
            Rigidbody.linearVelocity  = Vector3.zero;
            Rigidbody.angularVelocity = Vector3.zero;
        }
        Rigidbody.isKinematic = true;

        transform.position = worldPosition;
        transform.rotation = worldRotation;

        // Fire the pinned event immediately so subscribers (EndingManager, etc.) react
        // even though the pin mesh hasn't spawned yet.
        GameEvents.CardPinned(this);

        // Wait one fixed-update so the Rigidbody fully commits its new kinematic position
        // before we read transform.position for pin placement — avoids the one-frame
        // misalignment race condition between card settling and pin spawn.
        StartCoroutine(SpawnPinAfterPhysicsStep());
    }

    IEnumerator SpawnPinAfterPhysicsStep()
    {
        yield return new WaitForFixedUpdate();
        SpawnPin();
    }

    void SpawnPin()
    {
        if (pinPrefab == null) return;

        _spawnedPin = Instantiate(pinPrefab);
        _spawnedPin.transform.position = transform.position + transform.TransformDirection(pinOffset);
        _spawnedPin.transform.rotation = transform.rotation * Quaternion.Euler(pinRotationOffset);

        PinBehaviour pin = _spawnedPin.GetComponentInChildren<PinBehaviour>();
        if (pin != null) pin.Init(this);

        foreach (Transform child in _spawnedPin.GetComponentsInChildren<Transform>())
            child.gameObject.layer = LayerMask.NameToLayer("Interactable");
    }

    public void OnUnpinned()
    {
        IsPinned = false;
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
}
