using UnityEngine;

namespace NitroxClient.GameLogic.VehicleUpgrades;

/// <summary>
/// The material of the tow cable. A line renderer made in code has no material (it draws pink or nothing), and the game's own materials cannot be read offline, so it is found at run time: the material
/// the game itself gives a line renderer (the beam of the Mobile Vehicle Bay's constructor) when one is in the scene, else a simple built-in shader (the first of a list that the game's player build
/// contains). Which one won is logged once. When none is found there is no cable, the tow works all the same.
/// </summary>
public static class TowCableMaterial
{
    private static readonly string[] ShaderNames = ["Sprites/Default", "UI/Default", "Unlit/Color", "Hidden/Internal-Colored", "Legacy Shaders/Particles/Alpha Blended"];
    private const float RETRY_SECONDS = 30f;

    private static Material material;
    private static float nextAttempt;
    private static bool logged;

    public static Material Get()
    {
        if (material)
        {
            return material;
        }
        if (Time.time < nextAttempt)
        {
            return null;
        }
        nextAttempt = Time.time + RETRY_SECONDS;

        ConstructorInput constructor = Object.FindObjectOfType<ConstructorInput>();
        if (constructor && constructor.beamMaterial)
        {
            material = new Material(constructor.beamMaterial);
            Report("the beam of the constructor");
            return material;
        }

        foreach (string name in ShaderNames)
        {
            Shader shader = Shader.Find(name);
            if (shader)
            {
                material = new Material(shader) { color = new Color(0.82f, 0.78f, 0.62f, 1f) };
                Report(name);
                return material;
            }
        }
        if (!logged)
        {
            logged = true;
            Log.Warn("[VehicleUpgrades] tow: no material for the cable was found, the tow has no cable on screen");
        }
        return null;
    }

    private static void Report(string source)
    {
        Log.Info($"[VehicleUpgrades] tow: the cable is drawn with {source}");
    }
}
