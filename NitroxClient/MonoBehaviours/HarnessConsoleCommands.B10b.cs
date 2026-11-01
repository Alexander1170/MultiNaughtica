#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Nitrox.Model.DataStructures;
using UnityEngine;

namespace NitroxClient.MonoBehaviours;

public partial class HarnessConsoleCommands
{
    /// <summary>
    ///     <c>craftdraw [amount]</c> (live check B10b, a harness script that is not part of this repository): does what a fabricator craft does to the power of a base, on THIS client. The harness cannot place a real
    ///     fabricator, so the command finds the <c>BasePowerRelay</c> of the nearest base and calls the static <c>CrafterLogic.ConsumeEnergy(PowerRelay, float)</c> that <c>GhostCrafter.Craft</c> calls
    ///     (5 units per craft) with it. The call goes through reflection so that the Harmony postfix <c>CrafterLogic_ConsumeEnergy_Patch</c> cannot be skipped by inlining. The postfix reports the
    ///     draw to the server (<c>BasePowerConsumed</c>) when this client does not hold the base's simulation lock. It only takes power in survival (<c>GameModeUtils.RequiresPower</c>).
    ///     With an amount of 0 (or less) nothing is drawn and only the state is logged. Two lines, key=value pairs:<br/>
    ///     <c>[SyncHarness] craftdraw: state base=&lt;id8&gt; lock=&lt;True|False|unknown&gt; inBase=&lt;bool&gt; baseComp=&lt;bool&gt; requiresPower=&lt;bool&gt; relay=&lt;power&gt;/&lt;max&gt; sourcePower=&lt;sum&gt; sources=&lt;n&gt; postfixes=[&lt;patch names on ConsumeEnergy&gt;]</c><br/>
    ///     <c>[SyncHarness] craftdraw: drew amount=&lt;a&gt; accepted=&lt;bool&gt; relay=&lt;before&gt;-&gt;&lt;after&gt; sourcePower=&lt;before&gt;-&gt;&lt;after&gt; reportExpected=&lt;bool&gt; sources=&lt;n&gt;</c>
    /// </summary>
    private void LiveCommand_craftdraw(NotificationCenter.Notification n)
    {
        float amount = LiveArgFloat(n, 0, 5f);
        if (!Player.main)
        {
            Log.Warn("[SyncHarness] craftdraw: there is no local player");
            return;
        }

        Base nearest = null;
        BasePowerRelay relay = null;
        float nearestDistance = float.MaxValue;
        foreach (Base candidate in FindObjectsOfType<Base>())
        {
            if (!candidate || candidate.isGhost)
            {
                continue;
            }

            BasePowerRelay candidateRelay = candidate.GetComponentInChildren<BasePowerRelay>(true);
            float distance = Vector3.Distance(candidate.transform.position, Player.main.transform.position);
            if (candidateRelay && distance < nearestDistance)
            {
                nearest = candidate;
                relay = candidateRelay;
                nearestDistance = distance;
            }
        }

        if (!nearest)
        {
            Log.Warn("[SyncHarness] craftdraw: no base with a BasePowerRelay is loaded on this client");
            return;
        }

        // The lock is read the way the Harmony postfix reads it (SimulationOwnership.HasAnyLockType), so that this line says whether a report is due
        string baseId = "noid";
        string lockText = "unknown";
        bool hasLock = false;
        if (nearest.TryGetNitroxId(out NitroxId id))
        {
            baseId = id.ToString().Substring(0, 8);
            try
            {
                hasLock = Nitrox.Model.Core.NitroxServiceLocator.LocateService<NitroxClient.GameLogic.SimulationOwnership>().HasAnyLockType(id);
                lockText = hasLock.ToString();
            }
            catch (Exception ex)
            {
                Log.Warn($"[SyncHarness] craftdraw: the lock state could not be read: {ex.Message}");
            }
        }

        MethodInfo consume = typeof(CrafterLogic).GetMethod("ConsumeEnergy", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(PowerRelay), typeof(float) }, null);
        bool requiresPower = GameModeUtils.RequiresPower();
        bool inBase = Player.main.currentSub && Player.main.currentSub.gameObject == nearest.gameObject;
        bool hasBaseComp = relay.baseComp;
        float relayBefore = relay.GetPower();
        float relayMax = relay.GetMaxPower();
        float sourceBefore = B10bSourcePower(nearest, out int sourceCount);
        string postfixes = B10bPostfixes(consume);
        Log.Info($"[SyncHarness] craftdraw: state base={baseId} lock={lockText} inBase={inBase} baseComp={hasBaseComp} requiresPower={requiresPower} relay={B10bFormat(relayBefore)}/{B10bFormat(relayMax)} sourcePower={B10bFormat(sourceBefore)} sources={sourceCount} postfixes=[{postfixes}]");

