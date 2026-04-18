using UnityEngine;

public class CorkboardInteractable : Interactable
{
    [Header("Camera")]
    [SerializeField] Transform cameraTarget;

    [Header("Proximity")]
    [SerializeField] float proximityRadius = 2f;

    bool _playerInRange;


    public override bool IsWithinInteractDistance(Vector3 playerPos)
        => Vector3.Distance(transform.position, playerPos) <= proximityRadius;
    public override void OnInteract()
    {
        if (GameManager.Instance.CurrentState == GameState.BoardMode)
        {
            GameManager.Instance.SetState(GameState.Exploration);
            return;
        }

        if (!_playerInRange) return;

        GameManager.Instance.SetState(GameState.BoardMode);
        CameraSystem.Instance.TransitionTo(cameraTarget);
    }

    void Update()
    {
        if (Camera.main == null) return;
        float dist = Vector3.Distance(Camera.main.transform.position, transform.position);
        bool inRange = dist <= proximityRadius;

        if (inRange != _playerInRange)
        {
            _playerInRange = inRange;
            // Re-fire focus event so TooltipSystem refreshes with updated promptText
            if (_playerInRange)
                GameEvents.FocusInteractable(this);
            else
                GameEvents.FocusInteractable(null);
        }
    }

    public override void OnFocus() => promptText = "Inspect Board";
    public override void OnLoseFocus() => promptText = "";

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, proximityRadius);
    }
}