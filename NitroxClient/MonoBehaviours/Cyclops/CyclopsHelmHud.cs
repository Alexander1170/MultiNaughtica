using System;
using System.Collections.Generic;
using NitroxClient.GameLogic.Navigator;
using NitroxClient.GameLogic.VehicleUpgrades;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace NitroxClient.MonoBehaviours.Cyclops;

/// <summary>
/// What the helm HUD of every Cyclops gets (the play-test of coop.12): in the right-hand column of buttons (camera, silent running, decoy, and the sonar and the shield while those modules are in), <b>a button for the
/// helm's map</b> while the Cyclops has the Terrain Sonar, which switches the pilot's own copy of the map on and off (<see cref="CyclopsHelmMap" />, for them alone and remembered on their machine), and <b>a power share
/// button</b> under it while the Cyclops has the Power Share module, which drives the same synced and saved switch as the button on the lighting panel (<see cref="PowerShareSwitch" />), so both stay in step on every
/// client; and next to the power bolt of the HUD <b>a small "+"</b> while the Cyclops Solar Charger is really adding power (<see cref="VehicleSolar.IsCyclopsCharging" />, worked out by every client). The buttons are copies of
/// the sonar button with the game's scripts removed, the way the lighting panel's extra buttons are made (<see cref="CyclopsPanelButtons.PanelButton" />), with a picture drawn in code (<see cref="HelmIcons" />); they
/// take the rows that the sonar and the shield do not use. The HUD is built by the game and its buttons are wired in the prefab, so everything is made defensively: if it does not look as expected nothing is added and
/// the log says so once.
/// </summary>
public class CyclopsHelmHud : MonoBehaviour
{
    private const float REFRESH_SECONDS = 0.25f;
    private const string MAP_NAME = "NitroxHelmMapButton";
    private const string SHARE_NAME = "NitroxHelmPowerShareButton";
    private const string PLUS_NAME = "NitroxSolarPlus";

    private SubRoot subRoot;
    private CyclopsHelmMap helmMap;
    private CyclopsLightingPanel lightingPanel;
    private RectTransform sonarButton;
    private RectTransform shieldButton;
    private CyclopsPanelButtons.PanelButton mapButton;
    private CyclopsPanelButtons.PanelButton shareButton;
    private RectTransform powerIcon;
    private TMP_Text powerText;
    private TMP_Text plus;
    private bool built;
    private bool failed;
    private float nextRefresh;

    private void Awake()
    {
        subRoot = GetComponent<SubRoot>();
        helmMap = GetComponent<CyclopsHelmMap>();
    }

    private void OnDestroy()
    {
        mapButton?.Destroy();
        shareButton?.Destroy();
        if (plus)
        {
            Destroy(plus.gameObject);
        }
    }

    private void Update()
    {
        if (Time.unscaledTime < nextRefresh)
        {
            return;
        }
        nextRefresh = Time.unscaledTime + REFRESH_SECONDS;
        if (!subRoot || !Multiplayer.Active || !VehicleUpgradeSettings.Enabled)
        {
            return;
        }
        if (!built && !failed)
        {
            Build();
        }
        if (!built)
        {
            return;
        }
        RefreshButtons();
        RefreshPlus();
    }

