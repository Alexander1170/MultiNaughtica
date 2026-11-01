using Nitrox.Model.DataStructures.Unity;
using Nitrox.Server.Subnautica.Models.GameLogic.Entities;
using Nitrox.Server.Subnautica.Models.Resources.Core;

namespace Nitrox.Server.Subnautica.Models.Resources;

/// <summary>
/// Some PrefabPlaceholders spawn GameObjects that are always there (decor, environment ...)
/// And some others spawn a GameObject with an EntitySlot in which case this field is not null.
/// </summary>
/// <param name="DataboxTechType">The name of the tech type that a databox placeholder (a <c>DataboxSpawner</c>) unlocks: vanilla spawns the databox only while that tech is not known (R11).</param>
[Serializable]
public record struct PrefabPlaceholderAsset(
    string ClassId,
    NitroxEntitySlot? EntitySlot = null,
    NitroxTransform? Transform = null,
    string? DataboxTechType = null
) : IPrefabAsset;
