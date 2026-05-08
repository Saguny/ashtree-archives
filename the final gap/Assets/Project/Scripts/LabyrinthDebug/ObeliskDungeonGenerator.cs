using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Obelisk Dungeon Generator — Streaming WFC Hallway System.
///
/// Generates the 5½ Minute Hallway aesthetic: infinite dark corridors,
/// spatial anomalies that escalate with exploration depth, prop-populated spaces.
///
/// Self-contained: no external game-state dependency.
/// Streaming: generates new tiles ahead of the observer (Camera.main) in real-time.
/// Future-proof: add tile types via ObeliskTileDefinition.BuildLibrary() only — no logic changes.
/// Never softlocks: path guarantee BFS runs every 20 tiles and force-places corridors if needed.
///
/// Steps (IDungeonGenerator — 9 total):
///   1  Build tile definition library + socket matrix
///   2  Seed entrance tile + initialize WFC frontier
///   3  First WFC collapse batch (animated, outward from origin)
///   4  Geometric deviation + perseveration passes
///   5  Prop placement pass
///   6  Anomaly injection (missing / void ceiling / inverse stairs)
///   7  Path guarantee validation
///   8  Activate streaming mode (camera-driven, infinite)
///   9  Terminal structure: Massive Pillar + Spiral Staircase (on-demand)
/// </summary>
public class ObeliskDungeonGenerator : MonoBehaviour, IDungeonGenerator
{
    // ── IDungeonGenerator ─────────────────────────────────────────────────────
    public string GeneratorName => "Obelisk";
    public int CurrentStep => _currentStep;
    public int TotalSteps => 9;
    public bool IsAnimating => _isAnimating;

    // ── Inspector — Grid ──────────────────────────────────────────────────────
    [Header("Tile Grid")]
    public float tileSize = 4f;
    public float tileHeight = 3f;

    // ── Inspector — Generation ────────────────────────────────────────────────
    [Header("Initial Generation")]
    public int initialRadius = 3;

    [Header("Streaming")]
    public bool streamingEnabled = true;
    public float generationRadius = 8f;   // world units ahead of camera to generate
    public float unloadRadius = 20f;  // destroy visuals beyond this — keeps scene lean
    public int cellsPerTick = 2;    // tiles collapsed per coroutine tick
    public float streamTickInterval = 0.08f;

    // ── Inspector — Path Guarantee ────────────────────────────────────────────
    [Header("Path Guarantee")]
    public int minExitPathLength = 6;

    // ── Inspector — Anomalies ─────────────────────────────────────────────────
    [Header("Anomaly Probabilities (scale with depth)")]
    [Range(0f, 1f)] public float geometricDeviationChance = 1.00f;
    [Range(0f, 1f)] public float perseverationChance = 0.30f;
    [Range(0f, 1f)] public float missingRate = 0.10f;
    [Range(0f, 1f)] public float inverseStairChance = 0.20f;
    [Range(0f, 1f)] public float voidCeilingChance = 0.15f;
    [Range(0f, .01f)] public float nullAdjacencyDepthScale = 0.002f;

    [Header("Escalation")]
    [Tooltip("All anomaly chances are multiplied by (1 + depth * this).")]
    public float anomalyDepthScale = 0.04f;

    // ── Inspector — Props ─────────────────────────────────────────────────────
    [Header("Props")]
    [Range(0f, 1f)] public float globalPropDensity = 0.55f;

    // ── Inspector — Visualization ─────────────────────────────────────────────
    [Header("Visualization")]
    public bool richVisualization = true;
    public float stepDelay = 0.8f;
    public Material cellMaterial;
    public Material voidMaterial;

    // ── Colors ────────────────────────────────────────────────────────────────
    static readonly Color C_FLOOR = new Color(0.18f, 0.18f, 0.22f);
    static readonly Color C_WALL = new Color(0.24f, 0.24f, 0.29f);
    static readonly Color C_CEILING = new Color(0.13f, 0.13f, 0.17f);
    static readonly Color C_CEILING_VOID = new Color(0.04f, 0.02f, 0.07f);
    static readonly Color C_DOOR_FRAME = new Color(0.38f, 0.33f, 0.26f);
    static readonly Color C_STAIR = new Color(0.28f, 0.26f, 0.22f);
    static readonly Color C_INVERSE_STAIR = new Color(0.80f, 0.28f, 0.10f);
    static readonly Color C_VOID = new Color(0.02f, 0.01f, 0.03f);
    static readonly Color C_MISSING = new Color(0.32f, 0.09f, 0.09f);
    static readonly Color C_FORCED_PATH = new Color(0.85f, 0.20f, 0.20f, 0.6f);
    static readonly Color C_ENTROPY = new Color(0.28f, 0.28f, 0.55f, 0.22f);
    static readonly Color C_TERMINAL = new Color(0.60f, 0.08f, 0.65f);
    static readonly Color C_PROP_FURN = new Color(0.48f, 0.42f, 0.33f);
    static readonly Color C_PROP_CLUE = new Color(0.92f, 0.88f, 0.14f);
    static readonly Color C_PROP_DECOR = new Color(0.22f, 0.21f, 0.24f);
    static readonly Color C_PROP_CREEPY = new Color(0.52f, 0.09f, 0.09f);

    // ── Face constants ────────────────────────────────────────────────────────
    const int FACE_N = 0, FACE_S = 1, FACE_E = 2, FACE_W = 3, FACE_U = 4, FACE_D = 5;
    static readonly Vector3Int[] FaceDir =
    {
        new Vector3Int( 0, 0,  1),   // N
        new Vector3Int( 0, 0, -1),   // S
        new Vector3Int( 1, 0,  0),   // E
        new Vector3Int(-1, 0,  0),   // W
        new Vector3Int( 0, 1,  0),   // U
        new Vector3Int( 0,-1,  0),   // D
    };
    static int Opp(int f) => f ^ 1;  // N↔S, E↔W, U↔D

    // ── Runtime state ─────────────────────────────────────────────────────────
    int _currentStep = 0;
    bool _isAnimating = false;
    bool _streamingActive = false;
    int _tilesGenerated = 0;
    int _anomalyCount = 0;
    int _forcedPaths = 0;
    float _maxDepth = 0f;

    readonly Dictionary<Vector3Int, ObeliskWFCCell> _grid = new Dictionary<Vector3Int, ObeliskWFCCell>();
    readonly HashSet<Vector3Int> _frontier = new HashSet<Vector3Int>();
    readonly HashSet<Vector3Int> _collapsed = new HashSet<Vector3Int>();

    ObeliskTileDefinition[] _tileDefs;
    Dictionary<ObeliskTileType, ObeliskTileDefinition> _tileDefDict;

    GameObject _goTiles, _goProps, _goEntropy, _goPathDebug, _goTerminal;
    Material _mat, _voidMat;

    // Shared material cache — one instance per unique color, reused by all
    // primitives.  sharedMaterial assignment lets Unity batch them properly.
    readonly Dictionary<int, Material> _matCache = new Dictionary<int, Material>();

    // ── Observer position (auto-updated from Camera.main) ─────────────────────
    public Vector3 ObserverPosition { get; set; }

    // =========================================================================
    //  Unity lifecycle
    // =========================================================================

    void Start()
    {
        BuildMaterials();
        BuildHierarchy();
    }

    void Update()
    {
        if (Camera.main != null)
            ObserverPosition = Camera.main.transform.position;
    }

    void BuildMaterials()
    {
        _mat = cellMaterial != null
            ? new Material(cellMaterial)
            : new Material(Shader.Find("Standard"));

        _voidMat = voidMaterial != null
            ? new Material(voidMaterial)
            : new Material(Shader.Find("Unlit/Color"));
        _voidMat.color = C_VOID;
    }

    void BuildHierarchy()
    {
        _goTiles = MakeChild("OBL_Tiles");
        _goProps = MakeChild("OBL_Props");
        _goEntropy = MakeChild("OBL_Entropy");
        _goPathDebug = MakeChild("OBL_PathDebug");
        _goTerminal = MakeChild("OBL_Terminal");
    }

