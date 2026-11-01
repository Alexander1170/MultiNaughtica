using System.Collections.Generic;
using System.Linq;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Metadata;

namespace Nitrox.Server.Subnautica.Models.GameLogic.Entities;

/// <summary>
///     Puts the sulfur plants (<c>CrashHome</c>) of a loaded world in line with the Crashfish that the world holds for them. Pure: it changes the metadata of the entities it is given, and the parent
///     of the transform of a Crashfish, nothing else.
/// </summary>
/// <remarks>
///     <para>
///         The metadata of a home is one number, <see cref="CrashHomeMetadata.SpawnTime" />: <see cref="FISH_ALIVE" /> while a fish lives in it, <see cref="PRISTINE" /> for a home nobody has met
///         (the bootstrapper writes it, the first player who holds the home makes the fish), and the game time of the next fish after a fish died. The fish is an entity of its own, a child of
///         the home. The two must agree: a home at -1 without a fish started a timer of 20 minutes whenever someone came back (the home looked exploded), a home with a timer and a fish made a
///         second fish when the timer ran out.
///     </para>
///     <para>
///         The world "Test" of coop.3 (read for this rule) held 1 fish for 66 homes: 14 homes at -1 without a fish (the client told the server that the fish was destroyed whenever the player who held the
///         lock of a home walked away, see <c>CrashHome_OnDestroy_Patch</c>), 36 with a timer, 16 that nobody had met, and 12 other entities (Peepers, RabbitRays, ...) that carried the metadata of a home
///         because the ids of homes were given to them by a batch that was generated again (the design notes). The one fish that is left belongs to a home with a timer. No client is
///         connected while this runs, so a home at -1 without a fish cannot be a fish that has just been destroyed: it is made fresh, and the first player who holds it gets the fish at once
///         instead of after 20 minutes.
///     </para>
///     <para>
///         A Crashfish is introduced by the client that makes it, with the position it has in the WORLD in the transform (<c>ToWorldDto</c>), and the home as its parent. The loader of a save
///         hangs every child under its parent's transform without converting (<c>SetParent(parent, false)</c>), which is right for the placeholders below a group (their positions are local) and
///         moves the fish far from its home on the server: its cell is wrong (<see cref="WorldEntity.AbsoluteEntityCell" />), so the players who see the fish are not the ones who are told that it
///         died. The fish of a home now stays through a restart, so the transform is taken out from under the parent again (the values stay, they are the world position).
///     </para>
/// </remarks>
internal static class CrashHomeStates
{
    internal const string CRASH_HOME = "CrashHome";
    internal const string CRASH = "Crash";

    /// <summary>The number of a home with a fish.</summary>
    internal const float FISH_ALIVE = -1f;

    /// <summary>The number the bootstrapper writes: a home nobody has met, which makes its fish as soon as a player holds it.</summary>
    internal const float PRISTINE = 0f;

    /// <param name="FishlessHomes">Homes that had no fish but a number that says "a fish lives here": made fresh.</param>
    /// <param name="HomesWithFishAndTimer">Homes that had a fish and a timer or no number: the fish wins, the number is -1.</param>
    /// <param name="OtherEntities">Entities that are no home but carried the metadata of one: it is removed.</param>
    /// <param name="FishTransforms">Crashfish whose transform hung under the transform of their home: taken out again.</param>
    internal readonly record struct Result(int FishlessHomes, int HomesWithFishAndTimer, int OtherEntities, int FishTransforms)
    {
        internal int Changed => FishlessHomes + HomesWithFishAndTimer + OtherEntities + FishTransforms;
    }

    internal static bool IsHome(Entity entity) => entity.TechType?.Name == CRASH_HOME;

    internal static bool IsCrash(Entity entity) => entity.TechType?.Name == CRASH;

