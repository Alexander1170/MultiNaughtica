# Adding your own player model

MultiNaughtica can show the other players as a custom 3D character instead of the normal diver. **No model is included**: you bring your own `.glb` file, put it in a folder, and pick it in the Options menu. This page tells you what the file has to be, where it goes, how to describe it in a small profile file, and how to check that it looks right in the game.

A model is optional in every way. With no model installed, the Options list shows only **Default** (the normal diver), there is no emote wheel, and nothing else changes.

Only use models you have the right to use. Many characters are somebody else's property.

## In short

1. Get a humanoid model as one **`.glb`** file, with a **Mixamo-style skeleton** (bones named `mixamorig:Hips`, `mixamorig:Spine`, and so on).
2. Name it `player-<something>.glb` and put it in `%APPDATA%\Nitrox\models` (create the folder if it is not there).
3. Start the game, press **Escape**, **Options**, the **Nitrox** tab. Under **Player model**, set **Model the others see** to your model.
4. Everybody who should see you in it needs **the same file under the same name**. Otherwise they see the normal diver.

## Where the files go

| Folder | What it is for |
|---|---|
| `%APPDATA%\Nitrox\models` | Your own models. This is the one to use. |
| `<the folder with the launcher>\Resources\PlayerModels` | Models that come with an install. This build ships none. |

If a file with the same name is in both folders, the one in `%APPDATA%\Nitrox\models` wins, together with the profile beside it.

The **id** of a model is its file name without `.glb`: 1 to 40 letters, digits, dots, dashes and underscores (`player-sample.glb` has the id `player-sample`). A file with any other name (blanks, other letters, too long) is skipped, and the game log says why.

The model file is never sent over the network. When you pick a model, the game tells the others its id and the **length of the file**. A player sees you in it only if he has a file of that id and of that length. Otherwise he sees the normal diver and the log says so once.

## Choosing a model

**Options, Nitrox, Player model, Model the others see**: a list with **Default** and one entry per model that was found. The choice is saved with the game's settings and takes effect at once, in the middle of a game. Picking **Default** gives you the normal diver back. The folders are read once, the first time the game needs the list; if you add a file while the game runs, restart the game.

**Show my model in first person** (same place, on by default): your own first-person view uses your model. Looking down, you see its legs and feet, and its arms and hands hold your tools, the PDA and the Cyclops wheel. Switch it off to see the normal diver's body again. The others always see your model, whatever this setting says.

## What the file has to be

