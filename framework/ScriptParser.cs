// ScriptParser.cs — converts a JSON action array into EventSequence events.
// Shared by the entry-script system, thing interactions, and any other caller.
// Supports: move, look_at, change_facing, narrate, speak, think,
//           set_flag, wait, play_animation, add_to_inventory, toggle_exist,
//           toggle_hide, remap_floor, if_flag, exit, run_sequence, conversation,
//           camera_pan, camera_zoom, return_camera.
// self — the thing being interacted with (for "self" actor references and
//         add_to_inventory). Pass null when not applicable.

using Godot;
using System.Text.Json.Nodes;

public static class ScriptParser
{
    public static void PopulateEventQueue(EventSequence queue, JsonArray actions, scene_script scene, thing self = null)
    {
        foreach (var item in actions)
        {
            var obj = item?.AsObject();
            if (obj == null || !obj.ContainsKey("action")) continue;
            if (!ConditionMet(obj, scene)) continue;

            switch (obj["action"].GetValue<string>())
            {
                case "move": {
                    NPC actor = ResolveNPC(obj["actor"].GetValue<string>(), scene, self);
                    if (actor == null) break;
                    bool strict = obj["strict"]?.GetValue<bool>() ?? false;
                    queue.AddEventMove(actor, ResolveMoveDest(obj, scene, actor, self), !strict);
                    break;
                }
                case "fly": {
                    string flyActorName = obj["actor"].GetValue<string>();
                    NPC actor = ResolveNPC(flyActorName, scene, self);
                    if (actor == null) { GD.PushWarning($"[ScriptParser] fly: NPC '{flyActorName}' not found"); break; }
                    bool strict = obj["strict"]?.GetValue<bool>() ?? false;
                    queue.AddEventFly(actor, ResolveMoveDest(obj, scene, actor, self), !strict);
                    break;
                }
                case "look_at": {
                    NPC   actor  = ResolveNPC(obj["actor"].GetValue<string>(), scene, self);
                    thing target = ResolveThing(obj["target"].GetValue<string>(), scene, self);
                    if (actor != null && target != null)
                        queue.AddEventChangeFacingToLookAt(actor, target);
                    break;
                }
                case "narrate": {
                    var corner = ParseNarrationCorner(obj["corner"]?.GetValue<string>());
                    var nStyle = ParseNarrationStyle(obj["style"]?.GetValue<string>());
                    Color? nColor  = ParseColor(obj["color"]?.GetValue<string>());
                    bool   strict  = obj["strict"]?.GetValue<bool>() ?? false;
                    queue.AddEventNarrate(obj["text"].GetValue<string>(), Vector2.Zero, strict: strict, corner: corner, style: nStyle, color: nColor);
                    break;
                }
                case "speak": {
                    string actorName = obj["actor"].GetValue<string>();
                    (Globals.DialogTypes dialogType, DialogBox.TailStyle? tailStyle) = ParseSpeakStyle(obj["style"]?.GetValue<string>());
                    Color? sColor = ParseColor(obj["color"]?.GetValue<string>());
                    if (ResolveThing(actorName, scene, self) is thing sa2)
                        queue.AddEventSpeak(sa2, obj["text"].GetValue<string>(), Vector2.Zero, tailStyle: tailStyle, dialogType: dialogType, color: sColor);
                    else if (scene.FindChild(actorName, true, false) is DialogAnchor sa)
                        queue.AddEventSpeakFromAnchor(sa, obj["text"].GetValue<string>(), tailStyle: tailStyle, dialogType: dialogType);
                    break;
                }
                case "think": {
                    string actorName = obj["actor"].GetValue<string>();
                    Color? tColor = ParseColor(obj["color"]?.GetValue<string>());
                    if (ResolveThing(actorName, scene, self) is thing ta2)
                        queue.AddEventThink(ta2, obj["text"].GetValue<string>(), Vector2.Zero, color: tColor);
                    else if (scene.FindChild(actorName, true, false) is DialogAnchor ta)
                        queue.AddEventThinkFromAnchor(ta, obj["text"].GetValue<string>());
                    break;
                }
                case "set_flag": {
                    string name = obj["name"].GetValue<string>();
                    string val  = obj["value"]?.GetValue<string>() ?? "true";
                    Variant v = (val == "true" || val == "false")
                        ? Variant.From(val == "true")
                        : Variant.From(val);
                    queue.AddEventAddFlag(new Globals.SceneFlag(scene.Name, name, v));
                    break;
                }
                case "change_facing": {
                    NPC actor = ResolveNPC(obj["actor"].GetValue<string>(), scene, self);
                    if (actor != null)
                        queue.AddEventChangeFacing(actor, obj["direction"].GetValue<string>() switch
                        {
                            "up"    => NPC.Direction.up,
                            "left"  => NPC.Direction.left,
                            "right" => NPC.Direction.right,
                            _       => NPC.Direction.down,
                        });
                    break;
                }
                case "play_animation": {
                    string actorName = obj["actor"]?.GetValue<string>() ?? "self";
                    thing  actor     = ResolveThing(actorName, scene, self);
                    if (actor?.animationPlayer != null)
                        queue.AddEventPlayAnimation(actor.animationPlayer, obj["animation"].GetValue<string>());
                    break;
                }
                case "add_to_inventory": {
                    if (self != null) queue.AddEventAddToInventory(self);
                    break;
                }
                case "toggle_exist": {
                    thing target = obj.ContainsKey("target")
                        ? ResolveThing(obj["target"].GetValue<string>(), scene, self)
                        : self;
                    bool? v = obj.ContainsKey("value") ? obj["value"].GetValue<bool>() : null;
                    if (target != null) queue.AddEventToggleExist(target, v);
                    break;
                }
                case "toggle_hide": {
                    thing target = obj.ContainsKey("target")
                        ? ResolveThing(obj["target"].GetValue<string>(), scene, self)
                        : self;
                    bool? v = obj.ContainsKey("value") ? obj["value"].GetValue<bool>() : null;
                    if (target != null) queue.AddEventToggleHide(target, v);
                    break;
                }
                case "wait": {
                    float seconds = obj["seconds"]?.GetValue<float>() ?? 1f;
                    queue.AddEventWait(seconds);
                    break;
                }
                case "remap_floor": {
                    string navReg  = obj["nav_region"]?.GetValue<string>() ?? "NavigationRegion2D";
                    string polygon = obj["polygon"].GetValue<string>();
                    queue.AddEventRemapFloor(navReg, polygon);
                    break;
                }
                case "if_flag": {
                    string flagName = obj["name"].GetValue<string>();
                    bool   expected = obj["is"]?.GetValue<bool>() ?? true;
                    bool   actual   = scene.GetFlag(flagName).Value.As<bool>();
                    var    branch   = (actual == expected ? obj["then"] : obj["else"])?.AsArray();
                    if (branch != null)
                        PopulateEventQueue(queue, branch, scene, self);
                    break;
                }
                case "exit": {
                    string dest      = Scenes.Resolve(obj["destination"]?.GetValue<string>() ?? "");
                    if (string.IsNullOrEmpty(dest)) break;
                    string arriveDir   = obj["arrive_dir"]?.GetValue<string>()   ?? "";
                    string arriveArea  = obj["arrive_area"]?.GetValue<string>()  ?? "";
                    string arriveThing = obj["arrive_thing"]?.GetValue<string>() ?? "";
                    bool   noWalk      = obj["no_walk"]?.GetValue<bool>() ?? false;
                    queue.AddEventExit(dest, arriveDir, arriveArea, !noWalk, arriveThing);
                    break;
                }
                case "run_sequence": {
                    string seqName = obj["name"]?.GetValue<string>() ?? "";
                    var    seq     = scene.GetNamedSequence(seqName);
                    if (seq != null) PopulateEventQueue(queue, seq, scene, self);
                    break;
                }
                case "conversation": {
                    string filePath = obj["file"].GetValue<string>();
                    queue.AddEventConversation(filePath);
                    break;
                }
                case "camera_pan": {
                    if (scene.camera == null) break;
                    var camTarget = ResolveCameraTarget(obj, scene);
                    if (!camTarget.HasValue) break;
                    float duration = obj["duration"]?.GetValue<float>() ?? 0.5f;
                    bool strict = obj["strict"]?.GetValue<bool>() ?? false;
                    queue.AddEventCameraPan(camTarget.Value, duration, strict);
                    break;
                }
                case "camera_zoom": {
                    if (scene.camera == null) break;
                    float zoom = obj["zoom"]?.GetValue<float>() ?? 1f;
                    float duration = obj["duration"]?.GetValue<float>() ?? 0.5f;
                    bool strict = obj["strict"]?.GetValue<bool>() ?? false;
                    queue.AddEventCameraZoom(zoom, ResolveCameraTarget(obj, scene), duration, strict);
                    break;
                }
                case "return_camera": {
                    if (scene.camera == null) break;
                    float duration = obj["duration"]?.GetValue<float>() ?? 0.5f;
                    bool strict = obj["strict"]?.GetValue<bool>() ?? false;
                    queue.AddEventReturnCamera(duration, strict);
                    break;
                }
            }
        }
    }

