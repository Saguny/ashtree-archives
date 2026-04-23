using UnityEngine;

/// <summary>
/// Extension method for Camera to calculate oblique projection matrix.
/// Based on Eric Lengyel's paper: "Oblique View Frustum Depth Projection and Clipping"
/// http://www.terathon.com/lengyel/Lengyel-Oblique.pdf
/// 
/// This is required for portal rendering to clip geometry at the portal plane
/// without needing custom clipping planes in shaders.
/// </summary>
public static class CameraExtensions
{
    /// <summary>
    /// Calculates an oblique projection matrix where the near clip plane is replaced
    /// by the given clip plane in camera space.
    /// </summary>
    /// <param name="camera">The camera to base the projection on</param>
    /// <param name="clipPlane">Clip plane in camera space (normal pointing toward camera, w = distance)</param>
    /// <returns>Modified projection matrix</returns>
    public static Matrix4x4 CalculateObliqueMatrix(this Camera camera, Vector4 clipPlane)
    {
        Matrix4x4 projection = camera.projectionMatrix;

        // Calculate the clip-space corner point opposite the clipping plane
        Vector4 q = projection.inverse * new Vector4(
            Mathf.Sign(clipPlane.x),
            Mathf.Sign(clipPlane.y),
            1.0f,
            1.0f
        );

        // Calculate the scaled plane vector
        Vector4 c = clipPlane * (2.0f / (Vector4.Dot(clipPlane, q)));

        // Replace the third row of the projection matrix
        projection[2] = c.x - projection[3];
        projection[6] = c.y - projection[7];
        projection[10] = c.z - projection[11];
        projection[14] = c.w - projection[15];

        return projection;
    }
}