using System.Collections;
using System.Collections.Generic;
using NitroxClient.Communication.Abstract;
using NitroxClient.Communication.Packets.Processors;
using NitroxClient.GameLogic.HUD.Components;
using NitroxClient.GameLogic.PlayerLogic.PlayerModel.Abstract;
using NitroxClient.MonoBehaviours.Gui.Modals;
using Nitrox.Model.Core;
using Nitrox.Model.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.Packets;
using UnityEngine;
using UnityEngine.UI;
using UWE;

namespace NitroxClient.GameLogic.HUD.PdaTabs;

public class uGUI_PlayerPingEntry : uGUI_PingEntry
{
    private uGUI_PlayerListTab parent;
    private INitroxPlayer player;

    public string PlayerName => player?.PlayerName ?? string.Empty;
    public bool IsLocalPlayer => player is LocalPlayer;
    public INitroxPlayer NitroxPlayer => player;

    // The stats under the name (item c, PlayerListStatsRules): the label holds the name and, in a smaller size, the lines of stats; the row is as tall as that text needs. The gate keeps the
    // label (and its mesh) from being rebuilt for a text that did not change.
    private const float STATS_SIZE_PERCENT = 65f;
    private const float ROW_MARGIN = 6f;
    private readonly PlayerListStatsRules.TextGate textGate = new();
    private LayoutElement rowLayout;
    private float rowMinHeight;
    private float rowPreferredHeight;
    private float rowHeight;
    private bool rowMeasured;
    private bool rowTall;

    /// <summary>The label text as it was last assigned (name, and the stats lines when there are any), for the Debug probe.</summary>
    public string ShownText => label ? label.text : string.Empty;

    /// <summary>The stats that <see cref="SetStats" /> was last given (all empty for a name only), for the Debug probe: the row is compared with what it was given, not with the numbers of now.</summary>
    public PlayerListStatsRules.StatsLine LastLine { get; private set; }

    public string LastStats => LastLine.Text;

    /// <summary>How often the label was really assigned by <see cref="SetStats" /> (the Debug probe checks that an unchanged text does not rebuild it).</summary>
    public int AssignCount { get; private set; }
    private bool showPing;

    private bool muted
    {
        get
        {
            if (player is RemotePlayer remotePlayer && remotePlayer.PlayerContext != null)
            {
                return remotePlayer.PlayerContext.IsMuted;
            }
            // By default we don't care about the local state
            return false;
        }
    }

    public GameObject ShowObject;
    public GameObject MuteObject;
    public GameObject KickObject;
    public GameObject TeleportToObject;
    public GameObject TeleportToMeObject;

    public Sprite MutedSprite;
    public Sprite UnmutedSprite;
    public Sprite KickSprite;
    public Sprite TeleportToSprite;
    public Sprite TeleportToMeSprite;

    public new void Awake()
    {
        NitroxServiceLocator.LocateService<MutePlayerProcessor>().OnPlayerMuted += (playerId, _) =>
        {
            if (player is RemotePlayer remotePlayer && remotePlayer.SessionId == playerId)
            {
                RefreshMuteButton();
            }
        };
        NitroxServiceLocator.LocateService<PermsChangedProcessor>().OnPermissionsChanged += (perms) => RefreshButtonsVisibility();
    }

    public IEnumerator Start()
    {
        // We must one frame so that the UI elements are initialized properly
        yield return null;
        // This action must happen after the yield so that they're correctly placed
        UpdateButtonsPosition();
        // We trigger it at least once so that the localizations are updated with the PlayerName
        OnLanguageChanged();
    }

    public void Initialize(string id, string name, uGUI_PlayerListTab parent)
    {
        this.id = id;
        this.parent = parent;

        gameObject.SetActive(true);
        visibilityIcon.sprite = spriteVisible;
        icon.SetForegroundSprite(SpriteManager.Get(SpriteManager.Group.Tab, "TabInventory"));
        showPing = true;

        UpdateLabel(name);
        OnLanguageChanged();

        CoroutineHost.StartCoroutine(AssignSprites());
    }

    public void OnLanguageChanged()
    {
        // the words of the stats change with the language: the next refresh assigns the label again
        textGate.Reset();
        GetTooltip(ShowObject).TooltipText = GetLocalizedText(showPing ? "Nitrox_HidePing" : "Nitrox_ShowPing");
        GetTooltip(MuteObject).TooltipText = GetLocalizedText(muted ? "Nitrox_Unmute" : "Nitrox_Mute");
        GetTooltip(KickObject).TooltipText = GetLocalizedText("Nitrox_Kick");
        GetTooltip(TeleportToObject).TooltipText = GetLocalizedText("Nitrox_TeleportTo");
        GetTooltip(TeleportToMeObject).TooltipText = GetLocalizedText("Nitrox_TeleportToMe");
    }