    GameObject MakeChild(string n)
    {
        var go = new GameObject(n);
        go.transform.SetParent(transform, false);
        return go;
    }

    // =========================================================================
    //  IDungeonGenerator
    // =========================================================================

    public void NextStep()
    {
        if (_isAnimating) return;
        _currentStep++;
        if (_currentStep > TotalSteps) { _currentStep = TotalSteps; return; }
        StartCoroutine(RunStep(_currentStep));
    }

    public void GenerateNewChunk()
    {
        if (_isAnimating) return;
        StopAllCoroutines();
        _streamingActive = false;
        ClearAll();
        _currentStep = 0;
        StartCoroutine(RunAllSteps());
    }

    public string GetStepDescription()
    {
        switch (_currentStep)
        {
            case 0: return $"Obelisk WFC ready — {(richVisualization ? "Rich" : "Flat")}, streaming {(streamingEnabled ? "ON" : "OFF")}";
            case 1: return $"Step 1/9 — Tile library built ({(_tileDefs != null ? _tileDefs.Length : 0)} types)";
            case 2: return $"Step 2/9 — Entrance seeded, WFC frontier initialized (radius {initialRadius})";
            case 3: return $"Step 3/9 — First collapse batch, {_tilesGenerated} tiles";
            case 4: return $"Step 4/9 — Geometric deviation + perseveration";
            case 5: return $"Step 5/9 — Prop placement (density {globalPropDensity:P0})";
            case 6: return $"Step 6/9 — Anomaly injection, {_anomalyCount} anomalies";
            case 7: return $"Step 7/9 — Path guarantee, {_forcedPaths} forced corridor(s)";
            case 8: return $"Step 8/9 — Streaming, {_tilesGenerated} tiles, depth {_maxDepth:F0}m, {_anomalyCount} anomalies, {_forcedPaths} forced";
            case 9: return $"Step 9/9 — Terminal: Massive Pillar + Spiral Staircase";
            default: return $"Complete, {_tilesGenerated} tiles, {_anomalyCount} anomalies, {_forcedPaths} forced";
        }
    }

    // =========================================================================
    //  Orchestration
    // =========================================================================

    IEnumerator RunAllSteps()
    {
        _isAnimating = true;
        for (int s = 1; s <= TotalSteps; s++)
        {
            _currentStep = s;
            DungeonDebugController.Instance?.UpdateStepDescription(GetStepDescription());
            yield return StartCoroutine(DoStep(s));
        }
        _isAnimating = false;
    }

    IEnumerator RunStep(int step)
    {
        _isAnimating = true;
        DungeonDebugController.Instance?.UpdateStepDescription(GetStepDescription());
        yield return StartCoroutine(DoStep(step));
        _isAnimating = false;
    }

    IEnumerator DoStep(int step)
    {
        switch (step)
        {
            case 1: yield return StartCoroutine(Step1_BuildLibrary()); break;
            case 2: yield return StartCoroutine(Step2_SeedAndInit()); break;
            case 3: yield return StartCoroutine(Step3_FirstCollapse()); break;
            case 4: yield return StartCoroutine(Step4_DeviationAndPersev()); break;
            case 5: yield return StartCoroutine(Step5_PropPlacement()); break;
            case 6: yield return StartCoroutine(Step6_AnomalyInjection()); break;
            case 7: yield return StartCoroutine(Step7_PathGuarantee()); break;
            case 8: yield return StartCoroutine(Step8_ActivateStreaming()); break;
            case 9: yield return StartCoroutine(Step9_TerminalStructure()); break;
        }
    }

    // =========================================================================
    //  STEP 1 — Build Tile Library
    // =========================================================================

    IEnumerator Step1_BuildLibrary()
    {
        _tileDefs = ObeliskTileDefinition.BuildLibrary();
        _tileDefDict = new Dictionary<ObeliskTileType, ObeliskTileDefinition>();
        foreach (var d in _tileDefs) _tileDefDict[d.TileType] = d;
        Debug.Log($"[Obelisk] Step 1 — {_tileDefs.Length} tile types, {_tileDefs.Sum(d => d.PropSlots.Length)} prop slots total");
        DungeonDebugController.Instance?.UpdateStepDescription(GetStepDescription());
        yield return new WaitForSeconds(stepDelay * 0.4f);
    }

    // =========================================================================
    //  STEP 2 — Seed Entrance + Init WFC Grid
    // =========================================================================

    IEnumerator Step2_SeedAndInit()
    {
        _grid.Clear(); _frontier.Clear(); _collapsed.Clear();
        ClearGO(_goEntropy); ClearGO(_goTiles); ClearGO(_goProps);

        // Place root tile — always a straight hallway, the entrance
        var root = new ObeliskWFCCell(Vector3Int.zero);
        root.Collapse(ObeliskTileType.HallwayStraight_NS);
        _grid[Vector3Int.zero] = root;
        _collapsed.Add(Vector3Int.zero);
        SpawnTileVisual(root);
        _tilesGenerated = 1;

        // Seed frontier in a square radius
        for (int dx = -initialRadius; dx <= initialRadius; dx++)
            for (int dz = -initialRadius; dz <= initialRadius; dz++)
            {
                if (dx == 0 && dz == 0) continue;
                var pos = new Vector3Int(dx, 0, dz);
                if (!_grid.ContainsKey(pos))
                {
                    var cell = new ObeliskWFCCell(pos);
                    _grid[pos] = cell;
                    _frontier.Add(pos);
                    SpawnEntropyVisual(pos);
                }
            }

        Debug.Log($"[Obelisk] Step 2 — {_frontier.Count} frontier cells seeded");
        yield return new WaitForSeconds(stepDelay * 0.4f);
    }

    // =========================================================================
    //  STEP 3 — First WFC Collapse Batch
    // =========================================================================

    IEnumerator Step3_FirstCollapse()
    {
        // Collapse outward from origin for a satisfying outward-bloom visual
        var toCollapse = _frontier
            .OrderBy(p => p.x * p.x + p.z * p.z)
            .ToList();

        foreach (var pos in toCollapse)
        {
            if (!_frontier.Contains(pos)) continue;
            CollapseCell(pos);
            yield return new WaitForSeconds(0.035f);
        }

        Debug.Log($"[Obelisk] Step 3 — {_tilesGenerated} tiles collapsed");
        yield return new WaitForSeconds(stepDelay);
    }

    // =========================================================================
    //  STEP 4 — Geometric Deviation + Perseveration
    // =========================================================================

    IEnumerator Step4_DeviationAndPersev()
    {
        int corners = RunGeometricDeviation();
        yield return new WaitForSeconds(0.25f);
        int clones = RunPerseveration();
        yield return new WaitForSeconds(0.25f);
        Debug.Log($"[Obelisk] Step 4 — {corners} corner(s) forced, {clones} sequence clone(s)");
        yield return new WaitForSeconds(stepDelay);
    }

    // =========================================================================
    //  STEP 5 — Prop Placement
    // =========================================================================

    IEnumerator Step5_PropPlacement()
    {
        ClearGO(_goProps);
        int propCount = 0;
        foreach (var pos in _collapsed.ToList())
        {
            if (!_grid.TryGetValue(pos, out var cell)) continue;
            if (cell.IsMissing) continue;
            var def = GetDef(cell.TileType);
            if (def == null) continue;
            foreach (var slot in def.PropSlots)
            {
                if (Random.value > slot.SpawnChance * globalPropDensity) continue;
                SpawnPropVisual(pos, slot);
                propCount++;
            }
            if (propCount % 8 == 0) yield return null;
        }
        Debug.Log($"[Obelisk] Step 5 — {propCount} props placed");
        yield return new WaitForSeconds(stepDelay);
    }

    // =========================================================================
    //  STEP 6 — Anomaly Injection
    // =========================================================================

    IEnumerator Step6_AnomalyInjection()
    {
        _anomalyCount = 0;
        foreach (var pos in _collapsed.ToList())
        {
            if (!_grid.TryGetValue(pos, out var cell)) continue;
            InjectAnomalies(cell, pos);
            yield return null;
        }
        Debug.Log($"[Obelisk] Step 6 — {_anomalyCount} anomalies injected");
        yield return new WaitForSeconds(stepDelay);
    }

