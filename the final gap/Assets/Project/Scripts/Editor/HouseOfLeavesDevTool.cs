#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// House of Leaves — Dev Tool
/// Open via: Tools → House of Leaves → Dev Tool
///
/// Each section both DISPLAYS live state AND lets you drive the system directly,
/// so you can verify correct behaviour without replaying the game.
///
/// ADDING A NEW SECTION:
///   1. Add a private bool _showMySection = true; field.
///   2. Add a private void DrawMySection() { ... } method.
///   3. Call DrawMySection() inside OnGUI() below the other section calls.
/// </summary>
public class HouseOfLeavesDevTool : EditorWindow
{
    // ── Section fold-out states ───────────────────────────────────────────────
    bool _showStatus      = true;
    bool _showSceneNav    = false;
    bool _showGameState   = true;
    bool _showTagSim      = true;
    bool _showMinotaur    = true;
    bool _showBindings    = true;
    bool _showPropSwaps   = true;
    bool _showHotbar      = true;
    bool _showTapeSeq     = true;
    bool _showEndings     = true;
    // TODO: bool _showSaveSystem   = true;
    // TODO: bool _showSpawner      = true;
    // TODO: bool _showRoomGeometry = true;

    // ── Scene Navigator ───────────────────────────────────────────────────────
    string _customScene = "";

    // Tape scene names — update if your scene names differ
    readonly string[] _tapeSceneNames =
    {
        "Tape_Measurements",
        "Tape_HalfMinuteHallway",
        "Tape_Exploration",
        "Tape_DaisyChad",
        "Tape_Expedition",
        "Tape_Uncanny",
        "Tape_Photography",
        "Tape_Minotaur",
        "Tape_Goodbye",
    };
    readonly string[] _tapeSceneLabels =
    {
        "Measurements",
        "5½ Min Hallway",
        "Exploration",
        "Daisy & Chad",
        "Expedition",
        "Uncanny",
        "Photography",
        "Minotaur",
        "Goodbye",
    };

    // ── Game State ────────────────────────────────────────────────────────────
    GameState _targetState = GameState.Exploration;

    // ── Tag Simulator ─────────────────────────────────────────────────────────
    CardTag _evalTagA = CardTag.Environment;
    CardTag _evalTagB = CardTag.House;
    string  _evalResult = "";

    // Simulate-with-effects (fires actual GameEvents chain)
    CardBehaviour[] _allCards   = new CardBehaviour[0];
    string[]        _cardLabels = new string[0];
    int  _simCardA = 0;
    int  _simCardB = 0;
    string _simResult = "";

    // ── Minotaur Counter ──────────────────────────────────────────────────────
    int _counterSetValue = 0;

    // ── Character Bindings ────────────────────────────────────────────────────
    CardBehaviour[] _charCards   = new CardBehaviour[0];
    string[]        _charLabels  = new string[0];
    RoomConfig[]    _allRooms    = new RoomConfig[0];
    string[]        _roomLabels  = new string[0];
    int _bindCharIdx = 0;
    int _bindRoomIdx = 0;

    // ── Tape Sequencer ────────────────────────────────────────────────────────
    int _jumpToActionIdx = 0;

    // ── Scroll ────────────────────────────────────────────────────────────────
    Vector2 _scroll;

    // ── Styles ────────────────────────────────────────────────────────────────
    GUIStyle _headerStyle;
    GUIStyle _sectionStyle;
    GUIStyle _okStyle;
    GUIStyle _warnStyle;
    GUIStyle _todoStyle;
    bool _stylesReady;

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
        DrawSceneNavigator();
        DrawGameState();
        DrawTagSimulator();
        DrawMinotaurCounter();
        DrawCharacterBindings();
        DrawPropSwaps();
        DrawHotbarInspector();
        DrawTapeSequencer();
        DrawEndingChecker();

        // TODO: DrawSaveSystem();
        // TODO: DrawSpawner();
        // TODO: DrawRoomGeometry();

