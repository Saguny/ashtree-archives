using UnityEngine;
using UnityEngine.InputSystem;

public class InteractionSystem : MonoBehaviour
{
    [SerializeField] float interactRange = 3f;
    [SerializeField] LayerMask interactableLayerMask;

    Interactable _currentFocus;
    bool _wasWithinDistance; // Track if we were close enough last frame
    Camera _cam;

    void Awake() => _cam = Camera.main;

    void Update()
    {
        CheckFocus();
        if (_currentFocus == null) return;

        if (GameManager.Instance.CurrentState != GameState.BoardMode)
        {
            if (_currentFocus is PinBehaviour) return;
            if (_currentFocus is CardBehaviour c && c.IsPinned) return;
        }

        bool clickPressed = Mouse.current.leftButton.wasPressedThisFrame;
        bool usePressed = Keyboard.current.eKey.wasPressedThisFrame;

        bool triggered = _currentFocus.interactKey switch
        {
            InteractKey.LeftClick => clickPressed,
            InteractKey.UseKey => usePressed,
            _ => clickPressed || usePressed
        };

        if (triggered) _currentFocus.OnInteract();
    }

    void CheckFocus()
    {
        Vector3 screenPoint = GameManager.Instance.CurrentState == GameState.BoardMode
            ? (Vector3)Mouse.current.position.ReadValue()
            : new Vector3(Screen.width / 2, Screen.height / 2);

        Ray ray = _cam.ScreenPointToRay(screenPoint);

        if (Physics.Raycast(ray, out RaycastHit hit, interactRange, interactableLayerMask))
        {
            Interactable target = hit.collider.GetComponent<Interactable>();
            bool withinDistance = target.IsWithinInteractDistance(_cam.transform.position);

            // Focus changed to a different object
            if (target != _currentFocus)
            {
                _currentFocus?.OnLoseFocus();
                _currentFocus = target;
                _wasWithinDistance = withinDistance;
                _currentFocus?.OnFocus();
                GameEvents.FocusInteractable(_currentFocus);
            }
            // Same object, but distance requirement changed
            else if (withinDistance != _wasWithinDistance)
            {
                _wasWithinDistance = withinDistance;
                GameEvents.FocusInteractable(_currentFocus); // Re-fire event
            }
        }
        else
        {
            if (_currentFocus != null)
            {
                _currentFocus.OnLoseFocus();
                _currentFocus = null;
                _wasWithinDistance = false;
                GameEvents.FocusInteractable(null);
            }
        }
    }
}