    // ── Condition / resolution helpers (shared with ConversationStep) ────────────

    public static bool ConditionMet(JsonObject obj, scene_script scene)
    {
        if (obj.ContainsKey("if_flag")     && !scene.GetFlag(obj["if_flag"].GetValue<string>()).Value.As<bool>())
            return false;
        if (obj.ContainsKey("if_not_flag") &&  scene.GetFlag(obj["if_not_flag"].GetValue<string>()).Value.As<bool>())
            return false;
        return true;
    }

    public static (Vector2 tail, NPC.Direction facing, Color color)? ResolveActorAnchor(string name, scene_script scene)
    {
        if (ResolveThing(name, scene) is thing t)
            return (t.topPoint, t.DialogFacing, t.dialogColor);
        if (scene.FindChild(name, true, false) is DialogAnchor anchor)
            return (anchor.GlobalPosition, anchor.Facing, anchor.dialogColor);
        return null;
    }

    // ── Shared JSON field parsers (also used by ConversationStep) ────────────────

    // Accepts hex ("#rrggbb") or Godot named colors ("yellow", "red", etc.). Returns null on empty/invalid.
    public static Color? ParseColor(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        if (Color.FromString(raw, new Color(0, 0, 0, 0)) is Color c && (c.A > 0f || raw == "transparent"))
            return c;
        try { return new Color(raw); } catch { return null; }
    }