        if (amount <= 0f)
        {
            return;
        }

        bool accepted;
        try
        {
            accepted = consume != null ? (bool)consume.Invoke(null, new object[] { relay, amount }) : CrafterLogic.ConsumeEnergy(relay, amount);
        }
        catch (Exception ex)
        {
            string reason = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            Log.Warn($"[SyncHarness] craftdraw: CrafterLogic.ConsumeEnergy threw {ex.GetType().Name}: {reason}");
            return;
        }

        float relayAfter = relay.GetPower();
        float sourceAfter = B10bSourcePower(nearest, out int sourceCountAfter);
        // What the postfix does with this call: it reports when the draw went through, power is required, the relay belongs to a base with an id and this client holds no lock
        bool reportExpected = requiresPower && accepted && hasBaseComp && lockText != "unknown" && !hasLock;
        Log.Info($"[SyncHarness] craftdraw: drew amount={B10bFormat(amount)} accepted={accepted} relay={B10bFormat(relayBefore)}->{B10bFormat(relayAfter)} sourcePower={B10bFormat(sourceBefore)}->{B10bFormat(sourceAfter)} reportExpected={reportExpected} sources={sourceCountAfter}");
    }

    /// <summary>
    ///     The charge of every <c>PowerSource</c> under the base added up (the fake generator of the harness is one of them), and how many there are.
    /// </summary>
    private static float B10bSourcePower(Base baseComponent, out int sources)
    {
        float total = 0f;
        sources = 0;
        foreach (PowerSource source in baseComponent.GetComponentsInChildren<PowerSource>(true))
        {
            sources++;
            total += source.power;
        }

        return total;
    }

    private static string B10bFormat(float value)
    {
        return value.ToString("F1", CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     The names of the Harmony postfixes installed on a method ("Class.Method,..."), "none", or "unknown" when Harmony could not be asked. Harmony is read by reflection like
    ///     <c>patchcheck</c> does (NitroxClient does not reference it).
    /// </summary>
    private static string B10bPostfixes(MethodBase method)
    {
        if (method == null)
        {
            return "unknown";
        }

        try
        {
            Type harmonyType = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                harmonyType = assembly.GetType("HarmonyLib.Harmony");
                if (harmonyType != null)
                {
                    break;
                }
            }

            if (harmonyType == null)
            {
                return "unknown";
            }

            MethodInfo getPatchInfo = harmonyType.GetMethod("GetPatchInfo", new[] { typeof(MethodBase) });
            object info = getPatchInfo != null ? getPatchInfo.Invoke(null, new object[] { method }) : null;
            if (info == null)
            {
                return "none";
            }

            List<string> names = [];
            if (ReadMember(info, "Postfixes") is System.Collections.IEnumerable postfixes)
            {
                foreach (object patch in postfixes)
                {
                    MethodInfo patchMethod = ReadMember(patch, "PatchMethod") as MethodInfo;
                    names.Add(patchMethod != null && patchMethod.DeclaringType != null ? $"{patchMethod.DeclaringType.Name}.{patchMethod.Name}" : "?");
                }
            }

            return names.Count == 0 ? "none" : string.Join(",", names);
        }
        catch (Exception ex)
        {
            // without the word "Exception", so that the state line is not taken for an error line by a log scan
            string typeName = ex.GetType().Name.Replace("Exception", string.Empty);
            return $"unknown-{typeName}";
        }
    }
}
#endif