    public new void Uninitialize()
    {
        base.Uninitialize();
        player = null;
        // a recycled row must not keep the height or the numbers of the player it showed before
        RestoreRowHeight();
        textGate.Reset();
        LastLine = default;
    }

    public void UpdateLabel(string text)
    {
        label.text = text;
        textGate.Reset();
        LastLine = default;
        RestoreRowHeight();
    }

    /// <summary>
    /// Shows the stats of this player under his name, or only the name when the line is empty (the local player's row, a player whose line is not built yet). The label is
    /// assigned only when the text changed. Rich text: the name is kept literal (<c>noparse</c>), the stats are 65 percent of the name's size.
    /// </summary>
    public void SetStats(PlayerListStatsRules.StatsLine line)
    {
        if (!label)
        {
            return;
        }

        MeasureRow();
        string stats = line.Text;
        bool hasStats = stats.Length > 0;
        LastLine = line;
        string name = PlayerName;
        string text = hasStats ? $"<noparse>{name}</noparse>\n<size={STATS_SIZE_PERCENT}%>{stats}</size>" : name;
        if (textGate.Changed(text))
        {
            label.richText = true;
            label.text = text;
            AssignCount++;
        }

        // The height is decided every time, not only when the text changed: the first one is made before the row has had its layout pass (its rectangle may be a placeholder then), and a text
        // that stays the same for minutes would keep a wrong height. The preferred height of the label is cached by TextMeshPro until the text or the width changes: asking is cheap.
        if (hasStats)
        {
            ApplyRowHeight(PlayerListStatsRules.RowHeight(rowHeight, label.preferredHeight, ROW_MARGIN));
        }
        else
        {
            RestoreRowHeight();
        }
    }

    public void ResetStatsGate()
    {
        textGate.Reset();
    }

    private void MeasureRow()
    {
        if (rowMeasured)
        {
            // while the row is not made taller its rectangle is the laid-out height of a one-line row: take it over (the first measure may have been made before the layout pass)
            if (!rowTall && rectTransform.rect.height > 0f)
            {
                rowHeight = rectTransform.rect.height;
            }

            return;
        }

        float height = rectTransform.rect.height;
        if (height <= 0f)
        {
            return;
        }

        rowHeight = height;
        rowLayout = GetComponent<LayoutElement>();
        if (!rowLayout)
        {
            rowLayout = gameObject.AddComponent<LayoutElement>();
        }

        rowMinHeight = rowLayout.minHeight;
        rowPreferredHeight = rowLayout.preferredHeight;
        rowMeasured = true;
    }

    private void ApplyRowHeight(float height)
    {
        if (!rowMeasured || (rowTall && Mathf.Approximately(rectTransform.rect.height, height)))
        {
            return;
        }

        rowLayout.minHeight = height;
        rowLayout.preferredHeight = height;
        rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        rowTall = true;
        UpdateButtonsPosition();
    }

