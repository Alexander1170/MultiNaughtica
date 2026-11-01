using System;
using System.Collections.Generic;
using NitroxClient.GameLogic.Settings;
using NitroxClient.GameLogic.Spawning.Bases;
using NitroxClient.GameLogic.Spawning.Metadata;
using NitroxClient.MonoBehaviours;
using Nitrox.Model.DataStructures;
using Nitrox.Model.Subnautica.DataStructures.GameLogic;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Bases;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities;
using Nitrox.Model.Subnautica.DataStructures.GameLogic.Entities.Bases;
using UnityEngine;

namespace NitroxClient.GameLogic.Bases;

public static class BuildUtils
{
    public static bool TryGetIdentifier(BaseDeconstructable baseDeconstructable, out BuildPieceIdentifier identifier, BaseCell? baseCell = null, Base.Face? baseFace = null)
    {
        // It is unimaginable to have a BaseDeconstructable that is not child of a BaseCell
        if (!baseCell && !baseDeconstructable.TryGetComponentInParent(out baseCell, true))
        {
            identifier = default;
            return false;
        }

        identifier = GetIdentifier(baseDeconstructable, baseCell, baseFace);
        return true;
    }

    public static BuildPieceIdentifier GetIdentifier(BaseDeconstructable baseDeconstructable, BaseCell baseCell, Base.Face? baseFace = null)
    {
        return new BuildPieceIdentifier
        {
            Recipe = baseDeconstructable.recipe.ToDto(),
            BaseFace = baseFace?.ToDto() ?? baseDeconstructable.face?.ToDto(),
            BaseCell = baseCell.cell.ToDto(),
            PiecePoint = baseDeconstructable.deconstructedBase.WorldToGrid(baseDeconstructable.transform.position).ToDto()
        };
    }

    public static bool TryGetGhostFace(BaseGhost baseGhost, out Base.Face face)
    {
        // Copied code from BaseAddModuleGhost.Finish() and BaseAddFaceGhost.Finish() to obtain the face at which the module was spawned
        switch (baseGhost)
        {
            case BaseAddModuleGhost moduleGhost:
                face = moduleGhost.anchoredFace.Value;
                face.cell += baseGhost.targetBase.GetAnchor();
                return true;
            case BaseAddFaceGhost faceGhost:
                if (faceGhost.anchoredFace.HasValue)
                {
                    face = faceGhost.anchoredFace.Value;
                    face.cell += faceGhost.targetBase.GetAnchor();
                    return true;
                }
                if (BaseAddFaceGhost.FindFirstMaskedFace(faceGhost.ghostBase, out face))
                {
                    Vector3 point = faceGhost.ghostBase.GridToWorld(Int3.zero);
                    faceGhost.targetOffset = faceGhost.targetBase.WorldToGrid(point);
                    face.cell += faceGhost.targetOffset;
                    return true;
                }
                break;
            case BaseAddWaterPark waterPark:
                if (waterPark.anchoredFace.HasValue)
                {
                    face = waterPark.anchoredFace.Value;
                    face.cell += waterPark.targetBase.GetAnchor();
                    return true;
                }
                break;
            case BaseAddMapRoomGhost:
                face = new Base.Face(GetMapRoomFunctionalityCell(baseGhost), 0);
                return true;
        }

        face = default;
        return false;
    }

