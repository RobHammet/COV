// ScriptParser.cs — converts a JSON action array into EventSequence events.
// Shared by the entry-script system, thing interactions, and any other caller.
// Supports: move, look_at, change_facing, narrate, speak, think,
//           set_flag, wait, play_animation, add_to_inventory, toggle_exist,
//           toggle_hide, remap_floor, if_flag, exit, run_sequence, conversation.
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

            if (obj.ContainsKey("if_flag") && !scene.GetFlag(obj["if_flag"].GetValue<string>()).Value.As<bool>())
                continue;
            if (obj.ContainsKey("if_not_flag") && scene.GetFlag(obj["if_not_flag"].GetValue<string>()).Value.As<bool>())
                continue;

            switch (obj["action"].GetValue<string>())
            {
                case "move": {
                    NPC actor = ResolveNPC(obj["actor"].GetValue<string>(), scene, self);
                    Vector2 dest;
                    if (obj.ContainsKey("target"))
                    {
                        string targetName = obj["target"].GetValue<string>();
                        thing  target     = ResolveThing(targetName, scene, self);
                        if (target != null)
                            dest = target.interactPoint;
                        else
                        {
                            var node = scene.FindChild(targetName, true, false) as Node2D;
                            dest = node?.Position ?? actor.Position;
                        }
                    }
                    else if (obj.ContainsKey("to_area"))
                    {
                        dest = ResolveAreaCenter(obj["to_area"].GetValue<string>(), scene);
                    }
                    else
                    {
                        var to = obj["to"].AsArray();
                        dest = new Vector2(to[0].GetValue<float>(), to[1].GetValue<float>());
                    }
                    bool strict = obj["strict"]?.GetValue<bool>() ?? false;
                    queue.AddEventMove(actor, dest, !strict);
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
                    Globals.NarrationCorner corner = obj["corner"]?.GetValue<string>() switch {
                        "topleft"     => Globals.NarrationCorner.TopLeft,
                        "topright"    => Globals.NarrationCorner.TopRight,
                        "bottomleft"  => Globals.NarrationCorner.BottomLeft,
                        "bottomright" => Globals.NarrationCorner.BottomRight,
                        _             => Globals.NarrationCorner.Auto,
                    };
                    queue.AddEventNarrate(obj["text"].GetValue<string>(), Vector2.Zero, corner: corner);
                    break;
                }
                case "speak": {
                    string actorName = obj["actor"].GetValue<string>();
                    NPC actor = ResolveNPC(actorName, scene, self);
                    (Globals.DialogTypes dialogType, DialogBox.TailStyle? tailStyle) = ParseSpeakStyle(obj["style"]?.GetValue<string>());
                    if (actor != null)
                        queue.AddEventSpeak(actor, obj["text"].GetValue<string>(), Vector2.Zero, tailStyle: tailStyle, dialogType: dialogType);
                    else if (scene.FindChild(actorName, true, false) is DialogAnchor sa)
                        queue.AddEventSpeakFromAnchor(sa, obj["text"].GetValue<string>(), tailStyle: tailStyle, dialogType: dialogType);
                    break;
                }
                case "think": {
                    string actorName = obj["actor"].GetValue<string>();
                    NPC actor = ResolveNPC(actorName, scene, self);
                    if (actor != null)
                        queue.AddEventThink(actor, obj["text"].GetValue<string>(), Vector2.Zero);
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
            }
        }
    }

    private static (Globals.DialogTypes type, DialogBox.TailStyle? tail) ParseSpeakStyle(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return (Globals.DialogTypes.speaking, null);
        var type = Globals.DialogTypes.speaking;
        DialogBox.TailStyle? tail = null;
        foreach (var token in raw.Split('|'))
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

    private static thing ResolveThing(string name, scene_script scene, thing self = null)
    {
        if (name == "self") return self;
        if (name == "ego")  return scene.ego;
        return scene.FindChild(name, true, false) as thing;
    }

    private static NPC ResolveNPC(string name, scene_script scene, thing self = null)
    {
        if (name == "self") return self as NPC;
        if (name == "ego" || name == "character" || name == "ego_point") return scene.ego;
        return scene.FindChild(name, true, false) as NPC;
    }

    private static Vector2 ResolveAreaCenter(string shapeName, scene_script scene)
    {
        var shape = scene.GetNodeOrNull<CollisionShape2D>(shapeName);
        return shape?.Position ?? Vector2.Zero;
    }
}