    public static (Globals.DialogTypes type, DialogBox.TailStyle? tail) ParseSpeakStyle(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return (Globals.DialogTypes.speaking, null);
        var type = Globals.DialogTypes.speaking;
        DialogBox.TailStyle? tail = null;
        foreach (var token in raw.Replace(",", "|").Split('|'))
            switch (token.Trim())
            {
                case "exclaim":   type = Globals.DialogTypes.exclaim;   break;
                case "straight":  tail = DialogBox.TailStyle.Straight;  break;
                case "wavy":      tail = DialogBox.TailStyle.Wavy;      break;
                case "lightning": tail = DialogBox.TailStyle.Lightning; break;
                case "notail":    tail = DialogBox.TailStyle.NoTail;    break;
                case "curved":    tail = DialogBox.TailStyle.Curved;    break;
            }
        return (type, tail);
    }

    public static Globals.NarrationCorner ParseNarrationCorner(string raw) =>
        raw switch {
            "topleft"     => Globals.NarrationCorner.TopLeft,
            "topright"    => Globals.NarrationCorner.TopRight,
            "bottomleft"  => Globals.NarrationCorner.BottomLeft,
            "bottomright" => Globals.NarrationCorner.BottomRight,
            _             => Globals.NarrationCorner.Auto,
        };

