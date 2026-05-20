using UnityEngine;

public interface IPinnable
{
    void OnPinned(Vector3 worldPosition, Quaternion worldRotation);
    void OnUnpinned();
}
