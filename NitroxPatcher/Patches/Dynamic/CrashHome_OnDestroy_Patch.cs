using System.Reflection;
using NitroxClient.GameLogic;
using Nitrox.Model.DataStructures;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// <see cref="CrashHome.OnDestroy" /> runs when a home is unloaded: its cell went to sleep because the players left. That is not a death of the Crashfish inside it.
/// </summary>
/// <remarks>
/// This patch used to tell the server that the fish was destroyed whenever the player who held the lock of the home was the one who left. Every home then lost its fish for everybody (also for
/// the player who stood next to it) the first time that player walked away, the server kept the number -1 of a home with a fish, and whoever came back found an empty home that started the
/// timer of 20 minutes: the world "Test" held 1 Crashfish for its 66 homes (14 homes at -1 without a fish, 36 with a timer, 16 nobody had met). The fish is an entity of the server, a child of its home, and
/// comes back with the home when a cell holding them is loaded again. A fish that really dies announces it itself (<see cref="LiveMixin_Kill_Patch" />, the Detonate of the fish kills it).
/// The patch only writes the Debug line that the live check of the crash homes reads.
/// <para>
/// The simulation lock of the unloaded home is left alone on purpose. The client is not told when the server revokes it silently (a sleeping cell is reported, the server revokes the lock and only
/// announces a new holder), so it can keep a lock that the server no longer has, but that never makes two holders: the moment another player is made the holder the server tells every client
/// (<c>CellVisibilityChangedProcessor</c> broadcasts the change to all), and <c>SimulationOwnership.TreatSimulatedEntity</c> drops the lock of an entity that is not loaded as well; when nobody else
/// takes the home, the same player is announced as holder again as soon as the cell is loaded (<c>SimulationOwnershipData.TryToAcquire</c> is true for the current owner). Dropping the lock here would
/// only be right if every destruction of a held home came with a change of visibility, and nothing here proves that.
/// </para>
/// </remarks>
public sealed partial class CrashHome_OnDestroy_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((CrashHome t) => t.OnDestroy());

    public static void Prefix(CrashHome __instance)
    {
        if (!__instance.crash ||
            !__instance.TryGetNitroxId(out NitroxId crashHomeId) ||
            !__instance.crash.TryGetNitroxId(out NitroxId crashId))
        {
            return;
        }

        Log.Debug($"[{nameof(CrashHome_OnDestroy_Patch)}] CrashHome {crashHomeId} unloaded (lock held: {Resolve<SimulationOwnership>().HasAnyLockType(crashHomeId)}), its Crash {crashId} stays an entity of the server");
    }
}
