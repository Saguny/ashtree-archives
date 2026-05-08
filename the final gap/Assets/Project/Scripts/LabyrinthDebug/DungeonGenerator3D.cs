using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class DungeonGenerator3D : MonoBehaviour, IDungeonGenerator
{
    // ── IDungeonGenerator ────────────────────────────────────────────────────
    // These four members expose the existing state to the interface without
    // touching any of the original algorithm logic below.
    public string GeneratorName => "Classic Dungeon";
    public int CurrentStep => currentStep;
    public int TotalSteps => 6;
    public bool IsAnimating => isAnimating;
    // ────────────────────────────────────────────────────────────────────────

    [Header("Grid Settings")]
    public Vector3Int chunkSize = new Vector3Int(40, 1, 40);
    public float cellSize = 1f;

    [Header("Room Settings")]
    public int roomsPerChunk = 20;
    public Vector3Int minRoomSize = new Vector3Int(4, 1, 4);
    public Vector3Int maxRoomSize = new Vector3Int(10, 1, 10);
    public int maxCorridorsPerRoom = 2;

    [Header("Visualization")]
    public float roomAlpha = 0.5f;
    public float corridorAlpha = 0.8f;
    public Material cellMaterial;
    public Material lineMaterial;
    public float stepDelay = 1.5f;

    // Chunk system
    List<DungeonChunk> chunks = new List<DungeonChunk>();
    DungeonChunk currentChunk;

    // Track room connections (direct corridors only - not T-branches)
    Dictionary<Room, int> roomDirectCorridorCount = new Dictionary<Room, int>();

    // Track corridor paths for branching (PERSISTS ACROSS ALL CHUNKS!)
    Dictionary<Connection, List<Vector3Int>> corridorPaths = new Dictionary<Connection, List<Vector3Int>>();

    // Track corridor-to-corridor bridges (for loop edges between corridors)
    List<CorridorBridge> corridorBridges = new List<CorridorBridge>();

    // Visuals
    GameObject connectionsParent;
    GameObject roomsParent;
    GameObject corridorsParent;
    Material roomMaterial;

    // Algorithm state
    public int currentStep = 0;
    bool isAnimating = false;

    void Start()
    {
        SetupMaterials();
        SetupVisualHierarchy();
        CreateNewChunk(Vector3Int.zero);
    }

    void SetupMaterials()
    {
        if (cellMaterial == null)
        {
            roomMaterial = new Material(Shader.Find("Standard"));
        }
        else
        {
            roomMaterial = new Material(cellMaterial);
        }

        roomMaterial.SetFloat("_Mode", 3);
        roomMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        roomMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        roomMaterial.SetInt("_ZWrite", 0);
        roomMaterial.DisableKeyword("_ALPHATEST_ON");
        roomMaterial.EnableKeyword("_ALPHABLEND_ON");
        roomMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        roomMaterial.renderQueue = 3000;

        if (lineMaterial == null)
        {
            lineMaterial = new Material(Shader.Find("Unlit/Color"));
        }
    }

    void SetupVisualHierarchy()
    {
        connectionsParent = new GameObject("Connections");
        connectionsParent.transform.parent = transform;

        roomsParent = new GameObject("Rooms");
        roomsParent.transform.parent = transform;

        corridorsParent = new GameObject("Corridors");
        corridorsParent.transform.parent = transform;
    }

    void CreateNewChunk(Vector3Int offset)
    {
        DungeonChunk chunk = new DungeonChunk(chunkSize, offset);
        chunks.Add(chunk);
        currentChunk = chunk;
        InitializeChunkGrid(chunk);
    }

    void InitializeChunkGrid(DungeonChunk chunk)
    {
        for (int x = 0; x < chunk.size.x; x++)
        {
            for (int z = 0; z < chunk.size.z; z++)
            {
                Vector3Int localPos = new Vector3Int(x, 0, z);
                Vector3Int worldPos = chunk.offset + localPos;
                chunk.grid[localPos] = new DungeonCell(worldPos);
            }
        }
    }

    public void GenerateNewChunk()
    {
        if (isAnimating) return;

        Vector3Int maxOffset = Vector3Int.zero;
        foreach (var chunk in chunks)
        {
            if (chunk.offset.x > maxOffset.x)
                maxOffset = chunk.offset;
        }

        Vector3Int newOffset = maxOffset + new Vector3Int(chunkSize.x, 0, 0);
        CreateNewChunk(newOffset);

        currentStep = 0;
        // FIX BUG #1: DON'T clear corridorPaths - must persist across chunks for inter-chunk connection!
        // corridorPaths.Clear(); // ❌ REMOVED - This deleted previous chunks' paths
        corridorBridges.Clear(); // OK to clear - these are per-chunk
        StartCoroutine(ExecuteAllSteps());
    }

    public void NextStep()
    {
        if (isAnimating) return;

        currentStep++;
        if (currentStep > 6)
        {
            currentStep = 6;
            return;
        }

        StartCoroutine(ExecuteStep());
    }

    IEnumerator ExecuteAllSteps()
    {
        isAnimating = true;

        for (int step = 1; step <= 6; step++)
        {
            currentStep = step;
            DungeonDebugController.Instance?.UpdateStepDescription(GetStepDescription());

            switch (step)
            {
                case 1: yield return StartCoroutine(GenerateRooms()); break;
                case 2: yield return StartCoroutine(SelectMainRooms()); break;
                case 3: yield return StartCoroutine(DelaunayTriangulation()); break;
                case 4: yield return StartCoroutine(MinimumSpanningTree()); break;
                case 5: yield return StartCoroutine(AddLoopEdges()); break;
                case 6: yield return StartCoroutine(GenerateCorridors()); break;
            }
        }

        yield return StartCoroutine(ConnectAdjacentChunks());
        ValidateAllConnections();

        isAnimating = false;
    }

    IEnumerator ExecuteStep()
    {
        isAnimating = true;
        DungeonDebugController.Instance?.UpdateStepDescription(GetStepDescription());

        switch (currentStep)
        {
            case 1: yield return StartCoroutine(GenerateRooms()); break;
            case 2: yield return StartCoroutine(SelectMainRooms()); break;
            case 3: yield return StartCoroutine(DelaunayTriangulation()); break;
            case 4: yield return StartCoroutine(MinimumSpanningTree()); break;
            case 5: yield return StartCoroutine(AddLoopEdges()); break;
            case 6: yield return StartCoroutine(GenerateCorridors()); break;
        }

        isAnimating = false;
    }

    public string GetStepDescription()
    {
        switch (currentStep)
        {
            case 0: return $"Max {maxCorridorsPerRoom} corridors/room, ∞ corridor branches";
            case 1: return "Generating random rooms";
            case 2: return "Selecting larger rooms as main rooms";
            case 3: return "Delaunay triangulation graph between rooms";
            case 4: return $"MST: max {maxCorridorsPerRoom} corridors/room";
            case 5: return $"Adding loops for variety";
            case 6: return "A*: Carving corridors, end of algorithm";
            default: return $"{chunks.Count} chunks generated";
        }
    }

    void ValidateAllConnections()
    {
        Debug.Log("=== VALIDATION ===");
        foreach (var chunk in chunks)
        {
            foreach (var room in chunk.rooms)
            {
                int count = GetRoomDirectCorridorCount(room);
                if (count > maxCorridorsPerRoom)
                {
                    Debug.LogError($"VIOLATION! Room {room.position} has {count} corridors");
                }
                else if (count == 0)
                {
                    Debug.LogWarning($"ISOLATED! Room {room.position} has NO corridors");
                }
                else
                {
                    Debug.Log($"OK: Room {room.position} has {count} corridor(s)");
                }
            }
        }
        Debug.Log($"Corridor bridges: {corridorBridges.Count}");
    }

    IEnumerator GenerateRooms()
    {
        currentChunk.rooms.Clear();
        int attempts = 0;
        int maxAttempts = roomsPerChunk * 10;

        while (currentChunk.rooms.Count < roomsPerChunk && attempts < maxAttempts)
        {
            attempts++;

            Vector3Int size = new Vector3Int(
                Random.Range(minRoomSize.x, maxRoomSize.x + 1),
                1,
                Random.Range(minRoomSize.z, maxRoomSize.z + 1)
            );

            Vector3Int localPos = new Vector3Int(
                Random.Range(2, chunkSize.x - size.x - 2),
                0,
                Random.Range(2, chunkSize.z - size.z - 2)
            );

            Vector3Int worldPos = currentChunk.offset + localPos;
            Room newRoom = new Room(worldPos, size);

            bool overlaps = false;
            foreach (var existingRoom in currentChunk.rooms)
            {
                if (RoomsOverlap(newRoom, existingRoom))
                {
                    overlaps = true;
                    break;
                }
            }

            if (!overlaps)
            {
                newRoom.color = new Color(0, 1, 1, roomAlpha);
                newRoom.chunkIndex = chunks.IndexOf(currentChunk);
                currentChunk.rooms.Add(newRoom);
                roomDirectCorridorCount[newRoom] = 0;
                CreateRoomBox(newRoom);
                yield return new WaitForSeconds(0.05f);
            }
        }

        Debug.Log($"Generated {currentChunk.rooms.Count} rooms");
        yield return new WaitForSeconds(stepDelay);
    }

    bool RoomsOverlap(Room a, Room b)
    {
        Vector3Int aMin = a.position;
        Vector3Int aMax = a.position + a.size;
        Vector3Int bMin = b.position;
        Vector3Int bMax = b.position + b.size;

        return !(aMax.x + 1 <= bMin.x || aMin.x >= bMax.x + 1 ||
                 aMax.z + 1 <= bMin.z || aMin.z >= bMax.z + 1);
    }

    IEnumerator SelectMainRooms()
    {
        int threshold = (minRoomSize.x + maxRoomSize.x) / 2;

        foreach (var room in currentChunk.rooms)
        {
            if (room.size.x >= threshold && room.size.z >= threshold)
            {
                room.isMain = true;
                room.color = new Color(0, 1, 0, roomAlpha);
                UpdateRoomBox(room);
                yield return new WaitForSeconds(0.1f);
            }
        }

        currentChunk.rooms.RemoveAll(r => !r.isMain);
        UpdateVisualization();

        Debug.Log($"Selected {currentChunk.rooms.Count} main rooms");
        yield return new WaitForSeconds(stepDelay);
    }

    IEnumerator DelaunayTriangulation()
    {
        currentChunk.connections.Clear();

        foreach (var room in currentChunk.rooms)
        {
            List<Room> others = new List<Room>(currentChunk.rooms);
            others.Remove(room);
            others.Sort((a, b) =>
                Vector3.Distance(a.Center, room.Center).CompareTo(
                    Vector3.Distance(b.Center, room.Center)));

            for (int i = 0; i < Mathf.Min(3, others.Count); i++)
            {
                Connection conn = new Connection(room, others[i]);
                if (!currentChunk.connections.Exists(c =>
                    (c.roomA == conn.roomA && c.roomB == conn.roomB) ||
                    (c.roomA == conn.roomB && c.roomB == conn.roomA)))
                {
                    currentChunk.connections.Add(conn);
                }
            }
        }

        DrawAllConnections();
        yield return new WaitForSeconds(stepDelay);
    }

    int GetRoomDirectCorridorCount(Room room)
    {
        if (roomDirectCorridorCount.ContainsKey(room))
            return roomDirectCorridorCount[room];
        return 0;
    }

    void AddDirectConnection(Connection conn, DungeonChunk targetChunk)
    {
        targetChunk.mstConnections.Add(conn);

        if (!roomDirectCorridorCount.ContainsKey(conn.roomA))
            roomDirectCorridorCount[conn.roomA] = 0;
        if (!roomDirectCorridorCount.ContainsKey(conn.roomB))
            roomDirectCorridorCount[conn.roomB] = 0;

        roomDirectCorridorCount[conn.roomA]++;
        roomDirectCorridorCount[conn.roomB]++;

        Debug.Log($"Direct: {conn.roomA.position} ↔ {conn.roomB.position} (counts: {roomDirectCorridorCount[conn.roomA]}, {roomDirectCorridorCount[conn.roomB]})");
    }

    void AddBranchConnection(CorridorBranch branch, DungeonChunk targetChunk)
    {
        targetChunk.branchConnections.Add(branch);

        if (!roomDirectCorridorCount.ContainsKey(branch.targetRoom))
            roomDirectCorridorCount[branch.targetRoom] = 0;

        roomDirectCorridorCount[branch.targetRoom]++;

        Debug.Log($"T-Branch: {branch.targetRoom.position} from corridor (count: {roomDirectCorridorCount[branch.targetRoom]})");
    }

    void AddCorridorBridge(Connection corridorA, Connection corridorB)
    {
        CorridorBridge bridge = new CorridorBridge
        {
            corridorA = corridorA,
            corridorB = corridorB
        };
        corridorBridges.Add(bridge);
        Debug.Log($"Corridor-Bridge: {corridorA.roomA.position}-{corridorA.roomB.position} ↔ {corridorB.roomA.position}-{corridorB.roomB.position}");
    }

    bool CanAddCorridor(Room room)
    {
        int count = GetRoomDirectCorridorCount(room);
        return count < maxCorridorsPerRoom;
    }

    Connection FindAnyCorridorInChunk()
    {
        if (currentChunk.mstConnections.Count > 0)
        {
            return currentChunk.mstConnections[Random.Range(0, currentChunk.mstConnections.Count)];
        }
        return null;
    }

    // Find valid corridor with path in chunk - tries ALL corridors
    Connection FindValidCorridorInChunk(DungeonChunk chunk)
    {
        foreach (var corridor in chunk.mstConnections)
        {
            if (corridorPaths.ContainsKey(corridor) && corridorPaths[corridor].Count > 2)
            {
                return corridor;
            }
        }
        return null;
    }

    // Find the closest point on a corridor path to a target position
    // Returns the index into the path and the squared distance
    (int index, float sqrDist) FindClosestPointOnPath(List<Vector3Int> path, Vector3 target)
    {
        int bestIndex = path.Count / 2; // fallback to midpoint
        float bestSqrDist = float.MaxValue;

        for (int i = 0; i < path.Count; i++)
        {
            float sqrDist = (new Vector3(path[i].x, 0, path[i].z) - target).sqrMagnitude;
            if (sqrDist < bestSqrDist)
            {
                bestSqrDist = sqrDist;
                bestIndex = i;
            }
        }

        return (bestIndex, bestSqrDist);
    }

    // Find the best corridor in a chunk to reach a target room, ranked by closest point proximity
    // Returns the corridor and the index of the closest point on its path
    (Connection corridor, int junctionIndex) FindBestCorridorForTarget(DungeonChunk chunk, Room targetRoom)
    {
        Connection bestCorridor = null;
        int bestJunctionIndex = -1;
        float bestSqrDist = float.MaxValue;

        Vector3 targetCenter = targetRoom.Center;

        foreach (var corridor in chunk.mstConnections)
        {
            if (corridorPaths.ContainsKey(corridor) && corridorPaths[corridor].Count > 2)
            {
                List<Vector3Int> path = corridorPaths[corridor];
                (int index, float sqrDist) = FindClosestPointOnPath(path, targetCenter);

                if (sqrDist < bestSqrDist)
                {
                    bestSqrDist = sqrDist;
                    bestCorridor = corridor;
                    bestJunctionIndex = index;
                }
            }
        }

        return (bestCorridor, bestJunctionIndex);
    }

    // Find best room pair for inter-chunk connection
    (Room, Room) FindBestInterChunkRooms(DungeonChunk leftChunk, DungeonChunk rightChunk)
    {
        Room bestLeft = null;
        Room bestRight = null;
        float bestScore = float.MaxValue;

        foreach (var l in leftChunk.rooms)
        {
            foreach (var r in rightChunk.rooms)
            {
                float dist = Vector3.Distance(l.Center, r.Center);
                int leftCount = GetRoomDirectCorridorCount(l);
                int rightCount = GetRoomDirectCorridorCount(r);

                float priority = 2.0f;

                if (leftCount < maxCorridorsPerRoom && rightCount < maxCorridorsPerRoom)
                    priority = 0.5f;
                else if (leftCount < maxCorridorsPerRoom || rightCount < maxCorridorsPerRoom)
                    priority = 1.0f;

                float score = dist * priority;

                if (!FindValidCorner(l, r).HasValue)
                    score += 500f;

                if (leftCount >= maxCorridorsPerRoom || rightCount >= maxCorridorsPerRoom)
                {
                    float corridorBonus = 0f;

                    if (leftCount >= maxCorridorsPerRoom)
                    {
                        float bestCorridorDist = GetBestCorridorDistanceToRoom(leftChunk, r);
                        corridorBonus += bestCorridorDist;
                    }

                    if (rightCount >= maxCorridorsPerRoom)
                    {
                        float bestCorridorDist = GetBestCorridorDistanceToRoom(rightChunk, l);
                        corridorBonus += bestCorridorDist;
                    }

                    score = score * 0.4f + corridorBonus * 0.6f;
                }

                if (score < bestScore)
                {
                    bestScore = score;
                    bestLeft = l;
                    bestRight = r;
                }
            }
        }

        return (bestLeft, bestRight);
    }

    float GetBestCorridorDistanceToRoom(DungeonChunk chunk, Room targetRoom)
    {
        float bestDist = float.MaxValue;
        Vector3 targetCenter = targetRoom.Center;

        foreach (var corridor in chunk.mstConnections)
        {
            if (corridorPaths.ContainsKey(corridor) && corridorPaths[corridor].Count > 2)
            {
                List<Vector3Int> path = corridorPaths[corridor];
                (_, float sqrDist) = FindClosestPointOnPath(path, targetCenter);
                if (sqrDist < bestDist)
                {
                    bestDist = sqrDist;
                }
            }
        }

        return bestDist < float.MaxValue ? Mathf.Sqrt(bestDist) : 1000f;
    }

    IEnumerator MinimumSpanningTree()
    {
        currentChunk.mstConnections.Clear();
        currentChunk.branchConnections.Clear();

        if (currentChunk.rooms.Count == 0) yield break;

        HashSet<Room> connectedRooms = new HashSet<Room>();
        connectedRooms.Add(currentChunk.rooms[0]);

        int iterations = 0;
        int maxIterations = currentChunk.rooms.Count * 10;

        while (connectedRooms.Count < currentChunk.rooms.Count && iterations < maxIterations)
        {
            iterations++;

            Connection bestEdge = null;
            float bestCost = float.MaxValue;
            Room roomToAdd = null;
            Room existingRoom = null;

            foreach (var conn in currentChunk.connections)
            {
                bool aConnected = connectedRooms.Contains(conn.roomA);
                bool bConnected = connectedRooms.Contains(conn.roomB);

                if (aConnected != bConnected)
                {
                    float cost = Vector3.Distance(conn.roomA.Center, conn.roomB.Center);
                    if (cost < bestCost)
                    {
                        bestCost = cost;
                        bestEdge = conn;
                        roomToAdd = aConnected ? conn.roomB : conn.roomA;
                        existingRoom = aConnected ? conn.roomA : conn.roomB;
                    }
                }
            }

            if (bestEdge == null)
            {
                Debug.LogWarning($"MST BLOCKED: {connectedRooms.Count}/{currentChunk.rooms.Count}");
                break;
            }

            bool pathClear = FindValidCorner(roomToAdd, existingRoom).HasValue;
            if (CanAddCorridor(roomToAdd) && CanAddCorridor(existingRoom) && pathClear)
            {
                AddDirectConnection(bestEdge, currentChunk);
                connectedRooms.Add(roomToAdd);
            }
            else if (CanAddCorridor(roomToAdd))
            {
                Connection baseCorridor = FindAnyCorridorInChunk();
                if (baseCorridor != null)
                {
                    CorridorBranch branch = new CorridorBranch
                    {
                        baseCorridor = baseCorridor,
                        targetRoom = roomToAdd,
                        junctionPoint = Vector3Int.zero
                    };
                    AddBranchConnection(branch, currentChunk);
                    connectedRooms.Add(roomToAdd);
                }
                else
                {
                    Debug.LogError("No corridors to branch from!");
                    connectedRooms.Add(roomToAdd);
                }
            }
            else
            {
                Debug.LogWarning($"BOTH rooms full!");
                connectedRooms.Add(roomToAdd);
            }

            DrawAllConnections();
            yield return new WaitForSeconds(0.3f);
        }

        EnsureAllRoomsConnected();

        Debug.Log($"MST: {currentChunk.mstConnections.Count} direct + {currentChunk.branchConnections.Count} branches");
        yield return new WaitForSeconds(stepDelay);
    }

    void EnsureAllRoomsConnected()
    {
        foreach (var room in currentChunk.rooms)
        {
            if (GetRoomDirectCorridorCount(room) > 0) continue;

            Debug.LogWarning($"Isolated room at {room.position} — forcing connection.");

            Room nearest = null;
            float bestDist = float.MaxValue;

            foreach (var other in currentChunk.rooms)
            {
                if (other == room) continue;
                float d = Vector3.Distance(room.Center, other.Center);
                if (d < bestDist) { bestDist = d; nearest = other; }
            }

            if (nearest != null)
            {
                Connection forced = new Connection(room, nearest);
                currentChunk.mstConnections.Add(forced);

                if (!roomDirectCorridorCount.ContainsKey(room)) roomDirectCorridorCount[room] = 0;
                if (!roomDirectCorridorCount.ContainsKey(nearest)) roomDirectCorridorCount[nearest] = 0;

                roomDirectCorridorCount[room]++;
                roomDirectCorridorCount[nearest]++;

                Debug.Log($"Forced direct: {room.position} ↔ {nearest.position}");
                continue;
            }

            Connection base_ = FindAnyCorridorInChunk();
            if (base_ != null)
            {
                CorridorBranch branch = new CorridorBranch
                {
                    baseCorridor = base_,
                    targetRoom = room,
                    junctionPoint = Vector3Int.zero
                };
                AddBranchConnection(branch, currentChunk);
                Debug.Log($"Forced branch to isolated room: {room.position}");
            }
            else
            {
                Debug.LogError($"Could not connect isolated room at {room.position} — no corridors or neighbours.");
            }
        }
    }

    IEnumerator AddLoopEdges()
    {
        List<Connection> candidateEdges = currentChunk.connections
            .Except(currentChunk.mstConnections)
            .ToList();

        int loopCount = Mathf.Max(2, (int)(currentChunk.connections.Count * 0.30f));
        int added = 0;

        for (int i = 0; i < candidateEdges.Count && added < loopCount; i++)
        {
            Connection edge = candidateEdges[i];

            if (CanAddCorridor(edge.roomA) && CanAddCorridor(edge.roomB)
                && FindValidCorner(edge.roomA, edge.roomB).HasValue)
            {
                AddDirectConnection(edge, currentChunk);
                added++;
                DrawAllConnections();
                yield return new WaitForSeconds(0.3f);
            }
        }

        if (currentChunk.mstConnections.Count > 1)
        {
            int bridgesNeeded = Mathf.Min(4, Mathf.Max(2, loopCount - added));

            List<Connection> shuffled = new List<Connection>(currentChunk.mstConnections);
            for (int i = shuffled.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                Connection tmp = shuffled[i]; shuffled[i] = shuffled[j]; shuffled[j] = tmp;
            }

            int bridgesAdded = 0;
            for (int i = 0; i < shuffled.Count && bridgesAdded < bridgesNeeded; i++)
            {
                for (int k = i + 1; k < shuffled.Count && bridgesAdded < bridgesNeeded; k++)
                {
                    if (shuffled[i] != shuffled[k])
                    {
                        AddCorridorBridge(shuffled[i], shuffled[k]);
                        bridgesAdded++;
                        added++;
                        DrawAllConnections();
                        yield return new WaitForSeconds(0.3f);
                    }
                }
            }
        }

        Debug.Log($"Added {added} loop edges");
        yield return new WaitForSeconds(stepDelay);
    }

    IEnumerator GenerateCorridors()
    {
        foreach (var conn in currentChunk.mstConnections)
        {
            Vector3Int start = new Vector3Int((int)conn.roomA.Center.x, 0, (int)conn.roomA.Center.z);
            Vector3Int end = new Vector3Int((int)conn.roomB.Center.x, 0, (int)conn.roomB.Center.z);
            Vector3Int? cornerOpt = FindValidCorner(conn.roomA, conn.roomB);
            Vector3Int corner = cornerOpt.HasValue ? cornerOpt.Value : new Vector3Int(end.x, 0, start.z);

            List<Vector3Int> path = new List<Vector3Int>();
            CarveCorridorSegment(start, corner, path);
            CarveCorridorSegment(corner, end, path);
            corridorPaths[conn] = path;
            MarkRoomEntrances(path, conn.roomA, conn.roomB);

            UpdateCorridorVisuals();
            yield return new WaitForSeconds(0.2f);
        }

        foreach (var branch in currentChunk.branchConnections)
        {
            if (corridorPaths.ContainsKey(branch.baseCorridor))
            {
                List<Vector3Int> basePath = corridorPaths[branch.baseCorridor];

                if (basePath.Count > 2)
                {
                    int minIndex = basePath.Count / 4;
                    int maxIndex = basePath.Count * 3 / 4;
                    int junctionIndex = Random.Range(minIndex, maxIndex);
                    Vector3Int junctionPoint = basePath[junctionIndex];
                    branch.junctionPoint = junctionPoint;

                    Vector3Int targetPos = new Vector3Int(
                        (int)branch.targetRoom.Center.x, 0, (int)branch.targetRoom.Center.z);
                    Vector3Int corner = new Vector3Int(targetPos.x, 0, junctionPoint.z);

                    List<Vector3Int> branchPath = new List<Vector3Int>();
                    CarveCorridorSegment(junctionPoint, corner, branchPath, true);
                    CarveCorridorSegment(corner, targetPos, branchPath, true);
                    MarkRoomEntrances(branchPath, null, branch.targetRoom);

                    Vector3Int localPos = junctionPoint - currentChunk.offset;
                    if (currentChunk.grid.ContainsKey(localPos))
                    {
                        currentChunk.grid[localPos].color = new Color(1f, 0f, 1f, corridorAlpha);
                    }

                    UpdateCorridorVisuals();
                    yield return new WaitForSeconds(0.3f);
                }
            }
        }

        foreach (var bridge in corridorBridges)
        {
            if (!corridorPaths.ContainsKey(bridge.corridorA) || !corridorPaths.ContainsKey(bridge.corridorB))
                continue;

            List<Vector3Int> pathA = corridorPaths[bridge.corridorA];
            List<Vector3Int> pathB = corridorPaths[bridge.corridorB];

            if (pathA.Count <= 2 || pathB.Count <= 2)
                continue;

            int stepA = Mathf.Max(1, pathA.Count / 12);
            int stepB = Mathf.Max(1, pathB.Count / 12);

            Vector3Int bestJunctionA = Vector3Int.zero;
            Vector3Int bestJunctionB = Vector3Int.zero;
            float bestDist = float.MaxValue;
            bool useHorizFirst = true;
            bool foundClearPair = false;

            for (int ai = 0; ai < pathA.Count; ai += stepA)
            {
                Vector3Int ptA = pathA[ai];
                for (int bi = 0; bi < pathB.Count; bi += stepB)
                {
                    Vector3Int ptB = pathB[bi];
                    float dist = Vector3.Distance(new Vector3(ptA.x, 0, ptA.z), new Vector3(ptB.x, 0, ptB.z));
                    if (dist >= bestDist) continue;

                    bool clearH = !LPathCrossesRoom(ptA, ptB, true, null, null);
                    bool clearV = !LPathCrossesRoom(ptA, ptB, false, null, null);

                    if (clearH || clearV)
                    {
                        bestDist = dist;
                        bestJunctionA = ptA;
                        bestJunctionB = ptB;
                        useHorizFirst = clearH;
                        foundClearPair = true;
                    }
                }
            }

            if (!foundClearPair)
            {
                Debug.LogWarning("Bridge: no clear path found between corridor pair — skipping.");
                continue;
            }

            Vector3Int bridgeCorner = useHorizFirst
                ? new Vector3Int(bestJunctionB.x, 0, bestJunctionA.z)
                : new Vector3Int(bestJunctionA.x, 0, bestJunctionB.z);

            List<Vector3Int> bridgePath = new List<Vector3Int>();
            CarveCorridorSegment(bestJunctionA, bridgeCorner, bridgePath, true);
            CarveCorridorSegment(bridgeCorner, bestJunctionB, bridgePath, true);

            foreach (Vector3Int jpt in new[] { bestJunctionA, bestJunctionB })
            {
                DungeonChunk owner = GetChunkForWorldPos(jpt);
                if (owner != null)
                {
                    Vector3Int localPt = jpt - owner.offset;
                    if (owner.grid.ContainsKey(localPt))
                        owner.grid[localPt].color = new Color(1f, 1f, 0f, corridorAlpha);
                }
            }

            UpdateCorridorVisuals();
            yield return new WaitForSeconds(0.3f);
        }

        yield return new WaitForSeconds(stepDelay);
    }

    bool IsInsideRoom(Vector3Int worldPos, Room room)
    {
        Vector3Int min = room.position;
        Vector3Int max = room.position + room.size;
        return worldPos.x >= min.x && worldPos.x < max.x &&
               worldPos.z >= min.z && worldPos.z < max.z;
    }

    bool IsAdjacentToRoom(Vector3Int worldPos, Room room)
    {
        return IsInsideRoom(new Vector3Int(worldPos.x + 1, 0, worldPos.z), room) ||
               IsInsideRoom(new Vector3Int(worldPos.x - 1, 0, worldPos.z), room) ||
               IsInsideRoom(new Vector3Int(worldPos.x, 0, worldPos.z + 1), room) ||
               IsInsideRoom(new Vector3Int(worldPos.x, 0, worldPos.z - 1), room);
    }

    void MarkNearestEntrance(List<Vector3Int> path, Room room)
    {
        if (room == null) return;

        Vector3Int bestCell = Vector3Int.zero;
        float bestDist = float.MaxValue;
        bool found = false;

        foreach (var worldPos in path)
        {
            if (!IsAdjacentToRoom(worldPos, room)) continue;
            float dist = Vector3.Distance(new Vector3(worldPos.x, 0, worldPos.z), room.Center);
            if (dist < bestDist) { bestDist = dist; bestCell = worldPos; found = true; }
        }

        if (!found) return;
        DungeonChunk owner = GetChunkForWorldPos(bestCell);
        if (owner == null) return;
        Vector3Int localPos = bestCell - owner.offset;
        if (owner.grid.ContainsKey(localPos))
            owner.grid[localPos].color = new Color(1f, 0f, 0f, corridorAlpha);
    }

    void MarkRoomEntrances(List<Vector3Int> path, Room roomA, Room roomB)
    {
        MarkNearestEntrance(path, roomA);
        MarkNearestEntrance(path, roomB);
    }

    DungeonChunk GetChunkForWorldPos(Vector3Int worldPos)
    {
        foreach (var chunk in chunks)
        {
            Vector3Int local = worldPos - chunk.offset;
            if (local.x >= 0 && local.x < chunk.size.x &&
                local.z >= 0 && local.z < chunk.size.z)
                return chunk;
        }
        return null;
    }

    void CarveCorridorSegment(Vector3Int from, Vector3Int to, List<Vector3Int> path, bool allowOutsideGrid = false)
    {
        int minX = Mathf.Min(from.x, to.x);
        int maxX = Mathf.Max(from.x, to.x);
        int minZ = Mathf.Min(from.z, to.z);
        int maxZ = Mathf.Max(from.z, to.z);

        for (int x = minX; x <= maxX; x++)
        {
            Vector3Int worldPos = new Vector3Int(x, 0, from.z);

            if (!IsInsideAnyRoom(worldPos))
            {
                DungeonChunk ownerChunk = GetChunkForWorldPos(worldPos);
                if (ownerChunk != null)
                {
                    Vector3Int localPos = worldPos - ownerChunk.offset;
                    if (ownerChunk.grid.ContainsKey(localPos) &&
                        ownerChunk.grid[localPos].type != CellType.Hallway)
                    {
                        ownerChunk.grid[localPos].type = CellType.Hallway;
                        ownerChunk.grid[localPos].color = new Color(1f, 0.5f, 0f, corridorAlpha);
                    }
                }
                path.Add(worldPos);
            }
        }

        for (int z = minZ; z <= maxZ; z++)
        {
            Vector3Int worldPos = new Vector3Int(to.x, 0, z);

            if (!IsInsideAnyRoom(worldPos))
            {
                DungeonChunk ownerChunk = GetChunkForWorldPos(worldPos);
                if (ownerChunk != null)
                {
                    Vector3Int localPos = worldPos - ownerChunk.offset;
                    if (ownerChunk.grid.ContainsKey(localPos) &&
                        ownerChunk.grid[localPos].type != CellType.Hallway)
                    {
                        ownerChunk.grid[localPos].type = CellType.Hallway;
                        ownerChunk.grid[localPos].color = new Color(1f, 0.5f, 0f, corridorAlpha);
                    }
                }
                path.Add(worldPos);
            }
        }
    }

    IEnumerator ConnectAdjacentChunks()
    {
        if (chunks.Count < 2) yield break;

        {
            int i = chunks.Count - 2;
            DungeonChunk leftChunk = chunks[i];
            DungeonChunk rightChunk = chunks[i + 1];

            (Room leftRoom, Room rightRoom) = FindBestInterChunkRooms(leftChunk, rightChunk);

            if (leftRoom != null && rightRoom != null)
            {
                int leftCount = GetRoomDirectCorridorCount(leftRoom);
                int rightCount = GetRoomDirectCorridorCount(rightRoom);

                Debug.Log($"Inter-chunk: Left room has {leftCount}, Right room has {rightCount}");

                bool connected = false;

                if (CanAddCorridor(leftRoom) && CanAddCorridor(rightRoom))
                    connected = TryDirectConnection(leftChunk, leftRoom, rightRoom);

                if (!connected && CanAddCorridor(rightRoom))
                    connected = TryBranchFromChunkToRoom(leftChunk, rightRoom, "left->right");

                if (!connected && CanAddCorridor(leftRoom))
                    connected = TryBranchFromChunkToRoom(rightChunk, leftRoom, "right->left");

                if (!connected)
                    connected = TryBridgeChunks(leftChunk, rightChunk);

                if (!connected)
                    Debug.LogError("Inter-chunk CONNECTION IMPOSSIBLE - This should NEVER happen!");

                yield return new WaitForSeconds(0.3f);
            }
        }

        UpdateCorridorVisuals();
    }

    bool TryDirectConnection(DungeonChunk chunk, Room roomA, Room roomB)
    {
        Vector3Int? cornerOpt = FindValidCorner(roomA, roomB);
        if (!cornerOpt.HasValue)
        {
            Debug.LogWarning("Inter-chunk TryDirectConnection: both L-path orientations cross a room — falling back.");
            return false;
        }

        Connection interConn = new Connection(roomA, roomB);
        chunk.mstConnections.Add(interConn);

        roomDirectCorridorCount[roomA]++;
        roomDirectCorridorCount[roomB]++;

        Vector3Int start = new Vector3Int((int)roomA.Center.x, 0, (int)roomA.Center.z);
        Vector3Int end = new Vector3Int((int)roomB.Center.x, 0, (int)roomB.Center.z);
        Vector3Int corner = cornerOpt.Value;

        List<Vector3Int> tempPath = new List<Vector3Int>();
        CarveCorridorSegment(start, corner, tempPath, true);
        CarveCorridorSegment(corner, end, tempPath, true);
        corridorPaths[interConn] = tempPath;
        MarkRoomEntrances(tempPath, roomA, roomB);

        DrawConnectionLine(start, end, new Color(1f, 0f, 1f), 0.25f);

        Debug.Log("Inter-chunk: DIRECT connection");
        return true;
    }

    bool TryBranchFromChunkToRoom(DungeonChunk chunk, Room targetRoom, string direction)
    {
        (Connection bestCorridor, int junctionIndex) = FindBestCorridorForTarget(chunk, targetRoom);

        if (bestCorridor != null && junctionIndex >= 0)
        {
            List<Vector3Int> basePath = corridorPaths[bestCorridor];
            Vector3Int junctionPoint = basePath[junctionIndex];
            Vector3Int targetPos = new Vector3Int((int)targetRoom.Center.x, 0, (int)targetRoom.Center.z);
            Vector3Int corner = new Vector3Int(targetPos.x, 0, junctionPoint.z);

            List<Vector3Int> branchPath = new List<Vector3Int>();
            CarveCorridorSegment(junctionPoint, corner, branchPath, true);
            CarveCorridorSegment(corner, targetPos, branchPath, true);
            MarkRoomEntrances(branchPath, null, targetRoom);

            Vector3Int localPos = junctionPoint - chunk.offset;
            if (chunk.grid.ContainsKey(localPos))
                chunk.grid[localPos].color = new Color(0f, 1f, 1f, corridorAlpha);

            roomDirectCorridorCount[targetRoom]++;
            DrawConnectionLine(junctionPoint, targetPos, new Color(1f, 0.8f, 0f), 0.2f);

            Debug.Log($"Inter-chunk: T-BRANCH ({direction}) using closest corridor point (index {junctionIndex}/{basePath.Count})");
            return true;
        }

        Debug.LogWarning($"Inter-chunk: Could not branch from {direction} (no valid corridors)");
        return false;
    }

    bool TryBridgeChunks(DungeonChunk leftChunk, DungeonChunk rightChunk)
    {
        Connection bestLeftCorridor = null;
        Connection bestRightCorridor = null;
        int bestLeftIndex = -1;
        int bestRightIndex = -1;
        float bestTotalDist = float.MaxValue;

        foreach (var leftCorr in leftChunk.mstConnections)
        {
            if (!corridorPaths.ContainsKey(leftCorr) || corridorPaths[leftCorr].Count <= 2) continue;
            List<Vector3Int> leftPath = corridorPaths[leftCorr];

            foreach (var rightCorr in rightChunk.mstConnections)
            {
                if (!corridorPaths.ContainsKey(rightCorr) || corridorPaths[rightCorr].Count <= 2) continue;
                List<Vector3Int> rightPath = corridorPaths[rightCorr];

                for (int li = 0; li < leftPath.Count; li++)
                {
                    Vector3 leftPt = new Vector3(leftPath[li].x, 0, leftPath[li].z);
                    (int ri, float sqrDist) = FindClosestPointOnPath(rightPath, leftPt);

                    if (sqrDist < bestTotalDist)
                    {
                        bestTotalDist = sqrDist;
                        bestLeftCorridor = leftCorr;
                        bestRightCorridor = rightCorr;
                        bestLeftIndex = li;
                        bestRightIndex = ri;
                    }
                }
            }
        }

        if (bestLeftCorridor != null && bestRightCorridor != null)
        {
            Vector3Int junctionLeft = corridorPaths[bestLeftCorridor][bestLeftIndex];
            Vector3Int junctionRight = corridorPaths[bestRightCorridor][bestRightIndex];
            Vector3Int corner = new Vector3Int(junctionRight.x, 0, junctionLeft.z);

            List<Vector3Int> bridgePath = new List<Vector3Int>();
            CarveCorridorSegment(junctionLeft, corner, bridgePath, true);
            CarveCorridorSegment(corner, junctionRight, bridgePath, true);

            DrawConnectionLine(junctionLeft, junctionRight, new Color(0f, 1f, 0.5f), 0.2f);

            Debug.Log($"Inter-chunk: CORRIDOR-BRIDGE using closest points (dist: {Mathf.Sqrt(bestTotalDist):F1})");
            return true;
        }

        Debug.LogWarning("Inter-chunk: Could not bridge (no valid corridors in one or both chunks)");
        return false;
    }

    bool IsInsideAnyRoom(Vector3Int worldPos)
    {
        foreach (var chunk in chunks)
        {
            foreach (var room in chunk.rooms)
            {
                Vector3Int min = room.position;
                Vector3Int max = room.position + room.size;

                if (worldPos.x >= min.x && worldPos.x < max.x &&
                    worldPos.z >= min.z && worldPos.z < max.z)
                    return true;
            }
        }
        return false;
    }

    bool IsInsideRoomExcept(Vector3Int worldPos, Room exceptA, Room exceptB)
    {
        foreach (var chunk in chunks)
        {
            foreach (var room in chunk.rooms)
            {
                if (room == exceptA || room == exceptB) continue;
                Vector3Int min = room.position;
                Vector3Int max = room.position + room.size;
                if (worldPos.x >= min.x && worldPos.x < max.x &&
                    worldPos.z >= min.z && worldPos.z < max.z)
                    return true;
            }
        }
        return false;
    }

    bool LPathCrossesRoom(Vector3Int start, Vector3Int end, bool horizontalFirst, Room ignoreA, Room ignoreB)
    {
        if (horizontalFirst)
        {
            for (int x = Mathf.Min(start.x, end.x); x <= Mathf.Max(start.x, end.x); x++)
                if (IsInsideRoomExcept(new Vector3Int(x, 0, start.z), ignoreA, ignoreB)) return true;
            for (int z = Mathf.Min(start.z, end.z); z <= Mathf.Max(start.z, end.z); z++)
                if (IsInsideRoomExcept(new Vector3Int(end.x, 0, z), ignoreA, ignoreB)) return true;
        }
        else
        {
            for (int z = Mathf.Min(start.z, end.z); z <= Mathf.Max(start.z, end.z); z++)
                if (IsInsideRoomExcept(new Vector3Int(start.x, 0, z), ignoreA, ignoreB)) return true;
            for (int x = Mathf.Min(start.x, end.x); x <= Mathf.Max(start.x, end.x); x++)
                if (IsInsideRoomExcept(new Vector3Int(x, 0, end.z), ignoreA, ignoreB)) return true;
        }
        return false;
    }

    Vector3Int? FindValidCorner(Room roomA, Room roomB)
    {
        Vector3Int start = new Vector3Int((int)roomA.Center.x, 0, (int)roomA.Center.z);
        Vector3Int end = new Vector3Int((int)roomB.Center.x, 0, (int)roomB.Center.z);

        if (!LPathCrossesRoom(start, end, true, roomA, roomB))
            return new Vector3Int(end.x, 0, start.z);
        if (!LPathCrossesRoom(start, end, false, roomA, roomB))
            return new Vector3Int(start.x, 0, end.z);
        return null;
    }

    void DrawAllConnections()
    {
        foreach (Transform child in connectionsParent.transform)
            Destroy(child.gameObject);

        if (currentChunk.mstConnections.Count == 0 && currentChunk.connections.Count > 0)
        {
            foreach (var conn in currentChunk.connections)
                DrawConnectionLine(conn.roomA.Center, conn.roomB.Center, new Color(0.8f, 0.8f, 0.8f), 0.08f);
        }

        foreach (var conn in currentChunk.mstConnections)
            DrawConnectionLine(conn.roomA.Center, conn.roomB.Center, new Color(0.3f, 0.6f, 1f), 0.2f);

        foreach (var branch in currentChunk.branchConnections)
        {
            Vector3 roomPos = branch.targetRoom.Center;
            Vector3 junctionPos = (branch.junctionPoint != Vector3Int.zero)
                ? new Vector3(branch.junctionPoint.x, 0, branch.junctionPoint.z)
                : (branch.baseCorridor.roomA.Center + branch.baseCorridor.roomB.Center) / 2f;
            DrawConnectionLine(roomPos, junctionPos, new Color(1f, 0.8f, 0f), 0.15f);
        }

        foreach (var bridge in corridorBridges)
        {
            Vector3 posA, posB;

            if (corridorPaths.ContainsKey(bridge.corridorA) && corridorPaths[bridge.corridorA].Count > 2)
            {
                var pt = corridorPaths[bridge.corridorA][corridorPaths[bridge.corridorA].Count / 2];
                posA = new Vector3(pt.x, 0, pt.z);
            }
            else posA = (bridge.corridorA.roomA.Center + bridge.corridorA.roomB.Center) / 2f;

            if (corridorPaths.ContainsKey(bridge.corridorB) && corridorPaths[bridge.corridorB].Count > 2)
            {
                var pt = corridorPaths[bridge.corridorB][corridorPaths[bridge.corridorB].Count / 2];
                posB = new Vector3(pt.x, 0, pt.z);
            }
            else posB = (bridge.corridorB.roomA.Center + bridge.corridorB.roomB.Center) / 2f;

            DrawConnectionLine(posA, posB, new Color(1f, 1f, 0f), 0.12f);
        }
    }

    void DrawConnectionLine(Vector3 start, Vector3 end, Color color, float width)
    {
        GameObject lineObj = new GameObject("Connection");
        lineObj.transform.parent = connectionsParent.transform;

        LineRenderer lr = lineObj.AddComponent<LineRenderer>();
        lr.material = new Material(lineMaterial);
        lr.material.color = color;
        lr.startColor = color;
        lr.endColor = color;
        lr.startWidth = width;
        lr.endWidth = width;
        lr.positionCount = 2;
        lr.useWorldSpace = true;

        lr.SetPosition(0, start * cellSize);
        lr.SetPosition(1, end * cellSize);
    }

    void UpdateVisualization()
    {
        foreach (Transform child in roomsParent.transform)
            Destroy(child.gameObject);

        foreach (var chunk in chunks)
            foreach (var room in chunk.rooms)
                CreateRoomBox(room);
    }

    void UpdateCorridorVisuals()
    {
        foreach (Transform child in corridorsParent.transform)
            Destroy(child.gameObject);

        foreach (var chunk in chunks)
            foreach (var cell in chunk.grid.Values)
                if (cell.type == CellType.Hallway)
                    CreateCorridorCube(cell);
    }

    void CreateRoomBox(Room room)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = $"Room_{room.position}";
        box.transform.parent = roomsParent.transform;
        box.transform.position = room.Center * cellSize;
        box.transform.localScale = new Vector3(room.size.x * cellSize, room.size.y * cellSize, room.size.z * cellSize);

        Renderer rend = box.GetComponent<Renderer>();
        rend.material = new Material(roomMaterial);
        rend.material.color = room.color;
        room.visual = box;
    }

    void UpdateRoomBox(Room room)
    {
        if (room.visual != null)
        {
            Renderer rend = room.visual.GetComponent<Renderer>();
            rend.material.color = room.color;
        }
    }

    void CreateCorridorCube(DungeonCell cell)
    {
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.parent = corridorsParent.transform;
        cube.transform.position = new Vector3(cell.position.x * cellSize, 0, cell.position.z * cellSize);
        cube.transform.localScale = Vector3.one * cellSize * 0.9f;

        Renderer rend = cube.GetComponent<Renderer>();
        rend.material = new Material(roomMaterial);
        rend.material.color = cell.color;
    }
}

