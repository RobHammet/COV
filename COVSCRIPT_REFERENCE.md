# CovScript Reference

CovScript (`.covscript`) is the scripting language for COV scene interactions and dialogs. Files are parsed by `CovScript.cs` into the JSON structure consumed by `ScriptParser` and `ConversationStep`.

---

## Scene Files

Scene files define how things, areas, and sequences behave. The parser auto-detects scene vs dialog format.

### Top-level blocks

```
thing name:
  on verb:
    ...actions...

area name:
  on move_into:
    ...actions...

sequence name:
  ...actions...

on entry:
  ...actions...

on arrive_from scenename:
  ...actions...

border_style: thick
border_width: 6
```

**`thing`** — interaction handlers for a named node. Verbs: `use`, `look`, `talk`, `walk`. Combine with `, `: `on use, look:`. Item use: `on use_item keyname:`.

**`area`** — triggered when ego walks into a named `CollisionShape2D` or `CollisionPolygon2D`. Use `move_into` (fires once on entry) or `move_through` (fires each pass).

**`sequence`** — named reusable action block, callable with `run name` or `finish_and_run name`.

**`on entry`** — runs once when the scene becomes active (after transition completes).

**`on arrive_from`** — runs when arriving specifically from the named scene.

---

## Dialog Files

Dialog files define branching conversations.

```
start NodeName

node NodeName:
  ...actions...
  goto OtherNode

node Main:
  choices:
    "LABEL" -> NodeName
    "LABEL" -> NodeName  if_flag:someFlag
    "LABEL" -> NodeName  if_not_flag:someFlag
```

**`start`** — names the entry node.

**`node`** — defines a named dialog state. Actions run top-to-bottom. `goto` transfers to another node. Reaching the end without a `goto` ends the dialog.

**`choices`** — presents a dialog choice box. Each option has a label and target node. Conditionals: `if_flag:name` (shown only if flag is true), `if_not_flag:name` (shown only if flag is false).

---

## Actions

Actions are used inside any action block (thing/area handlers, sequences, entry/arrive blocks, dialog nodes).

### Speech

```
actor says "TEXT"
actor says "TEXT" style:exclaim,lightning
actor says "TEXT" for 2.0
actor says "TEXT" for 2.0 strict
actor thinks "TEXT"
actor thinks "TEXT" for 1.5 strict
narrate "TEXT"
narrate "TEXT" corner:topleft
narrate "TEXT" style:jagged
```

**`says`** — speech bubble. **`thinks`** — thought bubble with dotted trail.

**`style`** — comma-separated: `exclaim` (exclamation style), `wavy` (wavy tail), `straight` (straight tail), `lightning` (jagged tail), `curved` (curved tail), `notail`.

**`corner`** — narration placement: `topleft`, `topright`, `bottomleft`, `bottomright`.

**`for N`** — fires the dialog and immediately continues (async). The dialog closes after N seconds.

**`strict`** — when combined with `for N`: locks input (hourglass cursor) for the duration. When used alone on `says`/`thinks` in a scene sequence: prevents player interruption.

Actor can be any thing/NPC name, or `ego`.

---

### Movement

```
actor -> target
actor -> target strict
actor -> [x,y]
actor ~> target
actor ~> target strict
actor ~> crow_spot strict
```

**`->`** — walk using navigation mesh. **`~>`** — fly in a straight line, ignoring navigation.

**`strict`** — prevents player from interrupting the move.

**`target`** — named thing (walks to its interact point), named Node2D (walks to its position), or coordinate array `[x,y]`.

---

### Facing

```
actor faces left
actor faces right
actor faces up
actor faces down
actor faces otherthing
```

Directions are `left`, `right`, `up`, `down`. Any other name is treated as a look-at target (thing or NPC).

---

### Animation

```
actor animates animationname
```

Plays a named animation on the actor's `AnimationPlayer`.

---

### Waiting

```
wait 1.5
```

Pauses the action sequence for the given number of seconds.

---

### Flags

```
set flagname = true
set flagname = false
set flagname = somevalue
set global.flagname = true
```

**`set`** — sets a scene flag. Flags without a domain prefix are scene-scoped (tied to the current scene). `global.` prefix makes a flag global across all scenes.

```
if flagname is true:
  ...actions...

if flagname is false:
  ...actions...
else:
  ...actions...

if not flagname:
  ...actions...
```

Conditional blocks. The optional `else:` branch runs if the condition is not met. Supports `global.flagname` syntax.

---

### Inventory

```
obtain itemname
```

Adds a thing to the player's inventory (typically `self` in a `thing` handler).

---

### Visibility

```
show thingname
hide thingname
remove thingname
restore thingname
```

