using UnityEngine;

/// <summary>
/// Singleton that tracks the Minotaur Counter and broadcasts state transitions.
///
/// Two counter sources:
///   1. Negative tag connections  →  +1 or +2 per bad pairing  (live, fires immediately)
///   2. Volume tax               →  floor(currentConnections / N) at departure only
///
/// The volume tax is NOT applied during board work — it is calculated from the
/// actual number of connections on the board at the moment FinalizeForDeparture()
/// is called (i.e. when the player tries to leave the office). This prevents the
/// exploit of making and breaking connections to inflate the cumulative connection
/// count without committing to anything.
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
    [Tooltip("At departure, every N connections currently on the board adds +1 to the counter.")]
    [SerializeField] int connectionBatchSize = 5;

    // ── Runtime state ─────────────────────────────────────────────────────────

    int _penaltyTotal;   // live: accumulated penalty points from bad tag pairings only
    int _counterTotal;   // announced value: _penaltyTotal during board work,
                         // _penaltyTotal + volumeTax after FinalizeForDeparture()
    int _minotaurState;  // 0 = dormant, 1/2/3 = escalating

    public int CounterTotal  => _counterTotal;
    public int PenaltyTotal  => _penaltyTotal;
    public int MinotaurState => _minotaurState;

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

        // 3. Apply negative penalty immediately — this is permanent live feedback
        _penaltyTotal  += result.CounterDelta;
        _counterTotal   = _penaltyTotal;

        // 4. Notify listeners and check state
        //    Volume tax is NOT added here — it is deferred to FinalizeForDeparture().
        GameEvents.MinotaurCounterChanged(_counterTotal);
        CheckStateTransition();
    }

    /// <summary>
    /// Called by EndingManager.TryTriggerOnHouseEntry() the moment the player
    /// tries to leave the office. Calculates the volume tax from the number of
    /// connections currently committed to the board and applies it on top of the
    /// accumulated penalty total.
    ///
    /// Because the tax is based on live board state rather than cumulative connection
    /// history, making and breaking connections has no effect — only what's actually
    /// on the board when the player walks out counts.
    /// </summary>
    public void FinalizeForDeparture()
    {
        int connectionCount = YarnSystem.Instance != null
            ? YarnSystem.Instance.ConnectionCount
            : 0;

        int volumeTax  = Mathf.FloorToInt(connectionCount / (float)connectionBatchSize);
        _counterTotal  = _penaltyTotal + volumeTax;

        Debug.Log($"[MinotaurCounter] Departure finalized — " +
                  $"penalties={_penaltyTotal}, connections={connectionCount}, " +
                  $"tax={volumeTax}, total={_counterTotal}");

        GameEvents.MinotaurCounterChanged(_counterTotal);
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
        _penaltyTotal = value;
        _counterTotal = value;
        GameEvents.MinotaurCounterChanged(_counterTotal);
        CheckStateTransition();
    }
#endif

    // ── Save / Load ───────────────────────────────────────────────────────────

    /// <summary>
    /// Write MinotaurCounter state into a SaveData snapshot.
    /// Only the penalty total is persisted — the volume tax is always recalculated
    /// fresh from the actual board state on the next departure.
    /// </summary>
    public void PopulateSaveData(SaveData data)
    {
        data.minotaurCounter = _penaltyTotal;
        // minotaurTotalConnections is no longer used but left in SaveData for
        // backwards compatibility — it will always be written as 0.
        data.minotaurTotalConnections = 0;
    }

    /// <summary>Restore MinotaurCounter state from a SaveData snapshot.</summary>
    public void ApplySaveData(SaveData data)
    {
        _penaltyTotal = Mathf.Max(0, data.minotaurCounter);
        _counterTotal = _penaltyTotal;
        GameEvents.MinotaurCounterChanged(_counterTotal);
        CheckStateTransition();
        Debug.Log($"[MinotaurCounter] Loaded — penalties={_penaltyTotal}, state={_minotaurState}");
    }

    // ── Public dev-tool API (callable from EditorWindow at runtime) ───────────

    /// <summary>Set the penalty counter to an explicit value and re-evaluate state. Dev / test only.</summary>
    public void DevSetCounter(int value)
    {
        _penaltyTotal = Mathf.Max(0, value);
        _counterTotal = _penaltyTotal;
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

    /// <summary>Manually trigger departure finalization. Dev / test only.</summary>
    public void DevFinalizeDeparture() => FinalizeForDeparture();
}
