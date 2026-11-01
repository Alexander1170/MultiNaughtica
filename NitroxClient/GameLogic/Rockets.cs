using System.Collections.Generic;
using NitroxClient.Communication.Abstract;
using NitroxClient.MonoBehaviours;
using Nitrox.Model.DataStructures;
using Nitrox.Model.DataStructures.Unity;
using Nitrox.Model.Subnautica.Packets;
using UnityEngine;

namespace NitroxClient.GameLogic
{
    public class Rockets
    {
        private readonly IPacketSender packetSender;
        private readonly Vehicles vehicles;
        private readonly PlayerManager playerManager;

        public Rockets(IPacketSender packetSender, Vehicles vehicles, PlayerManager playerManager)
        {
            this.packetSender = packetSender;
            this.vehicles = vehicles;
            this.playerManager = playerManager;
        }

        public void RequestRocketLaunch(Rocket rocket)
        {
            if (rocket.TryGetNitroxEntity(out NitroxEntity entity))
            {
                packetSender.Send(new RocketLaunch(entity.Id));
            }
            else
            {
                Log.Error($"{nameof(Rockets.RequestRocketLaunch)}: Can't find a NitroxEntity attached to the Rocket: {rocket.name}");
            }
        }

        /// <summary>
        /// The rocket only launches when every connected player is inside it (ST1i), so that everyone sees the ending. The same rule as in <see cref="RocketLaunch(GameObject)" />
        /// applied to every remote player's body, with a little slack because their position is a moment old.
        /// </summary>
        /// <returns>The names of the remote players who are not inside the rocket, empty if everyone is (or nobody else is connected)</returns>
        public List<string> GetPlayersNotAboard(GameObject rocketObject)
        {
            List<string> missing = [];
            GameObject sphereCenter = rocketObject.FindChild("AtmosphereVolume");
            if (!sphereCenter)
            {
                return missing;
            }

            NitroxVector3 center = sphereCenter.transform.position.ToDto();
            foreach (RemotePlayer player in playerManager.GetAll())
            {
                // A player whose body does not exist (yet, or any more) cannot be counted as inside; asking for its position would throw and the click would do nothing at all
                if (!player.Body)
                {
                    missing.Add(player.PlayerName);
                    continue;
                }

                Vector3 position = player.Body.transform.position;
                if (!RocketBoarding.IsAboard(position.ToDto(), Ocean.GetDepthOf(position) > 0f, player.SubRoot || player.Vehicle, center, RocketBoarding.REMOTE_SLACK))
                {
                    missing.Add(player.PlayerName);
                }
            }
            return missing;
        }

        /// <summary>
        /// Whether the local player is inside the rocket (the same sphere that the others are measured against, without slack)
        /// </summary>
        public static bool IsLocalPlayerAboard(GameObject rocketObject)
        {
            GameObject sphereCenter = rocketObject.FindChild("AtmosphereVolume");
            return sphereCenter && RocketBoarding.IsAboard(Player.main.transform.position.ToDto(), Player.main.IsUnderwater(), Player.main.currentSub, sphereCenter.transform.position.ToDto());
        }

        /// <summary>
        /// A launch that another player started (the packet)
        /// </summary>
        public void RocketLaunch(NitroxId rocketId)
        {
            // Avoid useless calculations
            if (LaunchRocket.launchStarted)
            {
                return;
            }

            // A player who is still loading does not have the rocket yet; his world has no launch to join
            if (!NitroxEntity.TryGetObjectFrom(rocketId, out GameObject rocketObject))
            {
                Log.Warn($"A rocket launch arrived for {rocketId}, which does not exist on this client (still loading?): ignored");
                return;
            }

            // The launcher counted everyone who is near the rocket as aboard (with the slack for a position that is a moment old): the same must apply to the local player,
            // or he gets no ending although the launcher believes everyone has one
            GameObject sphereCenter = rocketObject.FindChild("AtmosphereVolume");
            if (!sphereCenter || !RocketBoarding.IsAboardForReceivedLaunch(Player.main.transform.position.ToDto(), Player.main.IsUnderwater(), Player.main.currentSub, sphereCenter.transform.position.ToDto()))
            {
                Log.InGame(Language.main.Get("Nitrox_RocketLaunchMissed"));
                return;
            }

            RocketLaunch(rocketObject);
        }

        /// <summary>
        /// Starts the launch on this client (the launching player, who checked that everyone is inside, and a player that a received launch applies to)
        /// </summary>
        public void RocketLaunch(GameObject rocketObject)
        {
            LaunchRocket launchRocket = rocketObject.RequireComponentInChildren<LaunchRocket>(true);

            // Code extracted from LaunchRocket::OnHandClick
            LaunchRocket.SetLaunchStarted();
            PlayerTimeCapsule.main.Submit(null);
            launchRocket.StartCoroutine(launchRocket.StartEndCinematic());
            HandReticle.main.RequestCrosshairHide();

            // We also need to hide the other players
            foreach (RemotePlayer player in playerManager.GetAll())
            {
                // The model can be missing for a player who is half created or removed
                if (player.PlayerModel)
                {
                    player.PlayerModel.SetActive(false);
                }
            }

            Log.InGame(Language.main.Get("Nitrox_ThankForPlaying"));
        }
    }
}
