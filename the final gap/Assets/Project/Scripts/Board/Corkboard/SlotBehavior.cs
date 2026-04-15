using UnityEngine;

public class SlotBehaviour : MonoBehaviour
{
    [Header("Card Placement")]
    [SerializeField] Vector3 cardPositionOffset = Vector3.zero;
    [SerializeField] Vector3 cardRotationOffset = Vector3.zero;

    public Vector3 CardPositionOffset => cardPositionOffset;
    public Quaternion CardRotationOffset => Quaternion.Euler(cardRotationOffset);

    public bool IsOccupied { get; private set; }
    public IPinnable OccupyingItem { get; private set; }

    public bool TryOccupy(IPinnable pinnable)
    {
        if (IsOccupied) return false;
        IsOccupied = true;
        OccupyingItem = pinnable;
        pinnable.OnPinned(this);
        return true;
    }

    public void Vacate()
    {
        if (!IsOccupied) return;
        OccupyingItem?.OnUnpinned();
        IsOccupied = false;
        OccupyingItem = null;
    }
}