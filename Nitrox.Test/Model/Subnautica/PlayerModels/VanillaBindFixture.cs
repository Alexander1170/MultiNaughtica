namespace Nitrox.Model.Subnautica.PlayerModels;

/// <summary>
/// The bind pose of the vanilla player body (the 64 bones of its biggest skinned mesh, in the frame of <c>player_view</c>, feet at y = 0), as the Debug command <c>vumodel dump</c> logged it in the live check of
/// 2026-10-29 (batch 20261029-110910), and the pose of the item attach point in the frame of the right hand. The numbers are rounded to three decimals. It lets the tests calibrate the pose copy without the game.
/// </summary>
internal static class VanillaBindFixture
{
    /// <summary>The pose of <c>attach1</c> relative to <c>hand_R</c> (a fixed child): local position and local rotation.</summary>
    public const string ATTACH = "pos=(0.096, 0.058, -0.156) rot=(0.817, -0.560, 0.111, -0.087)";

    /// <summary>One line per bone: the name, then <c>pos=(x, y, z) rot=(x, y, z, w)</c>.</summary>
    public const string BIND = """
        head_rig pos=(0.000, 1.615, 0.014) rot=(0.492, -0.508, -0.492, 0.508)
        Cam pos=(0.000, 1.641, 0.083) rot=(0.000, 1.000, 0.000, 0.000)
        neck pos=(0.000, 1.536, 0.004) rot=(0.547, -0.448, -0.547, 0.448)
        chest pos=(0.000, 1.502, 0.001) rot=(-0.500, 0.500, 0.500, -0.500)
        clav_L pos=(-0.026, 1.485, 0.046) rot=(0.000, -0.188, 0.000, 0.982)
        clav_L_aim pos=(-0.195, 1.430, -0.017) rot=(-0.036, 0.033, -0.002, 0.999)
        shoulder_L pos=(-0.195, 1.430, -0.017) rot=(-0.416, 0.638, 0.552, -0.339)
        shoulder_L_twist1_rig pos=(-0.235, 1.344, -0.020) rot=(-0.416, 0.638, 0.552, -0.339)
        elbow_L pos=(-0.315, 1.173, -0.025) rot=(-0.520, 0.840, -0.137, -0.067)
        elbow_L_twist_rig pos=(-0.360, 1.086, 0.001) rot=(-0.520, 0.840, -0.137, -0.067)
        hand_L pos=(-0.406, 1.000, 0.027) rot=(-0.087, 0.111, 0.560, 0.817)
        hand_L_pinky_base pos=(-0.425, 0.882, 0.007) rot=(-0.191, -0.106, 0.708, 0.672)
        hand_L_pinky_mid pos=(-0.424, 0.841, 0.013) rot=(-0.199, -0.091, 0.759, 0.614)
        hand_L_pinky_tip pos=(-0.420, 0.819, 0.017) rot=(-0.199, -0.091, 0.759, 0.614)
        hand_L_ring_base pos=(-0.438, 0.876, 0.029) rot=(-0.178, -0.025, 0.680, 0.711)
        hand_L_ring_mid pos=(-0.442, 0.831, 0.039) rot=(-0.180, -0.015, 0.718, 0.672)
        hand_L_ring_tip pos=(-0.441, 0.800, 0.047) rot=(-0.180, -0.007, 0.750, 0.637)
        hand_L_midl_base pos=(-0.445, 0.878, 0.055) rot=(-0.145, 0.037, 0.731, 0.666)
        hand_L_midl_mid pos=(-0.449, 0.833, 0.066) rot=(-0.144, 0.037, 0.733, 0.664)
        hand_L_midl_tip pos=(-0.447, 0.805, 0.073) rot=(-0.144, 0.037, 0.733, 0.664)
        hand_L_point_base pos=(-0.437, 0.888, 0.081) rot=(-0.042, 0.204, 0.631, 0.747)
        hand_L_point_mid pos=(-0.443, 0.847, 0.094) rot=(-0.023, 0.207, 0.695, 0.688)
        hand_L_point_tip pos=(-0.441, 0.820, 0.104) rot=(-0.023, 0.207, 0.695, 0.688)
        hand_L_thumb_base pos=(-0.402, 0.976, 0.065) rot=(0.355, 0.700, 0.290, 0.547)
        hand_L_thumb_mid pos=(-0.395, 0.939, 0.090) rot=(0.389, 0.682, 0.316, 0.533)
        hand_L_thumb_tip pos=(-0.389, 0.900, 0.112) rot=(0.430, 0.657, 0.349, 0.512)
        shoulder_L_twist2_rig pos=(-0.275, 1.259, -0.022) rot=(-0.416, 0.638, 0.552, -0.340)
        clav_R pos=(0.026, 1.485, 0.046) rot=(0.982, 0.000, -0.188, 0.000)
        clav_R_aim pos=(0.195, 1.430, -0.017) rot=(0.999, 0.002, 0.033, 0.036)
        shoulder_R pos=(0.195, 1.430, -0.017) rot=(-0.337, -0.548, 0.641, 0.419)
        shoulder_R_twist1_rig pos=(0.235, 1.344, -0.020) rot=(-0.337, -0.548, 0.641, 0.419)
        elbow_R pos=(0.315, 1.173, -0.025) rot=(-0.064, 0.142, 0.840, 0.520)
        elbow_R_twist_rig pos=(0.360, 1.086, 0.001) rot=(-0.086, 0.106, 0.845, 0.516)
        hand_R pos=(0.406, 1.000, 0.027) rot=(0.817, -0.560, 0.111, 0.087)
        hand_R_pinky_base pos=(0.425, 0.882, 0.007) rot=(-0.672, 0.708, 0.106, -0.191)
        hand_R_pinky_mid pos=(0.424, 0.841, 0.013) rot=(-0.614, 0.759, 0.091, -0.199)
        hand_R_pinky_tip_rig pos=(0.420, 0.819, 0.017) rot=(-0.614, 0.759, 0.091, -0.199)
        hand_R_ring_base pos=(0.438, 0.876, 0.029) rot=(0.711, -0.680, -0.025, 0.178)
        hand_R_ring_mid pos=(0.442, 0.831, 0.039) rot=(-0.672, 0.718, 0.015, -0.180)
        hand_R_ring_tip_rig pos=(0.440, 0.800, 0.046) rot=(-0.637, 0.750, 0.007, -0.180)
        hand_R_midl_base pos=(0.445, 0.878, 0.055) rot=(-0.666, 0.731, -0.037, -0.145)
        hand_R_midl_mid pos=(0.449, 0.833, 0.066) rot=(-0.664, 0.733, -0.037, -0.144)
        hand_R_midl_tip_rig pos=(0.447, 0.805, 0.073) rot=(-0.664, 0.733, -0.037, -0.144)
        hand_R_point_base pos=(0.437, 0.888, 0.081) rot=(0.747, -0.631, 0.204, 0.042)
        hand_R_point_mid pos=(0.443, 0.847, 0.094) rot=(-0.688, 0.695, -0.207, -0.023)
        hand_R_point_tip_rig pos=(0.442, 0.820, 0.101) rot=(-0.688, 0.695, -0.207, -0.023)
        hand_R_thumb_base pos=(0.402, 0.976, 0.065) rot=(0.547, -0.290, 0.700, -0.355)
        hand_R_thumb_mid pos=(0.395, 0.939, 0.090) rot=(0.533, -0.316, 0.682, -0.389)
        hand_R_thumb_tip_rig pos=(0.389, 0.900, 0.111) rot=(0.512, -0.349, 0.657, -0.430)
        shoulder_R_twist2_rig pos=(0.275, 1.258, -0.022) rot=(-0.337, -0.548, 0.641, 0.419)
        spine_3 pos=(0.000, 1.363, -0.010) rot=(0.019, 0.019, -0.707, 0.707)
        spine_2 pos=(0.000, 1.223, -0.004) rot=(-0.048, -0.048, -0.705, 0.705)
        spine_1 pos=(0.000, 1.088, 0.034) rot=(-0.073, -0.073, -0.703, 0.703)
        hips pos=(0.000, 0.984, 0.023) rot=(0.000, 0.000, -0.707, 0.707)
        thigh_L pos=(-0.072, 0.984, 0.027) rot=(0.631, 0.541, -0.386, -0.401)
        calf_L pos=(-0.123, 0.559, 0.050) rot=(0.592, -0.583, -0.432, 0.351)
        ankle_L pos=(-0.145, 0.137, 0.007) rot=(0.240, -0.797, -0.153, 0.532)
        toe_L_rig pos=(-0.208, 0.030, 0.160) rot=(0.000, -0.843, 0.000, 0.538)
        Flipper1_L pos=(-0.371, 0.009, 0.608) rot=(0.004, -0.823, -0.002, 0.567)
        thigh_R pos=(0.072, 0.984, 0.027) rot=(0.539, 0.630, 0.403, 0.387)
        calf_R pos=(0.123, 0.559, 0.050) rot=(0.591, 0.582, 0.433, 0.353)
        ankle_R pos=(0.145, 0.134, 0.007) rot=(0.235, 0.798, 0.150, 0.533)
        toe_R pos=(0.207, 0.031, 0.158) rot=(0.000, 0.843, 0.000, 0.538)
        Flipper1_R pos=(0.370, 0.016, 0.607) rot=(-0.004, 0.823, -0.002, 0.567)
        """;
}
