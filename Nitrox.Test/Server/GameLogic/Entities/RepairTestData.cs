using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities;

/// <summary>
///     Small entities for the tests of the world repair: what a generated batch puts into the entity data of a save.
/// </summary>
internal static class RepairTestData
{
    public static NitroxId Id(int number) => new(new Guid(number, 0, 0, new byte[8]));

    public static WorldEntity Creature(int id, string tech = "Boomerang", float x = 10f, float y = -5f, float z = 20f, bool spawnedByServer = true, string? classId = null)
    {
        return new WorldEntity(new NitroxVector3(x, y, z), NitroxQuaternion.Identity, NitroxVector3.One, new NitroxTechType(tech), 0, classId ?? $"class-{tech}", spawnedByServer, Id(id), null);
    }

    public static WorldEntity CreatureWithId(NitroxId id, string tech, float x, float y, float z)
    {
        return new WorldEntity(new NitroxVector3(x, y, z), NitroxQuaternion.Identity, NitroxVector3.One, new NitroxTechType(tech), 0, $"class-{tech}", true, id, null);
    }

    public static PlaceholderGroupWorldEntity Group(int id, string classId = "group-class", float x = 1f, float y = 2f, float z = 3f, string tech = "None")
    {
        WorldEntity plain = new(new NitroxVector3(x, y, z), NitroxQuaternion.Identity, NitroxVector3.One, new NitroxTechType(tech), 3, classId, true, Id(id), null);
        return new PlaceholderGroupWorldEntity(plain);
    }

    public static PrefabPlaceholderEntity Kid(int id, Entity parent)
    {
        WorldEntity plain = new(new NitroxVector3(0.5f, 0f, 0f), NitroxQuaternion.Identity, NitroxVector3.One, new NitroxTechType("None"), 1, "kid-class", true, Id(id), parent);
        PrefabPlaceholderEntity kid = new(plain);
        parent.ChildEntities.Add(kid);
        return kid;
    }

    public static SerializedWorldEntity Decor(int id, int batchX, int batchY, int batchZ, float x = 5f, float y = 5f, float z = 5f)
    {
        AbsoluteEntityCell cell = new(new NitroxInt3(batchX, batchY, batchZ), new NitroxInt3(1, 1, 1), 0);
        return new SerializedWorldEntity([], 0, new NitroxTransform(new NitroxVector3(x, y, z), NitroxQuaternion.Identity, NitroxVector3.One), Id(id), null, cell);
    }

    /// <summary>
    ///     The flat list of the entity data of a save: every entity once, parents and children side by side (the children are linked by their parent id).
    /// </summary>
    public static List<Entity> Flat(params Entity[] roots)
    {
        List<Entity> flat = [];
        Stack<Entity> pending = new(Enumerable.Reverse(roots));
        while (pending.Count > 0)
        {
            Entity entity = pending.Pop();
            flat.Add(entity);
            foreach (Entity child in Enumerable.Reverse(entity.ChildEntities))
            {
                pending.Push(child);
            }
        }
        return flat;
    }

    public static bool IsCreature(NitroxTechType? techType) => DuplicateEntityRepair.IsMobileCreature(techType);
}
