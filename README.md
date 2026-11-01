# MultiNaughtica

A multiplayer (co-op) mod for **Subnautica**, for a few friends playing together. It is a **fork of [Nitrox](https://github.com/SubnauticaNitrox/Nitrox)** with extra features and changes on top.

> **This is not an official Nitrox release.** It is not made, endorsed or supported by the Nitrox team, and it has nothing to do with Unknown Worlds Entertainment, the makers of Subnautica. If you want stock Nitrox, get it from the [Nitrox project](https://github.com/SubnauticaNitrox/Nitrox).

**Shared as it is. There is no support, no promise of fixes and no promise to answer issues.** Please read "Things to know" below before you use it.

## What it adds to Nitrox

On top of Nitrox's multiplayer, this fork has (see `docs/coop` for the details of each):

**Vehicles and the Cyclops** (`docs/coop/VEHICLE_UPGRADES.md`)
- New vehicle modules: deeper depth modules for the Seamoth, the Prawn Suit and the Cyclops, a Seamoth afterburner, solar chargers, tow hooks, a Seamoth rider bar, and a Cyclops power share that charges nearby vehicles and draws emergency power from a docked one.
- Two extra module slots on every vehicle, and eight quick slots for the player instead of five (the keys 6, 7 and 8 can be rebound in the options).
- Cyclops crew stations, each for a second player: a navigator (terrain hologram, map console, marks and a sonar ping), a gunner (fires stasis spheres from the hull), an engineer (routes power, manages overheating and damage on a PDA page) and a scout drone you can fly from a drone bay. The author has tested these stations by hand in the game, but they are new: expect rough edges.
- A server setting for the numbers of all of these, in `server.cfg`.

**Teamwork**
- Quick ping: look at something and press the middle mouse button, and everybody sees a marker (`docs/coop/QUICK_PING.md`).
- Buddy oxygen: hold a key next to a teammate who is low on air to share yours (`docs/coop/BUDDY_OXYGEN.md`).
- Crafting from the lockers of a base or a Cyclops (`docs/coop/CRAFTING.md`).
- A line under every other player's name in the PDA's player list (health, food and water, distance, depth, vehicle), a "Saving..." indicator when the server saves, and a "Clear all" button for the PDA's databank badges.

**Player models and emotes** (`docs/coop/PLAYER_MODELS.md`)
- You can wear a custom 3D character that the other players see, and use an emote wheel (hold G). **No model is included**: you bring your own `.glb` file. See "Player models" below.

**Fixes to the synchronisation** of bases, vehicles, creatures, the story and other players' movement, found while playing in small groups. They are not complete, and some things are still wrong.

## What you need

- **Windows 10 or 11, 64 bit.**
- **Your own copy of Subnautica** (the first game, on Steam). Nothing from the game is included in this mod.
- **The .NET 10 runtime and the ASP.NET Core 10 runtime** (x64). The installer installs them for you if they are missing.
- Everybody who plays together must use **the same zip**. The game refuses a server of another build and tells you so.

## Install

1. Download the zip from the Releases page and unzip it anywhere (not into the Subnautica folder, not into `Program Files`).
2. Double-click **`Install.cmd`**. It installs the two runtimes if they are missing, finds Subnautica, sets everything up and asks for the server address your host gave you (Enter skips that).
3. Start **MultiNaughtica** from your desktop and press **PLAY MULTIPLAYER**. In the game choose **Multiplayer**, pick the server and join.

`docs/coop/INSTALL.md` (also in the zip) has the details for players and for the host, and what to do when something goes wrong. To remove it, use **Uninstall MultiNaughtica** from the Start menu; your worlds are kept.

## Hosting a game

1. In the launcher, open **Servers** and create a new server (world). Use a world made with this version.
2. The server listens on **UDP port 11000**. If your friends are not on your own network, either use a VPN such as Tailscale, or **forward UDP 11000** on your router to the PC that runs the server.
3. **Set a world password** (letters, digits and the underscore only) on the world's **Manage** page whenever the server can be reached from the internet.
4. Start the server, and give your friends your address.

## Player models

This version ships **no player models**. To use one, put your own `.glb` file (a humanoid with a Mixamo-style skeleton) into `%APPDATA%\Nitrox\models`, then choose it in the game under Options, Nitrox, **Model the others see**. The other players only see it if they have the same file. With no model installed, the list only shows **Default** and there is no emote wheel. `docs/coop/PLAYER_MODELS.md` explains what the file has to be, the profile file, tuning and emotes. Only use models you have the right to use.

## Things to know

- **No support.** This is shared as it is. Nobody is obliged to fix bugs or answer questions. You are welcome to fork it and change it.
- **Play only with people you trust.** The server relays what a client sends to the other clients and does not authenticate it, so a modified client can send things that harm other players or alter the world. Set a password and do not share it with strangers. See `SECURITY.md`.
- **Back up your worlds.** Saves are in `%APPDATA%\Nitrox\saves`. The server keeps its last backups, but a copy of your own is cheap.
- **Written mostly with AI tools.** Most of the code, tests and documentation of this fork were written with AI tools (Claude, from Anthropic), directed and checked by a person and play-tested in small groups. It has had far less play-testing than a mature mod.

## Building from source

You need the .NET 10 SDK and Windows. `dotnet build Nitrox.slnx -c Release` builds everything, `dotnet test Nitrox.Test/Nitrox.Test.csproj` runs the tests, and `docs/coop/tools/package_release.ps1` builds the release zip. This version is build 17 of the fork (`CoopBuild` in `Directory.Build.props`); it is part of the connection key, so two different builds cannot join each other.

## Credits and licence

MultiNaughtica is built on **[Nitrox](https://github.com/SubnauticaNitrox/Nitrox)**, the open source Subnautica multiplayer mod, and owes everything underneath to the Nitrox team and all of its contributors. Please support them. The changes in this fork are described in `NOTICE.md`.

It uses the same licence as Nitrox: the **GNU General Public License v3** (`LICENSE.txt`). The copyright notices of the original authors are kept as they are. Subnautica is a trademark of Unknown Worlds Entertainment; this project is not affiliated with them.
