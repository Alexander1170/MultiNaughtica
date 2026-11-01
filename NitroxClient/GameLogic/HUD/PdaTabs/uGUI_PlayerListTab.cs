using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NitroxClient.Communication.Abstract;
using NitroxClient.GameLogic.HUD.Components;
using NitroxClient.GameLogic.PlayerLogic.PlayerModel.Abstract;
using Nitrox.Model.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static NitroxClient.Unity.Helper.AssetBundleLoader;

namespace NitroxClient.GameLogic.HUD.PdaTabs;

/// <summary>
/// The component containing a new PDA tab based on ping manager tab
/// </summary>
public class uGUI_PlayerListTab : uGUI_PingTab
{
    private NitroxPDATabManager nitroxPDATabManager;
    private PlayerManager playerManager;
    private LocalPlayer localPlayer;
    private IPacketSender packetSender;

    private readonly Dictionary<string, Sprite> assets = new();
    public bool FinishedLoadingAssets { get; private set; }

    private new readonly Dictionary<string, uGUI_PlayerPingEntry> entries = new();
    private PrefabPool<uGUI_PlayerPingEntry> pool;
    private new readonly Dictionary<string, uGUI_PlayerPingEntry> tempSort = new();

    // The stats under the names (item c): rebuilt about once a second while this tab is the shown one, never while the PDA is closed or on another tab
    private float lastStatsRefresh = float.NegativeInfinity;
    private bool statsFailed;

    /// <summary>How many times the stats were rebuilt since the game started (for the Debug probe: they must stand still while the PDA is closed).</summary>
    public int StatsRefreshCount { get; private set; }

    public override void Awake()
    {
        // Copied from uGUI_PingTab.Awake but we don't want it to be executed because it creates a PrefabPool
        selectableVisibilityToggle = new SelectableWrapper(visibilityToggle, delegate (GameInput.Button button)
        {
            if (button == GameInput.Button.UISubmit)
            {
                visibilityToggle.isOn = !visibilityToggle.isOn;
                return true;
            }

            return false;
        });

        // the words of the stats line are resolved in the language of this session (the static may hold those of an earlier one)
        PlayerListStatsSource.ResetLabels();
        nitroxPDATabManager = NitroxServiceLocator.LocateService<NitroxPDATabManager>();
        playerManager = NitroxServiceLocator.LocateService<PlayerManager>();
        localPlayer = NitroxServiceLocator.LocateService<LocalPlayer>();
        packetSender = NitroxServiceLocator.LocateService<IPacketSender>();
        // Need to reassign manually these variables and get rid of the objects we don't need
        content = gameObject.FindChild("Content").GetComponent<CanvasGroup>();
        pingManagerLabel = gameObject.GetComponentInChildren<TextMeshProUGUI>();
        scrollRect = gameObject.GetComponentInChildren<ScrollRect>();
        pingCanvas = (RectTransform)content.transform.Find("ScrollView/Viewport/ScrollCanvas");

        pool = new PrefabPool<uGUI_PlayerPingEntry>(prefabEntry, pingCanvas, 8, 4, delegate (uGUI_PlayerPingEntry entry)
        {
            entry.Uninitialize();
        }, delegate (uGUI_PlayerPingEntry entry)
        {
            entry.Uninitialize();
        });
    }

    public IEnumerator Start()
    {
        Transform buttonAll = content.transform.Find("ButtonAll");
        DestroyImmediate(buttonAll.gameObject);

        yield return LoadAllAssets(NitroxAssetBundle.PLAYER_LIST_TAB);

        foreach (Object asset in NitroxAssetBundle.PLAYER_LIST_TAB.LoadedAssets)
        {
            if (asset is Sprite sprite)
            {
                if (asset.name.Equals("player_list_tab@3x"))
                {
                    nitroxPDATabManager.AddTabSprite(asset.name, sprite);
                }
                assets.Add(asset.name, sprite);
            }
        }

        FinishedLoadingAssets = true;
        _isDirty = true;
    }
    
