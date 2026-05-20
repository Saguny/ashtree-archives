using System;
using System.Collections.Generic;

// ── Top-level save envelope ────────────────────────────────────────────────────

/// <summary>
/// Full serialisable snapshot of everything that needs to persist between sessions.
/// Serialised to JSON via JsonUtility → only public fields with primitive/List types.
///
/// Board positions are saved in world-space so they survive scene reloads without
/// depending on any local transform.
/// </summary>
[Serializable]
public class SaveData
{
    // Meta
    public string saveVersion  = "1.0";
    public string saveTimestamp;
    public string lastScene    = "Office";

    // ── Tape tracking (EndingManager) ─────────────────────────────────────────
    /// <summary>Scene names of every tape the player has fully watched.</summary>
    public List<string> watchedTapes = new List<string>();

    // ── Minotaur (MinotaurCounter) ────────────────────────────────────────────
    public int minotaurCounter;
    public int minotaurTotalConnections;

    // ── Character bindings (CharacterBindingHandler) ──────────────────────────
    public List<CharacterBindingSave> characterBindings = new List<CharacterBindingSave>();

    // ── Corkboard state (BoardRestorer) ───────────────────────────────────────
    /// <summary>Every card currently pinned to the board, with its world-space transform.</summary>
    public List<PinnedCardSave> pinnedCards = new List<PinnedCardSave>();

    /// <summary>Every active yarn connection, stored as a pair of card titles.</summary>
    public List<YarnConnectionSave> yarnConnections = new List<YarnConnectionSave>();

    // ── Ending tracking (EndingManager) ──────────────────────────────────────
    /// <summary>cardTitle of every card that was ever pinned this run.</summary>
    public List<string> everPinnedCardTitles = new List<string>();

    /// <summary>cardTitle of every card disposed of in the trash bin.</summary>
    public List<string> trashedCardTitles = new List<string>();

    // ── Inventory tracking (RunInventorySystem) ───────────────────────────────
    /// <summary>
    /// Prefab names (itemId) of every item the player has ever pocketed across all
    /// trips. Items in this list will not respawn in future house visits.
    /// </summary>
    public List<string> collectedItemIds = new List<string>();
}

// ── Supporting serialisable records ───────────────────────────────────────────

[Serializable]
public class CharacterBindingSave
{
    public string characterCardTitle;
    public string roomName;
}

[Serializable]
public class PinnedCardSave
{
    public string cardTitle;
    // World-space position
    public float posX, posY, posZ;
    // World-space rotation (quaternion)
    public float rotX, rotY, rotZ, rotW;
}

[Serializable]
public class YarnConnectionSave
{
    public string cardTitleA;
    public string cardTitleB;
}
