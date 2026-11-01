using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using UnityEngine;

namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// The slots on the PDA's equipment screen. The screen (<c>uGUI_Equipment</c>) finds the slot objects that are in its prefab, by the id on each, and shows the ones the open equipment has; a slot with no object is only
/// logged ("slot not found in pda screen") and cannot be seen. So the objects of the extra slots are copies of a plain slot of the same vehicle, made once when the screen starts (the PDA is created with the scene,
/// before the settings arrive: the copies are always made, and stay hidden while the equipment does not have their slot). Their places are those of <see cref="ExtraModuleSlotRules.LayoutOf" />, measured in the
/// running game.
/// </summary>
public static class ExtraModuleSlotsScreen
{
    public static void AddSlotObjects(uGUI_Equipment screen)
    {
        if (screen == null || screen.allSlots == null)
        {
            return;
        }
        foreach ((string slot, ExtraSlotKind kind) in ExtraModuleSlotRules.AllExtraSlots)
        {
            if (screen.allSlots.ContainsKey(slot))
            {
                continue;
            }
            string templateId = ExtraModuleSlotRules.TemplateOf(slot);
            if (!screen.allSlots.TryGetValue(templateId, out uGUI_EquipmentSlot template) || !template)
            {
                Log.Warn($"[VehicleUpgrades] extra module slots: the screen has no slot {templateId} to copy for {slot}; the slot cannot be shown");
                continue;
            }

            uGUI_EquipmentSlot copy = Object.Instantiate(template, template.transform.parent);
            copy.name = kind == ExtraSlotKind.Cyclops ? $"Cyclops{slot}" : slot;
            copy.slot = slot;
            copy.manager = screen;
            (float x, float y) = ExtraModuleSlotRules.LayoutOf(slot);
            if (copy.transform is RectTransform rect)
            {
                rect.anchoredPosition = new Vector2(x, y);
            }
            copy.gameObject.SetActive(false);
            screen.allSlots.Add(slot, copy);
        }
    }
}
