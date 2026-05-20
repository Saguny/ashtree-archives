using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Office-scene component that re-pins cards and re-draws yarn connections
/// from a loaded SaveData when the office scene starts.
///
/// SETUP
///   Attach to any persistent GameObject in the Office scene (e.g. the
///   CorkboardManager). It reads from SaveSystem.CurrentSaveData, so no
///   Inspector wiring is needed beyond placing it in the scene.
///
/// TIMING
///   Restoration runs two frames after Start so every card's Awake/Start
///   has completed and pins have had time to spawn.
///
/// WHAT IT DOES
///   1. Builds a title → CardBehaviour lookup from all cards in the scene.
///   2. Calls card.OnPinned(position, rotation) for each saved pinned card.
///   3. After one more frame (so pins are spawned), calls
///      YarnSystem.RestoreConnection for each saved yarn connection.
///      RestoreConnection is a silent variant that skips GameEvents so the
///      Minotaur Counter is not re-incremented on load.
/// </summary>
public class BoardRestorer : MonoBehaviour
{
    IEnumerator Start()
    {
        // Nothing to restore if there is no save data yet.
        var ss = SaveSystem.Instance;
        if (ss == null || ss.CurrentSaveData == null) yield break;

        var data = ss.CurrentSaveData;
        if (data.pinnedCards.Count == 0 && data.yarnConnections.Count == 0) yield break;

        // Wait two frames for all Awake/Start calls in this scene to finish.
        yield return null;
        yield return null;

        RestoreAll(data);
    }

    void RestoreAll(SaveData data)
    {
        // ── Build title → card lookup ────────────────────────────────────────
        var cardMap = new Dictionary<string, CardBehaviour>();
        foreach (var card in FindObjectsByType<CardBehaviour>(FindObjectsSortMode.None))
        {
            if (!cardMap.ContainsKey(card.cardTitle))
                cardMap[card.cardTitle] = card;
        }

        // ── Pin cards at saved world-space positions ─────────────────────────
        int restored = 0;
        foreach (var saved in data.pinnedCards)
        {
            if (!cardMap.TryGetValue(saved.cardTitle, out var card))
            {
                Debug.LogWarning($"[BoardRestorer] Could not find card '{saved.cardTitle}' in scene — skipped.");
                continue;
            }

            var pos = new Vector3(saved.posX, saved.posY, saved.posZ);
            var rot = new Quaternion(saved.rotX, saved.rotY, saved.rotZ, saved.rotW);
            card.OnPinned(pos, rot);
            restored++;
        }

        Debug.Log($"[BoardRestorer] Pinned {restored}/{data.pinnedCards.Count} cards.");

        // Yarn needs one more frame for the pin GameObjects to fully spawn.
        if (data.yarnConnections.Count > 0)
            StartCoroutine(RestoreYarnNextFrame(data, cardMap));
    }

    IEnumerator RestoreYarnNextFrame(SaveData data, Dictionary<string, CardBehaviour> cardMap)
    {
        yield return null; // let OnPinned's pin spawning complete

        var yarn = YarnSystem.Instance;
        if (yarn == null) yield break;

        int restored = 0;
        foreach (var saved in data.yarnConnections)
        {
            if (!cardMap.TryGetValue(saved.cardTitleA, out var cardA) ||
                !cardMap.TryGetValue(saved.cardTitleB, out var cardB))
            {
                Debug.LogWarning($"[BoardRestorer] Could not restore yarn: " +
                                 $"'{saved.cardTitleA}' ↔ '{saved.cardTitleB}'");
                continue;
            }

            yarn.RestoreConnection(cardA, cardB);
            restored++;
        }

        Debug.Log($"[BoardRestorer] Restored {restored}/{data.yarnConnections.Count} yarn connections.");
    }
}