    void InjectAnomalies(ObeliskWFCCell cell, Vector3Int pos)
    {
        float d = DepthFactor(pos);

        if (!cell.IsMissing && Random.value < missingRate * (1f + d))
        {
            cell.IsMissing = true;
            RefreshTileVisual(cell, pos);
            _anomalyCount++;
        }

        var def = GetDef(cell.TileType);
        if (!cell.HasVoidCeiling && def != null &&
            def.GetSocket(FACE_U) == ObeliskSocketType.CeilingStandard &&
            Random.value < voidCeilingChance * (1f + d))
        {
            cell.HasVoidCeiling = true;
            RefreshTileVisual(cell, pos);
            _anomalyCount++;
        }

        if ((cell.TileType == ObeliskTileType.StaircaseUp ||
             cell.TileType == ObeliskTileType.StaircaseDown) &&
            !cell.IsInverseStair &&
            Random.value < inverseStairChance * (1f + d))
        {
            cell.IsInverseStair = true;
            RefreshTileVisual(cell, pos);
            _anomalyCount++;
        }
    }

    // =========================================================================
    //  STEP 7 — Path Guarantee Validation
    // =========================================================================

    IEnumerator Step7_PathGuarantee()
    {
        ClearGO(_goPathDebug);
        bool valid = ValidateAndForceExitPath(Vector3Int.zero);
        if (!valid)
        {
            Debug.LogWarning("[Obelisk] Step 7 — Path guarantee triggered, forced corridor placed");
            _forcedPaths++;
        }
        else
        {
            Debug.Log("[Obelisk] Step 7 — Exit path valid");
        }
        DungeonDebugController.Instance?.UpdateStepDescription(GetStepDescription());
        yield return new WaitForSeconds(stepDelay);
    }

    // =========================================================================
    //  STEP 8 — Activate Streaming
    // =========================================================================

    IEnumerator Step8_ActivateStreaming()
    {
        if (!streamingEnabled)
        {
            Debug.Log("[Obelisk] Step 8 — Streaming OFF (Inspector toggle)");
            yield return new WaitForSeconds(stepDelay * 0.3f);
            yield break;
        }
        _streamingActive = true;
        StartCoroutine(StreamingLoop());
        Debug.Log("[Obelisk] Step 8 — Streaming active (Camera.main drives frontier)");
        yield return new WaitForSeconds(stepDelay * 0.5f);
    }

    // =========================================================================
    //  STEP 9 — Terminal Structure
    // =========================================================================

    IEnumerator Step9_TerminalStructure()
    {
        // Find furthest collapsed tile from origin
        Vector3Int terminus = Vector3Int.zero;
        float maxDist = 0f;
        foreach (var pos in _collapsed)
        {
            float d = TileWorldPos(pos).magnitude;
            if (d > maxDist) { maxDist = d; terminus = pos; }
        }

        ClearGO(_goTerminal);
        yield return StartCoroutine(SpawnTerminalRoom(terminus));
        Debug.Log($"[Obelisk] Step 9 — Terminal at {terminus} (dist {maxDist:F1}m)");
        yield return new WaitForSeconds(stepDelay);
    }

    // =========================================================================
    //  WFC Core
    // =========================================================================

    void CollapseCell(Vector3Int pos)
    {
        if (_collapsed.Contains(pos) || !_grid.TryGetValue(pos, out var cell)) return;

        // Compute valid tile types for this position
        var valid = GetValidTileTypes(pos);
        if (valid.Count == 0) valid.Add(ObeliskTileType.DeadEnd_N);

        // Null adjacency: at very high depth, all socket rules break down
        float depth = DepthFactor(pos);
        if (Random.value < nullAdjacencyDepthScale * depth)
            valid = _tileDefs.Select(d => d.TileType).ToList();

        ObeliskTileType chosen = WeightedPick(valid);
        cell.Collapse(chosen);
        _frontier.Remove(pos);
        _collapsed.Add(pos);
        _tilesGenerated++;

        float worldDist = TileWorldPos(pos).magnitude;
        if (worldDist > _maxDepth) _maxDepth = worldDist;

        // Remove entropy visual
        if (cell.EntropyVisual != null)
        {
            Destroy(cell.EntropyVisual);
            cell.EntropyVisual = null;
        }

        SpawnTileVisual(cell);

        // Expand frontier on passable horizontal faces only
        var def = GetDef(chosen);
        if (def == null) return;
        for (int f = 0; f < 4; f++)   // N, S, E, W only
        {
            if (def.GetSocket(f) == ObeliskSocketType.Wall) continue;
            var nb = pos + FaceDir[f];
            if (_grid.ContainsKey(nb) || _collapsed.Contains(nb)) continue;
            var nbCell = new ObeliskWFCCell(nb);
            _grid[nb] = nbCell;
            _frontier.Add(nb);
            SpawnEntropyVisual(nb);
        }

        // Vertical connection — staircases seed a landing one floor up so the
        // second floor generates naturally rather than climbing into void.
        bool isStairUp = chosen == ObeliskTileType.StaircaseUp ||
                         chosen == ObeliskTileType.InverseStairPortal;
        bool isStairDown = chosen == ObeliskTileType.StaircaseDown;

        if (isStairUp || isStairDown)
        {
            int yOffset = isStairUp ? 1 : -1;
            var landing = new Vector3Int(pos.x, pos.y + yOffset, pos.z);

            if (!_grid.ContainsKey(landing) && !_collapsed.Contains(landing))
            {
                // Force a landing tile at the top/bottom of the staircase
                var landingCell = new ObeliskWFCCell(landing);
                // Pick a horizontal tile so the second floor can keep propagating
                var landingType = Random.value > 0.5f
                    ? ObeliskTileType.HallwayStraight_NS
                    : ObeliskTileType.HallwayStraight_EW;
                landingCell.Collapse(landingType);
                _grid[landing] = landingCell;
                _collapsed.Add(landing);
                _tilesGenerated++;
                SpawnTileVisual(landingCell);

                // Seed the second floor's WFC frontier from this landing
                var landingDef = GetDef(landingType);
                if (landingDef != null)
                {
                    for (int f = 0; f < 4; f++)
                    {
                        if (landingDef.GetSocket(f) == ObeliskSocketType.Wall) continue;
                        var nb = landing + FaceDir[f];
                        if (_grid.ContainsKey(nb) || _collapsed.Contains(nb)) continue;
                        var nbCell = new ObeliskWFCCell(nb);
                        _grid[nb] = nbCell;
                        _frontier.Add(nb);
                        SpawnEntropyVisual(nb);
                    }
                }
            }
        }
    }

    List<ObeliskTileType> GetValidTileTypes(Vector3Int pos)
    {
        var candidates = new List<ObeliskTileType>(_tileDefDict.Keys);

        for (int f = 0; f < 4; f++)   // check horizontal neighbors
        {
            var nb = pos + FaceDir[f];
            if (!_grid.TryGetValue(nb, out var nbCell) || !_collapsed.Contains(nb)) continue;
            var nbDef = GetDef(nbCell.TileType);
            if (nbDef == null) continue;

            ObeliskSocketType required = nbDef.GetSocket(Opp(f));
            if (required == ObeliskSocketType.Null) continue;

            for (int i = candidates.Count - 1; i >= 0; i--)
            {
                var myDef = GetDef(candidates[i]);
                if (myDef == null) { candidates.RemoveAt(i); continue; }
                var mySocket = myDef.GetSocket(f);
                if (mySocket != required && mySocket != ObeliskSocketType.Null)
                    candidates.RemoveAt(i);
            }
        }

        return candidates;
    }

    ObeliskTileType WeightedPick(List<ObeliskTileType> options)
    {
        float total = 0f;
        foreach (var t in options) total += GetDef(t)?.Weight ?? 1f;
        float roll = Random.Range(0f, total);
        float acc = 0f;
        foreach (var t in options)
        {
            acc += GetDef(t)?.Weight ?? 1f;
            if (roll <= acc) return t;
        }
        return options[options.Count - 1];
    }

