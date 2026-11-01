#if DEBUG
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NitroxClient.GameLogic.VehicleUpgrades;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
///     Debug commands of the extra module slots (docs/coop/VEHICLE_UPGRADES.md, "Extra module slots"). Each logs <c>[SyncHarness] vuextra: key=value</c> lines.
///     <c>vuextra [dump|vehicles|pda kind|pdaclose|hud|apply n]</c>: <c>pda kind</c> does everything in one command because the chat cannot be typed while the PDA is open: it opens the PDA, lists the slot objects that are shown, takes the picture (tag <c>xs-pda-kind</c>) and closes the PDA (<c>pda done</c>). <c>apply n</c> sets the number of extra slots as the settings of a session would (0 to 2) and prints what the game's own code now says: the slot
///     count of every vehicle and the upgrade flags that <c>SubRoot.SetCyclopsUpgrades</c> sets, which proves that the lists follow the option in a process that has already used them.  <c>pda seamoth|prawn|cyclops</c> opens the PDA on the equipment of the nearest such vehicle (the call the vehicle upgrade console and the Cyclops' upgrade console make) and
///     lists the slot objects that are shown; <c>pdaclose</c> closes it; <c>hud</c> lists the icons of the quick slot bar. <c>dump</c> lists every slot object of the PDA's equipment screen (<c>uGUI_Equipment</c>) with its place in the hierarchy and its rect (anchors, position, size, scale), and
///     the chain of parents of the first slot of each group, to fit the extra slots into the panels of the vanilla layout; <c>vehicles</c> lists the Seamoths, Prawn Suits and Cyclops consoles of the world with their
///     slot lists and what sits in each slot.
/// </summary>
public partial class HarnessConsoleCommands
{
    private static string VuSlotRect(RectTransform rect)
    {
        if (!rect)
        {
            return "none";
        }
        return $"anchorMin={rect.anchorMin.ToString("F2")} anchorMax={rect.anchorMax.ToString("F2")} pivot={rect.pivot.ToString("F2")} pos={rect.anchoredPosition.ToString("F1")} size={rect.sizeDelta.ToString("F1")} rect={rect.rect.size.ToString("F1")} scale={rect.localScale.ToString("F2")}";
    }

    private void LiveCommand_vuextra(NotificationCenter.Notification n)
    {
        switch (LiveArgString(n, 0, "dump"))
        {
            case "vehicles":
                VuSlotsVehicles();
                break;
            case "pda":
                StartCoroutine(VuSlotsPda(LiveArgString(n, 1, "seamoth")));
                break;
            case "pdaclose":
                Player.main.GetPDA().Close();
                Log.Info($"{VU}vuextra: pda closed");
                break;
            case "hud":
                VuSlotsHud();
                break;
            case "apply":
                VuSlotsApply(Mathf.RoundToInt(LiveArgFloat(n, 1, 2f)));
                break;
            default:
                VuSlotsDump();
                break;
        }
    }

