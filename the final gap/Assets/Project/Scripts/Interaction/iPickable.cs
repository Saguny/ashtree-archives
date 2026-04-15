using UnityEngine;

public interface IPickable
{
    Rigidbody Rigidbody { get; }
    void OnPickedUp();
    void OnDropped(Vector3 throwVelocity);
}