    // =========================================================================
    //  Streaming Loop
    // =========================================================================

    IEnumerator StreamingLoop()
    {
        int ticksSinceGuarantee = 0;
        while (_streamingActive)
        {
            // Collapse frontier cells within generation radius
            var nearby = new List<Vector3Int>();
            foreach (var pos in _frontier)
            {
                if (Vector3.Distance(TileWorldPos(pos), ObserverPosition) < generationRadius)
                    nearby.Add(pos);
            }
            nearby.Sort((a, b) =>
                Vector3.Distance(TileWorldPos(a), ObserverPosition)
                    .CompareTo(Vector3.Distance(TileWorldPos(b), ObserverPosition)));

            int processed = 0;
            foreach (var pos in nearby)
            {
                if (processed >= cellsPerTick) break;
                CollapseCell(pos);

                // Inline anomaly + props for streaming tiles
                if (_grid.TryGetValue(pos, out var cell))
                {
                    InjectAnomalies(cell, pos);
                    SpawnStreamingProps(cell, pos);
                }
                processed++;
            }

            // Unload tiles beyond radius — destroy geometry entirely rather than
            // hiding it.  The WFC data (_grid, _collapsed) is kept so the tile
            // isn't re-collapsed if the player walks back; SpawnTileVisual is
            // just called again to rebuild the visuals cheaply.
            foreach (var pos in _collapsed)
            {
                if (!_grid.TryGetValue(pos, out var cell)) continue;
                float dist = Vector3.Distance(TileWorldPos(pos), ObserverPosition);

                if (dist > unloadRadius && cell.TileVisualRoot != null)
                {
                    Destroy(cell.TileVisualRoot);
                    cell.TileVisualRoot = null;
                }
                else if (dist <= unloadRadius && cell.TileVisualRoot == null)
                {
                    // Re-enter range — rebuild visuals from existing cell data
                    SpawnTileVisual(cell);
                }
            }

            // Periodic path guarantee
            ticksSinceGuarantee++;
            if (ticksSinceGuarantee >= 20)
            {
                ticksSinceGuarantee = 0;
                // Clamp Y to 0 — generation is 2D; fly-camera Y must not bleed in.
                // Then snap to the nearest actually-collapsed cell so the BFS
                // always starts from a real tile, never from mid-air.
                Vector3Int observerCell = WorldToTile(ObserverPosition);
                observerCell.y = 0;
                observerCell = NearestCollapsedCell(observerCell);
                if (!ValidateAndForceExitPath(observerCell))
                    _forcedPaths++;
            }

            DungeonDebugController.Instance?.UpdateStepDescription(GetStepDescription());
            yield return new WaitForSeconds(streamTickInterval);
        }
    }

    void SpawnStreamingProps(ObeliskWFCCell cell, Vector3Int pos)
    {
        if (cell.IsMissing) return;
        var def = GetDef(cell.TileType);
        if (def == null) return;
        foreach (var slot in def.PropSlots)
            if (Random.value < slot.SpawnChance * globalPropDensity)
                SpawnPropVisual(pos, slot);
    }

    // =========================================================================
    //  Path Guarantee
    // =========================================================================

    bool ValidateAndForceExitPath(Vector3Int from)
    {
        // BFS through collapsed cells — check reachable depth
        var visited = new HashSet<Vector3Int> { from };
        var queue = new Queue<(Vector3Int pos, int dist)>();
        queue.Enqueue((from, 0));

        while (queue.Count > 0)
        {
            var (pos, dist) = queue.Dequeue();

            // Check for open frontier neighbor — means there's room ahead
            for (int f = 0; f < 4; f++)
            {
                var nb = pos + FaceDir[f];
                if (_frontier.Contains(nb)) return true;  // path is valid
                if (visited.Contains(nb)) continue;
                if (!_collapsed.Contains(nb)) continue;
                visited.Add(nb);
                if (dist + 1 < minExitPathLength)
                    queue.Enqueue((nb, dist + 1));
            }
        }

        // No valid exit found — force-carve a corridor north from the given cell
        for (int i = 1; i <= minExitPathLength; i++)
        {
            var fp = new Vector3Int(from.x, from.y, from.z + i);
            if (!_grid.ContainsKey(fp)) _grid[fp] = new ObeliskWFCCell(fp);
            var fc = _grid[fp];
            if (_collapsed.Contains(fp)) continue;

            fc.Collapse(ObeliskTileType.HallwayStraight_NS);
            fc.IsForcedPath = true;
            _frontier.Remove(fp);
            _collapsed.Add(fp);
            _tilesGenerated++;
            SpawnTileVisual(fc, C_FORCED_PATH);

            // Ensure frontier extends past forced segment
            if (i == minExitPathLength)
            {
                var ahead = new Vector3Int(fp.x, fp.y, fp.z + 1);
                if (!_grid.ContainsKey(ahead))
                {
                    _grid[ahead] = new ObeliskWFCCell(ahead);
                    _frontier.Add(ahead);
                    SpawnEntropyVisual(ahead);
                }
            }
        }
        return false;
    }

    // =========================================================================
    //  Anomaly Passes
    // =========================================================================

    int RunGeometricDeviation()
    {
        if (Random.value > geometricDeviationChance) return 0;
        int count = 0;

        // Find runs of 3+ HallwayStraight_NS tiles; force the middle one to a corner
        var straightNS = _collapsed
            .Where(p => _grid.TryGetValue(p, out var c) && c.TileType == ObeliskTileType.HallwayStraight_NS)
            .OrderBy(p => p.z).ThenBy(p => p.x)
            .ToList();

        for (int i = 1; i < straightNS.Count - 1; i++)
        {
            var prev = straightNS[i - 1];
            var curr = straightNS[i];
            var next = straightNS[i + 1];
            bool sameX = curr.x == prev.x && curr.x == next.x;
            bool inARow = curr.z == prev.z + 1 && curr.z + 1 == next.z;
            if (!sameX || !inARow) continue;

            if (!_grid.TryGetValue(curr, out var cell)) continue;
            cell.Collapse(Random.value > 0.5f ? ObeliskTileType.HallwayCorner_NE : ObeliskTileType.HallwayCorner_NW);
            RefreshTileVisual(cell, curr);
            _anomalyCount++;
            count++;
            i++; // skip next tile to avoid cascade
        }

        // Also do the EW axis
        var straightEW = _collapsed
            .Where(p => _grid.TryGetValue(p, out var c) && c.TileType == ObeliskTileType.HallwayStraight_EW)
            .OrderBy(p => p.x).ThenBy(p => p.z)
            .ToList();

        for (int i = 1; i < straightEW.Count - 1; i++)
        {
            var prev = straightEW[i - 1];
            var curr = straightEW[i];
            var next = straightEW[i + 1];
            bool sameZ = curr.z == prev.z && curr.z == next.z;
            bool inARow = curr.x == prev.x + 1 && curr.x + 1 == next.x;
            if (!sameZ || !inARow) continue;

            if (!_grid.TryGetValue(curr, out var cell)) continue;
            cell.Collapse(Random.value > 0.5f ? ObeliskTileType.HallwayCorner_SE : ObeliskTileType.HallwayCorner_SW);
            RefreshTileVisual(cell, curr);
            _anomalyCount++;
            count++;
            i++;
        }

        return count;
    }

    int RunPerseveration()
    {
        if (Random.value > perseverationChance) return 0;

        // Find the longest NS straight-run and clone it forward once
        var straightNS = _collapsed
            .Where(p => _grid.TryGetValue(p, out var c) && c.TileType == ObeliskTileType.HallwayStraight_NS)
            .OrderBy(p => p.z)
            .ToList();

        if (straightNS.Count < 4) return 0;

        int cloneLen = Mathf.Min(5, straightNS.Count);
        int cloneCount = 0;

        for (int i = 0; i < cloneLen; i++)
        {
            var src = straightNS[i];
            var dest = new Vector3Int(src.x, src.y, src.z + straightNS.Count + i + 1);
            if (_collapsed.Contains(dest)) continue;

            if (!_grid.ContainsKey(dest)) _grid[dest] = new ObeliskWFCCell(dest);
            var c = _grid[dest];
            c.Collapse(ObeliskTileType.HallwayStraight_NS);
            c.IsPerseverated = true;
            _frontier.Remove(dest);
            _collapsed.Add(dest);
            _tilesGenerated++;
            SpawnTileVisual(c);
            cloneCount++;
        }

        return cloneCount;
    }

