using UnityEngine;

namespace NitroxClient.MonoBehaviours;

/// <summary>
/// Put by <c>IncubatorEgg_HatchNow_Patch</c> on the real, networked Sea Emperor baby of the hatch that this player runs, so that <c>SeaEmperorBaby_Start_Patch</c> can tell it from a baby that
/// came from the server without looking at the hierarchy (<see cref="GameLogic.SeaEmperorBabyRules" />): the egg animation sends this one to the mother, nothing else must.
/// </summary>
public class HatchedHereBaby : MonoBehaviour
{
}
