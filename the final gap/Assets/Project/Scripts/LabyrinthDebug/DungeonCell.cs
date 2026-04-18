using UnityEngine;

public enum CellType
{
    Empty,
    Room,
    Hallway,
    Staircase
}

public class DungeonCell
{
    public Vector3Int position;
    public CellType type = CellType.Empty;
    public bool isMainRoom = false;
    public GameObject visual;
    public Color color = Color.white;
    
    public DungeonCell(Vector3Int pos)
    {
        position = pos;
    }
}