    // =========================================================================
    //  Terminal Structure (Step 9)
    // =========================================================================

    IEnumerator SpawnTerminalRoom(Vector3Int center)
    {
        Vector3 wc = TileWorldPos(center);

        // Clear a 5x5 area and force SmallRoom tiles
        for (int dx = -2; dx <= 2; dx++)
            for (int dz = -2; dz <= 2; dz++)
            {
                var p = new Vector3Int(center.x + dx, center.y, center.z + dz);
                if (!_grid.ContainsKey(p)) _grid[p] = new ObeliskWFCCell(p);
                var c = _grid[p];
                c.Collapse(ObeliskTileType.SmallRoom);
                c.IsTerminal = true;
                if (!_collapsed.Contains(p)) { _collapsed.Add(p); _tilesGenerated++; }
                _frontier.Remove(p);
                RefreshTileVisual(c, p, C_TERMINAL);
                yield return new WaitForSeconds(0.04f);
            }

        yield return new WaitForSeconds(0.2f);

        // Massive Pillar
        float pillarH = tileHeight * 6f;
        var pillar = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pillar.transform.SetParent(_goTerminal.transform, false);
        pillar.transform.position = wc + Vector3.up * (pillarH * 0.5f);
        pillar.transform.localScale = new Vector3(tileSize * 0.35f, pillarH * 0.5f, tileSize * 0.35f);
        SetColor(pillar, new Color(0.10f, 0.08f, 0.12f));
        yield return new WaitForSeconds(0.4f);

        // Spiral staircase descending around the pillar
        int spiralSteps = 24;
        float angleStep = 15f;
        float descentStep = 0.22f;
        float armRadius = tileSize * 0.85f;

        for (int i = 0; i < spiralSteps; i++)
        {
            float angle = i * angleStep * Mathf.Deg2Rad;
            float wx = wc.x + Mathf.Cos(angle) * armRadius;
            float wz = wc.z + Mathf.Sin(angle) * armRadius;
            float wy = wc.y - i * descentStep;

            var step = GameObject.CreatePrimitive(PrimitiveType.Cube);
            step.transform.SetParent(_goTerminal.transform, false);
            step.transform.position = new Vector3(wx, wy, wz);
            step.transform.rotation = Quaternion.Euler(0f, -(i * angleStep), 0f);
            step.transform.localScale = new Vector3(tileSize * 0.5f, 0.14f, tileSize * 0.28f);
            SetColor(step, C_STAIR);
            yield return new WaitForSeconds(0.035f);
        }

        yield return new WaitForSeconds(0.15f);

        // Void at base of the staircase
        float voidY = wc.y - spiralSteps * descentStep - 0.5f;
        var voidBox = GameObject.CreatePrimitive(PrimitiveType.Cube);
        voidBox.transform.SetParent(_goTerminal.transform, false);
        voidBox.transform.position = new Vector3(wc.x, voidY, wc.z);
        voidBox.transform.localScale = Vector3.one * tileSize * 1.6f;
        SetColor(voidBox, C_VOID, _voidMat);
    }

    // =========================================================================
    //  Visualization
    // =========================================================================

    void SpawnTileVisual(ObeliskWFCCell cell, Color? overrideCol = null)
    {
        if (cell.TileVisualRoot != null)
        {
            Destroy(cell.TileVisualRoot);
            cell.TileVisualRoot = null;
        }

        var def = GetDef(cell.TileType);
        if (def == null) return;

        var root = new GameObject($"Tile_{cell.TileType}_{cell.Position}");
        root.transform.SetParent(_goTiles.transform, false);
        root.transform.position = TileWorldPos(cell.Position);
        cell.TileVisualRoot = root;

        // Void tile — solid black fill
        if (cell.TileType == ObeliskTileType.VoidTile)
        {
            SpawnSlab(root, new Vector3(0, tileHeight * 0.5f, 0),
                      new Vector3(tileSize, tileHeight, tileSize), C_VOID, _voidMat);
            return;
        }

        if (!richVisualization)
        {
            Color fc = overrideCol ?? TileColor(cell);
            SpawnSlab(root, new Vector3(0, tileHeight * 0.5f, 0),
                      new Vector3(tileSize * 0.88f, tileHeight * 0.88f, tileSize * 0.88f), fc);
            return;
        }

        // ── Rich visualization ──────────────────────────────────────────────

        // Floor
        Color floorCol = overrideCol ?? (cell.IsForcedPath ? C_FORCED_PATH : C_FLOOR);
        SpawnSlab(root, new Vector3(0, 0.06f, 0),
                  new Vector3(tileSize, 0.12f, tileSize), floorCol);

        // Ceiling
        if (!cell.HasVoidCeiling)
        {
            SpawnSlab(root, new Vector3(0, tileHeight, 0),
                      new Vector3(tileSize, 0.12f, tileSize),
                      overrideCol ?? C_CEILING);
        }
        else
        {
            // Void ceiling: thin dark cap way above, creates "walls to infinity" look
            SpawnSlab(root, new Vector3(0, tileHeight * 2.8f, 0),
                      new Vector3(tileSize, 0.14f, tileSize), C_CEILING_VOID);
        }

        // Walls and door frames
        for (int f = 0; f < 4; f++)
        {
            ObeliskSocketType sock = def.GetSocket(f);
            Color wallCol = overrideCol ?? (cell.IsMissing ? C_MISSING : C_WALL);

            if (sock == ObeliskSocketType.Wall)
            {
                SpawnSlab(root, FaceWallPos(f), FaceWallScale(f), wallCol);
            }
            else if (sock == ObeliskSocketType.Door && !cell.IsMissing)
            {
                SpawnDoorFrame(root, f, overrideCol ?? C_DOOR_FRAME);
            }
        }

        // Stair geometry
        if (cell.TileType == ObeliskTileType.StaircaseUp ||
            cell.TileType == ObeliskTileType.StaircaseDown ||
            cell.TileType == ObeliskTileType.InverseStairPortal)
        {
            SpawnStairGeometry(root, cell.TileType, cell.IsInverseStair);
        }
    }

    void RefreshTileVisual(ObeliskWFCCell cell, Vector3Int pos, Color? overrideCol = null)
    {
        SpawnTileVisual(cell, overrideCol);
    }

    void SpawnSlab(GameObject parent, Vector3 localPos, Vector3 scale, Color col,
                   Material matOverride = null)
    {
        var s = GameObject.CreatePrimitive(PrimitiveType.Cube);
        s.transform.SetParent(parent.transform, false);
        s.transform.localPosition = localPos;
        s.transform.localScale = scale;
        SetColor(s, col, matOverride);
    }

    void SpawnDoorFrame(GameObject parent, int face, Color col)
    {
        var dir = new Vector3(FaceDir[face].x, 0, FaceDir[face].z);
        var right = Vector3.Cross(Vector3.up, dir).normalized;
        float hw = tileSize * 0.30f;  // half door opening width
        float ft = 0.14f;             // frame thickness

        // Left pillar
        SpawnSlab(parent,
            dir * tileSize * 0.5f + right * (hw + ft * 0.5f) + Vector3.up * (tileHeight * 0.5f),
            new Vector3(ft, tileHeight, ft), col);

        // Right pillar
        SpawnSlab(parent,
            dir * tileSize * 0.5f - right * (hw + ft * 0.5f) + Vector3.up * (tileHeight * 0.5f),
            new Vector3(ft, tileHeight, ft), col);

        // Lintel
        SpawnSlab(parent,
            dir * tileSize * 0.5f + Vector3.up * (tileHeight - ft * 0.5f),
            new Vector3(hw * 2f + ft * 2f, ft, ft), col);
    }

