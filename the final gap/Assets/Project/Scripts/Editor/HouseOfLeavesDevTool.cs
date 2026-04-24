#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// House of Leaves — Dev Tool
/// Open via: Tools → House of Leaves → Dev Tool
///
/// ADDING A NEW SECTION:
///   1. Add a private bool _showMySection = true; field.
///   2. Add a private void DrawMySection() { ... } method.
///   3. Call DrawMySection() inside OnGUI() below the other section calls.
///   That's it — no other changes needed.
/// </summary>
public class HouseOfLeavesDevTool : EditorWindow
{
    // ── Section fold-out states ───────────────────────────────────────────────
    bool _showStatus = true;
    bool _showTagEval = true;
    bool _showMinotaur = true;
    bool _showBindings = true;
    bool _showPropSwaps = true;
    // TODO: bool _showRoomGeometry  = true;
    // TODO: bool _showEndingChecker = true;
    // TODO: bool _showTapeSequencer = true;

    // ── Tag evaluator state ───────────────────────────────────────────────────
    CardTag _tagA = CardTag.Environment;
    CardTag _tagB = CardTag.House;
    string _tagResult = "";

    // ── Minotaur state ────────────────────────────────────────────────────────
    int _setCounterValue = 0;

    // ── Scroll ────────────────────────────────────────────────────────────────
    Vector2 _scroll;

    // ── Styles ────────────────────────────────────────────────────────────────
    GUIStyle _headerStyle;
    GUIStyle _okStyle;
    GUIStyle _warnStyle;
    bool _stylesInitialised;

    // ── Menu item ─────────────────────────────────────────────────────────────
    [MenuItem("Tools/House of Leaves/Dev Tool")]
    static void Open() => GetWindow<HouseOfLeavesDevTool>("HoL Dev Tool");

    // ── Unity callbacks ───────────────────────────────────────────────────────

    void OnGUI()
    {
        EnsureStyles();
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("House of Leaves — Dev Tool", _headerStyle);
        EditorGUILayout.Space(4);

        DrawStatus();
        DrawTagEvaluator();
        DrawMinotaurCounter();
        DrawCharacterBindings();
        DrawPropSwaps();

        // TODO: DrawRoomGeometry();
        // TODO: DrawEndingChecker();
        // TODO: DrawTapeSequencer();

        EditorGUILayout.Space(8);
        EditorGUILayout.EndScrollView();

        // Auto-repaint while playing so live values stay fresh
        if (Application.isPlaying) Repaint();
    }

    // ── Section: System Status ────────────────────────────────────────────────

    void DrawStatus()
    {
        _showStatus = EditorGUILayout.Foldout(_showStatus, "System Status", true);
        if (!_showStatus) return;

        EditorGUI.indentLevel++;
        StatusRow("GameManager", GameManager.Instance != null);
        StatusRow("MinotaurCounter", MinotaurCounter.Instance != null);
        StatusRow("CharacterBindingHandler", CharacterBindingHandler.Instance != null);
        StatusRow("PropSwapHandler", PropSwapHandler.Instance != null);
        StatusRow("YarnSystem", YarnSystem.Instance != null);
        StatusRow("BoardDragSystem", BoardDragSystem.Instance != null);
        StatusRow("PickupSystem", PickupSystem.Instance != null);
        int roomCount = RoomConfig.All?.Count ?? 0;
        EditorGUILayout.LabelField("RoomConfigs registered", roomCount.ToString(),
            roomCount > 0 ? _okStyle : _warnStyle);
        EditorGUI.indentLevel--;
        EditorGUILayout.Space(4);
    }

    void StatusRow(string label, bool ok)
    {
        EditorGUILayout.LabelField(label, ok ? "✓  Online" : "✗  Missing",
            ok ? _okStyle : _warnStyle);
    }

    // ── Section: Tag Evaluator ────────────────────────────────────────────────

    void DrawTagEvaluator()
    {
        _showTagEval = EditorGUILayout.Foldout(_showTagEval, "Tag Evaluator", true);
        if (!_showTagEval) return;

        EditorGUI.indentLevel++;
        _tagA = (CardTag)EditorGUILayout.EnumPopup("Card Tag A", _tagA);
        _tagB = (CardTag)EditorGUILayout.EnumPopup("Card Tag B", _tagB);

        if (GUILayout.Button("Evaluate (no side-effects)"))
        {
            TagConnectionResult r = TagConnectionResolver.Evaluate(_tagA, _tagB);
            _tagResult = r.Polarity == ConnectionPolarity.Positive
                ? $"✓ Positive — {r.PositiveEffect}  (Δ counter: {r.CounterDelta})"
                : $"✗ Negative — {r.NegativeEffect}  (Δ counter: +{r.CounterDelta})";
        }

        if (!string.IsNullOrEmpty(_tagResult))
            EditorGUILayout.HelpBox(_tagResult, MessageType.Info);

        EditorGUI.indentLevel--;
        EditorGUILayout.Space(4);
    }