    /// <remarks>
    /// Even if the corresponding module was found, in some cases (with WaterParks notably) we don't want to transfer the id.
    /// We then return false because the GameObject may have already been marked.
    /// </remarks>
    /// <returns>
    /// Whether or not the id was successfully transferred
    /// </returns>
    public static bool TryTransferIdFromGhostToModule(BaseGhost baseGhost, NitroxId id, ConstructableBase constructableBase, out GameObject? moduleObject)
    {
        // 1. Find the face of the target piece
        Base.Face? face = null;
        bool isWaterPark = baseGhost is BaseAddWaterPark;
        bool isMapRoomGhost = baseGhost is BaseAddMapRoomGhost;
        // Only four types of ghost which spawn a module
        if (baseGhost is BaseAddFaceGhost faceGhost && faceGhost.modulePrefab ||
            baseGhost is BaseAddModuleGhost moduleGhost && moduleGhost.modulePrefab ||
            isMapRoomGhost ||
            isWaterPark)
        {
            if (TryGetGhostFace(baseGhost, out Base.Face ghostFace))
            {
                face = ghostFace;
            }
            else
            {
                Log.Error($"Couldn't find the module spawned by {baseGhost}");
                moduleObject = null;
                return false;
            }
        }
        // If the ghost is under a BaseDeconstructable(Clone), it may have an associated module
        else if (IsBaseDeconstructable(constructableBase))
        {
            face = new Base.Face(constructableBase.moduleFace.Value.cell + baseGhost.targetBase.GetAnchor(), constructableBase.moduleFace.Value.direction);
        }
        else
        {
            switch (constructableBase.techType)
            {
                case TechType.BaseWaterPark:
                    // Edge case that happens when a Deconstructed WaterPark is built onto another deconstructed WaterPark that has its module
                    // A new module will be created by the current Deconstructed WaterPark which is the one we'll be aiming at
                    IBaseModuleGeometry baseModuleGeometry = constructableBase.GetComponentInChildren<IBaseModuleGeometry>(true);
                    if (baseModuleGeometry != null)
                    {
                        face = baseModuleGeometry.geometryFace;
                    }
                    break;

                case TechType.BaseMoonpool:
                    // Moonpools are a very specific case, we tweak them to work as interior pieces (while they're not)
                    Optional<GameObject> objectOptional = baseGhost.targetBase.gameObject.EnsureComponent<MoonpoolManager>().RegisterMoonpool(constructableBase.transform, id);
                    moduleObject = objectOptional.Value;
                    return moduleObject;

                case TechType.BaseMapRoom:
                    // In the case the ghost is under a BaseDeconstructable, this is a good way to identify a MapRoom
                    face = new Base.Face(GetMapRoomFunctionalityCell(baseGhost), 0);
                    isMapRoomGhost = true;
                    break;

                default:
                    moduleObject = null;
                    return false;
            }
        }

        if (!face.HasValue)
        {
            if (constructableBase.techType != TechType.BaseWaterPark)
            {
                Log.Error($"No face could be found for ghost {baseGhost}");
            }
            moduleObject = null;
            return false;
        }

        // 2. Use that face to find the newly created piece and set its id to the desired one
        if (isMapRoomGhost)
        {
            MapRoomFunctionality mapRoomFunctionality = baseGhost.targetBase.GetMapRoomFunctionalityForCell(face.Value.cell);
            if (mapRoomFunctionality)
            {
                // As MapRooms can be built as the first piece of a base, we need to make sure that they receive a new id if they're not in a base
                // The sender and the receiving clients decide the id with the same rule, see MapRoomIdRules
                bool ghostIsInBase = constructableBase.GetComponentInParent<Base>(true);
                NitroxId nitroxId = MapRoomIdRules.GetMapRoomId(id, ghostIsInBase);
                NitroxEntity.SetNewId(mapRoomFunctionality.gameObject, nitroxId);
                moduleObject = mapRoomFunctionality.gameObject;
                return true;
            }
            Log.Error($"Couldn't find MapRoomFunctionality of built MapRoom (cell: {face.Value.cell}). {DescribeMapRoomLookup(baseGhost, constructableBase)}");
            moduleObject = null;
            return false;
        }

        IBaseModule module = baseGhost.targetBase.GetModule(face.Value);
        if (module != null)
        {
            // If the WaterPark is higher than one, it means that the newly built WaterPark will be merged with one that already has a NitroxEntity
            if (module is WaterPark waterPark && waterPark.height > 1)
            {
                // as the WaterPark is necessarily merged, we won't need to do anything about it
                moduleObject = null;
                return false;
            }

            moduleObject = (module as Component).gameObject;
            NitroxEntity.SetNewId(moduleObject, id);
            return true;
        }
        // When a WaterPark is merged with another one, we won't find its module but we don't care about that
        if (!isWaterPark)
        {
            Log.Error("Couldn't find the module's GameObject of built interior piece when transferring its NitroxEntity to the module.");
        }

        moduleObject = null;
        return false;
    }

    /// <remarks>
    ///     The criteria to make sure that a ConstructableBase is one of a BaseDeconstructable is if it has a moduleFace
    ///     because this field is only filled for the base deconstruction (<see cref="BaseDeconstructable.Deconstruct"/>, <seealso cref="ConstructableBase.LinkModule(Base.Face?)"/>).
    /// </remarks>
    public static bool IsBaseDeconstructable(ConstructableBase constructableBase)
    {
        return constructableBase.moduleFace.HasValue;
    }

    /// <remarks>
    /// A BaseDeconstructable's ghost component is a simple BaseGhost so we need to identify it by the parent ConstructableBase instead.
    /// </remarks>
    /// <param name="faceAlreadyLinked">Whether <see cref="ConstructableBase.moduleFace"/> was already set or not</param>
    public static bool IsUnderBaseDeconstructable(BaseGhost baseGhost, bool faceAlreadyLinked)
    {
        return baseGhost.TryGetComponentInParent(out ConstructableBase constructableBase, true) &&
            (IsBaseDeconstructable(constructableBase) || !faceAlreadyLinked);
    }

