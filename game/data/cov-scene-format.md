# COV Scene & Dialog JSON Format

You write JSON files for a point-and-click adventure game. There are two file types: **scene files** and **conversation files**.

A JSON Schema for validation is available at `game/data/cov.schema.json`.

---

## Scene Files

Every scene has a corresponding `.json` file. The top-level keys are:

```json
{
  "areas":          { ... },
  "things":         { ... },
  "sequences":      { ... },
  "on_arrive_from": { ... },
  "default":        []
}
```

`areas`, `things`, `sequences`, and `on_arrive_from` are all optional (omit or leave empty `{}`). `default` is typically `{}` or `[]`.

### `areas`

Keyed by the name of a CollisionArea2D node in the scene. Supported triggers:

- `"move_into"` — fires when the player walks into the area
- `"move_through"` — fires when the player attempts to walk through the area

```json
"areas": {
  "exit_to_housefront": {
    "move_into": [{ "action": "exit", "destination": "housefront", "arrive_dir": "left", "arrive_area": "exit_to_theroad" }]
  },
  "towards_house": {
    "move_through": [
      {
        "action": "if_flag", "name": "talkedAboutHouse", "is": false,
        "then": [
          { "action": "think", "actor": "ego", "text": "I DON'T KNOW WHERE I'M GOING." },
          { "action": "move",  "actor": "ego", "target": "start_point", "strict": true }
        ]
      }
    ]
  }
}
```

### `things`

Keyed by the name of a `thing` node in the scene. Each entry maps **verb** → action array. Supported verbs: `"use"`, `"look"`, `"talk"`, `"use_item"`.

`"use_item"` is a sub-object keyed by inventory item name:

```json
"things": {
  "door": {
    "look": [{ "action": "speak", "actor": "ego", "text": "THE FRONT DOOR." }],
    "use":  [{ "action": "move",  "actor": "ego", "target": "door" }]
  },
  "farmer": {
    "talk": [{ "action": "conversation", "file": "game/data/dialogs/ChatWithFarmer.json" }],
    "use_item": {
      "key": [{ "action": "speak", "actor": "self", "text": "NO, THANKS." }]
    }
  }
}
```

### `sequences`

Named reusable action arrays, called via `run_sequence`:

```json
"sequences": {
  "arrive_from_porch": [
    { "action": "narrate", "text": "HE STEPPED INSIDE.", "strict": true },
    { "action": "move",    "actor": "ego", "to": [200, 465], "strict": true }
  ]
}
```

### `on_arrive_from`

Fires when the player arrives from a named scene. Value is an action array (or a `run_sequence` call):

```json
"on_arrive_from": {
  "theporch": [{ "action": "run_sequence", "name": "arrive_from_porch" }]
}
```

---

## Conversation Files

Used for branching NPC dialogue. Triggered by `{ "action": "conversation", "file": "..." }`.

```json
{
  "start": "Intro",
  "nodes": {
    "Intro": [ ...actions..., { "action": "call", "node": "Main" } ],
    "Main": [
      { "action": "choices", "options": [
        { "label": "TOPIC A",  "node": "TOPIC_A" },
        { "label": "TOPIC B",  "if_flag": "someFlag",      "node": "TOPIC_B" },
        { "label": "TOPIC C",  "if_not_flag": "otherFlag", "node": "TOPIC_C" },
        { "label": "BYE",      "node": "BYE" }
      ]}
    ],
    "TOPIC_A": [ ...actions..., { "action": "call", "node": "Main" } ],
    "BYE":     [ ...actions... ]
  }
}
```

- `"start"` names the first node to run.
- `{ "action": "call", "node": "NodeName" }` jumps to another node (use at the end of a branch to loop back to a choices menu).
- `"choices"` presents a dialog menu. Each option may have `"if_flag"` and/or `"if_not_flag"` to conditionally show it.
- A node with no `call` at the end closes the conversation.

---

## Action Reference

All actions share `{ "action": "...", ...params }`.

Any action may also include the optional guard properties `"if_flag"` and `"if_not_flag"` (string flag names) to conditionally skip that action at runtime.

| Action | Required params | Optional params | Notes |
|---|---|---|---|
| `speak` | `actor`, `text` | `style` | NPC or ego says something aloud |
| `think` | `actor`, `text` | | Thought bubble (ego internal monologue) |
| `narrate` | `text` | `corner` | Caption box. `corner`: `topleft`, `topright`, `bottomleft`, `bottomright`, or omit for auto |
| `move` | `actor`, and one of: `target` / `to` / `to_area` | `strict` | Move to a named thing/marker node, `[x,y]` coords, or area name. `strict: true` disables pathfinding shortcuts |
| `look_at` | `actor`, `target` | | Turn actor to face a thing node |
| `change_facing` | `actor`, `direction` | | `direction`: `up`, `down`, `left`, `right` |
| `wait` | | `seconds` | Pause. Defaults to 1.0s |
| `set_flag` | `name` | `value` | `value` is `"true"` or `"false"` (default `"true"`), or any string |
| `if_flag` | `name`, `then` | `is`, `else` | Branch on a boolean flag. `is` defaults to `true`. `then`/`else` are action arrays |
| `play_animation` | `animation` | `actor` | Play a named AnimationPlayer animation. `actor` defaults to `"self"` |
| `add_to_inventory` | | | Picks up `self` (the thing being interacted with) |
| `toggle_exist` | | `target`, `value` | Add/remove a thing from the scene. `target` defaults to `self`; omit `value` to toggle |
| `toggle_hide` | | `target`, `value` | Hide/show a thing. `target` defaults to `self`; omit `value` to toggle |
| `remap_floor` | `polygon` | `nav_region` | Replace the navmesh polygon. `nav_region` defaults to `"NavigationRegion2D"` |
| `exit` | `destination` | `arrive_dir`, `arrive_area`, `arrive_thing`, `no_walk` | Change scene. `arrive_dir`: `left`, `right`, `up`, `down` — which side ego walks in from |
| `run_sequence` | `name` | | Run a named sequence from the scene's `sequences` block |
| `conversation` | `file` | | Launch a conversation file by path |
| `call` | `node` | | *(Conversation files only)* Jump to another conversation node |
| `choices` | `options` | | *(Conversation files only)* Present a dialog menu |

**Actors:** `"ego"` (the player character), `"self"` (the thing being interacted with), or any named node in the scene.

---

## `speak` / `think` Style Tokens

`"style"` is a `|`-separated string of tokens. Mix and match:

| Token | Effect |
|---|---|
| `exclaim` | Starburst/explosion bubble shape |
| `straight` | Straight tail |
| `curved` | Curved tail (default for `speak`) |
| `wavy` | Wavy/thought-cloud tail |
| `lightning` | Jagged lightning bolt tail |
| `notail` | No tail |

Example: `"style": "exclaim|lightning"` — starburst bubble with a lightning tail.

`think` always uses a thought-bubble style; `speak` defaults to a rounded bubble with a curved tail.

---

## Text Formatting

Dialog text is ALL CAPS by convention. BBCode tags work inline: `[b]WORD[/b]` for bold.

---

## Flags

Flags are scene-scoped by default. Use `set_flag` to write, `if_flag` / `if_not_flag` (as an action or as a guard on any action) to read. Values are `"true"` / `"false"` unless you need a string value.