    private void RestoreRowHeight()
    {
        if (!rowMeasured || !rowTall)
        {
            return;
        }

        rowLayout.minHeight = rowMinHeight;
        rowLayout.preferredHeight = rowPreferredHeight;
        rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, rowHeight);
        rowTall = false;
        UpdateButtonsPosition();
    }

    public void UpdateEntryForNewPlayer(INitroxPlayer newPlayer, LocalPlayer localPlayer, IPacketSender packetSender)
    {
        player = newPlayer;

        UpdateLabel(player.PlayerName);
        Color playerColor = player.PlayerSettings.PlayerColor.ToUnity();
        icon.SetColors(playerColor, playerColor, playerColor);
        RefreshMuteButton();

        // We need to update each button's listener whether or not they have enough perms because they may become OP during playtime
        ClearButtonListeners();

        GetToggle(ShowObject).onValueChanged.AddListener(delegate (bool toggled)
        {
            if (player is RemotePlayer remotePlayer)
            {
                PingInstance pingInstance = remotePlayer.PlayerModel.GetComponentInChildren<PingInstance>();
                pingInstance.SetVisible(toggled);
                GetTooltip(ShowObject).TooltipText = GetLocalizedText(toggled ? "Nitrox_HidePing" : "Nitrox_ShowPing");
                visibilityIcon.sprite = toggled ? spriteVisible : spriteHidden;
            }
        });
        // Each of those clicks involves a confirmation modal
        GetToggle(MuteObject).onValueChanged.AddListener(delegate (bool toggled)
        {
            Modal.Get<ConfirmModal>()?.Show(GetLocalizedText(muted ? "Nitrox_Unmute" : "Nitrox_Mute", true), () =>
            {
                GetToggle(MuteObject).SetIsOnWithoutNotify(!toggled);
                if (player is RemotePlayer remotePlayer)
                {
                    packetSender.Send(new ServerCommand($"{(toggled ? "" : "un")}mute {player.PlayerName}"));
                }
            });
        });
        GetToggle(KickObject).onValueChanged.AddListener(delegate (bool toggled)
        {
            Modal.Get<ConfirmModal>()?.Show(GetLocalizedText("Nitrox_Kick", true), () =>
            {
                packetSender.Send(new ServerCommand($"kick {player.PlayerName}"));
            });
        });
        GetToggle(TeleportToObject).onValueChanged.AddListener(delegate (bool toggled)
        {
            Modal.Get<ConfirmModal>()?.Show(GetLocalizedText("Nitrox_TeleportTo", true), () =>
            {
                packetSender.Send(new ServerCommand($"warp {player.PlayerName}"));
            });
        });
        GetToggle(TeleportToMeObject).onValueChanged.AddListener(delegate (bool toggled)
        {
            Modal.Get<ConfirmModal>()?.Show(GetLocalizedText("Nitrox_TeleportToMe", true), () =>
            {
                packetSender.Send(new ServerCommand($"warp {player.PlayerName} {localPlayer.PlayerName}"));
            });
        });

        RefreshButtonsVisibility();
    }

    private string GetLocalizedText(string key, bool isQuestion = false)
    {
        return Language.main.Get(isQuestion ? $"{key}Question" : key).Replace("{PLAYER}", PlayerName);
    }

    public void UpdateButtonsPosition()
    {
        float OFFSET = 0f;
        List<GameObject> buttonsToAlign = new() { MuteObject, KickObject, TeleportToObject, TeleportToMeObject };
        foreach (GameObject buttonObject in buttonsToAlign)
        {
            RectTransform buttonRect = buttonObject.GetComponent<RectTransform>();
            buttonRect.anchoredPosition = new Vector2(rectTransform.anchoredPosition.x, 0f);
            // centred on the row: y = 0 is the middle only when the pivot is in the middle, and a row with stats is taller than one without
            buttonRect.localPosition = new Vector2(OFFSET, rectTransform.rect.center.y);
            OFFSET += 80f;
        }
    }

    private void ClearButtonListeners()
    {
        GetToggle(MuteObject).onValueChanged = new Toggle.ToggleEvent();
        GetToggle(KickObject).onValueChanged = new Toggle.ToggleEvent();
        GetToggle(TeleportToObject).onValueChanged = new Toggle.ToggleEvent();
        GetToggle(TeleportToMeObject).onValueChanged = new Toggle.ToggleEvent();
    }

    private IEnumerator AssignSprites()
    {
        yield return new WaitUntil(() => parent.FinishedLoadingAssets);

        // NB: Those textures MUST be exported with a Texture Type of "Sprite (2D and UI)", else they will look blurry not matter what
        // NB 2: Those textures for the buttons are scaled 68x61 but the image inside but not hit the borders to have a better render
        MutedSprite = parent.GetSprite("muted@3x");
        UnmutedSprite = parent.GetSprite("unmuted@3x");
        KickSprite = parent.GetSprite("kick@3x");
        TeleportToSprite = parent.GetSprite("teleport_to@3x");
        TeleportToMeSprite = parent.GetSprite("teleport_to_me@3x");

        MuteObject.FindChild("Eye").GetComponent<Image>().sprite = muted ? MutedSprite : UnmutedSprite;
        KickObject.FindChild("Eye").GetComponent<Image>().sprite = KickSprite;
        TeleportToObject.FindChild("Eye").GetComponent<Image>().sprite = TeleportToSprite;
        TeleportToMeObject.FindChild("Eye").GetComponent<Image>().sprite = TeleportToMeSprite;
    }

    private void RefreshMuteButton()
    {
        GetToggle(MuteObject).SetIsOnWithoutNotify(muted);
        GetTooltip(MuteObject).TooltipText = GetLocalizedText(muted ? "Nitrox_Unmute" : "Nitrox_Mute");
        MuteObject.FindChild("Eye").GetComponent<Image>().sprite = muted ? MutedSprite : UnmutedSprite;
    }

    private void RefreshButtonsVisibility()
    {
        LocalPlayer localPlayer = NitroxServiceLocator.LocateService<LocalPlayer>();

        bool isNotLocalPlayer = !IsLocalPlayer;
        // We don't want any control buttons to appear for the local player
        ShowObject.SetActive(isNotLocalPlayer);

        // The perms here should be the same as the perm each command asks for
        MuteObject.SetActive(isNotLocalPlayer && localPlayer.Permissions >= Perms.MODERATOR);
        KickObject.SetActive(isNotLocalPlayer && localPlayer.Permissions >= Perms.MODERATOR);
        TeleportToObject.SetActive(isNotLocalPlayer && localPlayer.Permissions >= Perms.MODERATOR);
        TeleportToMeObject.SetActive(isNotLocalPlayer && localPlayer.Permissions >= Perms.MODERATOR);
    }

    private Toggle GetToggle(GameObject gameObject)
    {
        return gameObject.GetComponent<Toggle>();
    }

    private ButtonTooltip GetTooltip(GameObject gameObject)
    {
        return gameObject.GetComponent<ButtonTooltip>();
    }
}