        EditorGUILayout.Space(8);
        EditorGUILayout.EndScrollView();

        if (Application.isPlaying) Repaint();
    }

    // =========================================================================
    // Section: System Status
    // =========================================================================

    void DrawStatus()
    {
        _showStatus = Foldout(_showStatus, "System Status");
        if (!_showStatus) return;
        EditorGUI.indentLevel++;

        bool p = Application.isPlaying;

        StatusRow("GameManager",             p && GameManager.Instance != null);
        StatusRow("MinotaurCounter",         p && MinotaurCounter.Instance != null);
        StatusRow("CharacterBindingHandler", p && CharacterBindingHandler.Instance != null);
        StatusRow("PropSwapHandler",         p && PropSwapHandler.Instance != null);
        StatusRow("YarnSystem",              p && YarnSystem.Instance != null);
        StatusRow("BoardDragSystem",         p && BoardDragSystem.Instance != null);
        StatusRow("PickupSystem",            p && PickupSystem.Instance != null);
        StatusRow("HotbarSystem",            p && HotbarSystem.Instance != null);
        StatusRow("SceneSwitcher",           p && SceneSwitcher.Instance != null);

        int roomCount = RoomConfig.All?.Count ?? 0;
        EditorGUILayout.LabelField("RoomConfigs registered", roomCount.ToString(),
            roomCount > 0 ? _okStyle : _warnStyle);

        if (p)
        {
            int cardCount = Object.FindObjectsByType<CardBehaviour>(FindObjectsSortMode.None).Length;
            EditorGUILayout.LabelField("CardBehaviours in scene", cardCount.ToString(),
                cardCount > 0 ? _okStyle : _warnStyle);

            var td = Object.FindFirstObjectByType<TapeDirector>();
            EditorGUILayout.LabelField("TapeDirector",
                td != null ? $"✓  Online  (action {td.CurrentActionIndex})" : "—  Not in scene",
                td != null ? _okStyle : EditorStyles.label);
        }

        EditorGUI.indentLevel--;
        Gap();
    }

    // =========================================================================
    // Section: Scene Navigator
    // =========================================================================

    void DrawSceneNavigator()
    {
        _showSceneNav = Foldout(_showSceneNav, "Scene Navigator");
        if (!_showSceneNav) return;
        EditorGUI.indentLevel++;

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to switch scenes at runtime.", MessageType.Warning);
            EditorGUI.indentLevel--;
            Gap();
            return;
        }

        EditorGUILayout.LabelField("Core Scenes", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Office")) SceneSwitcher.LoadScene("Office");
        if (GUILayout.Button("House"))  SceneSwitcher.LoadScene("House");
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Tape Scenes", EditorStyles.boldLabel);

        // 3-per-row button grid
        for (int i = 0; i < _tapeSceneNames.Length; i++)
        {
            if (i % 3 == 0) EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button(_tapeSceneLabels[i]))
                SceneSwitcher.LoadScene(_tapeSceneNames[i]);
            if (i % 3 == 2 || i == _tapeSceneNames.Length - 1) EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Custom", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        _customScene = EditorGUILayout.TextField(_customScene);
        if (GUILayout.Button("Load", GUILayout.Width(50)) && !string.IsNullOrEmpty(_customScene))
            SceneSwitcher.LoadScene(_customScene);
        EditorGUILayout.EndHorizontal();

        EditorGUI.indentLevel--;
        Gap();
    }

    // =========================================================================
    // Section: Game State
    // =========================================================================

    void DrawGameState()
    {
        _showGameState = Foldout(_showGameState, "Game State");
        if (!_showGameState) return;
        EditorGUI.indentLevel++;

        if (!Application.isPlaying || GameManager.Instance == null)
        {
            EditorGUILayout.HelpBox(
                Application.isPlaying ? "GameManager not found." : "Enter Play Mode.",
                Application.isPlaying ? MessageType.Error : MessageType.Warning);
            EditorGUI.indentLevel--;
            Gap();
            return;
        }

        EditorGUILayout.LabelField("Current State", GameManager.Instance.CurrentState.ToString(), _okStyle);
        EditorGUILayout.Space(2);
        _targetState = (GameState)EditorGUILayout.EnumPopup("Force To", _targetState);
        if (GUILayout.Button("Apply State"))
            GameManager.Instance.SetState(_targetState);

        EditorGUI.indentLevel--;
        Gap();
    }

    // =========================================================================
    // Section: Tag Connection Simulator
    // =========================================================================

    void DrawTagSimulator()
    {
        _showTagSim = Foldout(_showTagSim, "Tag Connection Simulator");
        if (!_showTagSim) return;
        EditorGUI.indentLevel++;

        // ── Evaluate only (pure logic check, zero side-effects) ──────────────
        EditorGUILayout.LabelField("Evaluate — no side-effects", EditorStyles.boldLabel);
        _evalTagA = (CardTag)EditorGUILayout.EnumPopup("Tag A", _evalTagA);
        _evalTagB = (CardTag)EditorGUILayout.EnumPopup("Tag B", _evalTagB);

        if (GUILayout.Button("Evaluate"))
        {
            TagConnectionResult r = TagConnectionResolver.Evaluate(_evalTagA, _evalTagB);
            _evalResult = r.Polarity == ConnectionPolarity.Positive
                ? $"✓ Positive — {r.PositiveEffect}  (Δ counter: {r.CounterDelta})"
                : $"✗ Negative — {r.NegativeEffect}  (Δ counter: +{r.CounterDelta})";
        }
        if (!string.IsNullOrEmpty(_evalResult))
            EditorGUILayout.HelpBox(_evalResult, MessageType.Info);

        EditorGUILayout.Space(6);

        // ── Simulate with full chain (fires MinotaurCounter, PropSwap, Bindings) ─
        EditorGUILayout.LabelField("Simulate — fires full GameEvents chain", EditorStyles.boldLabel);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to simulate with real effects.", MessageType.Warning);
        }
        else
        {
            if (GUILayout.Button("↺  Refresh Card List"))
                RefreshCardList();

            if (_allCards.Length < 2)
            {
                EditorGUILayout.HelpBox(
                    _allCards.Length == 0
                        ? "No CardBehaviours found — hit Refresh."
                        : "Need at least 2 cards in scene to simulate a connection.",
                    MessageType.Warning);
            }
            else
            {
                _simCardA = EditorGUILayout.Popup("Card A", ClampIdx(_simCardA, _cardLabels), _cardLabels);
                _simCardB = EditorGUILayout.Popup("Card B", ClampIdx(_simCardB, _cardLabels), _cardLabels);

                bool sameCard = _simCardA == _simCardB;
                if (sameCard)
                    EditorGUILayout.HelpBox("A and B must be different cards.", MessageType.Warning);

                EditorGUI.BeginDisabledGroup(sameCard);
                if (GUILayout.Button("Simulate Connection  →  fires all subscribers"))
                {
                    var a = _allCards[_simCardA];
                    var b = _allCards[_simCardB];
                    GameEvents.YarnConnected(a, b);   // goes through MinotaurCounter → PropSwap → Bindings

                    TagConnectionResult r = TagConnectionResolver.Evaluate(a.CardTag, b.CardTag);
                    string pol = r.Polarity == ConnectionPolarity.Positive ? "✓ Positive" : "✗ Negative";
                    string eff = r.Polarity == ConnectionPolarity.Positive
                        ? r.PositiveEffect.ToString()
                        : $"{r.NegativeEffect}  (+{r.CounterDelta} to counter)";
                    _simResult = $"{a.cardTitle} ({a.CardTag})  +  {b.cardTitle} ({b.CardTag})\n{pol} — {eff}";
                }
                EditorGUI.EndDisabledGroup();

                if (!string.IsNullOrEmpty(_simResult))
                    EditorGUILayout.HelpBox(_simResult, MessageType.Info);
            }
        }

        EditorGUI.indentLevel--;
        Gap();
    }

    // =========================================================================
    // Section: Minotaur Counter
    // =========================================================================

    void DrawMinotaurCounter()
    {
        _showMinotaur = Foldout(_showMinotaur, "Minotaur Counter");
        if (!_showMinotaur) return;
        EditorGUI.indentLevel++;

        if (!Application.isPlaying || MinotaurCounter.Instance == null)
        {
            EditorGUILayout.HelpBox(
                Application.isPlaying ? "MinotaurCounter not found." : "Enter Play Mode.",
                Application.isPlaying ? MessageType.Error : MessageType.Warning);
            EditorGUI.indentLevel--;
            Gap();
            return;
        }

        var mc = MinotaurCounter.Instance;

        EditorGUILayout.LabelField("Counter",     mc.CurrentTotal.ToString(),     _okStyle);
        EditorGUILayout.LabelField("Connections", mc.TotalConnections.ToString());

        string stateLabel = mc.CurrentState switch
        {
            0 => "0  — Dormant",
            1 => "1  — Subtle anomalies; grey hue + vignette",
            2 => "2  — Strong anomalies; tinnitus + ambient audio",
            3 => "3  — ⚠ MINOTAUR ending unlocked",
            _ => mc.CurrentState.ToString()
        };
        MessageType stateMsg = mc.CurrentState >= 2 ? MessageType.Warning : MessageType.Info;
        EditorGUILayout.HelpBox($"State  {stateLabel}", stateMsg);

        EditorGUILayout.Space(2);
        _counterSetValue = EditorGUILayout.IntSlider("Set Counter To", _counterSetValue, 0, 50);
        if (GUILayout.Button("Apply")) mc.DevSetCounter(_counterSetValue);

        EditorGUILayout.Space(2);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("→ State 1")) mc.DevForceState(1);
        if (GUILayout.Button("→ State 2")) mc.DevForceState(2);
        if (GUILayout.Button("→ State 3")) mc.DevForceState(3);
        if (GUILayout.Button("Reset"))     mc.DevSetCounter(0);
        EditorGUILayout.EndHorizontal();

        EditorGUI.indentLevel--;
        Gap();
    }

    // =========================================================================
    // Section: Character Bindings
    // =========================================================================

    void DrawCharacterBindings()
    {
        _showBindings = Foldout(_showBindings, "Character Bindings");
        if (!_showBindings) return;
        EditorGUI.indentLevel++;

        if (!Application.isPlaying || CharacterBindingHandler.Instance == null)
        {
            EditorGUILayout.HelpBox(
                Application.isPlaying ? "CharacterBindingHandler not found." : "Enter Play Mode.",
                Application.isPlaying ? MessageType.Error : MessageType.Warning);
            EditorGUI.indentLevel--;
            Gap();
            return;
        }

        var cbh = CharacterBindingHandler.Instance;

        // Live binding display
        EditorGUILayout.LabelField("Live Bindings", EditorStyles.boldLabel);
        var bindings = cbh.AllBindings;
        if (bindings.Count == 0)
        {
            EditorGUILayout.LabelField("No bindings registered yet.");
        }
        else
        {
            foreach (var kvp in bindings)
            {
                string c = kvp.Key  != null ? kvp.Key.cardTitle  : "null";
                string r = kvp.Value != null ? kvp.Value.roomName : "none";
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(c, GUILayout.Width(140));
                EditorGUILayout.LabelField("→", GUILayout.Width(16));
                EditorGUILayout.LabelField(r, _okStyle);
                EditorGUILayout.EndHorizontal();
            }
        }

        EditorGUILayout.Space(6);

        // Force rebind
        EditorGUILayout.LabelField("Force Rebind", EditorStyles.boldLabel);
        if (GUILayout.Button("↺  Refresh Lists"))
            RefreshBindingLists();

        if (_charCards.Length == 0)
        {
            EditorGUILayout.HelpBox("No Character-tagged cards found. Hit Refresh.", MessageType.Warning);
        }
        else if (_allRooms.Length == 0)
        {
            EditorGUILayout.HelpBox("No RoomConfigs found. Make sure rooms are in the scene.", MessageType.Warning);
        }
        else
        {
            _bindCharIdx = EditorGUILayout.Popup("Character", ClampIdx(_bindCharIdx, _charLabels), _charLabels);
            _bindRoomIdx = EditorGUILayout.Popup("Room",      ClampIdx(_bindRoomIdx, _roomLabels), _roomLabels);

            if (GUILayout.Button("Apply Rebind"))
                cbh.SetRoom(_charCards[_bindCharIdx], _allRooms[_bindRoomIdx]);

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField("Swap Two Characters", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Pick two Character slots above (Char A = Character dropdown, Char B = Room dropdown treated as second char index).\n" +
                "Or use the button below to swap the two characters selected above if both map to Character cards.",
                MessageType.None);

            if (GUILayout.Button("Swap Selected Characters' Rooms"))
            {
                int bIdx = ClampIdx(_bindRoomIdx, _charLabels); // use room slot as second char picker
                if (bIdx < _charCards.Length && _bindCharIdx != bIdx)
                {
                    var a = _charCards[_bindCharIdx];
                    var b = _charCards[bIdx];
                    var roomA = cbh.GetRoom(a);
                    var roomB = cbh.GetRoom(b);
                    cbh.SetRoom(a, roomB);
                    cbh.SetRoom(b, roomA);
                }
            }
        }

        EditorGUI.indentLevel--;
        Gap();
    }

    // =========================================================================
    // Section: Prop Swaps
    // =========================================================================

    void DrawPropSwaps()
    {
        _showPropSwaps = Foldout(_showPropSwaps, "Prop Swaps");
        if (!_showPropSwaps) return;
        EditorGUI.indentLevel++;

        if (!Application.isPlaying || PropSwapHandler.Instance == null)
        {
            EditorGUILayout.HelpBox(
                Application.isPlaying ? "PropSwapHandler not found." : "Enter Play Mode.",
                Application.isPlaying ? MessageType.Error : MessageType.Warning);
            EditorGUI.indentLevel--;
            Gap();
            return;
        }

        var psh = PropSwapHandler.Instance;

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Swap All"))    psh.SwapAll();
        if (GUILayout.Button("Distort All")) { foreach (var r in RoomConfig.All) psh.DistortRandom(r); }
        if (GUILayout.Button("Reset All"))   psh.ResetAll();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);

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
                if (GUILayout.Button("Swap",    GUILayout.Width(50))) psh.SwapRandom(room);
                if (GUILayout.Button("Distort", GUILayout.Width(55))) psh.DistortRandom(room);
                if (GUILayout.Button("Reset",   GUILayout.Width(50)))
                    foreach (var s in room.propSlots) s.ResetToDefault();
                EditorGUILayout.EndHorizontal();
            }
        }

        EditorGUI.indentLevel--;
        Gap();
    }

    // =========================================================================
    // Section: Hotbar Inspector
    // =========================================================================

    void DrawHotbarInspector()
    {
        _showHotbar = Foldout(_showHotbar, "Hotbar Inspector");
        if (!_showHotbar) return;
        EditorGUI.indentLevel++;

        if (!Application.isPlaying || HotbarSystem.Instance == null)
        {
            EditorGUILayout.HelpBox(
                Application.isPlaying ? "HotbarSystem not found." : "Enter Play Mode.",
                Application.isPlaying ? MessageType.Error : MessageType.Warning);
            EditorGUI.indentLevel--;
            Gap();
            return;
        }

        var hs = HotbarSystem.Instance;

        // Tape slot
        string tapeName = hs.HasTape ? hs.StoredTape.name : "— empty —";
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("[TAPE]", GUILayout.Width(65));
        EditorGUILayout.LabelField(tapeName, hs.HasTape ? _okStyle : EditorStyles.label);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(2);

        // Card slots
        for (int i = 0; i < hs.MaxSlots; i++)
        {
            var card    = hs.GetSlot(i);
            bool sel    = i == hs.SelectedIndex;
            string lbl  = $"[Slot {i}]{(sel ? " ◄" : "")}";
            string info = card != null ? $"{card.cardTitle}  ({card.CardTag})" : "— empty —";

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(lbl, sel ? _okStyle : EditorStyles.label, GUILayout.Width(75));
            EditorGUILayout.LabelField(info, card != null ? _okStyle : EditorStyles.label);
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space(4);
        if (GUILayout.Button("Clear All Slots (dev)"))
            hs.DevClearAllSlots();

        EditorGUI.indentLevel--;
        Gap();
    }

    // =========================================================================
    // Section: Tape Sequencer
    // =========================================================================

    void DrawTapeSequencer()
    {
        _showTapeSeq = Foldout(_showTapeSeq, "Tape Sequencer");
        if (!_showTapeSeq) return;
        EditorGUI.indentLevel++;

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to control tape sequences.", MessageType.Warning);
            EditorGUI.indentLevel--;
            Gap();
            return;
        }

        var td = Object.FindFirstObjectByType<TapeDirector>();
        if (td == null)
        {
            EditorGUILayout.HelpBox("No TapeDirector in the current scene. Load a tape scene first.", MessageType.Info);
            EditorGUI.indentLevel--;
            Gap();
            return;
        }

        EditorGUILayout.LabelField("Current Action Index", td.CurrentActionIndex.ToString(), _okStyle);

        EditorGUILayout.Space(2);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Skip Current Action")) td.DevSkipCurrentAction();
        if (GUILayout.Button("Force End Tape"))      td.DevForceEnd();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(2);
        _jumpToActionIdx = EditorGUILayout.IntField("Jump To Action", _jumpToActionIdx);
        if (GUILayout.Button("Jump"))
            td.DevJumpToAction(_jumpToActionIdx);

        EditorGUILayout.HelpBox(
            "Jump immediately restarts the sequence from the given action index.\n" +
            "Player locks, camera, and audio are reset before jumping so you don't get stuck.",
            MessageType.None);

        EditorGUI.indentLevel--;
        Gap();
    }

    // =========================================================================
    // Section: Ending Prerequisite Checker
    // =========================================================================

    void DrawEndingChecker()
    {
        _showEndings = Foldout(_showEndings, "Ending Prerequisites");
        if (!_showEndings) return;
        EditorGUI.indentLevel++;

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to check live prerequisites.", MessageType.Warning);
            EditorGUI.indentLevel--;
            Gap();
            return;
        }

        var mc  = MinotaurCounter.Instance;
        var cbh = CharacterBindingHandler.Instance;

        // ── THIS IS NOT FOR YOU ──────────────────────────────────────────────
        EditorGUILayout.LabelField("THIS IS NOT FOR YOU", EditorStyles.boldLabel);
        bool stateInRange = mc != null && mc.CurrentState >= 1 && mc.CurrentState <= 2;
        CheckRow("Minotaur State 1 or 2 reached",     stateInRange);
        CheckRow("All 9 tapes collected",              false, todo: true);

        EditorGUILayout.Space(4);

        // ── MINOTAUR ─────────────────────────────────────────────────────────
        EditorGUILayout.LabelField("MINOTAUR", EditorStyles.boldLabel);
        bool state3 = mc != null && mc.CurrentState >= 3;
        CheckRow("Minotaur State 3 reached (counter ≥ threshold)", state3);

        EditorGUILayout.Space(4);

        // ── THE INFINITE DESCENT — Path A ────────────────────────────────────
        EditorGUILayout.LabelField("THE INFINITE DESCENT — Path A", EditorStyles.boldLabel);

        bool willInLivingRoom = false;
        if (cbh != null)
        {
            foreach (var kvp in cbh.AllBindings)
            {
                if (kvp.Key  != null && kvp.Key.cardTitle   == "Will Navidson"
                &&  kvp.Value != null && kvp.Value.roomName  == "Living Room")
                {
                    willInLivingRoom = true;
                    break;
                }
            }
        }

        CheckRow("Tape MINOTAUR collected (Indeterminacy clue)",   false, todo: true);
        CheckRow("Tape EXPEDITION collected (Missing clue)",        false, todo: true);
        CheckRow("Will Navidson bound to Living Room",              willInLivingRoom);
        CheckRow("Will card placed in center of corkboard",         false, todo: true);
        CheckRow("Indeterminacy + Missing connected to Will",       false, todo: true);

        EditorGUILayout.Space(4);

        // ── THE INFINITE DESCENT — Path B ────────────────────────────────────
        EditorGUILayout.LabelField("THE INFINITE DESCENT — Path B", EditorStyles.boldLabel);
        CheckRow("Tape GOODBYE collected (Concession clue)",        false, todo: true);
        CheckRow("Tape MINOTAUR collected (Indeterminacy clue)",    false, todo: true);
        CheckRow("Karen + Kids + Will all connected on board",      false, todo: true);
        CheckRow("Karen + Kids connected to Entrance Door clue",    false, todo: true);
        CheckRow("Concession + Indeterminacy connected to Will",    false, todo: true);
        CheckRow("Will card placed in center of corkboard",         false, todo: true);
        CheckRow("Concession NOT connected to Living Room",         false, todo: true);
        CheckRow("Entrance Door clue in game  (COMING SOON)",       false, todo: true);

        EditorGUILayout.HelpBox(
            "TODO rows need a BoardManager/SaveManager to check board state and tape collection.\n" +
            "They'll fill in automatically once those systems exist.",
            MessageType.None);

        EditorGUI.indentLevel--;
        Gap();
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    void RefreshCardList()
    {
        _allCards   = Object.FindObjectsByType<CardBehaviour>(FindObjectsSortMode.None);
        _cardLabels = _allCards.Select(c => $"{c.cardTitle} ({c.CardTag})").ToArray();
    }

    void RefreshBindingLists()
    {
        var all = Object.FindObjectsByType<CardBehaviour>(FindObjectsSortMode.None);

        _charCards  = all.Where(c => c.CardTag == CardTag.Character).ToArray();
        _charLabels = _charCards.Select(c => c.cardTitle).ToArray();

        _allRooms   = RoomConfig.All?.ToArray() ?? new RoomConfig[0];
        _roomLabels = _allRooms.Select(r => r.roomName).ToArray();
    }

    bool Foldout(bool state, string label)
        => EditorGUILayout.Foldout(state, label, true, _sectionStyle);

    void Gap() => EditorGUILayout.Space(6);

    static int ClampIdx(int i, string[] arr)
        => arr.Length > 0 ? Mathf.Clamp(i, 0, arr.Length - 1) : 0;

    void StatusRow(string label, bool ok)
        => EditorGUILayout.LabelField(label, ok ? "✓  Online" : "✗  Missing",
            ok ? _okStyle : _warnStyle);

    void CheckRow(string label, bool met, bool todo = false)
    {
        string tag   = todo ? "TODO" : (met ? "✓" : "✗");
        GUIStyle sty = todo ? _todoStyle : (met ? _okStyle : _warnStyle);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(label);
        EditorGUILayout.LabelField(tag, sty, GUILayout.Width(45));
        EditorGUILayout.EndHorizontal();
    }

    void EnsureStyles()
    {
        if (_stylesReady) return;

        _headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };

        _sectionStyle = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };

        _okStyle = new GUIStyle(EditorStyles.label);
        _okStyle.normal.textColor = new Color(0.25f, 0.75f, 0.35f);

        _warnStyle = new GUIStyle(EditorStyles.label);
        _warnStyle.normal.textColor = new Color(0.85f, 0.35f, 0.3f);

        _todoStyle = new GUIStyle(EditorStyles.label);
        _todoStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f);

        _stylesReady = true;
    }
}
#endif
