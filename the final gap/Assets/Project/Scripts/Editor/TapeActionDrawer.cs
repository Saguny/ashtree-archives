using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom property drawer for TapeDirector.TapeAction.
/// Shows only the fields relevant to the selected ActionType —
/// no more clutter from unrelated sections.
/// </summary>
[CustomPropertyDrawer(typeof(TapeDirector.TapeAction))]
public class TapeActionDrawer : PropertyDrawer
{
    const float LINE = 20f;   // singleLineHeight (18) + standardVerticalSpacing (2)
    const float PAD  = 6f;    // padding inside the box

    // ── Which fields to show per action type ──────────────────────────────

    static string[] FieldsFor(TapeDirector.ActionType type) => type switch
    {
        TapeDirector.ActionType.PlayNarration         => new[] { "narrationClip", "subtitle" },
        TapeDirector.ActionType.ShowSubtitle          => new[] { "subtitle" },
        TapeDirector.ActionType.PlaySound             => new[] { "soundClip", "soundSource" },

        // Dialogue — self-contained with auto movement lock + camera look-at
        TapeDirector.ActionType.PlayDialogue          => new[] { "dialogueSource", "dialogueClip", "subtitle", "dialogueCameraTarget" },
        // StopDialogue and EndDialogue have no extra fields

        TapeDirector.ActionType.Wait                  => new[] { "duration" },

        TapeDirector.ActionType.LockCameraOn          => new[] { "cameraTarget" },
        TapeDirector.ActionType.LookAtTarget          => new[] { "cameraTarget" },

        TapeDirector.ActionType.SetAnimatorTrigger    => new[] { "animator", "animatorParam" },
        TapeDirector.ActionType.SetAnimatorBool       => new[] { "animator", "animatorParam", "animatorBoolValue" },
        TapeDirector.ActionType.SetAnimatorFloat      => new[] { "animator", "animatorParam", "animatorFloatValue" },
        TapeDirector.ActionType.SetAnimatorInt        => new[] { "animator", "animatorParam", "animatorIntValue" },
        TapeDirector.ActionType.WaitForAnimationState => new[] { "animator", "animatorParam", "animatorLayer", "duration" },

        TapeDirector.ActionType.WaitForPlayerAt       => new[] { "advanceTrigger" },
        TapeDirector.ActionType.WaitForPlayerInteract => new[] { "interactable" },

        TapeDirector.ActionType.UnlockZone            => new[] { "zone" },
        TapeDirector.ActionType.LockZone              => new[] { "zone" },

        TapeDirector.ActionType.SetGameObjectActive   => new[] { "targetObject", "activeState" },

        TapeDirector.ActionType.RecordingCut          => new[] { "cutColor", "duration", "cutFadeSpeed" },
        TapeDirector.ActionType.TeleportPlayer        => new[] { "teleportTarget", "teleportWithCut", "duration", "cutColor", "cutFadeSpeed", "teleportZonesToActivate", "teleportZonesToDeactivate" },

        TapeDirector.ActionType.FireUnityEvent        => new[] { "unityEvent" },

        // No extra fields: HideSubtitle, StopNarration, StopDialogue, EndDialogue,
        //                  LockMovement, UnlockMovement, LockLook, UnlockLook,
        //                  UnlockCamera, EndTape
        _ => System.Array.Empty<string>()
    };

    // ── Friendlier labels for fields that appear in multiple contexts ──────