    private static void VuSlotsDump()
    {
        // the PDA screens may be inactive (the screen hides itself in Awake): every loaded one, assets of the project excluded
        uGUI_Equipment[] screens = Resources.FindObjectsOfTypeAll<uGUI_Equipment>().Where(screen => screen.gameObject.scene.IsValid()).ToArray();
        Log.Info($"{VU}vuextra: equipment screens={screens.Length}");
        foreach (uGUI_Equipment screen in screens)
        {
            Log.Info($"{VU}vuextra: screen path={screen.gameObject.GetFullHierarchyPath()} active={screen.gameObject.activeInHierarchy} slots={screen.allSlots.Count} {VuSlotRect(screen.transform as RectTransform)}");
            StringBuilder ancestors = new();
            for (Transform t = screen.transform.parent; t; t = t.parent)
            {
                ancestors.Append($" [{t.name} {VuSlotRect(t as RectTransform)}]");
            }
            Log.Info($"{VU}vuextra: ancestors of the screen:{ancestors}");
            HashSet<Transform> parentsDone = [];
            foreach (KeyValuePair<string, uGUI_EquipmentSlot> pair in screen.allSlots.OrderBy(p => p.Key, System.StringComparer.Ordinal))
            {
                uGUI_EquipmentSlot slot = pair.Value;
                RectTransform rect = slot.transform as RectTransform;
                string children = string.Join("|", Enumerable.Range(0, slot.transform.childCount).Select(i => slot.transform.GetChild(i).name));
                Log.Info($"{VU}vuextra: slot id={pair.Key} path={slot.gameObject.GetFullHierarchyPath()} sibling={slot.transform.GetSiblingIndex()} active={slot.gameObject.activeSelf} {VuSlotRect(rect)} children={children}");
                // the art of a slot (the silhouette of the vehicle, the arm background): everything but the three parts every slot has
                for (int i = 0; i < slot.transform.childCount; i++)
                {
                    Transform child = slot.transform.GetChild(i);
                    if (child.name is "Background" or "Hint" or "IconRect")
                    {
                        continue;
                    }
                    UnityEngine.UI.Image image = child.GetComponent<UnityEngine.UI.Image>();
                    Log.Info($"{VU}vuextra: art of {pair.Key}: {child.name} active={child.gameObject.activeSelf} {VuSlotRect(child as RectTransform)} sprite={(image && image.sprite ? image.sprite.name : "none")} color={(image ? image.color.ToString("F2") : "none")}");
                }
                Transform parent = slot.transform.parent;
                if (parent && parentsDone.Add(parent))
                {
                    StringBuilder chain = new();
                    for (Transform t = parent; t && t != screen.transform.parent; t = t.parent)
                    {
                        chain.Append($" [{t.name} {VuSlotRect(t as RectTransform)}]");
                    }
                    Log.Info($"{VU}vuextra: chain of {pair.Key}:{chain}");
                    Log.Info($"{VU}vuextra: siblings of {pair.Key}: {string.Join("|", Enumerable.Range(0, parent.childCount).Select(i => parent.GetChild(i).name))}");
                }
            }
        }
        Log.Info($"{VU}vuextra: dump done");
    }

    private static void VuSlotsVehicles()
    {
        foreach (Vehicle vehicle in FindObjectsOfType<Vehicle>())
        {
            StringBuilder installed = new();
            if (vehicle.modules != null)
            {
                foreach (string slot in vehicle.slotIDs)
                {
                    installed.Append($" {slot}={vehicle.modules.GetTechTypeInSlot(slot)}");
                }
            }
            Log.Info($"{VU}vuextra: vehicle type={vehicle.GetType().Name} id={VuId(vehicle.gameObject)} slotCount={vehicle.slotIDs.Length} slotIDs=[{string.Join(",", vehicle.slotIDs)}] installed:{installed}");
        }
        foreach (SubRoot subRoot in FindObjectsOfType<SubRoot>())
        {
            if (!VuIsRealCyclops(subRoot) || !subRoot.upgradeConsole || subRoot.upgradeConsole.modules == null)
            {
                continue;
            }
            StringBuilder installed = new();
            foreach (string slot in VehicleSlotLists.CyclopsSlotNames)
            {
                installed.Append($" {slot}={subRoot.upgradeConsole.modules.GetTechTypeInSlot(slot)}");
            }
            Log.Info($"{VU}vuextra: cyclops id={VuId(subRoot.gameObject)} slotNames=[{string.Join(",", VehicleSlotLists.CyclopsSlotNames)}] flags: shield={subRoot.shieldUpgrade} sonar={subRoot.sonarUpgrade} repair={subRoot.vehicleRepairUpgrade} decoy={subRoot.decoyTubeSizeIncreaseUpgrade} thermal={subRoot.thermalReactorUpgrade} installed:{installed}");
        }
        Log.Info($"{VU}vuextra: vehicles done");
    }

    private static string VuSlotPosition(Transform transform) => transform is RectTransform rect ? rect.anchoredPosition.ToString("F0") : "?";