**`show`/`hide`** — toggle visibility. **`remove`/`restore`** — toggle existence (remove frees the node entirely).

---

### Floor / Navigation

```
remap_floor PolygonNodeName
remap_floor PolygonNodeName nav:NavigationRegion2DName
```

Replaces the navigation mesh polygon with the outline of a `Polygon2D` node (child of the navigation region). Used to open or close off parts of the walkable area.

---

### Scene exit

```
exit to scenename
  arrive_dir left
  arrive_area exit_to_theroad
  arrive_thing something

exit to scenename no_walk
```

Transitions to another scene. Sub-parameters (indented below) set the arrival state:
- **`arrive_dir`** — facing direction on arrival: `left`, `right`, `up`, `down`.
- **`arrive_area`** — name of the arrival `CollisionShape2D` in the destination scene.
- **`arrive_thing`** — name of a thing to position near on arrival.
- **`no_walk`** — skip the automatic walk-out-of-zone move on arrival.

---

### Conversation

```
converse DialogFileName
```

Starts a branching dialog from `game/data/dialogs/DialogFileName.covscript`. Resolves automatically to the correct path.

---

### Sequences

```
run sequencename
finish_and_run sequencename
```

**`run`** — runs a named sequence inline (within the current dialog or action stream).

**`finish_and_run`** — dialog-only. Ends the conversation immediately, restores player input, and hands off to the scene's event queue to run the named sequence. Use this for end-of-dialog transitions where the player should be able to move during the sequence.

---

### Camera

Camera actions disconnect ego camera-follow while active. `return_camera` re-enables it.

```
camera_pan target
camera_pan target duration:0.8
camera_pan target duration:0.8 strict
camera_pan [x,y]
```

Smoothly pans the camera to the target position. In a **scene sequence**, the queue waits for the pan to finish before proceeding; `strict` also locks player input. In a **dialog**, the pan fires and the dialog continues immediately unless `strict` is specified (which makes it await completion).

```
camera_zoom 2.0
camera_zoom 2.0 duration:0.5
camera_zoom 2.0 target:farmer duration:0.5 strict
```

Smoothly zooms the camera to the given factor. If `target` is also specified, pans and zooms simultaneously. Same strict/async rules as `camera_pan`.

```
return_camera
return_camera duration:0.5
return_camera duration:0.5 strict
```

Re-enables ego camera-follow and tweens the zoom back to the scene's original value (as set in the Godot editor). Position returns naturally via the smooth-follow system.

**target** for camera actions accepts: a thing name, an NPC name (centers on their mid-point), any named `Node2D`, or a coordinate array `[x,y]`.

**duration** defaults to `0.5` seconds for all camera actions.

---

## Argument Reference

These trailing tokens can be appended to actions that support them:

| Token | Applies to | Effect |
|---|---|---|
| `strict` | move, fly, says, thinks (for N), narrate, camera_* | Locks player input for duration |
| `for N` | says, thinks | Async: fires dialog, closes after N seconds |
| `style:x` | says, thinks, narrate | Dialog/narration style |
| `corner:x` | narrate | Narration box placement |
| `color:x` | says, thinks, narrate | Dialog color (hex `#rrggbb` or named) |
| `duration:N` | camera_pan, camera_zoom, return_camera | Tween duration in seconds |
| `target:name` | camera_zoom | Also pan to this target while zooming |

---

## Examples

### Scene file

```
thing farmer:
  on talk:
    converse ChatWithFarmer

  on use:
    ego thinks "I DON'T NEED TO TOUCH HIM."

area exit_to_housefront:
  on move_into:
    exit to housefront
      arrive_dir left
      arrive_area exit_to_theroad

sequence farmer_exit:
  farmer says "DON'T BE A STRANGER." for 3.0 strict
  farmer -> exit_to_housefront
  remove farmer
```

### Dialog file

```
start Intro

node Intro:
  ego says "HEY THERE!" style:exclaim,lightning
  farmer says "WHAT DO YOU WANT?" style:lightning
  goto Main

node Main:
  choices:
    "JACKETS" -> JACKETS  if_not_flag:talkedAboutJackets
    "BYE"     -> BYE

node JACKETS:
  ego says "I'M SELLING THESE FINE LEATHER JACKETS!" style:exclaim
  set talkedAboutJackets = true
  goto Main

node BYE:
  ego says "THANKS FOR YOUR TIME."
  finish_and_run farmer_exit
```

### Camera usage

```
sequence examine_valley:
  camera_zoom 2.0 target:valley_marker duration:1.0 strict
  narrate "THE VALLEY STRETCHES OUT BELOW." corner:bottomleft
  wait 2.0
  return_camera duration:0.8 strict
```
