using UnityEngine;

public abstract class Interactable : MonoBehaviour
{
    public string promptText = "Interact";
    public InteractKey interactKey = InteractKey.Either;
    public float minInteractDistance = 0f; // 0 = no distance requirement

    public abstract void OnInteract();

    public virtual void OnFocus() { }
    public virtual void OnLoseFocus() { }

    // Helper to check if player is close enough
    public virtual bool IsWithinInteractDistance(Vector3 playerPos)
    {
        if (minInteractDistance <= 0f) return true;
        return Vector3.Distance(transform.position, playerPos) <= minInteractDistance;
    }
}