    public static Int3 GetMapRoomFunctionalityCell(BaseGhost baseGhost)
    {
        // Code found from Base.GetMapRoomFunctionalityForCell
        return baseGhost.targetBase.NormalizeCell(baseGhost.targetBase.WorldToGrid(GetGhostOccupiedCenter(baseGhost.ghostBase)));
    }

    /// <summary>
    /// The centre of the cells that the ghost's Base holds, at the pose the ghost has NOW.
    /// </summary>
    /// <remarks>
    /// <c>Base.occupiedBounds</c> is a world space box that <c>Base.RebuildGeometry</c> stores and nothing refreshes when the ghost is moved afterwards. On the builder the ghost is
    /// finished at the pose it was placed at, so the stored box is right. A receiving client restores the ghost in <see cref="GhostEntitySpawner.RestoreGhost" /> with the base-local
    /// position of the entity as its world position (no parent yet), runs <c>FinishDeserialization</c> and <c>Place</c> (both rebuild the geometry, so the box is stored there) and
    /// only then puts the ghost under the base and moves it to its real pose. For a ghost that is added to an existing base, which is not at the world origin, the stored centre is
    /// then far from the room, <c>Base.GetMapRoomFunctionalityForCell</c> finds nothing and the room stays without an id. <c>Base.RecomputeOccupiedBounds</c> on a ghost
    /// (<c>isGhost</c>) only rewrites that box (the LOD part is skipped for ghosts) and gives the same box as the builder had when the ghost is already where it was placed.
    /// </remarks>
    private static Vector3 GetGhostOccupiedCenter(Base ghostBase)
    {
        // A ghost that is already destroyed can't be asked to recompute (Base.GridToWorld needs its transform): the stored box is all that is left
        if (!ghostBase || !ghostBase.isGhost)
        {
            return ghostBase.occupiedBounds.center;
        }

        Vector3 storedCenter = ghostBase.occupiedBounds.center;
        ghostBase.RecomputeOccupiedBounds();
        Vector3 currentCenter = ghostBase.occupiedBounds.center;
        if ((currentCenter - storedCenter).sqrMagnitude > 0.0001f)
        {
            Log.Debug($"[{nameof(BuildUtils)}] The stored occupiedBounds of the ghost base were stale (centre {storedCenter.ToString("F2")}), refreshed to the ghost's current pose (centre {currentCenter.ToString("F2")})");
        }
        return currentCenter;
    }

    /// <summary>
    /// A scanner room ghost: its own ghost type, or the plain <see cref="BaseGhost"/> of a deconstructed one, which <see cref="TryTransferIdFromGhostToModule"/> identifies by the TechType.
    /// </summary>
    public static bool IsMapRoomGhost(BaseGhost baseGhost, ConstructableBase constructableBase)
    {
        return baseGhost is BaseAddMapRoomGhost || constructableBase.techType == TechType.BaseMapRoom;
    }

    /// <summary>
    /// Everything that is needed to see why <c>Base.GetMapRoomFunctionalityForCell</c> did not find the room that a ghost just built. Only for the error line, never throws.
    /// </summary>
    private static string DescribeMapRoomLookup(BaseGhost baseGhost, ConstructableBase constructableBase)
    {
        try
        {
            Base targetBase = baseGhost.targetBase;
            // The lookup that just failed already refreshed this box to the ghost's current pose (GetGhostOccupiedCenter); a stale value, if there was one, is in the Debug line before this error
            Vector3 center = baseGhost.ghostBase.occupiedBounds.center;
            Int3 centerGrid = targetBase.WorldToGrid(center);
            Vector3 modelPosition = baseGhost.transform.position;
            // includeInactive on purpose: Base.GetMapRoomFunctionalityForCell only sees the active ones
            MapRoomFunctionality[] rooms = targetBase.GetComponentsInChildren<MapRoomFunctionality>(true);
            List<string> roomTexts = new();
            for (int i = 0; i < rooms.Length; i++)
            {
                Vector3 roomPosition = rooms[i].transform.position;
                Int3 roomGrid = targetBase.WorldToGrid(roomPosition);
                string roomId = rooms[i].TryGetNitroxId(out NitroxId existingId) ? existingId.ToString() : "none";
                roomTexts.Add($"#{i} at {roomPosition.ToString("F2")} grid {roomGrid} cell {targetBase.NormalizeCell(roomGrid)} activeInHierarchy {rooms[i].gameObject.activeInHierarchy} id {roomId}");
            }

            string ghostText = $"ghost occupiedBounds centre (refreshed to the current pose) {center.ToString("F2")} -> grid {centerGrid} -> cell {targetBase.NormalizeCell(centerGrid)}";
            string positionText = $"ghost root {constructableBase.transform.position.ToString("F2")} model {modelPosition.ToString("F2")} (model grid in the built base {targetBase.WorldToGrid(modelPosition)}, ghost targetOffset {baseGhost.targetOffset})";
            string baseText = $"targetBase size {targetBase.GetSize()} anchor {targetBase.GetAnchor()} cellOffset {targetBase.cellOffset} active {targetBase.gameObject.activeInHierarchy}";
            string roomsText = $"{rooms.Length} MapRoomFunctionality under the base: {string.Join("; ", roomTexts)}";
            return string.Join("; ", ghostText, positionText, baseText, roomsText);
        }
        catch (Exception exception)
        {
            return $"(the lookup could not be described: {exception.GetType().Name} {exception.Message})";
        }
    }