    private IEnumerator VuSlotsPda(string kind)
    {
        Equipment target = null;
        Transform at = null;
        float best = float.MaxValue;
        Vector3 from = Player.main ? Player.main.transform.position : Vector3.zero;
        if (kind == "cyclops")
        {
            foreach (SubRoot subRoot in FindObjectsOfType<SubRoot>())
            {
                float distance = Vector3.Distance(from, subRoot.transform.position);
                if (VuIsRealCyclops(subRoot) && subRoot.upgradeConsole && subRoot.upgradeConsole.modules != null && distance < best)
                {
                    best = distance;
                    target = subRoot.upgradeConsole.modules;
                    at = subRoot.upgradeConsole.transform;
                }
            }
        }
        else
        {
            foreach (Vehicle vehicle in FindObjectsOfType<Vehicle>())
            {
                bool wanted = kind == "prawn" ? vehicle is Exosuit : vehicle is SeaMoth;
                float distance = Vector3.Distance(from, vehicle.transform.position);
                if (wanted && vehicle.modules != null && distance < best)
                {
                    best = distance;
                    target = vehicle.modules;
                    at = vehicle.transform;
                }
            }
        }
        if (target == null)
        {
            Log.Info($"{VU}vuextra: pda no {kind} with equipment");
            yield break;
        }
        Inventory.main.SetUsedStorage(target, false);
        bool opened = Player.main.GetPDA().Open(PDATab.Inventory, at, null);
        yield return new WaitForSeconds(2f);
        uGUI_Equipment screen = Resources.FindObjectsOfTypeAll<uGUI_Equipment>().FirstOrDefault(candidate => candidate.gameObject.scene.IsValid());
        string shown = screen ? string.Join("|", screen.allSlots.Where(pair => pair.Value && pair.Value.gameObject.activeSelf).OrderBy(pair => pair.Key, System.StringComparer.Ordinal).Select(pair => $"{pair.Key}@{VuSlotPosition(pair.Value.transform)}")) : "no screen";
        Log.Info($"{VU}vuextra: pda opened={opened} of={kind} distance={VuNum(best)} slotsOfTheEquipment={target.equipment.Count} shown={shown}");
        // the picture and the closing in the same command: the chat cannot be typed while the PDA is open
        yield return LiveShotRoutine(false, 0f, $"xs-pda-{kind}");
        Player.main.GetPDA().Close();
        yield return new WaitForSeconds(0.5f);
        Log.Info($"{VU}vuextra: pda done of={kind}");
    }

    private static void VuSlotsHud()
    {
        uGUI_QuickSlots bar = FindObjectOfType<uGUI_QuickSlots>();
        if (!bar)
        {
            Log.Info($"{VU}vuextra: hud no quick slot bar");
            return;
        }
        StringBuilder icons = new();
        if (bar.icons != null)
        {
            foreach (uGUI_ItemIcon icon in bar.icons)
            {
                icons.Append(icon ? $" [{icon.name} {(icon.rectTransform ? icon.rectTransform.anchoredPosition.ToString("F0") : "?")} active={icon.gameObject.activeInHierarchy}]" : " [null]");
            }
        }
        Log.Info($"{VU}vuextra: hud target={(bar.target != null ? bar.target.GetType().Name : "none")} slotCount={(bar.target != null ? bar.target.GetSlotCount() : -1)} icons={(bar.icons != null ? bar.icons.Length : -1)}{icons}");
    }

    private static void VuSlotsApply(int extra)
    {
        VehicleSlotLists.Apply(extra);
        foreach (Vehicle vehicle in FindObjectsOfType<Vehicle>())
        {
            Log.Info($"{VU}vuextra: apply extra={VehicleSlotLists.Extra} vehicle type={vehicle.GetType().Name} id={VuId(vehicle.gameObject)} getSlotCount={vehicle.GetSlotCount()} slotIDs={vehicle.slotIDs.Length}");
        }
        foreach (SubRoot subRoot in FindObjectsOfType<SubRoot>())
        {
            if (!VuIsRealCyclops(subRoot) || !subRoot.upgradeConsole)
            {
                continue;
            }
            // the game's own code, with the patch in it: the modules in the seventh and the eighth slot count only when the option says the slots exist
            subRoot.SetCyclopsUpgrades();
            Log.Info($"{VU}vuextra: apply extra={VehicleSlotLists.Extra} cyclops id={VuId(subRoot.gameObject)} shield={subRoot.shieldUpgrade} sonar={subRoot.sonarUpgrade} slotCount={VehicleSlotLists.CyclopsSlotCount}");
        }
        Log.Info($"{VU}vuextra: apply done");
    }
}
#endif