[System.Serializable]
public class DungeonChunk
{
    public Vector3Int size;
    public Vector3Int offset;
    public Dictionary<Vector3Int, DungeonCell> grid;
    public List<Room> rooms;
    public List<Connection> connections;
    public List<Connection> mstConnections;
    public List<CorridorBranch> branchConnections;

    public DungeonChunk(Vector3Int size, Vector3Int offset)
    {
        this.size = size;
        this.offset = offset;
        this.grid = new Dictionary<Vector3Int, DungeonCell>();
        this.rooms = new List<Room>();
        this.connections = new List<Connection>();
        this.mstConnections = new List<Connection>();
        this.branchConnections = new List<CorridorBranch>();
    }
}

[System.Serializable]
public class Room
{
    public Vector3Int position;
    public Vector3Int size;
    public bool isMain = false;
    public Color color = Color.white;
    public GameObject visual;
    public int chunkIndex = 0;

    public Vector3 Center => new Vector3(
        position.x + (size.x - 1) / 2f,
        0,
        position.z + (size.z - 1) / 2f
    );

    public Room(Vector3Int pos, Vector3Int s)
    {
        position = pos;
        size = s;
    }
}

public class Connection
{
    public Room roomA;
    public Room roomB;

    public Connection(Room a, Room b)
    {
        roomA = a;
        roomB = b;
    }
}

public class CorridorBranch
{
    public Connection baseCorridor;
    public Room targetRoom;
    public Vector3Int junctionPoint;
}

public class CorridorBridge
{
    public Connection corridorA;
    public Connection corridorB;
}
