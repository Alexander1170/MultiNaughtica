# Notice of modification

**MultiNaughtica is a modified version of Nitrox.** It is not the original program, and it is not made or endorsed by the Nitrox team.

Nitrox is free software released under the GNU General Public License, version 3 (see `LICENSE.txt`). This modified version is released under the same licence. All the copyright notices and licence texts of the original are kept as they are, and the copyright of the original authors remains theirs. As the licence asks (section 5), this file states that the files were modified and when.

| | |
|---|---|
| Original program | Nitrox, https://github.com/SubnauticaNitrox/Nitrox |
| Based on | commit `7815b4e413aa690041ebf92210447fe1c95a21f8` of the `master` branch of Nitrox ("Add server loading progress display in launcher (#2593)", 2026-09-28) |
| Modified | between 2026-09-28 and 2026-10-31 |
| This version | build 17 of the fork (`CoopBuild` in `Directory.Build.props`, version `1.9.0.0-coop.17`), released 2026-10-31 |

## What was changed, in broad terms

- **Vehicle upgrades**: new modules for the Seamoth, the Prawn Suit and the Cyclops (depth modules, an afterburner, solar chargers, a power share, tow hooks, a rider bar), two extra module slots per vehicle, eight quick slots for the player, and the server settings for their numbers.
- **Cyclops crew stations**: a navigator console with a terrain hologram, a gunner station, an engineer console, and a scout drone with its bay.
- **Teamwork features**: quick ping, buddy oxygen, crafting from base and Cyclops lockers, player statistics in the PDA's player list, a saving indicator, and a button that clears the PDA's databank badges.
- **Player models and emotes**: a reader for glTF (`.glb`) models, the way a model follows the player's body, the hands, first-person view, an emote wheel, the choice of a model and its announcement to the other players, and shared emotes between models. No model file is included.
- **Synchronisation fixes** in many areas (bases, vehicles, creatures, the story, other players' movement, world generation and saving), and a repair of worlds that a server restart had damaged.
- **Connection key**: client and server only talk to a server of the same build of the fork (`CoopProtocol`).
- **Installer and packaging**: `Install.cmd` and its scripts, and `docs/coop/tools/package_release.ps1` which builds the release zip.
- **Tests and documentation** for the above, and a test harness that is compiled in Debug builds only.
- **Documentation and notices**: this file, a new `README.md`, `CONTRIBUTING.md` and `SECURITY.md` replace the ones of Nitrox.

The history of the files is not part of this repository; it starts with the commit that publishes this version. The history of the original files is in the Nitrox repository, up to the commit named above.

## No warranty

As the GPL says: there is no warranty for this program, to the extent permitted by law. See `LICENSE.txt`.
