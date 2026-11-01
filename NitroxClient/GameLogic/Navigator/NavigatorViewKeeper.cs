namespace NitroxClient.GameLogic.Navigator;

/// <summary>
/// The view of the map of one Cyclops, kept where the navigator left it (docs/coop/VEHICLE_UPGRADES.md). Sitting down at the console and getting up again do nothing to the view: the map stands at the angle,
/// the roll and the zoom it was turned to for the next one who walks up, and the view is kept on this machine for the Cyclops (by its id), so that it is there after a restart too. It is this player's own
/// view and is not sent to the others, who have the same console and the same map on their machines, turned as they turned it. Only the navigator puts it back (the R key, the Reset view button).
/// <para>
/// Pure: the console gives it the time and the saved text and does the reading and the writing (<see cref="NitroxClient.GameLogic.Settings.PrefsStore" />), so that the rules are tested.
/// </para>
/// </summary>
public sealed class NavigatorViewKeeper
{
    /// <summary>The view is written a moment after the last change, not at every frame of a drag.</summary>
    public const float SAVE_AFTER_SECONDS = 1.5f;

    /// <summary>The name of the preference that holds the view of a Cyclops starts with this and ends with the Cyclops' id.</summary>
    public const string PREF_PREFIX = "Nitrox.NavigatorMapView.";

    private int savedRevision;
    private int seenRevision;
    private float saveAt = -1f;

    public NavigatorMapView View { get; } = new();

    /// <summary>Whether the saved view has been looked at (it is, once, when the Cyclops' id is known).</summary>
    public bool Loaded { get; private set; }

    /// <summary>The name of the preference that holds the view of the Cyclops with this id.</summary>
    public static string PrefKey(string cyclopsId)
    {
        return $"{PREF_PREFIX}{cyclopsId}";
    }

    /// <summary>
    /// Takes the view that was saved for this Cyclops (the text of <see cref="NavigatorMapView.ToText" />, empty when there is none), once: later calls change nothing, and neither does a view the navigator has already
    /// touched. True when the view was restored. A text that cannot be read leaves the map as it first stands. What was loaded does not need saving again.
    /// </summary>
    public bool Load(string saved)
    {
        if (Loaded)
        {
            return false;
        }
        Loaded = true;
        if (View.Revision != 0)
        {
            // (the navigator got to the view first: theirs stands, and is written with the next change or when the console is left)
            return false;
        }
        bool restored = View.TryLoad(saved);
        savedRevision = View.Revision;
        seenRevision = View.Revision;
        return restored;
    }

    /// <summary>
    /// Called every frame with the time: the text to write when the view has stood still for <see cref="SAVE_AFTER_SECONDS" /> since its last change, otherwise null (every change starts the wait again, so a drag
    /// is written once, after it).
    /// </summary>
    public string TakeDue(float now)
    {
        if (View.Revision != seenRevision)
        {
            seenRevision = View.Revision;
            saveAt = now + SAVE_AFTER_SECONDS;
        }
        if (saveAt < 0f || now < saveAt)
        {
            return null;
        }
        return Flush();
    }

    /// <summary>The text to write now if the view has changed since it was last saved (the console is left, the Cyclops goes), otherwise null.</summary>
    public string Flush()
    {
        saveAt = -1f;
        seenRevision = View.Revision;
        if (View.Revision == savedRevision)
        {
            return null;
        }
        savedRevision = View.Revision;
        return View.ToText();
    }
}