    static string LabelFor(string field, TapeDirector.ActionType type) => (field, type) switch
    {
        ("animatorParam", TapeDirector.ActionType.SetAnimatorTrigger)    => "Trigger Name",
        ("animatorParam", TapeDirector.ActionType.WaitForAnimationState) => "State Name",
        ("animatorParam", _)                                             => "Param Name",
        ("duration",      TapeDirector.ActionType.WaitForAnimationState) => "Timeout (0 = forever)",
        ("duration",      TapeDirector.ActionType.RecordingCut)         => "Hold Duration (s)",
        ("duration",      TapeDirector.ActionType.TeleportPlayer)       => "Hold Before Fade In (s)",
        ("duration",      _)                                             => "Duration (seconds)",
        ("animatorBoolValue",    _) => "Value",
        ("animatorFloatValue",   _) => "Value",
        ("animatorIntValue",     _) => "Value",
        ("dialogueSource",       _) => "Speaker (AudioSource)",
        ("dialogueClip",         _) => "Line (AudioClip)",
        ("dialogueCameraTarget", _) => "Camera Target (optional)",
        ("cameraTarget", TapeDirector.ActionType.LookAtTarget) => "Look At (Transform)",
        ("cameraTarget", _)         => "Camera Target",
        ("cutColor",     _)         => "Flash Colour",
        ("cutFadeSpeed", _)         => "Fade Speed (0 = 0.15s default)",
        ("teleportTarget",           _) => "Destination",
        ("teleportWithCut",          _) => "Wrap In Cut",
        ("teleportZonesToActivate",  _) => "Activate Zones",
        ("teleportZonesToDeactivate",_) => "Deactivate Zones",
        _                           => ObjectNames.NicifyVariableName(field)
    };

    // ── Height ────────────────────────────────────────────────────────────

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var typeProp = property.FindPropertyRelative("type");
        var type     = (TapeDirector.ActionType)typeProp.enumValueIndex;

        // type row + runConcurrent row
        float h = LINE * 2;

        foreach (string fieldName in FieldsFor(type))
        {
            var prop = property.FindPropertyRelative(fieldName);
            if (prop != null)
                h += EditorGUI.GetPropertyHeight(prop, true) + EditorGUIUtility.standardVerticalSpacing;
        }

        return h + PAD * 2; // top and bottom padding inside the box
    }

    // ── Drawing ───────────────────────────────────────────────────────────

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        var typeProp = property.FindPropertyRelative("type");
        var type     = (TapeDirector.ActionType)typeProp.enumValueIndex;

        // Background box
        EditorGUI.DrawRect(position, new Color(0f, 0f, 0f, 0.08f));

        float x = position.x + 4f;
        float y = position.y + PAD;
        float w = position.width - 8f;

        // ── Type (shown with a bold label) ────────────────────────────────
        EditorGUI.LabelField(
            new Rect(x, y, 80f, EditorGUIUtility.singleLineHeight),
            "Type",
            EditorStyles.boldLabel
        );
        EditorGUI.PropertyField(
            new Rect(x + 82f, y, w - 82f, EditorGUIUtility.singleLineHeight),
            typeProp,
            GUIContent.none
        );
        y += LINE;

        // ── Run Concurrent ────────────────────────────────────────────────
        var concurrentProp = property.FindPropertyRelative("runConcurrent");
        EditorGUI.PropertyField(
            new Rect(x, y, w, EditorGUIUtility.singleLineHeight),
            concurrentProp,
            new GUIContent("Run Concurrent",
                "If enabled, this action starts and the sequence immediately moves " +
                "to the next action — it doesn't wait for this one to finish.")
        );
        y += LINE;

        // ── Conditional fields ────────────────────────────────────────────
        string[] fields = FieldsFor(type);

        if (fields.Length > 0)
        {
            // Separator
            EditorGUI.DrawRect(new Rect(x, y, w, 1f), new Color(1f, 1f, 1f, 0.15f));
            y += 4f;
        }

        foreach (string fieldName in fields)
        {
            var prop = property.FindPropertyRelative(fieldName);
            if (prop == null) continue;

            float fieldH = EditorGUI.GetPropertyHeight(prop, true);
            EditorGUI.PropertyField(
                new Rect(x, y, w, fieldH),
                prop,
                new GUIContent(LabelFor(fieldName, type)),
                true
            );
            y += fieldH + EditorGUIUtility.standardVerticalSpacing;
        }

        EditorGUI.EndProperty();
    }
}