    // ── Section: Minotaur Counter ─────────────────────────────────────────────

    void DrawMinotaurCounter()
    {
        _showMinotaur = EditorGUILayout.Foldout(_showMinotaur, "Minotaur Counter", true);
        if (!_showMinotaur) return;

        EditorGUI.indentLevel++;

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to use counter controls.", MessageType.Warning);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);
            return;
        }

        var mc = MinotaurCounter.Instance;
        if (mc == null)
        {
            EditorGUILayout.HelpBox("MinotaurCounter not found in scene.", MessageType.Error);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);
            return;
        }

        EditorGUILayout.LabelField("Current Counter", mc.CurrentTotal.ToString());
        EditorGUILayout.LabelField("Current State", mc.CurrentState.ToString());

        EditorGUILayout.Space(2);
        _setCounterValue = EditorGUILayout.IntSlider("Set Counter To", _setCounterValue, 0, 50);
        if (GUILayout.Button("Apply Counter Value"))
            mc.DevSetCounter(_setCounterValue);

        EditorGUILayout.Space(2);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Force State 1")) mc.DevForceState(1);
        if (GUILayout.Button("Force State 2")) mc.DevForceState(2);
        if (GUILayout.Button("Force State 3")) mc.DevForceState(3);
        if (GUILayout.Button("Reset")) mc.DevSetCounter(0);
        EditorGUILayout.EndHorizontal();

        EditorGUI.indentLevel--;
        EditorGUILayout.Space(4);
    }

    // ── Section: Character Bindings ───────────────────────────────────────────

    void DrawCharacterBindings()
    {
        _showBindings = EditorGUILayout.Foldout(_showBindings, "Character Bindings", true);
        if (!_showBindings) return;

        EditorGUI.indentLevel++;

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to inspect live bindings.", MessageType.Warning);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);
            return;
        }

        var cbh = CharacterBindingHandler.Instance;
        if (cbh == null)
        {
            EditorGUILayout.HelpBox("CharacterBindingHandler not found in scene.", MessageType.Error);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);
            return;
        }

        var bindings = cbh.AllBindings;
        if (bindings.Count == 0)
        {
            EditorGUILayout.LabelField("No bindings registered yet.");
        }
        else
        {
            foreach (var kvp in bindings)
            {
                string charName = kvp.Key != null ? kvp.Key.cardTitle : "null";
                string roomName = kvp.Value != null ? kvp.Value.roomName : "none";
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(charName, GUILayout.Width(130));
                EditorGUILayout.LabelField("→", GUILayout.Width(16));
                EditorGUILayout.LabelField(roomName);
                EditorGUILayout.EndHorizontal();
            }
        }

        EditorGUI.indentLevel--;
        EditorGUILayout.Space(4);
    }

    // ── Section: Prop Swaps ───────────────────────────────────────────────────

    void DrawPropSwaps()
    {
        _showPropSwaps = EditorGUILayout.Foldout(_showPropSwaps, "Prop Swaps", true);
        if (!_showPropSwaps) return;

        EditorGUI.indentLevel++;

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to trigger prop swaps.", MessageType.Warning);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);
            return;
        }

        var psh = PropSwapHandler.Instance;
        if (psh == null)
        {
            EditorGUILayout.HelpBox("PropSwapHandler not found in scene.", MessageType.Error);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space(4);
            return;
        }

        // Global controls
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Swap All Rooms")) psh.SwapAll();
        if (GUILayout.Button("Distort All Rooms"))
        {
            foreach (var room in RoomConfig.All)
                psh.DistortRandom(room);
        }
        if (GUILayout.Button("Reset All Rooms")) psh.ResetAll();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

        // Per-room controls
        var rooms = RoomConfig.All;
        if (rooms == null || rooms.Count == 0)
        {
            EditorGUILayout.LabelField("No RoomConfigs registered.");
        }
        else
        {
            foreach (var room in rooms)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(room.roomName, GUILayout.Width(130));
                if (GUILayout.Button("Swap", GUILayout.Width(50))) psh.SwapRandom(room);
                if (GUILayout.Button("Distort", GUILayout.Width(55))) psh.DistortRandom(room);
                if (GUILayout.Button("Reset", GUILayout.Width(50)))
                    foreach (var slot in room.propSlots) slot.ResetToDefault();
                EditorGUILayout.EndHorizontal();
            }
        }

        EditorGUI.indentLevel--;
        EditorGUILayout.Space(4);
    }

    // ── Style initialisation ──────────────────────────────────────────────────

    void EnsureStyles()
    {
        if (_stylesInitialised) return;

        _headerStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 13,
            alignment = TextAnchor.MiddleLeft
        };

        _okStyle = new GUIStyle(EditorStyles.label);
        _okStyle.normal.textColor = new Color(0.25f, 0.75f, 0.35f);

        _warnStyle = new GUIStyle(EditorStyles.label);
        _warnStyle.normal.textColor = new Color(0.85f, 0.35f, 0.3f);

        _stylesInitialised = true;
    }
}
#endif