    private static Transform FindByName(Transform root, string name)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == name)
            {
                return child;
            }
        }
        return null;
    }

    private void Fail(string reason)
    {
        failed = true;
        Log.Warn($"[VehicleUpgrades] helm HUD: {subRoot.name}: nothing added: {reason}");
    }

    private void Build()
    {
        try
        {
            Transform right = FindByName(subRoot.transform, "Canvas_RightHUD");
            Transform abilities = right ? right.Find("Abilities") : null;
            Transform sonar = abilities ? abilities.Find("Button_Sonar") : null;
            Transform shield = abilities ? abilities.Find("Button_Shield") : null;
            if (!abilities || !sonar || !shield)
            {
                // the HUD is not built yet (the Cyclops is still being set up): try again at the next refresh, a few times
                tries++;
                if (tries > 40)
                {
                    Fail("the right-hand column of the helm HUD is not as expected (Canvas_RightHUD/Abilities/Button_Sonar and Button_Shield)");
                }
                return;
            }
            sonarButton = (RectTransform)sonar;
            shieldButton = (RectTransform)shield;
            lightingPanel = subRoot.GetComponentInChildren<CyclopsLightingPanel>(true);

            // the game's own sonar button knows which of its images is the button (the root's, as far as the scene shows); the path to it from the root is where the copy puts its picture
            CyclopsSonarButton sonarScript = sonarButton.GetComponent<CyclopsSonarButton>();
            string iconPath = sonarScript && sonarScript.image ? PathBelow(sonarButton, sonarScript.image.transform) : string.Empty;
            mapButton = CyclopsPanelButtons.PanelButton.Create(sonarButton, iconPath, MAP_NAME, HelmIcons.Map, () => ToggleHelmMap());
            shareButton = CyclopsPanelButtons.PanelButton.Create(sonarButton, iconPath, SHARE_NAME, HelmIcons.Share, () => PowerShareSwitch.Press(lightingPanel));

            Transform center = FindByName(subRoot.transform, "Canvas_CenterHUD");
            Transform status = center ? center.Find("PowerStatus") : null;
            powerIcon = status && status.Find("PowerIcon") ? (RectTransform)status.Find("PowerIcon") : null;
            powerText = status && status.Find("PowerText") ? status.Find("PowerText").GetComponent<TMP_Text>() : null;
            MakePlus();

            built = mapButton != null || shareButton != null;
            Log.Info($"[VehicleUpgrades] helm HUD: {subRoot.name}: map button {(mapButton != null ? "built" : "not built")}, power share button {(shareButton != null ? "built" : "not built")}, solar plus {(plus ? "built" : "not built (the power readout was not found)")}");
            if (!built)
            {
                Fail("the buttons could not be copied");
            }
        }
        catch (Exception exception)
        {
            Fail(exception.Message);
            Log.Error(exception, "[VehicleUpgrades] helm HUD: could not be built");
        }
    }

    private int tries;

    /// <summary>The path from a root to a transform below it ("" for the root itself).</summary>
    private static string PathBelow(Transform root, Transform child)
    {
        if (child == root)
        {
            return string.Empty;
        }
        List<string> parts = [];
        for (Transform t = child; t && t != root; t = t.parent)
        {
            parts.Add(t.name);
        }
        parts.Reverse();
        return string.Join("/", parts);
    }

    /// <summary>The click on the map button: the pilot's own copy of the map goes on or off (for them alone).</summary>
    private void ToggleHelmMap()
    {
        if (helmMap)
        {
            helmMap.SetOn(!helmMap.IsOn);
        }
    }

    /// <summary>The "+": a copy of the power readout's text, with its game scripts removed, left of the bolt.</summary>
    private void MakePlus()
    {
        if (!powerText || !powerIcon)
        {
            return;
        }
        GameObject copy = Instantiate(powerText.gameObject, powerText.transform.parent, false);
        copy.name = PLUS_NAME;
        foreach (MonoBehaviour behaviour in copy.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour && !IsUiLibrary(behaviour.GetType()))
            {
                Destroy(behaviour);
            }
        }
        plus = copy.GetComponent<TMP_Text>();
        if (!plus)
        {
            Destroy(copy);
            return;
        }
        plus.text = "+";
        plus.color = new Color(0.45f, 1f, 0.55f, 1f);
        plus.raycastTarget = false;
        copy.SetActive(false);
    }

    private static bool IsUiLibrary(Type type)
    {
        string space = type.Namespace;
        return space != null && (space.StartsWith("UnityEngine.UI", StringComparison.Ordinal) || space.StartsWith("UnityEngine.EventSystems", StringComparison.Ordinal) || space.StartsWith("TMPro", StringComparison.Ordinal));
    }

    private static bool ButtonThere(RectTransform button)
    {
        return button && button.gameObject.activeInHierarchy;
    }

    private void RefreshButtons()
    {
        bool wantMap = mapButton != null && helmMap && TerrainSonar.IsAvailable(subRoot);
        bool wantShare = shareButton != null && PowerShare.IsAvailable(subRoot);
        List<CyclopsPanelButtons.PanelButton> wanted = [];
        if (wantMap)
        {
            wanted.Add(mapButton);
        }
        if (wantShare)
        {
            wanted.Add(shareButton);
        }
        IReadOnlyList<int> rows = HelmLayout.FreeRows(ButtonThere(sonarButton), ButtonThere(shieldButton), wanted.Count);
        (float X, float Y) sonar = (sonarButton.anchoredPosition.x, sonarButton.anchoredPosition.y);
        (float X, float Y) shield = (shieldButton.anchoredPosition.x, shieldButton.anchoredPosition.y);
        for (int i = 0; i < wanted.Count; i++)
        {
            (float x, float y) = HelmLayout.RowPosition(sonar, shield, rows[i]);
            wanted[i].SetVisible(true);
            wanted[i].Root.anchoredPosition = new Vector2(x, y);
        }
        mapButton?.SetVisible(wantMap);
        shareButton?.SetVisible(wantShare);

        if (wantMap)
        {
            bool on = helmMap.IsOn;
            mapButton.SetLit(on);
            mapButton.HoverText = Language.main.Get(on ? "Nitrox_HelmMapHoverOn" : "Nitrox_HelmMapHoverOff");
            mapButton.HoverHint = Language.main.Get(on ? "Nitrox_HelmMapHintOff" : "Nitrox_HelmMapHintOn");
        }
        if (wantShare)
        {
            bool on = PowerShareSwitch.IsOn(subRoot);
            shareButton.SetLit(on);
            shareButton.HoverText = Language.main.Get(on ? "Nitrox_PowerShareHoverOn" : "Nitrox_PowerShareHoverOff");
            shareButton.HoverHint = Language.main.Get(on ? "Nitrox_PowerShareHintOff" : "Nitrox_PowerShareHintOn");
        }
    }

    private void RefreshPlus()
    {
        if (!plus || !powerIcon)
        {
            return;
        }
        bool show = VehicleSolar.IsCyclopsCharging(subRoot);
        if (plus.gameObject.activeSelf != show)
        {
            plus.gameObject.SetActive(show);
        }
        if (show)
        {
            // just left of the bolt, as the player sees it: the icon's own left, a little more than its half width away
            RectTransform rect = plus.rectTransform;
            float distance = (powerIcon.rect.width * 0.5f + 0.35f * powerIcon.rect.height) * powerIcon.lossyScale.x;
            rect.position = powerIcon.position - powerIcon.right * distance;
            rect.rotation = powerIcon.rotation;
        }
    }