    public Sprite GetSprite(string assetName)
    {
        if (assets.TryGetValue(assetName, out Sprite sprite))
        {
            return sprite;
        }
        return Sprite.Create(new Texture2D(100, 100), new Rect(0, 0, 100, 100), new Vector2(50, 50), 100);
    }

    public new void OnEnable()
    {
        // Enter events for player join and disconnect
        playerManager.OnCreate += OnAdd;
        playerManager.OnRemove += OnRemove;
    }

    public new void OnDestroy()
    {
        playerManager.OnCreate -= OnAdd;
        playerManager.OnRemove -= OnRemove;
    }

    /// <summary>
    /// The PDA closed: the next time it opens on this tab the lines are rebuilt at once, and a failure of the last time is forgotten.
    /// </summary>
    public override void OnClosePDA()
    {
        base.OnClosePDA();
        lastStatsRefresh = float.NegativeInfinity;
        statsFailed = false;
    }

    public override void OnLanguageChanged()
    {
        pingManagerLabel.text = Language.main.Get("Nitrox_PlayerListTabName");
        entries.Values.ForEach(entry => entry.OnLanguageChanged());
        PlayerListStatsSource.ResetLabels();
        lastStatsRefresh = float.NegativeInfinity;
    }

    /// <summary>
    /// The PDA calls this every frame while it is open, with <paramref name="isOpen" /> true only for the tab that is shown (and once from the warm-up in <c>Player.Awake</c>, when the PDA is not
    /// open: then <c>currentTab</c> is not this tab). Closed, or on another tab, it costs one bool test.
    /// </summary>
    public override void OnLateUpdate(bool isOpen)
    {
        bool wasDirty = _isDirty;
        UpdateEntries();
        if (!isOpen)
        {
            lastStatsRefresh = float.NegativeInfinity;
            statsFailed = false;
            return;
        }

        uGUI_PDA pda = uGUI_PDA.main;
        bool shown = !statsFailed && pda && pda.currentTab == this;
        if (wasDirty)
        {
            // a player came or went: the rows are new, they get their lines in this frame
            lastStatsRefresh = float.NegativeInfinity;
        }

        float now = Time.unscaledTime;
        if (!PlayerListStatsRules.ShouldRefresh(now, lastStatsRefresh, PlayerListStatsRules.REFRESH_INTERVAL, shown))
        {
            return;
        }

        lastStatsRefresh = now;
        try
        {
            RefreshStats(now);
        }
        catch (System.Exception exception)
        {
            // an exception here would skip the rest of the PDA's late update every frame: stop until the tab is shown again
            statsFailed = true;
            Log.Error(exception, "[PlayerList] the stats under the player names could not be refreshed");
        }
    }

    private void RefreshStats(float now)
    {
        StatsRefreshCount++;
        foreach (uGUI_PlayerPingEntry entry in entries.Values)
        {
            if (entry.IsLocalPlayer || entry.NitroxPlayer is not RemotePlayer remote)
            {
                entry.SetStats(default);
                continue;
            }

            entry.SetStats(PlayerListStatsSource.Read(remote, now));
        }
    }

    public new void UpdateEntries()
    {
        if (!_isDirty)
        {
            return;
        }
        _isDirty = false;

        Dictionary<string, INitroxPlayer> players = playerManager.GetAll().ToDictionary<RemotePlayer, string, INitroxPlayer>(player => player.SessionId.ToString(), player => player);
        players.Add(localPlayer.SessionId.ToString(), localPlayer);

        foreach (KeyValuePair<string, INitroxPlayer> entry in players)
        {
            if (!entries.ContainsKey(entry.Key))
            {
                // Sets up a new entry for the player
                AddNewEntry(entry.Key, entry.Value);
            }
        }

        // Sort the items by alphabetical order (based on SN's code)
        tempSort.Clear();
        foreach (KeyValuePair<string, uGUI_PlayerPingEntry> entry in entries)
        {
            if (!entry.Value.IsLocalPlayer)
            {
                tempSort.Add(entry.Value.PlayerName, entry.Value);
            }
        }

        List<string> sorted = new(tempSort.Keys);
        sorted.Sort();

        entries[localPlayer.SessionId.ToString()].rectTransform.SetSiblingIndex(0);
        for (int j = 0; j < sorted.Count; j++)
        {
            string id = tempSort[sorted[j]].id;
            entries[id].rectTransform.SetSiblingIndex(j + 1);
        }
    }

