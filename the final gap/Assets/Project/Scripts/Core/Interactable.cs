using UnityEngine;

public abstract class Interactable : MonoBehaviour
{
    public string promptText = "Interact";
    public InteractKey interactKey = InteractKey.Either;

    public abstract void OnInteract();

    public virtual void OnFocus() { }
    public virtual void OnLoseFocus() { }
}