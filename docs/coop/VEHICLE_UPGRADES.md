# Vehicle upgrades (co-op build)

New modules for the Seamoth, the Prawn Suit and the Cyclops (depth modules, an afterburner, solar chargers, a power share, tow hooks and a rider bar), and a server setting for each number. They come with the co-op build and need
nothing else: no mod loader, no extra files. Everything below can be changed by the host in `server.cfg`; players learn the numbers when they
join, and the tooltips of the modules show the real numbers of the server.

## How well this has been tested

- **Tested by hand in the game by the author:** the gunner station, the navigator's hologram with marks, routes and the full rotation of the map, the helm map and its buttons, the engineer's PDA screen, the scout drone (collisions, loading it and the fly screen, docking), the Seamoth rider bar, the quick slots 5 to 8, and the tow hook.
- **Checked in a live run in a survival world:** the solar plus at the Cyclops helm by day and by night, power sharing to a Seamoth, and the engineer's fire and breach marks.
- **Not checked in the game:** solar charging at depth. The rest of this page has unit tests and has been played less, or not at all; the sections say so where they know it.

## The modules

| Module | Vehicle | Crafted at | Recipe | Unlocks with |
| --- | --- | --- | --- | --- |
| Seamoth Depth Module MK4 | Seamoth | Modification Station | Seamoth Depth Module MK3, Plasteel Ingot, 3 Kyanite, 2 Nickel Ore | Seamoth Depth Module MK3 |
| Seamoth Depth Module MK5 | Seamoth | Modification Station | Seamoth Depth Module MK4, 2 Plasteel Ingot, 4 Kyanite, 2 Diamond | Seamoth Depth Module MK3 |
| Prawn Suit Depth Module MK3 | Prawn Suit | Modification Station | Prawn Suit Depth Module MK2, Plasteel Ingot, 3 Kyanite, 2 Nickel Ore | Prawn Suit Depth Module MK2 |
| Prawn Suit Depth Module MK4 | Prawn Suit | Modification Station | Prawn Suit Depth Module MK3, 2 Plasteel Ingot, 4 Kyanite, 2 Diamond | Prawn Suit Depth Module MK2 |
| Cyclops Depth Module MK4 | Cyclops | Modification Station | Cyclops Depth Module MK3, 2 Plasteel Ingot, 4 Kyanite, 3 Nickel Ore | Cyclops Depth Module MK3 |
| Cyclops Depth Module MK5 | Cyclops | Modification Station | Cyclops Depth Module MK4, 3 Plasteel Ingot, 5 Kyanite, 3 Diamond | Cyclops Depth Module MK3 |
| Seamoth Afterburner | Seamoth | Vehicle Upgrade Console, Seamoth tab | Advanced Wiring Kit, 2 Lubricant, 2 Crystalline Sulfur | The Seamoth |
| Seamoth Solar Charger | Seamoth | Vehicle Upgrade Console, Seamoth tab | the game's own (see below) | The Seamoth |
| Cyclops Solar Charger | Cyclops | Cyclops Fabricator | Advanced Wiring Kit, 2 Enameled Glass, 3 Quartz | The Cyclops |
| Cyclops Power Share | Cyclops | Cyclops Fabricator | Advanced Wiring Kit, Power Cell, 2 Copper Wire | The Cyclops |
| Seamoth Tow Hook | Seamoth | Vehicle Upgrade Console, Seamoth tab | Computer Chip, Lubricant, 3 Titanium | The Seamoth |
| Cyclops Tow Hook | Cyclops | Cyclops Fabricator | Advanced Wiring Kit, Plasteel Ingot, 2 Lubricant | The Cyclops |
| Seamoth Rider Bar | Seamoth | Vehicle Upgrade Console, Seamoth tab | 3 Titanium, Lubricant | The Seamoth |
| Cyclops Terrain Sonar | Cyclops | Cyclops Fabricator | Advanced Wiring Kit, 2 Computer Chip, 2 Enameled Glass | The Cyclops |
| Cyclops Gunner Station | Cyclops | Cyclops Fabricator | Advanced Wiring Kit, 2 Computer Chip, Plasteel Ingot | The Cyclops |
| Cyclops Engineer Console | Cyclops | Cyclops Fabricator | Advanced Wiring Kit, Computer Chip, 2 Lubricant | The Cyclops |
| Cyclops Drone Bay | Cyclops | Cyclops Fabricator | Advanced Wiring Kit, 2 Titanium, Lubricant | The Cyclops |
| Scout Drone | Cyclops | Cyclops Fabricator | Advanced Wiring Kit, Computer Chip, Battery, 2 Titanium | The Cyclops |

"Unlocks with" is the blueprint you must know before the new one is known; the PDA's blueprint list shows the new ones in the group of their fabricator once they are known. A world that already knows the blueprint (for example after the host switched the vehicle upgrades on for an old world) knows the new ones at the first join. With `UpgradesUnlockedFromStart=true` every blueprint is known from the
start. Creative mode shows every recipe, as always; Freedom mode needs the blueprints like Survival. Crafting from the lockers of a base or a Cyclops (see CRAFTING.md) works for these
recipes like for every other.

### Depth modules

The best installed depth module counts; they do not add up. A module of the game that is better than a new one still counts (the Seamoth's
reinforcement module gives 800 m of extra depth, 100 m more than the best depth module of the game, so a step below 100 m lets it beat MK4).

| Vehicle | Game's best module | New tier 1 | New tier 2 |
| --- | --- | --- | --- |
| Seamoth | MK3: 900 m | MK4: 1200 m | MK5: 1500 m |
| Prawn Suit | MK2: 1700 m | MK3: 2000 m | MK4: 2300 m |
| Cyclops | MK3: 1700 m | MK4: 2000 m | MK5: 2300 m |

These are the numbers with the default `ExtraDepthPerModule=300`: the game's best tier plus once and twice that step. The vehicle takes crush
damage below its crush depth, as always. Every player's depth display uses the same numbers because they come from the server.

### Seamoth Afterburner

A quick slot module (instant use). Put it in a Seamoth module slot and press the key of that slot while piloting:

* the Seamoth is **twice as fast for 5 seconds** (`AfterburnerSpeed`, `AfterburnerSeconds`);
* one use costs **10 energy**, 5 percent of a power cell (`AfterburnerEnergy`);
* afterwards the slot shows a cooldown of **15 seconds** (`AfterburnerCooldownSeconds`) before it can be used again;
* a press does nothing while it burns, while it recovers, and when the Seamoth has less energy than one use costs;
* the burn ends when you leave the Seamoth, when it docks, and when its power runs out; the cooldown starts then.

The other players see the faster Seamoth: its movement is what the game sends to them anyway.