    public uGUI_PlayerPingEntry GetEntry()
    {
        uGUI_PlayerPingEntry uGUI_PlayerEntry;
        if (pool.pool.Count == 0)
        {
            for (int i = 0; i < 4; i++)
            {
                uGUI_PlayerEntry = Instantiate(prefabEntry).GetComponent<uGUI_PlayerPingEntry>();
                uGUI_PlayerEntry.rectTransform.SetParent(pingCanvas, false);
                uGUI_PlayerEntry.Uninitialize();
                pool.pool.Add(uGUI_PlayerEntry);
            }
        }
        int index = pool.pool.Count - 1;
        uGUI_PlayerEntry = pool.pool[index];
        pool.pool.RemoveAt(index);
        return uGUI_PlayerEntry;
    }

    public void MakePrefab(GameObject basePrefab)
    {
        // We need to instantiate the prefab as we cannot directly make modifications in it
        GameObject newPrefab = Instantiate(basePrefab);
        newPrefab.name = "PlayerEntry";
        // We never want this to appear
        DestroyImmediate(newPrefab.FindChild("ColorToggle"));

        // Need to modify the pingTab's script from uGUI_PingEntry to uGUI_PlayerEntry
        uGUI_PingEntry pingEntry = newPrefab.GetComponent<uGUI_PingEntry>();
        uGUI_PlayerPingEntry playerEntry = newPrefab.AddComponent<uGUI_PlayerPingEntry>();
        playerEntry.visibility = pingEntry.visibility;
        playerEntry.visibilityIcon = pingEntry.visibilityIcon;
        playerEntry.icon = pingEntry.icon;
        playerEntry.label = pingEntry.label;
        // the stats are a second and third line under the name: no wrapping at a space, no cutting at the bottom of the label (the clone of the prefab is ours, the asset is not touched)
        playerEntry.label.enableWordWrapping = false;
        playerEntry.label.overflowMode = TextOverflowModes.Overflow;
        playerEntry._rectTransform = pingEntry._rectTransform;
        playerEntry.id = pingEntry.id;
        playerEntry.spriteVisible = pingEntry.spriteVisible;
        playerEntry.spriteHidden = pingEntry.spriteHidden;
        DestroyImmediate(pingEntry);

        // Make buttons for mute, kick, tp
        Transform container = newPrefab.transform;
        playerEntry.ShowObject = newPrefab.FindChild("ButtonVisibility");
        playerEntry.ShowObject.AddComponent<ButtonTooltip>();

        playerEntry.MuteObject = Instantiate(playerEntry.ShowObject, container);
        playerEntry.KickObject = Instantiate(playerEntry.ShowObject, container);
        playerEntry.TeleportToObject = Instantiate(playerEntry.ShowObject, container);
        playerEntry.TeleportToMeObject = Instantiate(playerEntry.ShowObject, container);
        playerEntry.MuteObject.name = "MuteObject";
        playerEntry.KickObject.name = "KickObject";
        playerEntry.TeleportToObject.name = "TeleportToObject";
        playerEntry.TeleportToMeObject.name = "TeleportToMeObject";

        prefabEntry = newPrefab;
    }

    private void AddNewEntry(string playerId, INitroxPlayer player)
    {
        uGUI_PlayerPingEntry entry = GetEntry();
        entry.Initialize(playerId, player.PlayerName, this);
        entry.UpdateEntryForNewPlayer(player, localPlayer, packetSender);
        entries.Add(playerId, entry);
    }

    private void OnAdd(SessionId sessionId, RemotePlayer remotePlayer)
    {
        _isDirty = true;
    }

    private void OnRemove(SessionId sessionId, RemotePlayer remotePlayers)
    {
        string playerIdString = sessionId.ToString();
        if (!entries.TryGetValue(playerIdString, out uGUI_PlayerPingEntry? entry))
        {
            return;
        }
        entries.Remove(playerIdString);
        pool.Release(entry);
        _isDirty = true;
    }
}