- **glTF 2.0 binary** (`.glb`), with the textures inside the file.
- **One skinned mesh**, made of triangles, with one material. The mesh needs `POSITION`, `NORMAL`, `TEXCOORD_0`, `JOINTS_0` and `WEIGHTS_0`.
- **Textures**: the base colour is enough. A normal map and a metallic-roughness map are used when they are there; without a normal map the model gets a flat one. Large textures cost loading time: a 2048 pixel texture takes about a tenth of a second to decode, a 4096 pixel one about four times that.
- **The skeleton is Mixamo's.** The nodes are named `mixamorig:` followed by the Mixamo bone name: `Hips`, `Spine`, `Spine1`, `Spine2`, `Neck`, `Head`, `HeadTop_End`; for each side `LeftShoulder`, `LeftArm`, `LeftForeArm`, `LeftHand`, `LeftHandThumb1` to `4`, `LeftHandIndex1` to `4`, and the same for `Middle`, `Ring` and `Pinky`; and `LeftUpLeg`, `LeftLeg`, `LeftFoot`, `LeftToeBase`, `LeftToe_End`. That is 65 bones. A bone that is missing is named in the log and its limb stays at the rest pose. A model from Mixamo, or from a tool that exports Mixamo's skeleton, has all of them.
- **Fingers need four bones each** (three that move the mesh and the tip). A finger that is missing stays at its rest pose and the log names it; a hand without its four knuckle bones is left at the pose copy of the body, and the log says why.
- **Extra bones** (hair, a skirt) are fine. They stay at their rest pose in the frame of their parent; there is no physics.
- **Units and orientation**: metres, Y up, the character faces +Z with its left side at +X (what Blender's glTF exporter makes). The model stands with its feet at y = 0, about 1.7 m tall.
- **Every node has scale 1.** Apply the scale of the armature before exporting (in Blender: apply all transforms). A scale on a bone is not handled.
- **The rest pose** can be an A pose or a T pose. The game lines every bone up with the diver's by the direction it points to, and the hands by the way the palm lies. Hands of different sizes are fine.
- **What the reader refuses** (the game log says which): another glTF version, required extensions such as Draco or meshopt, an external buffer, a sparse accessor, a node given as a matrix instead of translation and rotation, cubic-spline animation, a mesh that is not triangles, a file with no skinned mesh.
- **Size**: the game sizes the model by its hips, so that its feet stand on the floor. A model with short legs comes out taller than 1.7 m, one with long legs smaller. A profile can change this (below).

A skeleton whose bones are spelled differently (`mixamorig_Hips`, `R_Hand`) is possible: the profile's `bones.prefix` and `bones.map` say how the rig names its bones.

## The profile: `<id>.profile.json`

A model works without a profile. The profile is a small JSON file next to the model, with the same name and `.profile.json` instead of `.glb` (`player-sample.profile.json`). It only overrides what the game measures wrongly, or says what no measurement can, such as the name in the Options list. Every field is optional. A field the game cannot read is a line in the log (`profile of '<id>': ...`) and keeps its default; a profile never stops the game.

```json
{
  "schema": 1,
  "displayName": "Sample Diver",
  "notes": "Exported from Mixamo, nothing changed.",
  "scale": { "source": "hips" },
  "firstPerson": { "hiddenBones": ["Head"] },
  "emotes": { "shared": false }
}
```

| Field | Meaning | Default |
|---|---|---|
| `displayName` | The name in the Options list. Plain keyboard characters, at most 40 (the game's font lacks other letters). | The file name made presentable: `player-sample_diver` shows as "Sample Diver". |
| `notes` | Free text for whoever works on the model next. | none |
| `scale.source` | How the model is sized against the diver: `hips` (the feet stand on the floor) or `head`. | `hips` |
| `scale.factor` | An explicit size factor (0.2 to 5), instead of working it out. | none |
| `eyeHeight` | The height of the model's eyes above its feet, in the model's own units (0.3 to 3). With `scale.source` set to `head`, the eyes then land at the diver's camera. | the head bone |
| `bones.prefix`, `bones.map` | How the rig names its bones: the prefix in front of the Mixamo names (`mixamorig:`), and names it spells its own way, for example `{ "RightHandIndex1": "R_Pointer_01" }`. | `mixamorig:` |
| `emotes.shared`, `emotes.source` | Whether the model plays the emotes of another installed model, and which one (see Emotes). | `shared` true, but no source |
| `firstPerson.hiddenBones` | Bones that are not drawn when you look through the model's eyes. | `["Head"]` |
| `firstPerson.trims` | Bones made smaller in first person, for example `{ "LeftShoulder": 0.8 }` for a shoulder pad that comes into view. | none |
| `firstPerson.offset` | Where the whole model stands against the diver's body in first person, in metres: right, up, forward. | 0, 0, 0 |
| `hands.*` | `enabled`, `fingers`, `thumbs`, `toolTargets`, `maxStretch`, `pdaPullBack`, `anchor` (three numbers), `rollTrim`, `twistShare`: the starting values for the hand solve (see Tuning). | everything on, nothing moved |
| `measured` | What the Debug measuring command wrote, kept for the next person. The game does not read it. | none |

## Tuning

The game works out most things from the skeleton itself, so try the model with no profile first, and change one thing at a time.

- **Size.** If the model stands much taller or smaller than the other divers, set `scale.factor`, or `scale.source` to `head` (with `eyeHeight`).
- **First person.** If a collar, a hat or a shoulder pad fills your view when you look down, add the bone to `firstPerson.hiddenBones`, shrink it with `firstPerson.trims`, or move the whole model back a little with `firstPerson.offset` (a negative third number).
- **Hands.** In first person the model's hands are placed on the grips of your tool, the PDA and the Cyclops wheel. A model with short arms is stretched at the elbow and wrist up to `hands.maxStretch` (1.2 by default) and then falls short of the farthest grips. `hands.pdaPullBack` pulls the left glove toward the camera if it comes through the PDA; `hands.anchor` moves the grip against the knuckles; `hands.rollTrim` turns the palm; `hands.thumbs` set to false switches the thumbs off if they look wrong.
- **Measuring.** A Debug build of this project (built from source) has console commands that print how a model was sized, how many bones were matched and how much the arms are stretched, and that write the `measured` block into the profile. The released zip is a Release build and has none of them: edit the profile by hand and restart the game to try a value.

## Check it in the game

Put the model on and look at these. Each line is a quick look, not a test.

1. **Look down** on a floor and inside the Cyclops: the legs and feet are the model's, the head is hidden, nothing fills the view.
2. **Tools**: knife, flare, scanner, repair tool. Each one sits in the fist, looking level, up and down, and the glove does not slide around it.
3. **The PDA**: open it. The left glove does not come through the frame or the screen.
4. **The Cyclops wheel**: both hands on the rim.
5. **The Seamoth and the Prawn Suit**: seated, hands on the grips; getting in and out looks right.
6. **Ladders, doors, swimming and walking.**
7. **Another player's view**: a second player sees the model, its name in the list, and the emotes.
8. **The setting** "Show my model in first person" off gives the normal body back at once.

## Emotes

Hold **G** (Options, Controls, **Emote wheel (hold)** moves the key), move the mouse toward an emote and let go to play it. Let go in the middle of the wheel to cancel. The other players see it. While it plays, your camera moves a few metres away to look at your character (the mouse circles it, the mouse wheel moves it closer or farther).

- **Only with a model.** With the normal diver, or with a model that has no emotes, there is no wheel; a line at the bottom of the screen says why for a moment.
- **Which clips are emotes.** Any animation in the `.glb` is an emote, unless its name contains `walk`, `run`, `crawl` or `idle`. The clip's name is its id, so use plain letters, digits, dots, dashes and underscores, no blanks (`Wave_Hello`). The wheel holds up to 12 entries. A clip shorter than 1.5 seconds repeats, for 6 seconds in all; no emote plays longer than 15 seconds. Clips must use translation and rotation channels with linear or step interpolation.
- **Cancelled by** moving, losing health, pressing the key again, or getting into a vehicle, a seat, the PDA or a menu. A new emote is **refused** in a vehicle, on a seat, when dead and in a menu.
- **Shared emotes.** A model with no emote clips of its own can play the clips of **another installed model** that has the same Mixamo skeleton and does have emote clips. Put `"emotes": { "source": "<id of that model>" }` in the profile of the model that has none. The source's clips are retargeted onto your model's bones by bone name, so a raised arm is raised by the same angle even if your model rests its arms lower. A model's own clips come first on the wheel, then the shared ones it does not have. At least 15 bones must be shared, or nothing is shared (the log says so). Without a named source, a model with no clips has no emotes. `"emotes": { "shared": false }` switches sharing off. The poses were made for the source's proportions: a model with much shorter arms may stop short of its head.

Only the id of the clip travels over the network, so everybody needs the same files.

## Pitfalls

- **Scaled nodes.** An armature exported with scale 0.01 and compensating bone scales puts everything off. Apply the transforms before exporting.
- **A character that faces -Z**, or has its left side at -X, is a mirror image: its left hand follows the diver's right one. Fix it in the file.
- **A rest pose with the arms over the head** or the hands turned upside down makes the palm direction ambiguous. Export from an A or a T pose.
- **Finger chains in an unusual order.** The hands need the four knuckle bones in the order of the palm (index finger on the thumb's side, then middle, ring, little finger) and four bones in every finger. If a rig names its fingers differently, say so in `bones.map`.
- **A finger chain that hangs off the forearm** instead of the hand stays behind when the wrist turns. `docs/coop/tools/reparent_glb_bones.py` moves the root of a chain under another bone without moving it in the world, for example `python reparent_glb_bones.py in.glb out.glb Bone_019=mixamorig:LeftHand`. The skin does not change, and the original file stays as it was.
- **Short arms and low shoulders** reach less than the diver's: the arm stretches, then falls short of the farthest grips.
- **A model without a normal map** looks flat in the light; that is how it is meant to be.
- **Names**: a profile `displayName` with letters the game's font lacks is refused with a line in the log; a model file with a bad name is skipped with a warning when the game scans the folders.

## For developers

The code lives in `Nitrox.Model.Subnautica/DataStructures/GameLogic/PlayerModels` (the reader for `.glb` files, the profile, the pose-copy arithmetic, the hand and arm solve, the emote retargeting; all free of game types and tested) and `NitroxClient/GameLogic/PlayerModels` (the library that finds the files, the builder that makes a copy in the world, the pose copy, the emote wheel). The tests build their skeletons from `Nitrox.Test/Model/Subnautica/PlayerModels/RigFixtureData.cs`, so they need no model file.