The burn and the cooldown belong to the game of the player who flies: if two players take turns in the same Seamoth, each has a cooldown of
their own (the second player's afterburner is ready although the first player used it a moment ago). The cooldown also starts again from zero
after a rejoin.

### Solar chargers

Both charge like a base's solar panel: full at the surface at noon, less in the morning and the evening, nothing at night and nothing deeper than
about 200 m.

* **Seamoth Solar Charger** is the game's own module (the game has had it in its code and its crafting tree; the co-op build completes whatever of
  it was missing: recipe, name, icon, unlock). It adds **1 energy per second per module** at the surface at noon (`SolarChargeSeamoth`, 1 is
  how the game's module works). **Several modules add up**, like in the game. If the game's data already has a recipe, a name or an icon for it,
  those are kept; the recipe of the table is Advanced Wiring Kit, Enameled Glass, 2 Quartz.
* **Cyclops Solar Charger** is new. It adds **1.25 energy per second** to the Cyclops power cells (`SolarChargeCyclops`; for comparison the
  Cyclops Thermal Reactor Module adds up to 1.5 per second in hot water and a base's solar panel 1.25). **Only one counts.**

Only the player whose game simulates the vehicle charges it (the pilot, or whoever the server gave the vehicle to), so two players near a vehicle
never charge it twice, and both see the same charge. A vehicle that nobody is near does not charge, as with the game's other modules. The
Cyclops charger runs on the Cyclops itself, so it also charges while the player whose game simulates it is far away or looks elsewhere.

### Cyclops Power Share

A passive Cyclops module (put it in a slot of the Cyclops' upgrade console; **only one counts**). It shares power in two directions: it charges the vehicles around it, which needs no docking and has an **on/off button**, and it draws emergency power from the vehicle docked in it, which is automatic.

**The switch.** With the module installed, the lighting panel of the Cyclops (the panel by the helm with the buttons for the interior lights and the floodlights) gets a **Power share** button in its row,
next to the light buttons and in the same look, with the power cell on it. It is **off by default** and lit while the Cyclops shares. **Anybody aboard can press it**; pointing at it says "Power share: on" or
"Power share: off". The state belongs to the Cyclops: every player sees it lit or not, it is saved with the world like the state of the lights (so it survives a server restart, and a world from before the
switch loads with it off) and it is shown to a player who joins later. Without the module there is no button. The switch controls the charging of vehicles only; the emergency power stays automatic.
**A press changes only what it presses.** The lights and the switch travel in one object (the metadata of the lighting panel), and a client reports all of it from its own state, so a press on a light by a player whose
client had not yet got another player's press on the share button (or never would: an update for a Cyclops that is not spawned there is dropped) used to carry the old switch to the server, which stored it and turned the
share back off for everybody (and the other way round: a press on the share button carried old lights). A report now names the part its press changed (`CyclopsLightingMetadata.Changed`: floodlights, interior lights or
switch); the server takes that part from the report and the rest from itself, and a client whose view differed is told what the server kept (`CyclopsLightingMetadata.Merge`, `EntityMetadataUpdateProcessor.StoreLighting`).
The live checks are `vu2-sw-race-light` (Bob flips the share, Alice presses a light, the share stays on), `vu2-sw-race-stale` (Alice's client first forgets the switch, `vubutton forget`, then presses a light: the
share must stay on for both and Alice's client must show it again) and `vu2-sw-race-share` (the mirror: Alice's client forgets the interior lights, `vubutton forgetlights`, then presses the share button: the lights must
stay what the server has on both clients).

**Charging the vehicles.** While the Cyclops' switch is on, the Cyclops charges every Seamoth and Prawn Suit within range that is not full and not docked, at `PowerShareRate` energy per second *each* (a power cell holds 200). The range
(`PowerShareRange`) is measured from the hull of the Cyclops, not from its middle. A Cyclops that falls below its emergency threshold (or below 5 percent if that is higher) stops sharing, so charging
vehicles never leaves it without power. Switching the Cyclops off stops the charging at once. The pilot of a vehicle that is being charged sees **Charging from Cyclops** under the exit prompt.

**Emergency power.** When the power of the Cyclops falls below `EmergencyPowerThreshold` percent, it draws `EmergencyPowerRate` energy per second from the vehicle docked in it, down to
`EmergencyPowerReserve` percent of that vehicle's own power. The game's own docking charge of that vehicle (which would charge it from the Cyclops) waits for as long as the Cyclops is below the threshold or the charge would take it below it, and for three seconds after a draw. Whoever sits at the helm sees
**Emergency power from docked <vehicle>**. A threshold of 0, or a rate of 0, switches the emergency power off; a share rate of 0 switches the charging of vehicles off.

**How the energy gets from one to the other.** A vehicle's energy may only be changed by the player whose game simulates it (the pilot, or whoever the server gave the vehicle to), and the Cyclops and a
vehicle usually have different ones. The share is a request and a grant through the server: the one who simulates the *receiving* vehicle asks (about once a second, for the rate), the one who simulates
the *giving* vehicle takes the energy out first and says how much it was, and the receiver adds exactly that. What the receiver cannot use (it filled up on the way) goes back to the giver. When one player
simulates both nothing is sent at all. Energy is never made: the receiver only gets what the giver lost. It can be lost, never created: if the player who was to receive it leaves or the lock of the vehicle
moves while the energy is on its way, the server gives it back to the giver when it can (to whoever simulates the giver by then), and at the worst up to one and a half seconds of the share is lost.
The server keeps the books: it delivers the energy a giver took out once, to the player who simulates the receiver; that player may give back what it could not use, once per delivery and never more than was
delivered, and a return for anything else is ignored, so a modified game cannot make energy. A player can have only a handful of requests open at a time.

Creative mode has no energy to share, and with `VehicleUpgrades=false` the module does nothing.

### Tow hooks

Two modules, one tow each: a **Seamoth Tow Hook** (a quick slot module: switch it on to hook a vehicle, off to let go) and a **Cyclops Tow Hook** (a passive module: a button on the Cyclops' lighting panel). A Seamoth or a
Cyclops tows **one** Seamoth or Prawn Suit; the Cyclops does it **on top of the vehicle in its docking bay**.

* **Hooking.** The hook takes the nearest Seamoth or Prawn Suit that **nobody sits in**, is not docked and is not towed by someone else, within half of the longest cable (`TowMaxDistance`; 12.5 m with
  the default). The Seamoth's hook hooks when its quick slot is switched on and lets go when it is switched off; the slot stays lit while it tows. The Cyclops' hook is a **button on the lighting
  panel**, next to the power share button and in the same look as the light buttons (it is there while the Cyclops has the module). **Anybody aboard can press it**, not only the pilot: with nothing hooked a
  press hooks the nearest vehicle in reach, and while the Cyclops tows a press lets go. The button is lit while the Cyclops tows, on every player's screen, and pointing at it says what it does and what is
  hooked. The hooking itself is done by the player whose game simulates the Cyclops (the pilot, or whoever the server gave it to): the press of another player travels to that game through the server. If there
  is nothing to hook in reach, the vehicle is already in use by another player, nobody simulates the Cyclops or the server does not give the towing player the vehicle, nothing is hooked, the button stays dark
  and the player who pressed is told why. (There is no key for the Cyclops' hook any more.)
* **The cable.** Everybody sees a cable between the rear of the tower and the nose of the towed vehicle, also a player who joins later; it hangs slack and goes taut when the towed vehicle
  falls behind. The towed vehicle follows like a boat on a line: a spring pulls it toward the tower (like the Prawn Suit's grappling arm it ignores the mass), and the **tower is slowed** to
  `TowSpeedFactor` of its speed (70 percent with the default, an afterburner multiplies with it). The towing player's game simulates the towed vehicle, so the other players see it move like any
  other vehicle. The tower is warned when the towed vehicle is nearly (or already) below its crush depth.
  A vehicle that is far from the camera of the towing player (the game keeps a vehicle still, with nothing colliding with it, beyond about 37 m, which is the case at the stern of a Cyclops, 50 m from its helm) is
  **carried by the cable** instead: it follows the *path that the tower's rear anchor went along* (breadcrumbs half a metre apart, `TowTrail`), not the straight line to the tower, so that a bend of a cave is not cut through
  its wall; when the game lets it go again (it came near the player) the log says so if it stands inside the terrain.
* **It ends** when you let go, when somebody gets into the towed vehicle (they take it over), when **another player takes the Cyclops' helm** (the simulation of the Cyclops moves to them, so the tow
  ends by design; they press the button again, the tow is not handed over), when either vehicle docks or is destroyed, when the Seamoth's pilot gets out, when the cable is
  longer than the longest cable for more than a moment, when the hook is taken out of its module slot, when the server's vehicle upgrades are switched off and when the towing player leaves the game.
  An empty vehicle that is towed home and let go stays where it is.
* **Not saved.** A tow is never saved: after a server restart nothing is hooked.
* **Rescue.** A stranded player can ride the rider bar of the rescuer's Seamoth while their own empty Seamoth is towed home.

### Seamoth Rider Bar

A passive Seamoth module. It puts a **bar on the hull** of the Seamoth, which everybody sees. A swimming player looks at it and uses it (the hand text says "Hold on") to hold on and ride along;
**one rider per Seamoth** (the first wins; the others see "Somebody is holding on"). The rider "just rides along":

* **counts as inside the Seamoth**: no oxygen is used (the oxygen even fills up, as it does for a pilot), the heat of the water does not hurt, creatures do not target, bite or grab the rider
  separately (a Seamoth that is grabbed takes the rider with it), tools are put away, and the rider cannot steer or use anything of the Seamoth;
* **can look around freely** (the head turns about 140 degrees to each side; the view returns to the front while the PDA is open, which works as usual), and lets go with the **use key or the exit key**,
  which puts the rider beside the hull (or just behind the bar if the search for a spot beside it would end inside the hull);
* **rides when the pilot gets out**: the Seamoth with its rider stays where it is;
* **is let go** when the Seamoth starts to dock or is destroyed, when the bar is taken out, when the vehicle upgrades are switched off, when something else takes the player out of that mode (death) and when the
  rider leaves the game. A **teleport** (an admin's, the console's `goto` and `warpforward`) lets the rider go first and then moves them, so the teleport is not undone.

A rider can hold on while still near their own lifepod (the game keeps the lifepod as the "inside" of a swimmer until they are 15 m away). A player who is already inside a base or a Cyclops, or who flies a vehicle,
cannot. The server also checks that the Seamoth really has the bar installed (as it knows the Seamoth's modules), so a modified game cannot hold on to a bar that is not there. A late answer of the server (more than 15 seconds old, or when the player has swum more than 6 m away) is given back instead of snapping the player onto the Seamoth. When holding on is denied the game says
"somebody holds on already" only if somebody does. A click on the hull of a Seamoth that somebody else flies is refused ("Another player is interacting with that object"); that text shows only while the hand points at the hull itself,
so the bar, which has its own text, is not covered by it.

The rider's body is a child of the bar's anchor (a sibling of the bar, directly under the Seamoth) on every computer, so it moves with the Seamoth exactly, at any speed, however the Seamoth is drawn there; only where
the rider looks is sent (about 15 times a second while it changes; the others see the rider's head follow it, mainly up and down). The other players see the rider swimming with the hands on the bar. The bar itself is built at run time from the hull's own material; where it sits on the hull
(across the rear, in the upper half) is a first guess that the live check adjusts.

### Cyclops Terrain Sonar and the navigator console (feature group 3, station 1)

A passive Cyclops module (**only one counts**) that turns the cockpit into a place for two people: the pilot and a navigator. **Built (milestones 1 to 5): the helm hologram, the console with its map and icons, the marks, the route, the clearance warnings and the sonar ping. Tested by hand in the game: the hologram with the marks and the routes, the full rotation of the map, and the helm map with its buttons. The clearance warnings and the sonar ping have not been confirmed in the game.**
What the first live probes showed, and what has not been confirmed, is listed at the end of this section.

* **The hologram (milestone 1; in the build room since the play-test of coop.12).** The terrain around the Cyclops, within `TerrainSonarRange` metres (200 with the default, more while the engineer sends the power
  to the systems), as the game's own terrain hologram (the one of the scanner room and of the Seaglide's map), **floats in the middle of the build room**: the room between the helm and the vehicle bay ("Build room" on the
  engineer's map; read from the Cyclops scene's collision boxes: x -2.88 to 2.59, y -0.79 to 2.20, z -15.41 to -11.29 in the hull's frame). It hangs at waist height, a disc about a metre across tilted towards whoever looks
  at it, with the Cyclops in its middle. **It keeps the real world's directions**: what is away from you in the room is away from you on the map, and only its tilt follows you as you walk round it. It is at the scale of the
  creatures' pings: the walls of a cave system are where they are relative to the hull. It is the game's low detail map of the world, not the walls as the cameras see them. It is shown while **you are aboard**, near it (within
  9 m, kept until 12 m: the terrain it draws is freed beyond that) and not at the helm, and the Cyclops has the module; it is not synced: everybody's game draws the same static terrain for itself. A player who is not
  in the Cyclops pays nothing for it. **It has no collider**, only a trigger for the hand on the hologram's own volume: the walking, the hatches, the ladder and building in the room are as they were, and the player walks
  through it. The walking line from the helm to the vehicle bay runs through the middle of the room, where the hologram is; `NavigatorConsoleLayout.HologramCenter` is the one constant that moves it (for instance to the starboard
  side, to keep the aisle clear). The display at the helm is the game's own again: the first builds put the terrain on it, which was next to the pilot's hands. The Terrain Sonar no longer switches that display on.
* **The size of the hologram (play-test of coop.12).** The player makes it bigger or smaller, separately from the zoom of the map (which changes how much of the world the disc shows): **Shift and the wheel**, the **+** and
  **-** keys, or the **Smaller**, **Bigger** and **First size** buttons of the console, 10 percent a notch, from 0.15 m to 1.95 m radius (0.3 to 3.9 m across; it starts at 1.4 m, 2.8 m across, in a room that is 4.12 m long between
  its bulkheads). **The game fades the terrain by its distance in all three directions from the middle, so the hologram's volume is a sphere**, not a flat disc with low relief: terrain 100 m below the Cyclops shows 0.7 m below the
  plane at the first size. A small hologram hangs at waist height (1 m above the floor) and rises as it grows (`NavigatorConsoleLayout.CenterY`) until its sphere fills the room's height (1.445 m radius, 2.9 m across); a bigger one is cut
  by the floor and the ceiling, which hide what is beyond them, with the middle at half the room's height. It never reaches a wall or a bulkhead (unit-tested). The frame that the hologram hangs on is scaled, so the terrain and the
  icons follow, and the shader's fade is moved with them (`TerrainHologram.ApplyAnchorScale`): the terrain is not built again. **The size is the player's own, kept on their machine** (`PlayerPrefs`, not synced and not in the world save:
  each client draws the hologram from its own terrain, and a shared size would be a change to the saved Cyclops data); it is there again the next time. The hand finds a thin slab on the disc, not a sphere (a big sphere would have the
  player's eyes inside it, and a ray that starts inside a collider does not find it).
* **Marks and route points are on the terrain they are clicked on.** A click used to go to the flat reference plane of the hologram, which is the Cyclops' own depth, so the pins sat on it. Now it looks for the terrain under the
  cursor (`TerrainHologram.TryRaycastRelief`: a ray against the chunk meshes of the hologram, which the game loads readable, Moeller-Trumbore on the CPU) and brings that point back to the world with the full three-dimensional mapping
  (`HologramMath.ToWorld`, unit-tested with a turned, tilted and scaled hologram on a turned hull). **The pick is made on the terrain as it is drawn this frame** (`TerrainHologramAligner.SyncHolder`): the game's `MiniWorld` sets the
  terrain holder's turn to the identity in every `Update` and the aligner restores it in `LateUpdate`, so a click handled after the game's met the terrain unturned and was taken back to the world with the real turn (the play-test of Build 3c: picks
  near the middle of the map were right, picks near its rim one to two chunks off, and the same point of the hologram gave places 140 m apart in the same chunk). Every pick's log line ends with a check of the chunk it hit (the cube in its name:
  128 m cells in the Voxeland's frame, origin (-2048, -3040, -2048)) against the cell of the world place it came out at: "chunk check: ok", "next to the edge" or "MISMATCH" (`TerrainHologram.CheckPickedChunk`).
  **Only the part of the ray inside the sphere that the shader draws counts** (`HologramMath.RaySphere`): the chunks reach beyond it, their
  triangles out there are not drawn, and a pick that took the first triangle of the chunks put marks in empty water in front of what the player saw. A **mark** is on the terrain. A **route point** is stored, drawn on the hologram and sent to
  everybody where it was clicked, on the terrain like a mark; **the helm aims above it** (`RouteClearance`, `NavWaypoint.AimPosition`, used by `RouteGuide` for the arrow, the distance, the "reached" check and the line in the water): half the
  depth where the water is shallow, at most 20 m, at least 1 m, never above the surface. **Alt+click**, or a click whose ray crosses no terrain, puts the point at the Cyclops' own depth: it is stored that much lower
  (`RouteClearance.PinYFor`, the inverse), so that the helm aims at the depth that was clicked. Every pick writes one line to the log: hit or miss, the chunk, the point on the hologram and in the world, the depth, and why a click fell back
  to the Cyclops' depth. The message says what was done ("Route point 3 (floor at 143 m, the helm aims 20 m above it at 123 m)").
* **The projector.** A disc and a faint cone of light stand on the floor under the hologram, copied from the game's own little hologram at the helm (`SonarMap_Small`: mesh, materials and light, no scripts and no collider; `CyclopsTerrainSonar.BuildProjector`),
  at the same depth in the floor. They show while the hologram does.
* **The hologram waits for the join.** Nothing of it is set up, not even its frame, until the join is finished, the world has settled and no loading screen or intro shows (`HologramStartRules`, the log says what it waits for and when it
  starts); it latches once open, so terrain that loads while the Cyclops travels cannot switch it off. In the play-test of Build 3 the world did not settle for 60 s at a join and the screen stayed black; the log showed that no hologram had been
  built then, so this is a precaution, and the wait for the world now logs which part of `LargeWorldStreamer.IsWorldSettled` is false (`[WorldSettle] still waiting ...`).
* **The helm's copy of the map.** With the Terrain Sonar module the helm keeps a map of its own for the pilot who likes to see it while driving alone (`CyclopsHelmMap`, `HelmLayout`): a second hologram of the same kind hangs where the game's own helm map hung
  (`SonarMap_Small` of the Cyclops scene: 1.3 m to the pilot's right of the eyes, 0.6 m ahead and 0.4 m below them; an earlier build put it at the crosshair, straight ahead, and it was moved back to where it was), 15 percent smaller than the helm display's
  was (radius 1.06 m: the old one was 1.25 m for the display's 130 m), tilted towards the seat, for at most 130 m. It shows the marks and the route (`HelmMarkLayer` bound to it) and the contacts (`HelmContactLayer`: the Cyclops at the middle, the
  teammates, vehicles, creatures that the map warns about, beacons, bases and lifepods, with the console's shapes and colours). It does not follow the navigator's zoom and turn: those are the navigator's view on the navigator's machine, and
  another player's helm would need them sent (a small packet, not built). It is shown while the player is aboard and within 9 m of it (kept to 12 m).
* **The helm HUD: a map button, a power share button and a solar "+" (`CyclopsHelmHud`).** In the HUD's right-hand column (camera, silent running, decoy, and the sonar and the shield while those modules are in) the rows that the sonar and the
  shield do not use take **a map button** (while the Cyclops has the Terrain Sonar), which switches the pilot's own copy of the map on and off for them alone, remembered on their machine, and **a power share button** under it (while it has the
  Power Share module), lit while sharing, which drives the same synced and saved switch as the lighting panel's button (`PowerShareSwitch.Press`), so both stay in step on every client. They are copies of the sonar button with the game's
  scripts removed, the way the panel's extra buttons are made, with a picture drawn in code (`HelmIcons`: the game's buttons are single sprites with the icon baked in). Next to the power bolt a small **"+"** shows while the Cyclops Solar
  Charger is really adding power (`VehicleSolar.IsCyclopsCharging`: the module, depth, daylight and room in the cells, worked out by every client from what every client knows, so everybody aboard sees the same).
* **The navigator console (milestone 2).** The console is the hologram itself: look at it from where you stand and the hand says
  **"Use navigator console"**; click it. You then stay standing where you are, so that the others see you there, the camera turns to the hologram, the cursor is free, walking and looking are locked (the game's input
  group, as the PDA and the chat use it), and the hologram is yours to work with: its tilt faces you and the map opens with what is away from you away. **Drag** with the left mouse button
  to turn it any way, with no limit and nothing that springs back: across spins it about its own axis for as many turns as you drag, up and down rolls it over the top and under, from looking straight down at the terrain
  (the map facing you) to looking across it (the map flat, seen from the side) and on to its underside, so every view of the terrain from every side can be had. Dragging right always turns the map clockwise as you see it,
  whichever side of it faces you. **The map stays where you left it:** sitting down at the console and getting up again do nothing to its turn, roll or zoom, so the next one who walks up finds it as it was left (the tilt still swings round to face whoever stands in front of it), and the view is kept on your machine for that Cyclops, so it is there after a restart too. It is your own view and is not sent to the others: each player's map is turned as that player turned it on their machine. Only **R** or the Reset view button puts it back. The **wheel** zooms in (the whole range is the widest view, up to eight times closer), **Shift and the wheel** (or **+** and **-**) make it bigger or smaller,
  **R** resets the view, **Shift+R** the size, **T** also shows the small creatures, and the exit key,
  Escape or the **Leave** button leaves (it also closes by itself when you die, are moved away, leave the Cyclops, or the module is taken out). The icons are drawn on the screen over the hologram and follow it:
  the Cyclops' outline, teammates who swim or walk outside (an arrow in their colour), vehicles (a diamond, in the colour of the teammate who sits in it), **leviathans in red** (Reaper, Ghost and its juvenile, Sea Dragon, the
  Sea Emperor family), **other dangers in orange** (Warper, Crabsquid, Ampeel, Bone Shark, Spine Eel, Crabsnake, Lava Lizard; the **T** key adds the small ones, Stalker, Sandshark, Mesmer, Bleeder, Crashfish and the like), beacons (a yellow ring), other
  Cyclopses, lifepods and bases (a square). Creatures, teammates and vehicles beyond the rim are kept on the rim, smaller and fainter, so that a leviathan just outside the picture is not lost; the cursor on an icon
  names it with its distance and how far above or below you it is. The list is the game's own ping list (vehicles, Cyclopses, beacons, lifepods) plus the bases and the dangerous creatures; **a creature is only
  known to a client that has loaded the place it is in**, so the map shows the dangers that are near enough to be loaded, not all of them. The console itself is not synced: it is a view, and
  more than one player may have it open.
* **Marks (milestone 3).** **Click** on the map (without moving the mouse) and a mark is placed there, **for everybody**: keys **1**, **2** and **3** choose what the next one is (a plain point in cyan, a hazard in
  orange, a target in yellow), **Delete** removes the mark under the cursor, and the **Clear marks** button takes them all away; the crew shares the map, so anybody can remove any mark. A mark is a place on the
  Cyclops' own depth (the map is a plane through the hull), named after the player and numbered ("Alice: Mark 3"); at most 24 per Cyclops. Everybody **aboard** that Cyclops, the pilot included, sees each mark as
  one of the game's own pings on the HUD, with its icon and its distance (the game fades the text of a ping unless you look towards it), the marks are drawn as pins on the console map and on the
  hologram in the build room (beyond the range they stay on the rim, smaller), and a player who joins later is told about the marks that are on. **The marks belong to the Cyclops, not to the player, and are not saved with the
  world**: they stay when the player who placed them leaves, they are gone when the Cyclops is destroyed or the server restarts (like a tow). With `VehicleUpgrades=false` the server refuses them. 
* **The route (milestone 4).** A **right click** on the map appends a **route point** (a waypoint) to the route of the Cyclops, **Backspace** takes the last one out again, the **Clear route** button takes them all away; a
  route has at most 16 points, and a point must be 5 m from the last (a double click adds one). The route is on the map as numbered rings joined by lines (the legs already flown in grey, the one that is being
  flown bright), on the hologram as a line from the Cyclops through the points still to go to, and **for everybody aboard** while the route is on: the next point as a ping on the HUD (the game's edge arrow when it
  is off the screen, its distance), a **line in the water** from the bow to the next two points (a halo round a bright core that grows with the distance, so that it can be seen at 300 m), and at the top of the screen
  an arrow that points to the next point with "Route 2/4   340 m   ETA 1:12" and how many degrees to turn, to port or starboard. The line, the arrow and the numbers turn **orange when the Cyclops is off course**:
  more than 35 m from the line between the last point and the next, or moving more than 70 degrees away from the way to it (back on course only below 70 percent of those limits, so that it does not flicker). **A
  point is reached when the hull (its box, not its middle: the Cyclops is 54 m long) is within 12 m of it**, and a point the Cyclops slid past without coming within reach also advances the route; the route
  then goes on to the next point, and a route whose last point is reached is finished and gone from the guidance (the points stay on the map until they are cleared). **The client of the player who holds the Cyclops
  decides that a point is reached, so that the clients cannot disagree**, and the server only lets that player advance the route, forward and by one, **naming the point that was reached** (an advance that was decided before a removal or a clear reached the server names a point that
  is not next any more and is refused, and the pilot is told where the route really is, instead of a point being skipped). **The server's order is the route's order:** every point is relayed with its slot, a removal with
  the next point that results, so that two navigators who add at the same moment end up with the same route. Like the marks, the route belongs to the Cyclops, is not
  saved with the world, and is gone with the Cyclops or a restart; a player who joins later is told the route and which point is next. The line in the water is drawn with the game's own sprite shader, which the
  tow cable showed to be there; **how it looks under water at a distance has not been checked separately**.
* **Clearance warnings (milestone 5).** While you are aboard, five times a second the Cyclops casts spheres (1 m) from the faces of its hull's box at the terrain's colliders: port, starboard, above and below, at five
  places along the keel, as far as `ClearanceWarningDistance` (20 m by default, 5 to 60), and three more ahead along the way it moves (astern while it reverses). **Ahead the warning distance grows with the speed:** it is five seconds of travel (never less than the
  setting, at most 100 m), and the red line is 35 percent of it, so the faster the Cyclops goes the earlier a wall is reported; a wall at the side of the nose counts as a side, not as ahead. The tightest gap, in proportion to the
  distance it is measured against, is shown as **"Clearance 4 m, port"** (or "Obstacle ahead, 12 m") at the top of the screen of everybody aboard, orange when it is below the distance and red below 35 percent of it, and at the console under the
  title; when it gets worse it is also said in the game's message list, no more often than every four seconds. Every client computes its own; nothing is synced.
  **The game only builds the terrain's colliders near the local player's camera**, so terrain farther from the player than that (the far end of a 54 m hull) is not seen, and "no warning" means "no
  collider found", not "free". The scan's cost is logged every ten seconds while it runs.
* **The sonar ping from the console (milestone 5).** The key **P** (or the **Sonar ping** button) fires the Cyclops' own sonar ping: the wave on the screen of everybody aboard, the sound of the sonar button and
  the energy that the game's ping costs. **The Cyclops needs the Cyclops Sonar Module** (the console does not make a sonar of its own: the Terrain Sonar only shows terrain) and power; five seconds between two pings.
  The energy of a Cyclops is only changed by the client that simulates it (the pilot's), so a navigator who is not the pilot asks that client through the server and is told if it did not work ("The ping needs
  the Cyclops Sonar Module", "The Cyclops has not the power for a ping", "Nobody is running the Cyclops"); a navigator who is the pilot does it at once.
* **What the live probes have shown so far (2026-10-28):** the display at the helm is lit by the Terrain Sonar alone, the game's mesh unit measured 3.87 m (128 m for the largest chunk mesh of 33.1 units, a size that may include the chunk's skirt; the code uses 4.0 m until a picture of the helm shows the terrain off by a few percent, and `vuhelm scale` corrects it), the helm hologram costs nothing measurable at 200 m
  and about 0.2 to 1 ms at 500 m, marks reach the other player through the server and have their HUD pings on both sides, the console opens and draws its overlay. **Tested by hand since then:** the hologram with its icons, the marks and the routes, the full rotation of the map, and the helm map with its buttons. **Not confirmed in the game:** the clearance warnings and the sonar ping.
* **Off.** With `VehicleUpgrades=false` the module does nothing and the display is the game's.

### Cyclops Gunner Station (feature group 3, station 2)

A passive Cyclops module (**only one counts**) that turns the **right** desk of the cockpit (the pilot's right, the hull's starboard side, the one with the sub-name screen and the lighting panel) into a **gunner
station**: a second player looks through a turret on the hull and fires **stasis spheres** at what threatens the Cyclops. **The gunner station has been tested by hand in the game; the details below were written before that test.**
The list at the end of this section is what the first test had to confirm.

* **The desk.** Look at the middle of the right desk (between the sub-name screen and the lighting panel's buttons) and use it: "Use gunner station". Under the text stands the name of the player who is at the desk
  already, if somebody is. **One gunner per Cyclops, the first to ask gets the seat** (the server decides; a player who joins later is told who sits where). The seat is given up when the gunner leaves the desk,
  leaves the Cyclops, dies, disconnects, is moved away, or the Cyclops is destroyed or the module taken out. The desk and the navigator's console are two different consoles: one player has one open at a time.
* **The turret.** While you sit, you stay standing at the desk (everybody else sees you there, as at the navigator's console), the walking and the looking of your character are locked, and **your own camera is moved
  to the turret**: a mount above the conning tower, 9.9 m over the keel line, from where the view is all round. The mouse turns the turret (right = to starboard, up = up); it cannot point further down than the hull
  allows (about 14 degrees below the horizon straight ahead and 13 astern, about 50 to 58 abeam: the real limits are scanned from the hull's colliders when the console opens and logged), nor more than 75 degrees up.
  **R** turns it back to the bow. The exit key (**E**), Escape or the Leave button put your camera back and give the seat up. A crosshair, the charge, the cooldown, the Cyclops' power, the turret's angles and
  the distance to what a shot would hit first (with its name) are on the screen.
* **The shot.** **Hold** a mouse button to charge (three seconds for a full charge, like the Stasis Rifle; a full charge fires by itself and the button has to be let go before the next), **release** to fire.
  A release with under a tenth of a charge is a click, not a shot. What is held in your hands is put away while you sit. A shot costs `GunnerShotEnergy` energy of the Cyclops for a full charge (a partial charge
  takes the same part of it), the next one waits `GunnerCooldownSeconds` from the release, and the sphere flies `GunnerRange` metres: it slows down on the way, so it is shot at 2/3 of the range per second (at
  least the rifle's 25 m/s, at most 60) for the time that makes it reach that far (the rifle's own sphere reaches 37.5 m).
* **How a shot goes.** The gunner asks first: the server checks that the sender sits at the desk of a live Cyclops and that the Cyclops did not fire a moment ago (the server keeps 80 percent of the cooldown, and at
  least a quarter of a second, so that a client that does not keep its own cooldown cannot make the others pay as often as it likes), then passes the shot to **the player whose client simulates the Cyclops**
  (the pilot, mostly): only that client changes the Cyclops' energy, as with the sonar ping. That client checks the module and the power (**in creative mode nothing is paid and the shot is free**; in survival it looks
  at the energy first, because a drain of more than there is takes what is left and fails), takes the energy, and answers; the server tells the gunner alone, and **only when the answer is "paid"** the gunner's
  client launches the sphere from the mount along the aim of the release (the Cyclops may have moved since; so may the mouse). The flight of the sphere and what it freezes travel by the packets of the Stasis Rifle
  (`StasisSphereShot` and `StasisSphereHit`), unchanged: everybody sees the sphere, the field and the frozen creatures. Messages: "The Cyclops has not the power for a shot", "The turret needs a moment",
  "Nobody is running the Cyclops: the gunner cannot fire" (nobody holds the Cyclops' simulation: somebody has to be at the helm or near), "The Cyclops did not answer: no shot" (three seconds).
* **Freezing.** The sphere freezes what it hits for 6 to 30 seconds in a field of 1 to 10 metres, by the charge, as the rifle's does. **A stasis field never freezes a Cyclops, nor the vehicle that the player flies**
  (a frozen Cyclops would stop the pilot's boat; the replay of the gunner's sphere on the pilot's client would freeze exactly that). The sphere flies **through its own Cyclops**: the hull is wrapped in trigger
  volumes (the moonpool's box round the whole hull is 9 by 14 by 53 m) that the game's sphere would explode on, a few metres from the mount. Like the rifle's, the gunner's sphere holds **one field at a time**: a new
  shot ends the field of the last one, on every client; the other players replay one sphere per shooter, so a shot of the gunner's hand rifle ends the turret's field for them too (on the gunner's own screen the two are two spheres, and the turret ends the rifle's field before it fires, not the other way round). The sphere is kept for the whole session (a sphere that is destroyed while its field runs would leave what it
  froze frozen), and ended properly when the session ends.
* **Ammo.** Only the stasis sphere exists. The packet carries the kind of ammo (`CyclopsGunnerAmmo`), so that a second kind (decoys) can be added without changing it; **it is not built.**
* **Not a security boundary.** The seat and the cooldown stop an honest client from firing unpaid shots; the flight of the sphere is relayed like the rifle's, unchecked, as the Stasis Rifle's always was.
* **What stays as it was.** The desk is a different object from the navigator's: the route line, the route arrow and the clearance text step aside while either console is open. The lighting panel's buttons
  (the lights, the Power Share and the Tow Hook) are not touched: the hand target keeps clear of them. **No button was added to the lighting panel.**
* **What the first test had to show (written before the test; the gunner station has since been tested by hand in the game):** the hand target found by the eye from where a player stands; the camera at the mount (the look of the water, the fog and the surface from
  the tower; the pose sync keeping the gunner at the desk for the other client); the look delta under the input group with the cursor locked; the scanned horizon against the table; the sphere leaving the mount
  and hitting something below it (the trigger boxes); the payment (survival), the free shot (creative), the cooldown, the refusals; the other client replaying the shot (a counter and a log line say so); the seat
  ending on disconnect, death and a destroyed Cyclops; a creature that moves again when the field ends or the gunner leaves.
* **Off.** With `VehicleUpgrades=false` the module does nothing and no desk answers.

### Cyclops Engineer Console (feature group 3, station 3)

A passive Cyclops module (**only one counts**) that puts a **panel on the engine in the engine room** and makes it the **engineer's console**: a second player runs the power of the Cyclops, keeps the engine from
overheating and watches the damage. **The engineer's PDA screen has been tested by hand in the game, and the fire and breach marks were checked in a live survival-world run. The rest (the effect of the routes on speed, the heat steps, the overdrive, the vent, the noise and the emergency line with a docked vehicle) is code that has been tested in pieces and has not been confirmed in the game.** The list at the end of this section is what the first test had to confirm.

* **The panel.** The engine room's own console (the one with the screen, next to the engine) is the game's **Upgrade Console**, where the modules are installed, and stays that: the engineer's console is a small panel
  that the mod builds on the same face of the engine, about two metres aft of it, facing the walkway on the port side ("ENGINEER", the route that is set, who sits there). Look at it from the walkway and use it:
  "Use engineer console", with the name of the engineer under it if somebody sits there already. **One engineer per Cyclops, the first to ask gets the seat** (the server decides; a player who joins later is told who
  sits where). The seat is given up when the engineer leaves the console, leaves the Cyclops, dies, disconnects, is moved away, or the Cyclops is destroyed (the server ends it); when the module is taken out
  the engineer's client gives it up. The three consoles (navigator, gunner, engineer) are different objects: a client keeps one open at a time (the server keeps a seat table for each station and does not check across them). While you sit you stay standing in the engine room (everybody else sees you there), and **the game's PDA opens on a page of
  its own** (no toolbar): the picture of the Cyclops, with round bubbles in the look of the game's equipment slots on it (below). The PDA does what it always does: the cursor is free, Escape, the PDA key or the exit key
  (**E**) close it and give the seat up, and it closes by itself when you walk more than five metres from the engine (the console's own check; the PDA's own distance check, a metre past where you stood, is not used because it compares world positions
  and a flying Cyclops would close it).
* **Power routing.** Four presets, one at a time, sent with a click; the server keeps the route and tells everybody. A route gives its bonus and nothing else changes. **Balanced** is the game's own numbers.
  **Engines**: the Cyclops is `EngineerSpeedBonus` faster (15 percent; the engine's acceleration is scaled, which scales the top speed, the climb and the dive with it, not the turn) and the **overdrive** can be used.
  **Shields**: the shield uses `ShieldSaving` less power (40 percent). **Systems**: the navigator's map sees 1.5 times farther, **silent running is `SilentRunningBonus` quieter** on top of what the game takes away
  (creatures hear a Cyclops from `Lerp(0, 150 m, noise)`, so this halves the distance again) and silent running costs 1.5 times the power. The game's own creature sonar (a fixed 100 m) is not changed: its display
  drops everything beyond 105 m.
* **Engine heat.** The heat is the game's own, not an invention: a number from 0 to 10 that the game steps every five seconds while the Cyclops flanks under throttle (up one) and cools otherwise (down one). From 4 on the
  engine warns and each step has a one in five chance to start a **fire in the engine room**, from 6 on one in three. The game shows it nowhere; the console shows it as a gauge (it only exists on the client that
  runs the engine, the pilot's, which sends it to the engineer: a report arrives when it changes and every five seconds). **Vent**: takes 4 off the heat at once and 15 energy from the Cyclops (free in creative
  mode), then waits `VentCooldownSeconds` (30 s). **Overdrive** (the engines route, the engine on, flank speed, power): for `OverdriveSeconds` (12 s) the Cyclops is 35 percent faster on top of the route's bonus,
  the heat goes up by 2 at once and by one more at every step of the game while it runs; then `OverdriveCooldownSeconds` (45 s) before the next. A press that cannot be carried out says why ("The overdrive needs
  flank speed", "Send the power to the engines first", "The overdrive is still cooling").
* **How a press goes.** The server keeps the **cooldowns on its own clock** and tells everybody the seconds that are left (`CyclopsEngineerState`), so no computer compares a time with another's. A route
  is the server's alone. The overdrive and the vent go through the server to **the player whose client simulates the Cyclops** (the pilot, mostly): only there do the engine, its heat and the energy exist. That
  client checks the module, the lock, the engine, the power and the energy, changes the heat, pays, and answers; the server then starts the cooldown, tells everybody and answers the engineer alone.
  A press holds back the next one of its kind for four seconds while its answer is out (longer than the console's own three second timeout, so a console that gave up cannot press twice into a stalled simulator). The answer is not given up after that: it still counts for ten seconds (the simulator has paid by then), and the engineer is told. If the engineer sent the power elsewhere while an overdrive was out, nothing is started, no
  cooldown is burned and the engineer is told "Send the power to the engines first" (the heat step the simulator already added stays). A press that came to nothing (the simulator turned it down, or the route moved) is not forwarded again for
  a second. If the lock moves to another player while a press is out, the answer of the player who was asked still counts. `CyclopsEngineerState` is the server's alone: a client that sends one is ignored. What the route does is
  worked out on every client from the state: the speed in the physics step (only the client that simulates the Cyclops applies the force), the shield and silent running costs (only the client that pays), the
  noise where the game reads it (the creature's client: `AttackCyclops` takes the Cyclops the local player is in, so every client needs the route), the heat in the game's own step.
* **The page** (`EngineerPdaTab`, a `uGUI_PDATab` of our own under the id 9, registered in the PDA's tabs by `uGUI_PDA_Initialize_Patch` and left out of the toolbar; built the first time a console opens, laid out on a
  1600 x 1040 sheet that is scaled to the PDA's page, over an opaque dark backdrop so that nothing behind the PDA shows). The picture is a side view of the Cyclops (`Nitrox.Assets.Subnautica/Resources/EngineerUi/cyclops-side-view.png`, bow left; the layout numbers are
  `EngineerPictureLayout`, with tests). The **bubbles** sit at these spots: SHIELDS (the bow), SYSTEMS (above the engine room's front, "spot E"), ENGINES (the stern), BALANCED, OVERDRIVE,
  VENT and FIRE SUPPRESSION; each is a round bubble drawn in code (a dark disc, a lit ring, a halo; about 72 pixels at 1080p; the game's equipment slot was tried first and has a sprite of 2 by 19 pixels, no bubble) with the game's own module icons where there is one (shield, sonar, fire suppression) and our own glyphs
  (`EngineerGlyphs`) for the rest. The route bubbles are a radio group (the one that is set has a brighter ring, a glow and a gold name); the overdrive lights while it runs; a bubble that cannot be used now (a cooldown, no module) is dimmed (grey ring, icon at 60 percent) with its name still light; the name is bold on a dark plate under the bubble and the state is written under the name
  of the actions (ready, cooling 12 s, RUNNING 8 s, needs engines ...); the pointer over a bubble puts what it does in the line under the picture. The **engine heat** is a bar behind the stern, green, amber from 4 and red from 6 (ticks at both), with the number and the words under the picture.
* **Damage control.** On the same picture: a flame for each **fire** (a room with fire is tinted), a burst for each **hull breach**, a count in the top left corner of a room with fires and in the top right corner of a room with breaches, and a dot where you stand (the tint lies under the bubbles; the flames, bursts, counts and the dot lie over them: the first survival-world check had the flame of a fire at the middle of the helm hidden behind the bubble that sits there);
  the number of fires, and a **repair hint** naming the nearest breach from where you stand ("11 m aft, 2 m to port, 4 m below"). The marks are placed with one straight map from the hull's frame onto the picture
  (the seven rooms' fire spawn points fall inside their rectangles on the picture, within 0.03 of its width; `EngineerPictureLayout.Project`). The **Fire suppression** bubble starts the game's own system (the module has to be installed; the
  game's voice line, three seconds, then the dousing, then its cooldown): it is the game's button that is pressed, and Nitrox already relays that to everybody.
* **Emergency power.** One line: "drawing from <vehicle>" when a docked vehicle is giving the Cyclops its emergency power (the Power Share's automatic draw), else "standing by". It is automatic; the console just shows it.
* **Not saved.** The route and the cooldowns live on the server and are not saved with the world: a restarted server starts with the power balanced. (They were meant to travel in the lighting panel's metadata like
  the Power Share switch; that object is rewritten whole by every light press of every player, which would have overwritten the route with a passenger's stale copy.)
* **What stays as it was.** The game's Upgrade Console, the lighting panel and the desks are not touched. The tow slow-down and the engineer's speed are one patch on the game's physics step.
* **What the first test had to show (written before the test; the PDA screen and the fire and breach marks have since been checked, see above):** the hand target found by the eye from the walkway (its facing is inferred from the slope of the Upgrade Console's keys) and the panel
  (position, legibility, whether it hangs in the air or in the engine); the Upgrade Console still usable; the PDA opening on the engineer's page (the picture, the bubbles in the look of the equipment slots, the sizes of the texts, the heat bar; Escape and walking away closing it); the bubbles under the real cursor; whether a 15 percent bigger acceleration gives a 15 percent
  higher top speed (the tow slow-down did scale that way); the heat steps and the gauge; the overdrive's boost; the vent; the noise and the creatures' reaction; the breaches and fires in the picture and the
  suppression; the emergency line with a docked vehicle.
* **Off.** With `VehicleUpgrades=false` the module does nothing and no panel answers.

### Mouse input on the station screens (navigator, gunner; the engineer uses the PDA)

The navigator and the gunner take the game's input group (the PDA's mechanism: the cursor, the walking lock, the look lock; the engineer console, since its page of the PDA, uses the PDA itself and none of what follows). The game's `FPSInputModule.ProcessMousePress` decides what a mouse press does to a group, and two of its rules shaped the screens. They were found in the play-test of coop.12, when the gunner closed on every click (all 8 gunner sessions in a play-test log ended `the input was taken away`, none by the exit key, and no shot was ever fired):

- A press with nothing of the open group under the pointer is "a click outside": the module calls `ChangeGroup(null)` and the group is deselected, which the consoles read as "leave". With a locked cursor (the gunner) the pointer is the middle of the screen. Every station canvas therefore has an invisible full-screen graphic behind everything else (`ConsoleInputGroup.AddClickCatcher`), so that a press always lands on the group.
- A press on the group re-selects it with `lockMovement = false`, which would release the walking lock that the console took when it opened. `ConsoleInputGroup.OnReselect` keeps it on.
- Safety net: if a press takes the screen away all the same (and Escape did not), the screen takes the input back in the next frame, at most three times in two seconds, and logs a warning (`TakenAwayByAPress`, `RetakeIfAsked`). Escape, the exit key and the Leave button still leave.

The log lines, all `[VehicleUpgrades] <station>: ...`: `mouse button N pressed, the screen has the input: ...` (the first twelve of a session), `the input module took the screen away in frame ... (mouse buttons pressed this frame: ...; Escape ...)` and `the screen took the input back after a mouse press ...` (this one means the catcher missed a press). The Debug harness never produced a real press, which is how this was missed: a manual test of every station has to use real left and right presses, holds and drags.

### Cyclops Drone Bay and Scout Drone (feature group 3, station 4)

A passive Cyclops module (**Cyclops Drone Bay**) and a craftable item (**Scout Drone**), both from the Cyclops Fabricator. This section describes what is built so far
(the console and the drone on the client, the other players: the seat, the bay on the server, the drone's movement and health, the marks and the zapper). Tested by hand in the game: the drone's collisions, loading it and the fly screen, and docking. The zapper and the range limit have not been confirmed in the game.

* **The drone** is the game's own camera drone (`MapRoomCamera`) of a clone of its prefab with its own TechType, so nothing changes for the scanner room's cameras. It keeps what the game gives it: the controls (look,
  move in six directions, auto-move, the light, the exit key), the HUD (health, battery, distance, "connecting", "no signal"), the screen static, the first move input undocks it. What the mod changes: the thrust
  (`DroneSpeedFactor` times the Seamoth's 11 m/s: the drone's drag is 2, so the thrust is worked out from the live rigidbody), the health (`DroneHealth`; the game's drone has 400), collisions (they hurt only above 12 m/s, so
  that a bump at 22 m/s does not take the drone apart), no Stalker carry-off, and the recharge. **It collides with the terrain and the bases** like the game's drone: on the client that flies it the drone is a body that
  physics moves, not a kinematic one, with its collider on and continuous dynamic collision detection (`ScoutDrone.ConfigureFlightBody`; the prefab's plain continuous detection sweeps against static colliders only, and a base
  carries a rigidbody of its own); what it had to change, and the first impacts (what it hit and how fast), are in the log ("scout drone: flight body ...", "scout drone: hit ...").
* **The console** is the **left screen of the vehicle bay terminal** (the terminal in the docking bay that shows the docked Seamoth or Prawn Suit; its left screen, the one with the wave graphic, did nothing). The mod draws on it
  (`DroneTerminalScreen`, on the canvas of the centre screen, 712 canvas units to its left, which the game's own pointer pipeline takes) a heading, the state of the bay ("DOCKED, 96 PERCENT", "DRONE OUT", "BAY EMPTY") and who flies it,
  and two round bubbles in the look of the centre screen's module bubble (copies of it, without the game's scripts): the **load bubble** (empty while the bay is, the drone's icon while it holds one) takes a Scout Drone
  from the inventory into the bay, and the **fly bubble** (a triangle) flies the one that is there. The first builds had a panel on the engine; the play-test of coop.12 moved the drone here (the Drone Bay module still goes
  into the game's Upgrade Console). The game's own control takes over from there: the view is the drone's, and **the player's body stays standing where it used the terminal**: the control moves the camera and not the body, and
  before this the body was found in the middle of the ship while the drone flew; the bay now keeps the body at its place in the hull's frame every frame and tells the Cyclops pawn (`DroneBay.KeepBodyAtTheTerminal`, logged when
  it had to put the body back), so that the others see it there too.
  exit key gives the control back. The bay holds one drone; to take it out, pick it up from the dock. The console flies the bay's drone **wherever it is**: in the dock, out flying or adrift (the operator left the seat, died, or left
  the Cyclops and left the drone out), even past the range, where the leash only lets it come back. "The Scout Drone is out of reach" means the bay's drone is out but its cell is not loaded on this client (it cannot be flown from here).
* **The dock** is the game's `MapRoomCameraDocking` on a trigger outside the hull (port flank, level with the engine room's console of the first builds), parented to the Cyclops so that the drone rides along; it nudges the drone out when it leaves and takes
  it back when it flies in. A scanner room's dock takes no scout drone and the Cyclops' dock no scanner room camera. The drone recharges in the dock at a fixed 4 energy per second (25 seconds from flat); the Cyclops does
  not pay for it, because the Cyclops' energy can only be changed by the client that simulates it and the drone's battery by the client that flies it (a later item can route it through the Power Share).
* **With other players.** One operator per Cyclops, and a player sits at one drone console at a time (`CyclopsDroneSeat`: the first request wins, the server tells everybody; the seat ends when the operator leaves the
  console, leaves the Cyclops, dies, disconnects, or when the Cyclops or the drone is destroyed). The bay (which drone it holds, docked or out) is the server's and is told to everybody (`CyclopsDroneState`, the server's alone:
  a client that sends one is ignored); a client reports what its player does (`CyclopsDroneBay`: load, unload, dock, undock) and the server decides: a load needs a player aboard and a real Scout Drone (a drone that the bay holds out is
  docked again by a load when nobody flies it), an unload (the drone picked up by hand, from the dock or out in the water where it was left adrift) a player within 60 m of the drone or the Cyclops, and not while somebody flies it
  (the seat ends with the drone), a dock and an undock the operator; a request that is turned down is answered with the bay as it is, to the sender alone. The operator's client takes the drone's exclusive lock and the drone moves like a vehicle (30 times a second to the others, `DroneMovementReplicator`); a drone in its dock is carried by the hull
  and sends nothing. Its health is the lock holder's, as a vehicle's, and travels with the drone. Exactly one client recharges the drone in the dock: the one that simulates the Cyclops; the battery's charge travels from it.
* **Saved with the world.** The bay is kept in memory only; what the save keeps is the metadata of the drone's entity (`ScoutDroneMetadata`: the Cyclops whose bay it belongs to, docked or not, and its health). The first
  player who joins gets the bays rebuilt from it, and a drone in the dock is placed with its Cyclops on the server, so that the cells it is loaded in follow the Cyclops. A client report of a drone is its health; the bay part is
  the server's. A client that spawns the drone puts it into the dock as soon as it exists there (however late), and never lets go of a drone before the server's settings arrived.
* **Marks.** The operator aims the drone's view at something and presses the **left hand button** (the mark key): a mark is placed where the view meets the world (a ray of 300 m from the drone, not through the drone,
  the Cyclops or the player). A creature is a **threat** (red, named after the creature), anything else a **resource** (teal, named after what the game calls it, "Resource" when it has no name); **reload** marks the same thing as the
  other kind. These are two more kinds of the navigator's marks (`CyclopsNavChanged`, kinds 3 and 4): the server takes them from the seated operator of that Cyclops only (the navigator console cannot place them; any
  player may remove any mark), keeps them apart from the navigator's own (16 of the drone's, 24 of the navigator's: neither fills the other's room; past 16 the oldest drone mark gives way), and every player sees them on
  the HUD, aboard the Cyclops or not (the navigator's marks are for the players aboard). The helm hologram and the navigator console's map show them with the other marks.
* **The zapper.** The **alternate tool** key (the drone has to be out of its dock, and the battery needs `ZapEnergy`) asks the server (`CyclopsDroneZap`): it checks the seat, that the drone is where the request says, and the
  cooldown (`ZapCooldownSeconds`, on the server's clock; the server lets a zap pass at 80 percent of it) and tells everybody. Every client plays the effect (a flash and arcs of lightning out to `ZapRadius`) and applies it to what it
  simulates: the Lava Larvae within the radius that sit on a vehicle or a Cyclops are knocked off, and the creatures within the radius that can flee (the game's `FleeOnDamage`, the reaction of the Seamoth's electrical defence)
  flee. **Nothing is damaged**, and a creature that gets angry when it is hurt (the leviathans) is left alone. The operator's client pays the energy from the drone's battery when it hears its own zap.
* **Range and static:** the picture gets static from `DroneStaticStart` of `DroneRange` (1000 m) and is full static at the range; past it the picture fades to black and the controls only let the drone come back (the
  outward part of the input is dropped), so a drone cannot be lost by flying too far. The distance is measured from the dock.
* **Debug commands:** `vudrone state|give|load|fly|exit|nudge dx,dy,dz|ray|track seconds|charge v|hurt v|mark [other]|rawmark kind|zap|place dx,dy,dz|placein dx,dy,dz|aim creature|ground` (`HarnessConsoleCommands.Drone.cs`).

### Extra module slots

Every vehicle gets two more module slots: the **Seamoth** 4 to 6, the **Prawn Suit** 4 to 6 module slots (its two arm slots stay as they are), the **Cyclops** 6 to 8 upgrade slots. The server option
`ExtraModuleSlots` (0 to 2, default 2) says how many; 0 is the game as it is, 1 adds only the first new slot of each vehicle, and `VehicleUpgrades=false` takes the extra slots with it. Everybody in a
session sees the same slots: the number comes with the settings at the join, before any vehicle is made.

* **Where they are.** The PDA's equipment screen (opened at a vehicle upgrade console or the Cyclops' upgrade console) shows them: the Seamoth's as a middle row between its two rows, the Prawn Suit's and the
  Cyclops' in the middle of the top and the bottom row. They take modules like the slots next to them (a Seamoth Depth Module in slot 5 or 6 counts: the game counts depth modules by kind, not by slot), and the
  Cyclops' seventh and eighth upgrade slots work like the first six (shield, sonar, vehicle repair, decoy tubes, thermal reactor and every module of this build).
* **Keys.** The game has five quick slot keys (1 to 5, which `uGUI_QuickSlots.HandleInput` hands to the vehicle as 0 to 4): on a Seamoth they reach its first five modules, on a Prawn Suit its four modules and the fifth
  (`Exosuit.SlotKeyDown` adds its two arms). **The sixth module has a Nitrox key of its own, "Quick slot 6 (and a vehicle's sixth module)", "6" unless the player moves it in the options** (`QuickSlot6KeyBindingAction`, read by `VehicleQuickSlotKeys`, which
  does for it what the game's loop does for the others: the press, the hold and the release go to the vehicle as the number 5). Passive modules (depth, power, solar charger, tow hook rod...) need no key. The next and previous
  quick slot buttons still step through all the slots. The arithmetic (which slot a key reaches, whether the sixth module exists, the press and release of a polled key) is `QuickSlotKeyRules`.
  In the play-test of coop.12 active modules (the tow hook, the afterburner) in the fifth and sixth slot did nothing when their key was pressed, while the harness, which called `SlotKeyDown` directly, passed. The quick bar's frame now first
  makes the vehicle's quick slot arrays as long as its list of slots (`VehicleSlotLists.EnsureQuickSlotArrays`: a vehicle that started before the extra slots were in force had four of each, and reads past their end), and the log says
  what the keys 5 and 6 reach (`quick slot key N pressed in ...: slot, module, quick slot type, cooldown`, the first twelve per session), when the arrays were grown (`the quick slot arrays of ... were 4 long for 6 slots`) and when the
  game's handling of a key press throws (`the key press of quick slot N ... threw`).
* **What the game does not do for the new slots** (its own code, not changed): a torpedo module in a Seamoth's slot 5 or 6 fires from one fixed tube (the game picks the tube by the name of slots 1 and 3);
  the docked-vehicle screen of the Cyclops lists only the slots of its own console, and the Cyclops' upgrade console in the world has six module sockets, so the modules of its seventh and eighth slot are not
  drawn on it (the PDA shows them).
* **A module in a slot that is taken away.** Lower the option (or play the world with an older build) and a module that sits in a slot that no longer exists is **kept and returns with the slot**: it stays
  in the world's save untouched, the game does not install it, does not complain, and logs it once. Raise the option again and the module is back in its slot the next time the vehicle loads.
* **Old worlds** have the new slots empty. Nothing in the save changes when the option is 0.

### Eight quick slots for the player

The player's own quick bar has **eight slots instead of the game's five**. The keys **1 to 5** are the game's; **6, 7 and 8** are Nitrox key bindings ("Quick slot 6 (and a vehicle's
sixth module)", "Quick slot 7", "Quick slot 8" in the options, where a player can move them; "6", "7" and "8" are free in the game's default controls). The bar on the HUD is the game's own and sizes itself from
the number of slots, so eight icons stand centred in a row; the scroll wheel and the next and previous buttons go through all eight; in the PDA a key held over an item in the inventory binds the item to that slot
(`QuickSlots.SlotKeyDown` does both that and the selecting, so the new keys do what the first five do), and dragging an item onto an icon of the bar works for all eight. A **vehicle's** bar is its own (its modules) and is
not changed: the same "6" key is the vehicle's sixth module while the player sits in a Seamoth or a Prawn Suit that has one, and the bar's sixth slot on foot.

* **What had to change in the game's code.** `Inventory.Awake` makes the bar with the literal 5 (`Inventory_Awake_QuickSlots_Patch` writes `PlayerQuickSlots.COUNT` instead); the game's list of slot names, `QuickSlots.slotNames`,
  has six names (QuickSlot0 to QuickSlot5) and three methods (`Update`, `SelectInternal`, `DeselectInternal`) read it with the number of the slot for an equipment event, so the seventh and eighth slot would have thrown
  (`QuickSlots_Update_SlotNames_Patch`, and a transpiler in `QuickSlots_SelectInternal_Patch` and `QuickSlots_DeselectInternal_Patch`, read the player's list of eight, `PlayerQuickSlots.SlotNames`, instead); the keys 1 to 5 come from
  a static list of five buttons in `uGUI_QuickSlots.HandleInput`, so `PlayerQuickSlotKeys` (called from the bar's frame, `uGUI_QuickSlots_Update_ExtraSlots_Patch`) polls the three bindings and hands the numbers 5, 6 and 7 to
  `SlotKeyDown`, under the conditions in which the game reads its own keys (the player can use items, no intro). The rest of the bar (the binding, the unbinding, the scroll, the icons, the HUD) goes by the count the bar was
  made with. If a patch does not find its place (the game changed) the log says so once, and the bar keeps the game's five slots (the keys 6 to 8 then do nothing).
* **Saving and syncing.** Nothing about the save changes: the server keeps, for every player, a list of bindings of whatever length the client sends (`PlayerQuickSlotsBindingChanged` carries the whole list on every change; the list
  is empty until the player binds something), and the join gives it back (`QuickSlotInitialSyncProcessor`). A save from before the eight slots has five bindings: they are restored, and the other three slots are emptied (the game binds a tool to
  a free slot as the items come into the inventory at the join, and the saved bar is the truth); a save with eight bindings on a bar of five (a build whose patch did not take hold) restores five and writes past nothing. The
  arithmetic is `PlayerQuickSlots` (the numbers, the names, which slots a join empties), tested without the game; the patches are tested on the game's real code (`PlayerQuickSlotsTranspilerTest`).
* **What other players see** is unchanged: a selected slot is sent as the held item, not as a slot number, so the eighth slot is held like any other.
* **The log** says what the first twelve presses of the new keys reached (`[QuickSlots] key 7 pressed: slot 6 of 8 holds ..., slot N is selected, PDA in use: ...`), and once if a key binding is missing from the list.
* **Not live-checked yet** (written 2026-10-30): the HUD with eight icons, the keys 6, 7 and 8 on foot, in the PDA and with a vehicle, a join with a save of five bindings.

## Settings (server.cfg)

A number outside its range is clamped to the nearest limit, and a value that is not a number (NaN, infinity, or too large for a float) uses the default; the server log says so once.

| Key | Default | Range | Meaning |
| --- | --- | --- | --- |
| `VehicleUpgrades` | true | | Master switch. Off: no fabricator shows the new recipes, installed new modules do nothing (the depth is that of the game's best module), the Seamoth Solar Charger works exactly like in the game. |
| `UpgradesUnlockedFromStart` | false | | true: every new blueprint is known when a player joins. false: each unlocks with the blueprint in the table above. |
| `ExtraDepthPerModule` | 300 | 0 to 3000 | Metres per tier on top of the game's best depth module. 500 makes Seamoth MK4 1400 m. |
| `AfterburnerSpeed` | 2 | 1 to 5 | Speed factor while it burns. |
| `AfterburnerSeconds` | 5 | 0.5 to 60 | How long one use burns. |
| `AfterburnerCooldownSeconds` | 15 | 0 to 300 | Recovery time after a burn. |
| `AfterburnerEnergy` | 10 | 0 to 200 | Energy per use (a power cell holds 200, an ion power cell 1000). |
| `SolarChargeSeamoth` | 1 | 0 to 5 | Energy per second and module at the surface at noon. |
| `SolarChargeCyclops` | 1.25 | 0 to 5 | Energy per second at the surface at noon. |
| `PowerShareRange` | 30 | 5 to 200 | Metres from the hull of a Cyclops with the Power Share module within which a Seamoth or Prawn Suit is charged. |
| `PowerShareRate` | 2 | 0 to 20 | Energy per second that the Cyclops gives to each vehicle in range. 0 switches the charging of vehicles off. |
| `EmergencyPowerThreshold` | 10 | 0 to 50 | Percent of its power below which the Cyclops draws on the docked vehicle. 0 switches the emergency power off. |
| `EmergencyPowerReserve` | 25 | 0 to 90 | Percent of its own power that the docked vehicle keeps. |
| `EmergencyPowerRate` | 5 | 0 to 50 | Energy per second that the Cyclops draws from the docked vehicle. 0 switches the emergency power off. |
| `TowSpeedFactor` | 0.7 | 0.2 to 1 | Speed of a vehicle that tows another one, as a factor of its normal speed. |
| `TowMaxDistance` | 25 | 12 to 100 | The longest cable of a tow hook in metres; a hook attaches within half of it. |
| `TerrainSonarRange` | 200 | 50 to 500 | Metres of terrain around the Cyclops that the navigator's map shows (the scanner room of the game shows up to 500); the helm hologram shows the same but no more than the 130 m that its round display holds. A big radius costs more to draw. |
| `ClearanceWarningDistance` | 20 | 5 to 60 | Metres from the hull within which the navigator's clearance warning looks for walls at its sides, above and below (ahead it looks farther the faster the Cyclops goes). |
| `GunnerShotEnergy` | 10 | 0 to 200 | Energy that a full charge of the gunner station takes from the Cyclops (a partial charge takes the same fraction; the Stasis Rifle itself uses 5 of a battery of 100). |
| `GunnerCooldownSeconds` | 3 | 0 to 60 | Seconds after a shot before the gunner can charge the next. |
| `GunnerRange` | 60 | 20 to 120 | Metres that a stasis sphere of the gunner station flies before it is spent (it slows down on the way; the Stasis Rifle reaches 37.5 m). |
| `EngineerSpeedBonus` | 0.15 | 0 to 0.5 | Extra speed of a Cyclops whose engineer routes the power to the engines, as a fraction (0.15 is 15 percent faster; the overdrive's 35 percent multiplies with it: 1.15 times 1.35 is about 55 percent faster at flank). |
| `OverdriveSeconds` | 12 | 3 to 60 | Seconds that the overdrive of the engineer console lasts. |
| `OverdriveCooldownSeconds` | 45 | 10 to 300 | Seconds after an overdrive before the next one can be started. |
| `ShieldSaving` | 0.4 | 0 to 0.8 | Share of the shield's power use that is saved while the power is routed to the shields (0.4 is 40 percent less). |
| `VentCooldownSeconds` | 30 | 5 to 180 | Seconds after venting the engine heat before it can be vented again. |
| `SilentRunningBonus` | 0.5 | 0 to 0.9 | How much quieter silent running gets while the power is routed to the systems, as a share of the noise that is left (0.5 halves it again). |
| `DroneSpeedFactor` | 2 | 1 to 4 | Top speed of the scout drone as a multiple of the Seamoth's (the Seamoth does 11 m/s forward, so 2 is about 22 m/s). |
| `DroneRange` | 1000 | 200 to 3000 | Metres from its Cyclops at which the scout drone loses its signal: beyond it the picture is black and the controls only let the drone come back. |
| `DroneStaticStart` | 0.5 | 0.1 to 0.95 | Share of the range at which the static begins to cover the picture (0.5 is from half the range). |
| `DroneHealth` | 100 | 20 to 400 | Health of a scout drone (the game's camera drone has 400, a Seamoth 200). |
| `ZapEnergy` | 15 | 0 to 100 | Energy that one zap takes from the drone's battery (the battery holds 100). |
| `ZapCooldownSeconds` | 12 | 2 to 120 | Seconds after a zap before the drone can zap again. |
| `ZapRadius` | 20 | 5 to 60 | Metres around the drone that a zap reaches. |
| `ExtraModuleSlots` | 2 | 0 to 2 | Extra module slots for every vehicle: the Seamoth and the Prawn Suit 4 + 2, the Cyclops 6 + 2. 0 is the game as it is. A module in a slot that a lower number takes away stays in the save and returns with the slot. |

A world that has these modules in it keeps them when the master switch is off; they just do nothing until it is on again.

## For developers

* **The extra module slots**: `VehicleSlotLists` (`NitroxClient/GameLogic/VehicleUpgrades`) holds the lists for the option; `ExtraModuleSlotRules` (`Nitrox.Model.Subnautica`) the names, their order, the place of the slot
  objects on the PDA screen and the dormant rule, free of game types and tested. The game's own lists (`SeaMoth._slotIDs`, `Exosuit._slotIDs`, `SubRoot.slotNames`) are static readonly arrays and are **not replaced**:
  Mono's JIT may keep such an array in the code it compiled, so a second session in the same process would see the first one's. The getters `SeaMoth.slotIDs` and `Exosuit.slotIDs` are patched
  (`SeaMoth_slotIDs_ExtraSlots_Patch`, `Exosuit_slotIDs_ExtraSlots_Patch`), the one reader of `SubRoot.slotNames` (`SubRoot.SetCyclopsUpgrades`, with the number 6 written into it twice) by a transpiler
  (`SubRoot_SetCyclopsUpgrades_ExtraSlots_Patch`, tested on the game's real code), and `UpgradeConsole.UnlockDefaultModuleSlots`, which has the six names of the Cyclops written into it, by a postfix. The PDA slot
  objects are copies made in a postfix on `uGUI_Equipment.Awake`. A new reader of the slot lists in this build must read `VehicleSlotLists`, not the game's fields.
* **The table**: `NitroxClient/GameLogic/VehicleUpgrades/CoopItems.cs`. One `CoopItemDefinition` per item: fixed TechType number (new items start at
  20001, far above the game's highest value 10004), fixed class id, name and tooltip (placeholders such as `{depth}`), the game item whose prefab and
  icon it borrows, equipment and quick slot type, size, recipe, craft tree and tab, prerequisite. An *adopted* item (`SeamothSolarCharge`) is one the
  game has half finished: the registry only fills in what is missing. Numbers and class ids are written into saved worlds as text and must never change.
  The new item travels as the number (`NitroxTechType("20001")`); the server never parses names. An adopted item travels under the game's name.
* **The registry**: `CoopItemRegistry`. Runs from `Multiplayer.StartSession` before the client joins (and as an initial sync step, and as a guard in the
  entity spawner), idempotent per table: the five caches of `TechTypeExtensions`, the tooltip caches, a prefab clone under a hidden root that survives
  scene changes (registered in `ScenePrefabDatabase` and `CraftData.techMapping` / `entClassTechTable`, which is what every place that spawns an item
  again goes through: inventory, locker, vehicle slot, Cyclops console, dropped in the world), the icon, `TechData` (rewritten at every game load),
  craft nodes, unlock rules (`KnownTech.compoundTech`), the Prawn and Cyclops depth tables, and the language strings. The craft nodes and rules depend
  on the server's settings and follow `VehicleUpgradeSettings.Changed`; they are removed when the session ends.
* **Behaviour** is in Harmony patches keyed on the TechType, never in a component on the item: `SeaMoth_OnUpgradeModuleChange_Patch` and
  `Exosuit_OnUpgradeModuleChange_Patch` (crush depth), `SeaMoth_OnUpgradeModuleUse_Patch`, `Vehicle_GetQuickSlotCooldown_Patch`,
  `Vehicle_ApplyPhysicsMove_Patch`, `Vehicle_OnPilotModeEnd_Patch`, `Vehicle_OnDockedChanged_Patch`, `Vehicle_OnPoweredChanged_Patch` (afterburner),
  `SeaMoth_UpdateSolarRecharge_Patch`, `SubRoot_UpdateThermalReactorCharge_Patch` (solar). The rules are plain classes with tests: `DepthModuleRules`,
  `AfterburnerState`, `SolarChargeRules`, `VehicleUpgradeUnlockRules`, `VehicleUpgradeSettingsRules`, `CoopItemText`, `IconPixels`.
* **Power share** (group 2): `PowerShare` (the Unity side: ticks, the three packet handlers, the lines on the screen), `PowerTransfers` (the order of a transfer: take out, add, give back; testable with
  plain numbers), `PowerTransferRules` (the numbers and the arithmetic, shared by server and client), `PowerTransferTracker`, `TimedMarks`, `PowerShareGeometry`. The components
  `CyclopsPowerShare` (on every Cyclops, from `SubRoot_Start_Patch`: the emergency request, the list of Cyclops, the distance to the hull) and `VehiclePowerShare` (on every Seamoth and
  Prawn Suit, from `Vehicle_Start_Patch`: the forward request). One packet, `PowerTransfer`, with six kinds (Request, Offer, Grant, Delivery, Return, Refund): the server side is
  `PowerTransferProcessor` and `PowerTransferLedger` (open transfers, late grants). `Vehicle_UpdateEnergyRecharge_Patch` pauses the game's docking charge during an emergency draw,
  `HandReticle_LateUpdate_Patch` writes the line under the exit prompt. The switch is `PowerShareSwitch` (the state by the Cyclops' id) and travels, and is saved, in
  `CyclopsLightingMetadata.PowerShareOn` next to the two lights, through the metadata path the lights use. The buttons are `CyclopsPanelButtons` on the lighting panel of every Cyclops (added from `SubRoot_Start_Patch`,
  not on Nitrox's virtual copy): copies of the panel's own floodlight button with the game's scripts removed, a fresh click event and another icon, placed after the panel's two by `PanelButtonRules`.
  If the panel does not look as expected nothing is added and the whole hierarchy is written to the log once.
* **Tow hooks**: `VehicleTows` (state, attach, release, the speed factor), `TowRules` / `HullGeometry` (pure), `HullBounds` (the box around a hull, measured at run time), `TowLine` (the
  spring, on the towed vehicle, on the towing client only), `TowCable` and `TowCableMaterial` (the cable on every client). The Cyclops' button: `CyclopsTowPress` (the packet: press, forward to the simulator, failure) with `CyclopsTowPressProcessor` on the server, and
  `VehicleTows.OnCyclopsButton` / `OnPressForwarded` / `OnPressFailed` on the client. Server: `VehicleTowRegistry` (also an
  `ISessionCleaner`) and `VehicleTowChangedProcessor`, hooked into `SimulationOwnershipRequestProcessor`, `EntityDestroyedPacketProcessor` and `PlayerSyncFinishedProcessor`. Patches:
  `SeaMoth_OnUpgradeModuleToggle_Patch` (the Seamoth's quick slot), `SubControl_FixedUpdate_Patch` (the Cyclops' speed), and one line each in the patches of docking, destroying, entering a
  vehicle and changing modules.
* **Rider bar**: `RiderBars` (state, request, release, free look, the look packet), `RiderBarRules` / `RiderBarLayout` (pure), `SeamothRiderBar` (the bar on every Seamoth, the remote riders),
  `RiderBarHandTarget`, `LocalRider`. `RemotePlayer.SetBarRider` puts a remote body on a bar. Server: `SeamothRiderArbiter` (also an `ISessionCleaner`), `SeamothRiderChangedProcessor`,
  `SeamothRiderLookProcessor`. Patches: `MainCameraControl_OnUpdate_Patch` (free look), `Player_CanBeAttacked_Patch`. **Every path that destroys or docks a Seamoth must let the local rider go
  first** (`RiderBars.OnVehicleGone`): the player is a child of the Seamoth and would be destroyed with it; `LocalRider` watches the rest.
* **Terrain sonar** (group 3, station 1): `TerrainSonar` (is the module there, is the station allowed), `TerrainSonarRules` (pure: the scale of the display, the radius to load, when it is shown),
  `CyclopsTerrainSonar` (on every real Cyclops, from `SubRoot_Start_Patch`, not on Nitrox's virtual copy: polls four times a second and builds or shows the hologram while the local player is aboard), `TerrainHologram`
  (the game's `MiniWorld` on the display, sized from the sonar ping's own `radarDisplaySize / radarScanSize`; the material comes from the Seaglide's map prefab, `TerrainHologramTemplate`) and
  `TerrainHologramAligner` (the game keeps its hologram level to the world and centred on its own position, the display turns with the hull and shows the terrain around the hull's pivot: the aligner
  turns and moves the terrain under the display's middle after the game's update). What could not be read without running the game (the prefab's names, the unit of the game's terrain meshes, the shader's use
  of its fade radius) is logged when the hologram is built and again eight seconds later, and `vuhelm scale <factor>` corrects the size in the live check. Debug commands: `vuhelm [state|scale|dump]`, `vunavdump`
  (`HarnessConsoleCommands.Navigator.cs`).
* **Navigator console** (group 3, station 1, milestone 2): `NavigatorConsole` (on every real Cyclops, from `SubRoot_Start_Patch`: the hand target on the desk, opening, the watchdog that closes it, the contact list
  twice a second while open) with `NavigatorConsoleHandTarget`; `NavigatorConsoleScreen` (the game's `uGUI_InputGroup` plus the overlay canvas, the mouse, the icons projected from the hologram to the screen)
  and `NavigatorIcons` (the shapes, drawn from distance functions); `TerrainHologram.TryCreateOn` and `TerrainHologramAligner` (zoom, view and `WorldToMap`, which places an icon exactly where the hologram puts
  that place). Pure and tested: `NavigatorConsoleLayout` (where the desk, the hand target and the map are in the hull's frame — read from the game's own "cyclops" scene file: the desk's collider spans
  x 2.20 to 3.10, y -0.05 to 0.95, z -21.28 to -19.31, the pilot faces -z so his left is +x), `NavigatorMapView` (yaw and tilt, which go right round and never stop, and the zoom with its limits) and `NavigatorViewKeeper` (the view stays where the navigator left it, and is written down per Cyclops a moment after the last change), `CreatureDangerRules` (the red and orange table, by tech
  type, not by the game's behaviour type, which counts vehicles as sharks), `ContactRules`. `NavigatorContacts` (the list; `Creature_Start_Patch` registers the creatures that the map would ever draw,
  `SubRoot_Start_Patch` the bases). The numbers that only a live run can settle (where the hand target and the map sit relative to the player's eyes) are fitted with the Debug commands below.
  Debug commands (the server's `/console` passes the command and two words, so vectors are joined by commas): `vunav [state|open|close|hand dx,dy,dz|map dx,dy,dz|view yaw,tilt,zoom|ray|minor|stand x,z|look x,z]` and `vuhelm perf [range] [seconds]` (frame time with the helm hologram shown and hidden; 20 s each when `/console` gives no seconds), in
  `HarnessConsoleCommands.Navigator.cs`.
* **Marks** (group 3, station 1, milestone 3): the packet `CyclopsNavChanged` (`Nitrox.Model.Subnautica`: marks added, removed, cleared; the server puts the sender in as the owner), the pure `CyclopsNavRules` (limits, the
  label, the game's ping colours), on the server `CyclopsNavRegistry` (the marks of each Cyclops, a cap of 24, sent to a client that joins by `PlayerSyncFinishedProcessor`, dropped by
  `EntityDestroyedPacketProcessor`; **not** an `ISessionCleaner`: the marks stay when their player leaves) and `CyclopsNavChangedProcessor` (checks the Cyclops, the kind and the place in the world, passes it
  on to the others, answers a refusal to the sender alone with a "removed"); on the client `NavMarks` (the state by Cyclops id, applied at once and told to the server; the HUD ping of each mark: a `PingInstance`
  of type Signal on an object named `Nitrox_NavMark_<id>`, whose name keeps the fork's ping-preference hooks off it, with one of the game's five ping colours, shown only while the local player is aboard),
  `NavigatorConsole.SyncMarks` (makes the pings when the Cyclops is in the game), `HelmMarkLayer` (the pins on the helm hologram: a world-space canvas), the click on the map
  (`TerrainHologramAligner.TryRayToWorld`: the ray from the camera to the map's plane, back to the world). Debug: `vunav mark dx,dz`, `markkind`, `markdel`, `markclear` and the marks in the state line.
* **Route** (group 3, station 1, milestone 4): the pure `NavigationRules` (`Nitrox.Model.Subnautica`: the limits, reaching and passing a point, the nearest point on a leg and the distance from it, the bearing and the
  arrow angles, off course with its hysteresis, the line width), the packet `CyclopsRouteChanged` (a waypoint appended or removed, the route cleared, the advance), on the server `CyclopsNavRegistry` (the routes next to the
  marks: forward-only advance that names the point reached, slots, a removal keeps the next point where it was, snapshot for a joiner, dropped with a destroyed Cyclops) and `CyclopsRouteChangedProcessor` (checks the Cyclops, the place, the spacing and the
  limit; an advance only from the player whose client simulates the Cyclops; a refusal is answered to the sender alone with the opposite change); on the client `NavRoute` (the state by Cyclops id), `RouteGuide` (on
  every real Cyclops, ten times a second: the guidance, the advance on the client that holds the Cyclops, the HUD ping of the next point, the line in the water with `RouteLineMaterial`; its frame is the Cyclops'
  `subAxis`, x starboard, y up, z ahead), `RouteHud` (the arrow and the numbers, an overlay canvas that hides itself), the route on the console map (`NavigatorConsoleScreen.UpdateRoute`) and on the helm hologram
  (`HelmMarkLayer`). Debug: `vunav wp dx,dz`, `wpdel`, `wpclear`, and the guidance in the state line (`route=… guide=[…] axisInHull=…`).
* **Clearance and sonar ping** (group 3, station 1, milestone 5): the pure `ClearanceRules` (sides in the Cyclops' frame, the gap of a sphere hit, the look ahead, the levels with their hysteresis, the text, when it is said
  aloud), `CyclopsClearance` (on every real Cyclops: the casts at 5 Hz while a player is aboard, the cost log), `RouteHud.FeedWarning` and the console's line; the packet `CyclopsSonarPing` (press, forward, result, with
  the failures), on the server `CyclopsSonarPingProcessor` (a press only goes to the Cyclops' real simulator with the presser's session from the connection and at most once in four seconds per Cyclops, only the simulator can send a result, for a Cyclops, with a failure that exists), on the client
  `NavigatorSonar` (cooldown, the module and power checks on the simulating client, the effect of the game's own ping on every client aboard).
* **Gunner station** (group 3, station 2): the pure `GunnerRules` (the turret's yaw and pitch with the hull's pitch table and a scanned horizon, the direction in the hull's frame, the charge, the energy of a
  shot, the cooldown, the flight of a sphere for a range) and `GunnerStationLayout` (the hand target between the two screens of the right desk, the mount), `CyclopsGunnerRules` in `Nitrox.Model.Subnautica` for what the
  server also needs; the packets `CyclopsGunnerSeat` (take, leave; seated, released, denied with a reason) and `CyclopsGunnerFire` (request, forward, result, with a shot number and the ammo); on the server
  `CyclopsGunnerArbiter` (the seats, an `ISessionCleaner`; ended from `PlayerDeathEventProcessor`, `SubRootChangedPacketProcessor`, `EntityDestroyedPacketProcessor` and, for a wreck,
  `EntityMetadataUpdateProcessor`), `CyclopsGunnerSeatProcessor` and `CyclopsGunnerFireProcessor` (the seat, the cooldown, the forward to the simulator, the answer matched to the shot and the player asked, to the
  gunner alone); on the client `GunnerSeats` (the table, the request and its handshake), `GunnerFire` (the request, the payment as the simulator), `GunnerTurret` (the one sphere, launched; the seam for the ammo),
  `GunnerConsole` (on every real Cyclops from `SubRoot_Start_Gunner_Patch`: the hand target, the lifecycle, the shot), `GunnerConsoleScreen` (the input group and the overlay), `TurretCameraRig` (the camera
  moved and always put back), `GunnerBullet` with `Bullet_Update_Gunner_Patch` (the sphere skips its own Cyclops), `StasisSphere_Freeze_Patch` (no Cyclops, no flown vehicle), and in `BulletManager` the factory for the
  sphere, the `CancelAll` before any sphere is destroyed and the counters of the shots it replays. Debug commands: `vugun` (`HarnessConsoleCommands.Gunner.cs`).
* **Engineer console** (group 3, station 3): the pure `CyclopsEngineerRules` in `Nitrox.Model.Subnautica` (the routes and what each does to the speed, the shield, the sonar, the noise and the silent running cost; the
  overdrive and the vent checks of the server and of the simulator; the game's heat arithmetic), `EngineerConsoleLayout` and `CutawayRules` (the placement; the drawing's mapping and the repair hint); the packets
  `CyclopsEngineerSeat` (as the gunner's), `CyclopsEngineerAction` (request, forward, result for a route, an overdrive or a vent, with a counter), `CyclopsEngineerState` (route and seconds left, to everybody) and
  `CyclopsEngineerStatus` (the heat, from the simulator to the engineer); on the server `CyclopsEngineerArbiter` (the seats, an `ISessionCleaner`, and the route and the cooldowns on the server's clock; ended from
  the same five processors as the gunner's) with `CyclopsEngineerSeatProcessor`, `CyclopsEngineerActionProcessor` (the seat, the route, the floor, the cooldown, the forward to the simulator, the answer matched to the
  press and the player asked, the cooldown started only on a carried-out answer) and `CyclopsEngineerStatusProcessor` (only from the lock holder, only to the seated engineer, clamped and throttled); on the client
  `EngineerSeats`, `EngineerState` (what the server said, counted on the client's clock, and the factors the patches ask), `EngineerActions` (the press as the engineer, the carrying out as the simulator),
  `EngineerHeat`, `EngineerDamage` (the rooms, fires, breaches and the fire suppression button, read from the game), `EngineerConsole` (on every real Cyclops from `SubRoot_Start_Engineer_Patch`; the hand target,
  the lifecycle and the status feed), `EngineerPanelProp` (the built panel), `EngineerPdaTab` (the PDA page), `EngineerPictureLayout` and `EngineerGlyphs` (where things are on the picture, and the drawn icons); the effects: `SubControl_FixedUpdate_Patch` (with the tow), `CyclopsShieldButton_ShieldIteration_Patch` and
  `CyclopsSilentRunningAbilityButton_SilentRunningIteration_Patch` (the cost for the call, put back), `CyclopsNoiseManager_GetNoisePercent_Patch`, `SubFire_EngineOverheatSimulation_Patch` and the navigator's range.
  Debug commands: `vueng` (`HarnessConsoleCommands.Engineer.cs`).
  Debug commands: `vushare`, `vutow`, `vurider`, `vubar`, `vuline`, `vukill`, `vupanel` (the panel's hierarchy and the state of the buttons), `vubutton share|tow|lights|forget|forgetlights` (a click on the button; `lights` is a press on the interior lights, `forget` drops the client's own Power Share switch and `forgetlights` turns its interior lights over, both without a packet: the old views the race checks need) (`HarnessConsoleCommands.VehicleUpgrades2.cs`).
