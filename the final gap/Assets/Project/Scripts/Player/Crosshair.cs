using UnityEngine;

public class Crosshair : MonoBehaviour
{
    [SerializeField] float size = 4f;
    [SerializeField] Color color = Color.white;

    Texture2D _dot;

    void Awake()
    {
        _dot = new Texture2D(1, 1);
        _dot.SetPixel(0, 0, color);
        _dot.Apply();
    }

    void OnGUI()
    {
        float x = Screen.width / 2 - size / 2;
        float y = Screen.height / 2 - size / 2;
        GUI.DrawTexture(new Rect(x, y, size, size), _dot);
    }
}