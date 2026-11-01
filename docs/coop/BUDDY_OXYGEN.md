# Buddy oxygen

A diver with air gives some of it to a teammate who is running out, the way divers share a regulator.

## How to use it

**Hold the share key (B) while you look at the teammate.** Both of you have to be swimming under water, you at most **2.5 m** from them and they inside the cone of your view (about 35 degrees around the crosshair). The crosshair shows "Hold to share air with Alice" when you could; while the key is held it shows "Sharing air with Alice", and Alice sees "Bob is giving you air". Let go of the key to stop.

- The hint steps aside when the game already shows its own text at the crosshair (open a locker, pick up an item): it never replaces it.
- The key is a Nitrox key binding like the ping: **Options, the Nitrox key bindings, "Share air with a teammate (hold)"** moves it.
- The air flows at **5 seconds of air per second** (the game counts air in seconds of breathing; a diver without a tank holds 45). It comes out of your tank and goes into theirs.
- **You keep 15 seconds of air for yourself**: when you are down to that, the sharing stops ("Keep some air for yourself") and you have to let go of the key before it will start again.
- The teammate's tank caps what arrives. What does not fit is never lost: it goes back into yours. When nothing fits for two steps in a row (their tank is full, they surfaced, they are dead) the sharing stops and says why.
- It needs air to be a thing: in Creative mode (no air to breathe) it does nothing.

## What ends it

| Event | What happens |
|---|---|
| the teammate's tank is full | their game takes what fits and answers; the rest returns to you; two empty steps stop the sharing ("cannot take more air") |
| the teammate surfaces, dies, or climbs into a vehicle or a base | their game takes nothing and says so; the air of that step returns to you; two empty steps stop it |
| you go into a vehicle or a base, surface or die | you cannot give (the key does nothing) |
| the teammate swims away (more than 2.5 m for the client, 4.5 m for the server's slightly old positions) | you stop at once (your game checks) or the server turns the step down and your air comes back ("too far away") |
| the teammate or you disconnect | the server gives the air of the steps that were waiting back to the giver; if the giver left, nothing is left to return it to |
| two divers give to the same teammate | allowed: each pays what the teammate's tank took of their own steps, so the total is capped by the tank |

## What the server keeps

Each player's air belongs to their own game, so nothing sets another player's air. Every step is a few messages through the server (`BuddyOxygenTransfer`, four kinds): the giver's game **takes the step out of its own tank first** and says so (`Give`); the server passes it on (`Deliver`) after it has checked that the sharing is on, that the receiver is another player who is online, that the two are close enough by the positions it knows and that the giver does not take air faster than the rate (a bucket of 1.5 times the rate); the receiver's game adds as much as fits and answers (`Answer`); the server tells the giver what was taken and what comes back (`Settle`). **What the giver lost is what the receiver got** (to the last decimal): the receiver's answer is cut to what was delivered, so it cannot make air, and everything a refusal or a leaving player leaves open goes back to the giver. A step that nobody answers for 30 s is given back too. A giver has at most 8 steps waiting at a time. The sender is the connection's, never the packet's; a client cannot send the server's own steps (`Deliver`, `Settle`).

A step is a quarter of a second of the rate (1.25 seconds of air at the default), so a share of 6 s is about 24 packets each way.

## Server settings (`server.cfg`)

| Setting | Default | Range | What it does |
|---|---|---|---|
| `BuddyOxygenRate` | 5 | 0 to 20 | Seconds of air per second that flow from the giver to the teammate. 0 switches the buddy oxygen off. |
| `BuddyOxygenRange` | 2.5 | 1 to 6 | Metres between the two divers within which air can flow. The server allows 2 m more for the positions that are a moment old. |
| `BuddyOxygenFloor` | 15 | 0 to 60 | Seconds of air that the giver keeps for themselves. |

A number outside a range is clamped to the nearest limit and a value that is not a number uses the default. None of them depends on `VehicleUpgrades`.

## Choices that differ from the first idea

- **The trigger is a held key (B), not the hand.** The game's hand targets (LMB on a thing) only work with empty hands and share the button with every tool. A key of its own works with a tool in hand and moves in the options.
- **Bubbles are left out** (the brief said "if cheap"): the HUD texts are enough.
- **Two givers on one receiver are allowed** (the other choice was one at a time): the tank caps the total and each giver loses only what was taken.

## Checks

- Unit tests: `BuddyOxygenRulesTest` (steps, the floor, the answer, the refund, the view, the server's bucket, the three options and their clamps), `BuddyOxygenLedgerTest` (the books: who may answer, the cap, the limit, stale steps, leaving players), `BuddyOxygenTransferProcessorTest` (the whole exchange with its refusals) and `BuddyOxygenDocsTest` (this page).