    public static Globals.NarrationStyle ParseNarrationStyle(string raw) =>
        raw == "jagged" ? Globals.NarrationStyle.Jagged : Globals.NarrationStyle.Normal;

    public static thing ResolveThing(string name, scene_script scene, thing self = null)
    {
        if (name == "self") return self;
        if (name == "ego" || name == "character" || name == "ego_point") return scene.ego;
        return scene.FindChild(name, true, false) as thing;
    }

    public static NPC ResolveNPC(string name, scene_script scene, thing self = null)
    {
        if (name == "self") return self as NPC;
        if (name == "ego" || name == "character" || name == "ego_point") return scene.ego;
        return scene.FindChild(name, true, false) as NPC;
    }

    public static Vector2 ResolveAreaCenter(string shapeName, scene_script scene)
    {
        var shape = scene.GetNodeOrNull<CollisionShape2D>(shapeName);
        return shape?.Position ?? Vector2.Zero;
    }

    // Resolves a movement destination from a move/fly action object.
    // Handles: target (thing → interactPoint, else Node2D → Position), to_area, or [x,y] array.
    public static Vector2 ResolveMoveDest(JsonObject obj, scene_script scene, NPC actor, thing self = null)
    {
        if (obj.ContainsKey("target"))
        {
            string targetName = obj["target"].GetValue<string>();
            thing  target     = ResolveThing(targetName, scene, self);
            if (target != null) return target.interactPoint;
            var node = scene.FindChild(targetName, true, false) as Node2D;
            return node?.Position ?? actor.Position;
        }
        if (obj.ContainsKey("to_area"))
            return ResolveAreaCenter(obj["to_area"].GetValue<string>(), scene);
        var to = obj["to"].AsArray();
        return new Vector2(to[0].GetValue<float>(), to[1].GetValue<float>());
    }

    // Resolves a camera pan/zoom target position from a camera action object.
    // Handles: target (NPC → midpoint, thing/Node2D → GlobalPosition), or [x,y] array.
    public static Vector2? ResolveCameraTarget(JsonObject obj, scene_script scene)
    {
        if (obj.ContainsKey("to"))
        {
            var arr = obj["to"]?.AsArray();
            if (arr?.Count >= 2)
                return new Vector2(arr[0].GetValue<float>(), arr[1].GetValue<float>());
        }
        if (obj.ContainsKey("target"))
        {
            string name = obj["target"].GetValue<string>();
            NPC npc = ResolveNPC(name, scene);
            if (npc != null)
                return (npc.GlobalPosition + npc.topPoint) / 2f;
            thing t = ResolveThing(name, scene);
            if (t != null)
                return t.GlobalPosition;
            var node = scene.GetNodeOrNull<Node2D>(name);
            if (node != null)
                return node.GlobalPosition;
        }
        return null;
    }

    // Executes the remap_floor action: swaps in a new nav polygon from a Polygon2D guide node.
    public static void ExecuteRemapFloor(string navRegionNodeName, string guidePolygonNodeName, scene_script scene)
    {
        var navReg = scene.GetNodeOrNull<NavigationRegion2D>(navRegionNodeName ?? "NavigationRegion2D");
        if (navReg == null) return;
        var guide = navReg.GetNodeOrNull<Polygon2D>(guidePolygonNodeName);
        if (guide == null) return;
        var poly = new NavigationPolygon();
        poly.AddOutline(guide.Polygon);
#pragma warning disable CS0618
        poly.MakePolygonsFromOutlines();
#pragma warning restore CS0618
        navReg.NavigationPolygon = poly;
        scene.AddFlag($"nav_remap:{navRegionNodeName}:{guidePolygonNodeName}", true);
    }
}
