namespace NitroxClient.GameLogic;

/// <summary>
/// Who gives the dock camera of a scanner room its battery (the design notes (not part of this repository) section 6b). Plain data, no game types.
/// </summary>
public static class MapRoomCameraBatteryRules
{
    /// <summary>
    /// The dock made the camera itself in <c>MapRoomCameraDocking.Start</c> (not a deserialized dock), the camera is not a Nitrox entity (no id), it has no battery and its prefab has
    /// a default battery: nobody else will ever give it one (the vanilla default spawn is blanked by <c>EnergyMixin_SpawnDefaultAsync_Patch</c> because the entity system spawns the
    /// batteries of entities). A camera with an id (picked up, dropped, crafted) gets its battery from the entity system and must not get a second one.
    /// </summary>
    public static bool ShouldInstallLocalDefault(bool dockWasDeserialized, bool cameraHasNitroxId, bool cameraHasBattery, bool cameraHasDefaultBattery)
    {
        return !dockWasDeserialized && !cameraHasNitroxId && !cameraHasBattery && cameraHasDefaultBattery;
    }

    /// <summary>
    /// The default battery was spawned, which takes at least one frame (the prefab load is a coroutine task): may it still be put into the slot? The dock can be destroyed meanwhile (a base piece
    /// deconstructed, the base rebuilt by a resync, the world quit) and takes its camera with it: a battery added to the slot of a destroyed camera is never parented or deactivated and stays
    /// loose at the world origin. Somebody else can also have filled the slot (a second install, the entity system) or made the camera an entity by giving it an id.
    /// </summary>
    public static bool CanAddSpawnedBattery(bool cameraAlive, bool batterySlotExists, bool slotHasBattery, bool cameraHasNitroxId)
    {
        return cameraAlive && batterySlotExists && !slotHasBattery && !cameraHasNitroxId;
    }

    /// <summary>
    /// A battery in the <c>EnergyMixin</c> of a camera without id is local to this client: it is not an entity and nothing about it is broadcast.
    /// </summary>
    public static bool IsLocalOnlyBattery(bool ownerIsMapRoomCamera, bool ownerHasNitroxId)
    {
        return ownerIsMapRoomCamera && !ownerHasNitroxId;
    }
}
