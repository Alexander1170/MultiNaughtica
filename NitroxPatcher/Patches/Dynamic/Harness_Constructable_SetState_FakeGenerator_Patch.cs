#if DEBUG
using System;
using System.Reflection;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;
using NitroxClient.GameLogic.Spawning.Metadata.Processor;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// Sync test harness only (Debug builds, and only when the harness sets <c>NITROX_HARNESS_FAKE_GENERATOR=1</c>): the harness cannot build a solar panel or a bioreactor,
/// so every finished Locker module of a base gets a <see cref="PowerSource"/> of 75 power, full at first. It goes through the same code as a real generator
/// (PowerRelay, the PowerSource patches, metadata, spawners). Without the variable this patch does nothing.
/// </summary>
public sealed partial class Harness_Constructable_SetState_FakeGenerator_Patch : NitroxPatch, IDynamicPatch
{
    internal static readonly bool Enabled = Environment.GetEnvironmentVariable("NITROX_HARNESS_FAKE_GENERATOR") == "1";
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((Constructable t) => t.SetState(default, default));

    public static void Postfix(Constructable __instance, bool value)
    {
        if (value && __instance.GetComponentInParent<Base>())
        {
            AddFakeGenerator(__instance.gameObject);
        }
    }

    internal static readonly bool BioreactorEnabled = Environment.GetEnvironmentVariable("NITROX_HARNESS_FAKE_BIOREACTOR") == "1";

    /// <summary>
    /// Harness only (<c>NITROX_HARNESS_FAKE_BIOREACTOR=1</c>): the harness cannot place a bioreactor module either, so a finished Locker gets a real <see cref="BaseBioReactor"/>
    /// component that burns the items of the Locker's own container, and an empty <see cref="PowerSource"/> to charge. Everything else is the real code: the patches on
    /// <c>BaseBioReactor.ProducePower</c>, the container and item sync of the Locker (the same entity ids on the server), the power metadata.
    /// </summary>
    internal static void AddFakeBioreactor(GameObject gameObject)
    {
        if (!BioreactorEnabled || !gameObject.name.StartsWith("Locker") || gameObject.TryGetComponent(out BaseBioReactor _))
        {
            return;
        }

        StorageContainer storage = gameObject.GetComponentInChildren<StorageContainer>(true);
        if (!storage)
        {
            return;
        }

        PowerSource powerSource = gameObject.TryGetComponent(out PowerSource existing) ? existing : gameObject.AddComponent<PowerSource>();
        powerSource.maxPower = 200f;
        powerSource.power = 0f;
        BaseBioReactor reactor = gameObject.AddComponent<BaseBioReactor>();
        reactor._constructed = 1f;
        // The container is the Locker's: the items in it are the ones that are synced, with the ids the server knows
        typeof(BaseBioReactor).GetField("_container", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(reactor, storage.container);
        Log.Info($"[SyncHarness] fake bioreactor added to {gameObject.name}, it burns the {storage.container.count} items of the locker");
    }

    internal static void AddFakeGenerator(GameObject gameObject)
    {
        AddFakeBioreactor(gameObject);
        if (!Enabled || !gameObject.name.StartsWith("Locker") || gameObject.TryGetComponent(out PowerSource _))
        {
            return;
        }

        PowerSource powerSource = gameObject.AddComponent<PowerSource>();
        powerSource.maxPower = 75f;
        powerSource.power = 75f;
        Log.Info($"[SyncHarness] fake generator added to {gameObject.name}");
    }
}

/// <summary>
/// A module that is restored from its entity (join, resync, remote build) gets its fake generator when the entity's data is applied to it.
/// </summary>
public sealed partial class Harness_ModuleEntitySpawner_ApplyModuleData_FakeGenerator_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method(() => NitroxClient.GameLogic.Spawning.Bases.ModuleEntitySpawner.ApplyModuleData(default, default, default));

    public static void Postfix(GameObject moduleObject)
    {
        Harness_Constructable_SetState_FakeGenerator_Patch.AddFakeGenerator(moduleObject);
    }
}

/// <summary>
/// A module that is restored from its entity (join, resync) can have its metadata applied before it is a finished module under the base: the fake generator is there before the stored charge is applied.
/// </summary>
public sealed partial class Harness_PowerSourceMetadataProcessor_FakeGenerator_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((PowerSourceMetadataProcessor t) => t.ProcessMetadata(default(GameObject), default(PowerSourceMetadata)));

    public static void Prefix(GameObject gameObject)
    {
        Harness_Constructable_SetState_FakeGenerator_Patch.AddFakeGenerator(gameObject);
    }
}
#endif
