using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PickableObject : Interactable, IPickable
{
    public Rigidbody Rigidbody { get; private set; }

    protected virtual void Awake()
    {
        Rigidbody = GetComponent<Rigidbody>();
    }

    public override void OnInteract()
    {
        PickupSystem.Instance.PickUp(this);
    }

    public virtual void OnPickedUp() { }

    public virtual void OnDropped(Vector3 throwVelocity)
    {
        Rigidbody.AddForce(throwVelocity, ForceMode.Impulse);
    }
}