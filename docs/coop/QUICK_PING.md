# Quick ping

Look at something, press one key, and every teammate sees a marker there.

## The key

**The middle mouse button.** It is the key that stock Nitrox already uses for its player ping, and it is free in Subnautica (the game binds no action to the middle button). It works in every place the player can look from: swimming, walking in a base or in the Cyclops, at the helm of the Cyclops and piloting a Seamoth or a Prawn Suit (the ray goes from the camera, so you aim with the view).

To use another key: **Options, the Nitrox key bindings, "Ping what you look at"** (the label was missing from the language file before this build and showed as a bare code). The key does nothing while the PDA or a menu is open (the game's own rule for the actions of the avatar).

## What it marks

A ray from the camera, up to **300 m**. It skips your own body and tools, the vehicle you sit in and, at the helm of a Cyclops, the Cyclops itself (so that the ping reaches what is outside the windows); hits closer than half a metre count as your own gear.

| The ray meets | The ping says |
|---|---|
| a creature | its name ("Reaper Leviathan") |
| a resource, an item, a plant, a vehicle, a base piece the game knows by a name | that name ("Limestone Outcrop", "Seamoth") |
| a teammate | their player name |
| anything else (rock, sea floor, a plain wall) | "Look here" |
| nothing within 300 m | **no ping**: the error sound plays, nothing is sent (a marker in open water would hang in the air with nothing to explain it) |

## What teammates see

- The game's own HUD marker (the signal icon of the PDA pings), in the **colour of the player who pinged**, with the line **"Bob: Reaper Leviathan"** and the **distance** that the game adds. It sits half a metre off the surface that was hit.
- A short sonar sound when it appears.
- It lives **`QuickPingSeconds`** (default 20). A **new ping of the same player replaces the old one**, on every screen. The pinger sees their own marker at once.
- Pings are not saved and a player who joins later sees none.

## Rules the server keeps

- The server **stamps the sender**: the player and the name come from the connection, never from the packet, so a modified client cannot ping as somebody else.
- One ping per player every 1.8 s at most (the client keeps 2 s, so an honest client is never turned down); a flood from one client gets one ping through.
- The text is cleaned (no control characters, no angle brackets so that no markup can resize or recolour the line, at most 24 characters), and a position that is not a place in the world is dropped.

## Server settings (`server.cfg`)

| Setting | Default | Range | What it does |
|---|---|---|---|
| `QuickPingSeconds` | 20 | 5 to 120 | Seconds that a ping stays on everybody's HUD. A number outside the range is clamped to the nearest limit and a value that is not a number uses the default. It does not depend on `VehicleUpgrades`. |

## What changed from stock Nitrox

Stock Nitrox pings with the middle button too (`PlayerPingManager`, packet `PlayerPingCreated`). This build keeps the key and the marker and changes:

| Stock | This build |
|---|---|
| any client could send a ping as any player; the server relayed it as it came | the server stamps the sender, rate-limits and cleans |
| up to 3 pings per player, each for 10 s | one ping per player, `QuickPingSeconds` (default 20) |
| "Bob pinged " + a name made from the object (often a path or a code) | "Bob: " + a creature or item name, or "Look here" |
| 250 m, hits closer than 3 m ignored, own body not excluded | 300 m, own body and own vehicle skipped |
| a failed aim played the error sound | the same, and no ping |
| no label for the key in the options | "Ping what you look at" |

**Wire change:** `PlayerPingCreated` has a new field, `SenderName` (set by the server), and its `Text` is now only what was looked at, not the whole line. Both sides of a game must run the same build, as ever.

## Checks

- Unit tests: `QuickPingRulesTest` (names, lifetime, positions, the rate limiter, the option and its clamp), `PlayerPingCreatedProcessorTest` (stamp, cleaning, limit, drops) and `QuickPingDocsTest` (this page).