    public static MapRoomEntity CreateMapRoomEntityFrom(MapRoomFunctionality mapRoomFunctionality, Base @base, NitroxId id, NitroxId parentId)
    {
        Int3 mapRoomCell = @base.NormalizeCell(@base.WorldToGrid(mapRoomFunctionality.transform.position));
        return new(id, parentId, mapRoomCell.ToDto());
    }

    // TODO: Use this for a latter singleplayer save converter
    public static List<GlobalRootEntity> GetGlobalRootChildren(Transform globalRoot, EntityMetadataManager entityMetadataManager)
    {
        List<GlobalRootEntity> entities = new();
        foreach (Transform child in globalRoot)
        {
            if (child.TryGetComponent(out Base @base))
            {
                entities.Add(BuildEntitySpawner.From(@base, entityMetadataManager));
            }
            else if (child.TryGetComponent(out Constructable constructable))
            {
                if (constructable is ConstructableBase constructableBase)
                {
                    entities.Add(GhostEntitySpawner.From(constructableBase));
                    continue;
                }
                entities.Add(ModuleEntitySpawner.From(constructable));
            }
        }
        return entities;
    }

    public static List<Entity> GetChildEntities(Base targetBase, NitroxId baseId, EntityMetadataManager entityMetadataManager)
    {
        List<Entity> childEntities = new();
        void AddChild(Entity childEntity)
        {
            // Making sure that childEntities are correctly parented
            childEntity.ParentId = baseId;
            childEntities.Add(childEntity);
        }

        foreach (Transform transform in targetBase.transform)
        {
            if (transform.TryGetComponent(out MapRoomFunctionality mapRoomFunctionality))
            {
                if (!mapRoomFunctionality.TryGetNitroxId(out NitroxId mapRoomId))
                {
                    continue;
                }
                AddChild(CreateMapRoomEntityFrom(mapRoomFunctionality, targetBase, mapRoomId, baseId));
            }
            else if (transform.TryGetComponent(out IBaseModule baseModule))
            {
                // IBaseModules without a NitroxEntity are related to BaseDeconstructable and are saved with their ghost
                if (!(baseModule as MonoBehaviour).GetComponent<NitroxEntity>())
                {
                    continue;
                }
                AddChild(InteriorPieceEntitySpawner.From(baseModule, entityMetadataManager));
            }
            else if (transform.TryGetComponent(out Constructable constructable))
            {
                if (constructable is ConstructableBase constructableBase)
                {
                    AddChild(GhostEntitySpawner.From(constructableBase));
                    continue;
                }
                AddChild(ModuleEntitySpawner.From(constructable));
            }
        }

        if (targetBase.TryGetComponent(out MoonpoolManager nitroxMoonpool))
        {
            nitroxMoonpool.GetSavedMoonpools().ForEach(AddChild);
        }

        return childEntities;
    }

    public static Component? AliveOrNull(this IBaseModule baseModule)
    {
        return (baseModule as Component).AliveOrNull();
    }

    public static void DeconstructionAllowed(NitroxId baseId, ref bool result, ref string reason)
    {
        if (BuildingHandler.Main.BasesCooldown.ContainsKey(baseId))
        {
            result = false;
            reason = Language.main.Get("Nitrox_ErrorRecentBuildUpdate");
        }
        else if (BuildingHandler.Main.EnsureTracker(baseId).IsDesynced() && NitroxPrefs.SafeBuilding.Value)
        {
            result = false;
            reason = Language.main.Get("Nitrox_ErrorDesyncDetected");
        }
    }
}