    internal static Result Normalize(IEnumerable<Entity> entities)
    {
        List<Entity> all = [.. entities];

        // The child lists are rebuilt from the parent ids when a save is loaded, and a registry keeps both: either one says that a home has a fish
        HashSet<NitroxId> idsWithFish = [];
        foreach (Entity entity in all)
        {
            if (IsCrash(entity) && entity.ParentId != null)
            {
                idsWithFish.Add(entity.ParentId);
            }
        }

        int fishlessHomes = 0;
        int homesWithFishAndTimer = 0;
        int otherEntities = 0;
        int fishTransforms = 0;
        foreach (Entity entity in all)
        {
            if (IsCrash(entity))
            {
                if (entity is WorldEntity { Transform.Parent: not null } fish)
                {
                    fish.Transform.SetParent(null, false);
                    fishTransforms++;
                }
            }

            if (!IsHome(entity))
            {
                if (entity.Metadata is CrashHomeMetadata)
                {
                    entity.Metadata = null;
                    otherEntities++;
                }
                continue;
            }

            // Metadata of another kind on a home is not ours to judge
            if (entity.Metadata != null && entity.Metadata is not CrashHomeMetadata)
            {
                continue;
            }

            bool hasFish = idsWithFish.Contains(entity.Id) || entity.ChildEntities.Any(IsCrash);
            float? number = (entity.Metadata as CrashHomeMetadata)?.SpawnTime;
            if (hasFish)
            {
                if (number is not FISH_ALIVE)
                {
                    entity.Metadata = new CrashHomeMetadata(FISH_ALIVE);
                    homesWithFishAndTimer++;
                }
            }
            else if (number is null or < 0f)
            {
                entity.Metadata = new CrashHomeMetadata(PRISTINE);
                fishlessHomes++;
            }
        }

        return new Result(fishlessHomes, homesWithFishAndTimer, otherEntities, fishTransforms);
    }

    /// <summary>
    ///     The Crashfish that a world holds beyond the one fish of a home. A home points to one fish (<c>CrashHome.crash</c>); a second Crash entity under the same home is a ghost on every
    ///     client that loads the home (the client destroys only the fish its home points to, the others stay in the world until the scene ends), and since the fish of a home stay through a
    ///     restart it would come back at every load. The fish with the lowest id (text order, so that two runs agree) stays. Pure: it only reads, the caller takes the fish out of the registry.
    /// </summary>
    /// <remarks>
    ///     Only fish of a real home count (a Crash entity under any other parent, for instance an entity that carries the id of a home, is not touched). The fish is found by its own parent id or by
    ///     the child list of its home, either one is enough, and a fish seen both ways is one fish.
    /// </remarks>
    /// <returns>The fish to take out, ordered by id; empty in a healthy world.</returns>
    internal static List<Entity> FindSurplusFish(IEnumerable<Entity> entities)
    {
        List<Entity> all = [.. entities];

        HashSet<NitroxId> homeIds = [];
        foreach (Entity entity in all)
        {
            if (IsHome(entity))
            {
                homeIds.Add(entity.Id);
            }
        }

        Dictionary<NitroxId, Dictionary<NitroxId, Entity>> fishByHome = [];
        void AddFish(NitroxId homeId, Entity fish)
        {
            if (!fishByHome.TryGetValue(homeId, out Dictionary<NitroxId, Entity>? fishOfHome))
            {
                fishOfHome = [];
                fishByHome[homeId] = fishOfHome;
            }

            if (!fishOfHome.ContainsKey(fish.Id))
            {
                fishOfHome[fish.Id] = fish;
            }
        }

        foreach (Entity entity in all)
        {
            if (IsCrash(entity) && entity.ParentId != null && homeIds.Contains(entity.ParentId))
            {
                AddFish(entity.ParentId, entity);
            }
            else if (IsHome(entity))
            {
                foreach (Entity child in entity.ChildEntities.Where(IsCrash))
                {
                    AddFish(entity.Id, child);
                }
            }
        }

        List<Entity> surplus = [];
        foreach (Dictionary<NitroxId, Entity> fishGroup in fishByHome.Values)
        {
            if (fishGroup.Count > 1)
            {
                surplus.AddRange(fishGroup.Values.OrderBy(fish => fish.Id.ToString(), StringComparer.Ordinal).Skip(1));
            }
        }

        surplus.Sort((a, b) => string.CompareOrdinal(a.Id.ToString(), b.Id.ToString()));
        return surplus;
    }
}