    void SpawnStairGeometry(GameObject parent, ObeliskTileType type, bool inverse)
    {
        int steps = 7;
        bool goUp = (type == ObeliskTileType.StaircaseUp) != inverse;
        float stepH = tileHeight / steps;
        float stepD = tileSize / steps;
        Color col = inverse ? C_INVERSE_STAIR : C_STAIR;

        for (int i = 0; i < steps; i++)
        {
            float y = goUp ? i * stepH : tileHeight - i * stepH;
            var s = GameObject.CreatePrimitive(PrimitiveType.Cube);
            s.transform.SetParent(parent.transform, false);
            s.transform.localPosition = new Vector3(
                0f,
                y + stepH * 0.5f,
                -tileSize * 0.5f + i * stepD + stepD * 0.5f);
            s.transform.localScale = new Vector3(tileSize * 0.78f, stepH, stepD);
            SetColor(s, col);
        }
    }

    void SpawnEntropyVisual(Vector3Int pos)
    {
        if (!_grid.TryGetValue(pos, out var cell)) return;
        if (cell.EntropyVisual != null) return;
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.transform.SetParent(_goEntropy.transform, false);
        go.transform.position = TileWorldPos(pos) + Vector3.up * (tileHeight * 0.5f);
        go.transform.localScale = Vector3.one * tileSize * 0.65f;
        SetColor(go, C_ENTROPY);
        cell.EntropyVisual = go;
    }

    void SpawnPropVisual(Vector3Int tilePos, ObeliskPropSlot slot)
    {
        Vector3 baseWorld = TileWorldPos(tilePos);
        Vector3 offset = slot.LocalOffset;
        if (slot.RandomizePos)
            offset += new Vector3(Random.Range(-0.25f, 0.25f), 0f, Random.Range(-0.25f, 0.25f));

        float yRot = Random.Range(slot.RotRange.x, slot.RotRange.y);
        Quaternion rot = Quaternion.Euler(0f, yRot, 0f);

        GameObject prop = null;
        Color col = C_PROP_DECOR;

        switch (slot.Category)
        {
            case ObeliskPropCategory.Furniture:
                prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
                prop.transform.localScale = new Vector3(
                    tileSize * Random.Range(0.13f, 0.22f),
                    tileSize * Random.Range(0.12f, 0.28f),
                    tileSize * Random.Range(0.12f, 0.18f));
                col = C_PROP_FURN;
                break;

            case ObeliskPropCategory.ClueMarker:
                prop = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                prop.transform.localScale = Vector3.one * tileSize * 0.07f;
                col = C_PROP_CLUE;
                break;

            case ObeliskPropCategory.AmbientDecor:
                prop = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                prop.transform.localScale = new Vector3(
                    tileSize * 0.05f,
                    tileSize * Random.Range(0.14f, 0.22f),
                    tileSize * 0.05f);
                col = C_PROP_DECOR;
                break;

            case ObeliskPropCategory.CreepyDetail:
                prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
                prop.transform.localScale = new Vector3(
                    tileSize * Random.Range(0.03f, 0.09f),
                    tileSize * Random.Range(0.06f, 0.16f),
                    tileSize * Random.Range(0.03f, 0.07f));
                col = C_PROP_CREEPY;
                break;
        }

        if (prop == null) return;
        prop.transform.SetParent(_goProps.transform, false);
        prop.transform.position = baseWorld + offset;
        prop.transform.rotation = rot;
        SetColor(prop, col);
    }

    // =========================================================================
    //  Helpers
    // =========================================================================

    float DepthFactor(Vector3Int pos) =>
        TileWorldPos(pos).magnitude * anomalyDepthScale;

    Vector3 TileWorldPos(Vector3Int pos) =>
        new Vector3(pos.x * tileSize, pos.y * tileHeight, pos.z * tileSize);

    Vector3Int WorldToTile(Vector3 world) =>
        new Vector3Int(
            Mathf.RoundToInt(world.x / tileSize),
            Mathf.RoundToInt(world.y / tileHeight),
            Mathf.RoundToInt(world.z / tileSize));

    ObeliskTileDefinition GetDef(ObeliskTileType type)
    {
        if (_tileDefDict == null) return null;
        _tileDefDict.TryGetValue(type, out var d);
        return d;
    }

    Color TileColor(ObeliskWFCCell cell)
    {
        if (cell.IsInverseStair) return C_INVERSE_STAIR;
        if (cell.IsForcedPath) return C_FORCED_PATH;
        if (cell.IsMissing) return C_MISSING;
        if (cell.HasVoidCeiling) return C_CEILING_VOID;
        if (cell.IsTerminal) return C_TERMINAL;
        switch (cell.TileType)
        {
            case ObeliskTileType.HallwayStraight_NS:
            case ObeliskTileType.HallwayStraight_EW: return C_FLOOR;
            case ObeliskTileType.HallwayCorner_NE:
            case ObeliskTileType.HallwayCorner_NW:
            case ObeliskTileType.HallwayCorner_SE:
            case ObeliskTileType.HallwayCorner_SW: return new Color(0.21f, 0.21f, 0.26f);
            case ObeliskTileType.HallwayBranch_NES:
            case ObeliskTileType.HallwayBranch_NEW:
            case ObeliskTileType.HallwayBranch_NSW:
            case ObeliskTileType.HallwayBranch_SEW: return new Color(0.26f, 0.24f, 0.29f);
            case ObeliskTileType.HallwayCrossroads: return new Color(0.30f, 0.26f, 0.33f);
            case ObeliskTileType.DeadEnd_N:
            case ObeliskTileType.DeadEnd_S:
            case ObeliskTileType.DeadEnd_E:
            case ObeliskTileType.DeadEnd_W: return new Color(0.18f, 0.13f, 0.13f);
            case ObeliskTileType.SmallRoom: return new Color(0.26f, 0.23f, 0.19f);
            case ObeliskTileType.StaircaseUp:
            case ObeliskTileType.StaircaseDown: return C_STAIR;
            case ObeliskTileType.InverseStairPortal: return C_INVERSE_STAIR;
            case ObeliskTileType.VoidTile: return C_VOID;
            default: return Color.grey;
        }
    }

    Vector3 FaceWallPos(int face)
    {
        float h = tileHeight * 0.5f;
        float e = tileSize * 0.5f;
        switch (face)
        {
            case FACE_N: return new Vector3(0, h, e);
            case FACE_S: return new Vector3(0, h, -e);
            case FACE_E: return new Vector3(e, h, 0);
            case FACE_W: return new Vector3(-e, h, 0);
            default: return Vector3.zero;
        }
    }

    Vector3 FaceWallScale(int face)
    {
        float t = 0.13f;
        switch (face)
        {
            case FACE_N:
            case FACE_S: return new Vector3(tileSize, tileHeight, t);
            case FACE_E:
            case FACE_W: return new Vector3(t, tileHeight, tileSize);
            default: return Vector3.one;
        }
    }

    void SetColor(GameObject go, Color col, Material matOverride = null)
    {
        var rend = go.GetComponent<Renderer>();
        if (rend == null) return;
        rend.sharedMaterial = GetCachedMat(col, matOverride);
    }

    /// Returns a shared Material for <paramref name="col"/>, creating it once and
    /// caching it.  All primitives that share the same color share the same Material
    /// instance, which lets Unity batch them and collapses draw calls from thousands
    /// down to roughly the number of distinct colors in the palette (~12).
    Material GetCachedMat(Color col, Material baseOverride = null)
    {
        // Key on RGBA packed to int for fast dictionary lookup
        int key = ((int)(col.r * 255) << 24) |
                  ((int)(col.g * 255) << 16) |
                  ((int)(col.b * 255) << 8) |
                   (int)(col.a * 255);

        // Void material is never cached alongside regular materials
        if (baseOverride == _voidMat)
        {
            key ^= unchecked((int)0xDEAD0000);
            if (!_matCache.TryGetValue(key, out var vm))
            {
                vm = new Material(_voidMat);
                vm.color = col;
                _matCache[key] = vm;
            }
            return vm;
        }

        if (!_matCache.TryGetValue(key, out var m))
        {
            m = new Material(_mat);
            m.color = col;
            _matCache[key] = m;
        }
        return m;
    }

