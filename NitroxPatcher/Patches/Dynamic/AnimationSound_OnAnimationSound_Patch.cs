using System.Reflection;
using FMOD.Studio;
using UnityEngine;

namespace NitroxPatcher.Patches.Dynamic;

/// <summary>
/// #2549: docking a Seamoth in a Cyclops played its sound for every player on the map. The docking animation of the bay runs on every client (the remote vehicle is docked there
/// as well) and its <see cref="AnimationSound"/> events play 2D events (<c>event:/sub/seamoth/dock</c>), which have no distance: they are heard at full volume wherever they are triggered.
/// Vanilla only triggers them near the player. So an animation sound of a 2D event that is further away than the loudest 3D sound of the same animation (the bay doors, 70 m)
/// is dropped, unless the player is inside that very sub. Only sounds under a <see cref="SubRoot"/> (the docking bay of a Cyclops, the moonpool of a base) are touched: story cinematics and
/// scripted objects elsewhere in the world use <see cref="AnimationSound"/> too and must keep playing their 2D sounds wherever they are triggered.
/// </summary>
public sealed partial class AnimationSound_OnAnimationSound_Patch : NitroxPatch, IDynamicPatch
{
    private static readonly MethodInfo TARGET_METHOD = Reflect.Method((AnimationSound t) => t.OnAnimationSound(default(int)));

    private const float HEARING_DISTANCE = 70f;

    public static bool Prefix(AnimationSound __instance, int soundNum)
    {
        SubRoot sub = __instance.GetComponentInParent<SubRoot>();
        if (!sub || !Player.main || __instance.sounds == null || soundNum < 0 || soundNum >= __instance.sounds.Length)
        {
            return true;
        }

        if (Player.main.currentSub == sub || Vector3.Distance(Player.main.transform.position, __instance.transform.position) <= HEARING_DISTANCE)
        {
            return true;
        }

        FMODAsset asset = __instance.sounds[soundNum];
        if (!asset || string.IsNullOrEmpty(asset.path))
        {
            return true;
        }

        // A 3D event fades with the distance by itself
        if (FMODUWE.GetEventDescription(asset.path, out EventDescription description) == FMOD.RESULT.OK && description.is3D(out bool is3D) == FMOD.RESULT.OK && is3D)
        {
            return true;
        }

        return false;
    }
}
