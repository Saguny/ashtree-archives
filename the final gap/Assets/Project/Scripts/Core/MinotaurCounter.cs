using UnityEngine;

/// <summary>
/// Singleton that tracks the Minotaur Counter and broadcasts state transitions.
///
/// Two counter sources (per design doc):
///   1. Negative tag connections  →  +1 or +2 per bad pairing
///   2. Volume tax               →  +1 for every five connections made total
///
/// State thresholds (configurable in Inspector):
///   State 1  ≥ 5   →  subtle anomalies begin
///   State 2  ≥ 15  →  stronger anomalies + audio
///   State 3  ≥ 20  →  unlocks MINOTAUR ending
///
/// Does NOT directly manipulate the house — it fires GameEvents that a
/// separate house-manipulation system should subscribe to.
/// </summary>
public class MinotaurCounter : MonoBehaviour
{
    public static MinotaurCounter Instance { get; private set; }

    [Header("State Thresholds")]
    [SerializeField] int state1Threshold  = 5;
    [SerializeField] int state2Threshold  = 15;
    [SerializeField] int state3Threshold  = 20;

    [Header("Volume Tax")]
    [Tooltip("Every N total connections made adds +1 to the counter, regardless of polarity.")]
    [SerializeField] int connectionBatchSize = 5;

    // ── Runtime state ─────────────────────────────────────────────────────────

    int _counterTotal;      // running Minotaur Counter value
    int _totalConnections;  // cumulative connections ever made on the board
    int _minotaurState;     // 0 = dormant, 1/2/3 = escalating

    public int CounterTotal     => _counterTotal;
    public int MinotaurState    => _minotaurState;
    public int TotalConnections => _totalConnections;

    // Aliases used by the dev tool and external systems
    public int CurrentTotal => _counterTotal;
    public int CurrentState => _minotaurState;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void OnEnable()  => GameEvents.OnYarnConnected += HandleYarnConnected;
    void OnDisable() => GameEvents.OnYarnConnected -= HandleYarnConnected;

    // ── Core logic ────────────────────────────────────────────────────────────

    void HandleYarnConnected(CardBehaviour a, CardBehaviour b)
    {
        // 1. Evaluate the tag pairing
        TagConnectionResult result = TagConnectionResolver.Evaluate(a.CardTag, b.CardTag);

        // 2. Broadcast the resolved result so house-manipulation systems can react
        GameEvents.TagConnectionResolved(result, a, b);

        // 3. Apply negative penalty to counter
        _counterTotal += result.CounterDelta;

        // 4. Track total connections; apply volume tax every N connections
        _totalConnections++;
        if (_totalConnections % connectionBatchSize == 0)
            _counterTotal++;

        // 5. Notify listeners of new counter value
        GameEvents.MinotaurCounterChanged(_counterTotal);

        // 6. Check whether we've crossed a new state threshold
        CheckStateTransition();
    }

    void CheckStateTransition()
    {
        int newState;
        if      (_counterTotal >= state3Threshold) newState = 3;
        else if (_counterTotal >= state2Threshold) newState = 2;
        else if (_counterTotal >= state1Threshold) newState = 1;
        else                                       newState = 0;

        if (newState != _minotaurState)
        {
            _minotaurState = newState;
            GameEvents.MinotaurStateChanged(_minotaurState);
        }
    }

    // ── Debug helpers (Editor / LabyrinthDebug) ───────────────────────────────

#if UNITY_EDITOR
    [ContextMenu("Debug — Force State 1 (counter = 5)")]
    void DebugForceState1() => ForceCounter(state1Threshold);

    [ContextMenu("Debug — Force State 2 (counter = 15)")]
    void DebugForceState2() => ForceCounter(state2Threshold);

    [ContextMenu("Debug — Force State 3 (counter = 20)")]
    void DebugForceState3() => ForceCounter(state3Threshold);

    void ForceCounter(int value)
    {
        _counterTotal = value;
        GameEvents.MinotaurCounterChanged(_counterTotal);
        CheckStateTransition();
    }
#endif

    // ── Public dev-tool API (callable from EditorWindow at runtime) ───────────

    /// <summary>Set the counter to an explicit value and re-evaluate state. Dev / test only.</summary>
    public void DevSetCounter(int value)
    {
        _counterTotal = Mathf.Max(0, value);
        GameEvents.MinotaurCounterChanged(_counterTotal);
        CheckStateTransition();
    }

    /// <summary>Snap the counter to exactly the given state's threshold. Dev / test only.</summary>
    public void DevForceState(int state)
    {
        int target = state switch
        {
            1 => state1Threshold,
            2 => state2Threshold,
            3 => state3Threshold,
            _ => 0
        };
        DevSetCounter(target);
    }
}