    /// Returns the collapsed tile whose grid position is closest (XZ only) to <paramref name="from"/>.
    /// Falls back to Vector3Int.zero if the grid is empty.
    Vector3Int NearestCollapsedCell(Vector3Int from)
    {
        Vector3Int nearest = Vector3Int.zero;
        float minSqr = float.MaxValue;
        foreach (var p in _collapsed)
        {
            float dx = p.x - from.x;
            float dz = p.z - from.z;
            float sqr = dx * dx + dz * dz;
            if (sqr < minSqr) { minSqr = sqr; nearest = p; }
        }
        return nearest;
    }

    void ClearAll()
    {
        _grid.Clear();
        _frontier.Clear();
        _collapsed.Clear();
        _tilesGenerated = 0;
        _anomalyCount = 0;
        _forcedPaths = 0;
        _maxDepth = 0f;
        _matCache.Clear();
        ClearGO(_goTiles);
        ClearGO(_goProps);
        ClearGO(_goEntropy);
        ClearGO(_goPathDebug);
        ClearGO(_goTerminal);
    }

    static void ClearGO(GameObject parent)
    {
        if (parent == null) return;
        foreach (Transform t in parent.transform) Destroy(t.gameObject);
    }

    // =========================================================================
    //  Editor trigger — force terminal generation at any time
    // =========================================================================
#if UNITY_EDITOR
    [ContextMenu("Debug — Trigger Terminal Structure Now")]
    void DebugTriggerTerminal()
    {
        if (_currentStep < 3) { Debug.LogWarning("[Obelisk] Generate at least to step 3 first."); return; }
        StartCoroutine(Step9_TerminalStructure());
    }
#endif
}

// =============================================================================
//  Enums
// =============================================================================

public enum ObeliskSocketType
{
    Wall,
    Door,
    CeilingStandard,
    CeilingVoid,
    Floor,
    InverseStair,
    Null,           // wildcard — connects to anything (null-adjacency anomaly)
}

public enum ObeliskTileType
{
    HallwayStraight_NS,
    HallwayStraight_EW,
    HallwayCorner_NE,
    HallwayCorner_NW,
    HallwayCorner_SE,
    HallwayCorner_SW,
    HallwayBranch_NES,
    HallwayBranch_NEW,
    HallwayBranch_NSW,
    HallwayBranch_SEW,
    HallwayCrossroads,
    DeadEnd_N,
    DeadEnd_S,
    DeadEnd_E,
    DeadEnd_W,
    SmallRoom,
    StaircaseUp,
    StaircaseDown,
    InverseStairPortal,
    VoidTile,
    // ── future tiles go here — no other code needs to change ──
}

public enum ObeliskPropCategory
{
    Furniture,
    ClueMarker,
    AmbientDecor,
    CreepyDetail,
}

// =============================================================================
//  Data Classes
// =============================================================================

public class ObeliskPropSlot
{
    public ObeliskPropCategory Category;
    public Vector3 LocalOffset;
    public Vector2 RotRange;          // min/max Y rotation
    public float SpawnChance;
    public bool RandomizePos;

    public ObeliskPropSlot(ObeliskPropCategory cat, Vector3 offset,
                           float chance, bool randomize = true)
    {
        Category = cat;
        LocalOffset = offset;
        SpawnChance = chance;
        RandomizePos = randomize;
        RotRange = new Vector2(0f, 360f);
    }
}

/// <summary>
/// Tile definition: socket rules + spawn weight + prop slots.
/// Add new tile types by adding entries to BuildLibrary() — nothing else changes.
/// Socket order: [0]=N, [1]=S, [2]=E, [3]=W, [4]=U, [5]=D
/// </summary>
public class ObeliskTileDefinition
{
    public ObeliskTileType TileType;
    public ObeliskSocketType[] Sockets;   // length-6, indexed by face constant
    public float Weight;      // relative WFC spawn probability
    public ObeliskPropSlot[] PropSlots;

    public ObeliskSocketType GetSocket(int face) =>
        (Sockets != null && face >= 0 && face < Sockets.Length)
            ? Sockets[face]
            : ObeliskSocketType.Wall;

    // ── Socket shorthand ─────────────────────────────────────────────────────
    const ObeliskSocketType Wl = ObeliskSocketType.Wall;
    const ObeliskSocketType Dr = ObeliskSocketType.Door;
    const ObeliskSocketType Cs = ObeliskSocketType.CeilingStandard;
    const ObeliskSocketType Fl = ObeliskSocketType.Floor;
    const ObeliskSocketType Iv = ObeliskSocketType.InverseStair;

    static ObeliskSocketType[] S(
        ObeliskSocketType n, ObeliskSocketType s,
        ObeliskSocketType e, ObeliskSocketType w,
        ObeliskSocketType u = ObeliskSocketType.CeilingStandard,
        ObeliskSocketType d = ObeliskSocketType.Floor)
        => new[] { n, s, e, w, u, d };