#if DEBUG
    /// <summary>Debug (the live check): the helm HUD as plain tokens: what is built and shown, the power share switch, the "+" and the things that decide it (the solar modules, the depth, the light, the power).</summary>
    public string DebugState()
    {
        Equipment modules = subRoot && subRoot.upgradeConsole ? subRoot.upgradeConsole.modules : null;
        PowerRelay relay = subRoot ? subRoot.powerRelay : null;
        float depth = subRoot ? Ocean.GetDepthOf(subRoot.gameObject) : 0f;
        float light = DayNightCycle.main ? DayNightCycle.main.GetLocalLightScalar() : -1f;
        return $"hudBuilt={built} hudFailed={failed} mapShown={(mapButton != null && mapButton.Visible)} shareShown={(shareButton != null && shareButton.Visible)} shareLit={(shareButton != null && shareButton.Lit)} shareOn={PowerShareSwitch.IsOn(subRoot)} shareAvailable={PowerShare.IsAvailable(subRoot)} plusShown={(plus && plus.gameObject.activeSelf)} charging={VehicleSolar.IsCyclopsCharging(subRoot)} solarModules={(modules != null ? modules.GetCount((TechType)CoopItems.CYCLOPS_SOLAR) : -1)} depth={depth:F1} light={light:F2} power={(relay ? relay.GetPower() : -1f):F0}/{(relay ? relay.GetMaxPower() : -1f):F0} survival={GameModeUtils.RequiresPower()}";
    }

    /// <summary>Debug (the live check): a click on the helm HUD's power share button, as the pilot's mouse would make it; false when the button is not built or not shown.</summary>
    public bool DebugClickShare()
    {
        if (shareButton == null || !shareButton.Visible)
        {
            return false;
        }
        shareButton.Click();
        return true;
    }
#endif
}