    // ── Library ───────────────────────────────────────────────────────────────
    public static ObeliskTileDefinition[] BuildLibrary() => new[]
    {
        // ── Straights (most common — backbone of the hallway) ─────────────────
        new ObeliskTileDefinition
        {
            TileType = ObeliskTileType.HallwayStraight_NS,
            Sockets  = S(Dr, Dr, Wl, Wl),
            Weight   = 10f,
            PropSlots = new[]
            {
                new ObeliskPropSlot(ObeliskPropCategory.AmbientDecor, new Vector3(-1.3f, 0, 0),     0.22f),
                new ObeliskPropSlot(ObeliskPropCategory.AmbientDecor, new Vector3( 1.3f, 0, 0),     0.22f),
                new ObeliskPropSlot(ObeliskPropCategory.CreepyDetail, new Vector3( 0,    0, 0.6f),  0.08f),
                new ObeliskPropSlot(ObeliskPropCategory.ClueMarker,   new Vector3( 0, 0.05f, 0),    0.05f),
            }
        },
        new ObeliskTileDefinition
        {
            TileType = ObeliskTileType.HallwayStraight_EW,
            Sockets  = S(Wl, Wl, Dr, Dr),
            Weight   = 10f,
            PropSlots = new[]
            {
                new ObeliskPropSlot(ObeliskPropCategory.AmbientDecor, new Vector3(0, 0, -1.3f),    0.22f),
                new ObeliskPropSlot(ObeliskPropCategory.AmbientDecor, new Vector3(0, 0,  1.3f),    0.22f),
                new ObeliskPropSlot(ObeliskPropCategory.CreepyDetail, new Vector3(0.6f, 0, 0),     0.08f),
            }
        },

        // ── Corners ───────────────────────────────────────────────────────────
        new ObeliskTileDefinition
        {
            TileType = ObeliskTileType.HallwayCorner_NE, Sockets = S(Dr,Wl,Dr,Wl), Weight = 3f,
            PropSlots = new[] { new ObeliskPropSlot(ObeliskPropCategory.AmbientDecor, new Vector3(-1.1f,0,-1.1f), 0.28f) }
        },
        new ObeliskTileDefinition
        {
            TileType = ObeliskTileType.HallwayCorner_NW, Sockets = S(Dr,Wl,Wl,Dr), Weight = 3f,
            PropSlots = new[] { new ObeliskPropSlot(ObeliskPropCategory.AmbientDecor, new Vector3( 1.1f,0,-1.1f), 0.28f) }
        },
        new ObeliskTileDefinition
        {
            TileType = ObeliskTileType.HallwayCorner_SE, Sockets = S(Wl,Dr,Dr,Wl), Weight = 3f,
            PropSlots = new[] { new ObeliskPropSlot(ObeliskPropCategory.AmbientDecor, new Vector3(-1.1f,0, 1.1f), 0.28f) }
        },
        new ObeliskTileDefinition
        {
            TileType = ObeliskTileType.HallwayCorner_SW, Sockets = S(Wl,Dr,Wl,Dr), Weight = 3f,
            PropSlots = new[] { new ObeliskPropSlot(ObeliskPropCategory.AmbientDecor, new Vector3( 1.1f,0, 1.1f), 0.28f) }
        },

        // ── T-junctions ───────────────────────────────────────────────────────
        new ObeliskTileDefinition
        {
            TileType = ObeliskTileType.HallwayBranch_NES, Sockets = S(Dr,Dr,Dr,Wl), Weight = 2f,
            PropSlots = new[] { new ObeliskPropSlot(ObeliskPropCategory.Furniture, new Vector3(-1.1f,0,0), 0.35f) }
        },
        new ObeliskTileDefinition
        {
            TileType = ObeliskTileType.HallwayBranch_NEW, Sockets = S(Dr,Wl,Dr,Dr), Weight = 2f,
            PropSlots = new[] { new ObeliskPropSlot(ObeliskPropCategory.Furniture, new Vector3(0,0,-1.1f), 0.35f) }
        },
        new ObeliskTileDefinition
        {
            TileType = ObeliskTileType.HallwayBranch_NSW, Sockets = S(Dr,Dr,Wl,Dr), Weight = 2f,
            PropSlots = new[] { new ObeliskPropSlot(ObeliskPropCategory.Furniture, new Vector3(1.1f,0,0),  0.35f) }
        },
        new ObeliskTileDefinition
        {
            TileType = ObeliskTileType.HallwayBranch_SEW, Sockets = S(Wl,Dr,Dr,Dr), Weight = 2f,
            PropSlots = new[] { new ObeliskPropSlot(ObeliskPropCategory.Furniture, new Vector3(0,0,1.1f),  0.35f) }
        },

        // ── Crossroads ────────────────────────────────────────────────────────
        new ObeliskTileDefinition
        {
            TileType = ObeliskTileType.HallwayCrossroads, Sockets = S(Dr,Dr,Dr,Dr), Weight = 1f,
            PropSlots = new[]
            {
                new ObeliskPropSlot(ObeliskPropCategory.Furniture,    new Vector3( 1.1f,0, 1.1f),  0.45f),
                new ObeliskPropSlot(ObeliskPropCategory.CreepyDetail, new Vector3(-1.1f,0,-1.1f),  0.18f),
                new ObeliskPropSlot(ObeliskPropCategory.ClueMarker,   new Vector3(0, 0.05f, 0),    0.10f),
            }
        },

        // ── Dead ends (prop-heavy — natural stopping points) ──────────────────
        new ObeliskTileDefinition
        {
            TileType = ObeliskTileType.DeadEnd_N, Sockets = S(Dr,Wl,Wl,Wl), Weight = 1.5f,
            PropSlots = new[]
            {
                new ObeliskPropSlot(ObeliskPropCategory.Furniture,    new Vector3(0,    0,  1.0f), 0.55f),
                new ObeliskPropSlot(ObeliskPropCategory.CreepyDetail, new Vector3(0.4f, 0,  1.2f), 0.30f),
                new ObeliskPropSlot(ObeliskPropCategory.ClueMarker,   new Vector3(0, 0.05f, 0.8f), 0.18f),
            }
        },
        new ObeliskTileDefinition
        {
            TileType = ObeliskTileType.DeadEnd_S, Sockets = S(Wl,Dr,Wl,Wl), Weight = 1.5f,
            PropSlots = new[]
            {
                new ObeliskPropSlot(ObeliskPropCategory.Furniture,    new Vector3(0, 0, -1.0f),   0.55f),
                new ObeliskPropSlot(ObeliskPropCategory.CreepyDetail, new Vector3(0, 0, -1.3f),   0.25f),
            }
        },
        new ObeliskTileDefinition
        {
            TileType = ObeliskTileType.DeadEnd_E, Sockets = S(Wl,Wl,Dr,Wl), Weight = 1.5f,
            PropSlots = new[]
            {
                new ObeliskPropSlot(ObeliskPropCategory.Furniture,    new Vector3( 1.0f, 0, 0),   0.55f),
                new ObeliskPropSlot(ObeliskPropCategory.ClueMarker,   new Vector3( 1.2f, 0.05f, 0), 0.18f),
            }
        },
        new ObeliskTileDefinition
        {
            TileType = ObeliskTileType.DeadEnd_W, Sockets = S(Wl,Wl,Wl,Dr), Weight = 1.5f,
            PropSlots = new[]
            {
                new ObeliskPropSlot(ObeliskPropCategory.Furniture,    new Vector3(-1.0f, 0, 0),   0.55f),
                new ObeliskPropSlot(ObeliskPropCategory.ClueMarker,   new Vector3(-1.2f, 0.05f, 0), 0.18f),
            }
        },

        // ── Small room ────────────────────────────────────────────────────────
        new ObeliskTileDefinition
        {
            TileType = ObeliskTileType.SmallRoom, Sockets = S(Dr,Dr,Dr,Dr), Weight = 1.5f,
            PropSlots = new[]
            {
                new ObeliskPropSlot(ObeliskPropCategory.Furniture,    new Vector3(-1.2f, 0, -1.2f), 0.65f),
                new ObeliskPropSlot(ObeliskPropCategory.Furniture,    new Vector3( 1.2f, 0, -1.2f), 0.50f),
                new ObeliskPropSlot(ObeliskPropCategory.AmbientDecor, new Vector3( 0,    0,  1.2f), 0.45f),
                new ObeliskPropSlot(ObeliskPropCategory.ClueMarker,   new Vector3( 0, 0.05f, 0),    0.22f),
                new ObeliskPropSlot(ObeliskPropCategory.CreepyDetail, new Vector3(-1.0f, 0, 1.0f),  0.18f),
            }
        },

        // ── Staircases ────────────────────────────────────────────────────────
        new ObeliskTileDefinition
        {
            TileType = ObeliskTileType.StaircaseUp,
            Sockets  = S(Dr, Dr, Wl, Wl),
            Weight   = 0.8f,
            PropSlots = new[] { new ObeliskPropSlot(ObeliskPropCategory.CreepyDetail, new Vector3(0,0,0.5f), 0.22f) }
        },
        new ObeliskTileDefinition
        {
            TileType = ObeliskTileType.StaircaseDown,
            Sockets  = S(Dr, Dr, Wl, Wl),
            Weight   = 0.8f,
            PropSlots = new[] { new ObeliskPropSlot(ObeliskPropCategory.CreepyDetail, new Vector3(0,0,0.5f), 0.22f) }
        },

        // ── Inverse stair portal ──────────────────────────────────────────────
        new ObeliskTileDefinition
        {
            TileType = ObeliskTileType.InverseStairPortal,
            Sockets  = S(Dr, Dr, Wl, Wl, Iv, Fl),
            Weight   = 0.3f,
            PropSlots = new ObeliskPropSlot[0]
        },

        // ── Void tile ─────────────────────────────────────────────────────────
        new ObeliskTileDefinition
        {
            TileType = ObeliskTileType.VoidTile,
            Sockets  = new ObeliskSocketType[]
            {
                ObeliskSocketType.Wall, ObeliskSocketType.Wall,
                ObeliskSocketType.Wall, ObeliskSocketType.Wall,
                ObeliskSocketType.Wall, ObeliskSocketType.Wall
            },
            Weight   = 0.4f,
            PropSlots = new ObeliskPropSlot[0]
        },
    };
}

/// <summary>
/// WFC cell: position, collapsed tile type, and anomaly flags.
/// </summary>
public class ObeliskWFCCell
{
    public Vector3Int Position;
    public ObeliskTileType TileType { get; private set; }
    public bool IsCollapsed = false;

    // Anomaly flags
    public bool IsMissing = false;   // door frames + props stripped
    public bool HasVoidCeiling = false;  // ceiling open to void
    public bool IsInverseStair = false;  // stair direction inverted
    public bool IsForcedPath = false;   // placed by path guarantee
    public bool IsPerseverated = false;  // placed by perseveration clone
    public bool IsTerminal = false;   // part of terminal structure

    // Visuals
    public GameObject TileVisualRoot;
    public GameObject EntropyVisual;

    public ObeliskWFCCell(Vector3Int pos) { Position = pos; }

    public void Collapse(ObeliskTileType type)
    {
        TileType = type;
        IsCollapsed = true;
    }
}
