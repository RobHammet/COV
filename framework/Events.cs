// Events.cs — the event system: step types and EventSequence.
//
// EventSequence is the ordered step queue used by every room scene.
// Room scripts and thing overrides push steps via AddEvent* helpers;
// scene_script._Process() calls eventQueue.Tick() once per frame.
//
// STEP TYPES
//   InstantStep      — fires an action and is immediately complete.
//   SignalStep       — fires an action (returning the GodotObject to listen on),
//                      then completes when the named signal fires.
//   ConversationStep — runs a branching dialog JSON file; internal async is
//                      fully contained, outer EventSequence just polls IsComplete.
//
// USAGE
//   // In a thing override:
//   parentScene.eventQueue.AddEventMove(character, doorPos);
//   parentScene.eventQueue.AddEventSpeak(character, "IT'S LOCKED.");
//   // scene_script._Process() will run them in order the next frame.

using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

// ---------------------------------------------------------------------------
// IEventStep — interface for a single step in a sequence.
// ---------------------------------------------------------------------------
public interface IEventStep
{
    void   Start();
    bool   IsComplete    { get; }
    bool   IsInterruptable { get; }
    string DebugName     { get; }
}

// ---------------------------------------------------------------------------
// InstantStep — executes an action synchronously; IsComplete is always true.
// ---------------------------------------------------------------------------
public class InstantStep : IEventStep
{
    private readonly Action _action;
    public bool   IsInterruptable { get; }
    public string DebugName       => "instant";

    public InstantStep(Action action, bool interruptable = true)
    {
        _action        = action;
        IsInterruptable = interruptable;
    }

    public void Start()        => _action();
    public bool IsComplete     => true;
}

// ---------------------------------------------------------------------------
// SignalStep — calls a factory Func (which performs the action AND returns the
// GodotObject to listen on), then completes when the named signal fires once.
// ---------------------------------------------------------------------------
public class SignalStep : IEventStep
{
    private readonly Func<GodotObject> _start;
    private readonly StringName        _signal;
    private bool                       _done;

    public bool   IsInterruptable { get; }
    public string DebugName       { get; }

    public SignalStep(Func<GodotObject> start, StringName signal,
                      bool interruptable = false, string debugName = null)
    {
        _start          = start;
        _signal         = signal;
        IsInterruptable = interruptable;
        DebugName       = debugName ?? signal.ToString();
    }

    public void Start()
    {
        var source = _start();
        source?.Connect(_signal,
                        Callable.From(() => _done = true),
                        (uint)GodotObject.ConnectFlags.OneShot);
    }

    public bool IsComplete => _done;
}

// ---------------------------------------------------------------------------
// ConversationStep — runs a branching dialog JSON file. Internal async is
// fully contained; the outer EventSequence just polls IsComplete each frame.
// ---------------------------------------------------------------------------
public class ConversationStep : IEventStep
{
    private readonly scene_script _scene;
    private readonly string       _dialogFile;
    private bool                  _done;

    public bool   IsComplete      => _done;
    public bool   IsInterruptable { get; }
    public string DebugName       => $"conversation({_dialogFile})";

    public ConversationStep(scene_script scene, string dialogFile, bool interruptable = false)
    {
        _scene          = scene;
        _dialogFile     = dialogFile;
        IsInterruptable = interruptable;
    }

    public void Start() => RunAsync();

    private async void RunAsync()
    {
        string jsonText = FileAccess.GetFileAsString($"res://{_dialogFile}");
        var doc = JsonNode.Parse(jsonText)?.AsObject();
        if (doc == null) { _done = true; return; }

        var    allNodes = doc["nodes"]?.AsObject();
        string nodeId   = doc["start"]?.GetValue<string>();

        var prevMode = _scene.mainScene.GetInteractMode();
        _scene.mainScene.SetInteractMode(Globals.InteractModes.walk);

        while (nodeId != null && allNodes != null)
        {
            var nodeArray = allNodes[nodeId]?.AsArray();
            if (nodeArray == null) break;
            nodeId = await RunDialogArray(allNodes, nodeArray);
        }

        _scene.mainScene.SetInteractMode(prevMode);
        _done = true;
    }

    private bool DialogConditionMet(JsonObject obj)
    {
        if (obj.ContainsKey("if_flag")     && !_scene.GetFlag(obj["if_flag"].GetValue<string>()).Value.As<bool>())
            return false;
        if (obj.ContainsKey("if_not_flag") &&  _scene.GetFlag(obj["if_not_flag"].GetValue<string>()).Value.As<bool>())
            return false;
        return true;
    }

    private NPC ResolveDialogNPC(string name) =>
        (name == "character" || name == "ego" || name == "ego_point")
            ? _scene.ego
            : _scene.FindChild(name, true, false) as NPC;

    private (Vector2 tail, NPC.Direction facing, Color color)? ResolveActorAnchor(string name)
    {
        if (ResolveDialogNPC(name) is NPC npc)
            return (npc.topPoint, npc.Facing, npc.dialogColor);
        if (_scene.FindChild(name, true, false) is DialogAnchor anchor)
            return (anchor.GlobalPosition, anchor.Facing, anchor.dialogColor);
        return null;
    }

    private thing ResolveDialogThing(string name) => name == "character"
        ? _scene.ego
        : _scene.FindChild(name, true, false) as thing;

    // Parsing helpers are in ScriptParser (public static) — no duplicates here.

    private async Task<string> RunDialogArray(JsonObject allNodes, JsonArray actions)
    {
        foreach (var item in actions)
        {
            var obj = item?.AsObject();
            if (obj == null) continue;
            if (!DialogConditionMet(obj)) continue;

            string action = obj["action"]?.GetValue<string>();
            if (action == null) continue;

            switch (action)
            {
                case "call":
                    return obj["node"].GetValue<string>();

                case "choices": {
                    var rawOptions = obj["options"]?.AsArray();
                    if (rawOptions == null) break;
                    var labels  = new List<string>();
                    var nodeIds = new List<string>();
                    foreach (var opt in rawOptions)
                    {
                        var o = opt.AsObject();
                        if (!DialogConditionMet(o)) continue;
                        labels.Add(o["label"].GetValue<string>());
                        nodeIds.Add(o["node"].GetValue<string>());
                    }
                    if (labels.Count == 0) break;
                    string        choiceActor = obj.ContainsKey("actor") ? obj["actor"].GetValue<string>() : "ego";
                    var           ca          = ResolveActorAnchor(choiceActor);
                    Vector2       tp          = ca?.tail   ?? Vector2.Zero;
                    NPC.Direction facing      = ca?.facing ?? NPC.Direction.down;
                    DialogBox db = _scene.CreateDialog(
                        Globals.DialogTypes.choice, null, null, null, tp, facing, labels.ToArray());
                    Godot.Variant[] ret = await _scene.ToSignal(db, "DialogClosed");
                    int idx = (int)ret[0];
                    if (idx >= 0 && idx < nodeIds.Count)
                        return nodeIds[idx];
                    break;
                }

                case "speak": {
                    var a = ResolveActorAnchor(obj["actor"].GetValue<string>());
                    if (a == null) break;
                    bool strict = obj["strict"]?.GetValue<bool>() ?? false;
                    (Globals.DialogTypes type, DialogBox.TailStyle? tail) = ScriptParser.ParseSpeakStyle(obj["style"]?.GetValue<string>());
                    Color color = ScriptParser.ParseColor(obj["color"]?.GetValue<string>()) ?? a.Value.color;
                    DialogBox db = _scene.CreateDialog(
                        type, obj["text"].GetValue<string>(),
                        color, null, a.Value.tail, a.Value.facing,
                        strict: strict, tailStyle: tail);
                    await _scene.ToSignal(db, "DialogClosed");
                    break;
                }

                case "think": {
                    var a = ResolveActorAnchor(obj["actor"].GetValue<string>());
                    if (a == null) break;
                    bool strict = obj["strict"]?.GetValue<bool>() ?? false;
                    Color color = ScriptParser.ParseColor(obj["color"]?.GetValue<string>()) ?? a.Value.color;
                    DialogBox db = _scene.CreateDialog(
                        Globals.DialogTypes.thinking, obj["text"].GetValue<string>(),
                        color, null, a.Value.tail, a.Value.facing, strict: strict);
                    await _scene.ToSignal(db, "DialogClosed");
                    break;
                }

                case "narrate": {
                    bool strict = obj["strict"]?.GetValue<bool>() ?? false;
                    var corner = ScriptParser.ParseNarrationCorner(obj["corner"]?.GetValue<string>());
                    var nStyle = ScriptParser.ParseNarrationStyle(obj["style"]?.GetValue<string>());
                    Color color = ScriptParser.ParseColor(obj["color"]?.GetValue<string>()) ?? Colors.White;
                    DialogBox db = _scene.CreateDialog(
                        Globals.DialogTypes.narration, obj["text"].GetValue<string>(),
                        color, strict: strict, corner: corner, narrationStyle: nStyle);
                    await _scene.ToSignal(db, "DialogClosed");
                    break;
                }

                case "look_at": {
                    NPC   actor  = ResolveDialogNPC(obj["actor"].GetValue<string>());
                    thing target = ResolveDialogThing(obj["target"].GetValue<string>());
                    if (actor != null && target != null)
                        actor.ChangeFacingToLookAt(target);
                    break;
                }

                case "change_facing": {
                    NPC actor = ResolveDialogNPC(obj["actor"].GetValue<string>());
                    if (actor != null)
                        actor.ChangeFacing(obj["direction"].GetValue<string>() switch
                        {
                            "up"    => NPC.Direction.up,
                            "left"  => NPC.Direction.left,
                            "right" => NPC.Direction.right,
                            _       => NPC.Direction.down,
                        });
                    break;
                }

                case "move": {
                    NPC actor = ResolveDialogNPC(obj["actor"].GetValue<string>());
                    if (actor == null) break;
                    Vector2 dest;
                    if (obj.ContainsKey("target"))
                    {
                        thing target = ResolveDialogThing(obj["target"].GetValue<string>());
                        dest = target?.interactPoint ?? actor.Position;
                    }
                    else
                    {
                        var to = obj["to"].AsArray();
                        dest = new Vector2(to[0].GetValue<float>(), to[1].GetValue<float>());
                    }
                    actor.GoToLocation(dest);
                    await _scene.ToSignal(actor, "DestinationReached");
                    break;
                }

                case "wait": {
                    float secs = obj["seconds"]?.GetValue<float>() ?? 1f;
                    await _scene.ToSignal(_scene.GetTree().CreateTimer(secs, true), "timeout");
                    break;
                }

                case "toggle_exist": {
                    string tname = obj.ContainsKey("target") ? obj["target"].GetValue<string>() : null;
                    thing  t     = tname != null ? ResolveDialogThing(tname) : null;
                    bool?  v     = obj.ContainsKey("value") ? obj["value"].GetValue<bool>() : null;
                    t?.ToggleExist(v);
                    break;
                }

                case "toggle_hide": {
                    string tname = obj.ContainsKey("target") ? obj["target"].GetValue<string>() : null;
                    thing  t     = tname != null ? ResolveDialogThing(tname) : null;
                    bool?  v     = obj.ContainsKey("value") ? obj["value"].GetValue<bool>() : null;
                    t?.ToggleHide(v);
                    break;
                }

                case "set_flag": {
                    string fname = obj["name"].GetValue<string>();
                    string val   = obj["value"]?.GetValue<string>() ?? "true";
                    Variant fv   = (val == "true" || val == "false")
                        ? Variant.From(val == "true")
                        : Variant.From(val);
                    _scene.AddFlag(fname, fv);
                    break;
                }

                case "if_flag": {
                    string flagName = obj["name"].GetValue<string>();
                    bool   expected = obj["is"]?.GetValue<bool>() ?? true;
                    bool   actual   = _scene.GetFlag(flagName).Value.As<bool>();
                    var    branch   = (actual == expected ? obj["then"] : obj["else"])?.AsArray();
                    if (branch != null)
                    {
                        string result = await RunDialogArray(allNodes, branch);
                        if (result != null) return result;
                    }
                    break;
                }
            }
        }
        return null;
    }
}

// ---------------------------------------------------------------------------
// EventSequence — ordered list of IEventStep, advanced by Tick() each frame.
// Instantiate with a parent scene, push steps with the AddEvent* helpers.
// scene_script._Process() calls eventQueue.Tick() automatically.
// ---------------------------------------------------------------------------
public class EventSequence
{
    private readonly scene_script     _scene;
    private readonly List<IEventStep> _steps        = new();
    private          int              _current      = -1;
    private          bool             _weDidSuspend;

    // Readable by external callers (thing.cs, scene_script.cs).
    public bool isInterrupted { get; private set; }
    public bool isRunning     => _steps.Count > 0;

    // Exposed for debug display in scene_script._Process().
    public IReadOnlyList<IEventStep> Steps => _steps;

    public EventSequence(scene_script scene) => _scene = scene;

    public int  Count() => _steps.Count;

    public void Clear()
    {
        _steps.Clear();
        _current       = -1;
        isInterrupted  = false;
        _weDidSuspend  = false;
    }

    // Interrupt the running sequence if the current step allows it.
    // Sets isInterrupted so callers know to act (stop walking, etc.).
    public void TryInterrupt()
    {
        if (_steps.Count == 0 || _current < 0 || _current >= _steps.Count) return;
        if (!_steps[_current].IsInterruptable) return;
        _steps.Clear();
        _current      = -1;
        _weDidSuspend = false;
        isInterrupted = true;
    }

    // Called once per frame from scene_script._Process().
    // Starts the next pending step or advances past a completed one.
    public void Tick()
    {
        if (_steps.Count == 0) return;

        // First tick after steps were added: start the first step.
        if (_current < 0)
        {
            _current = 0;
            ApplySuspension(_steps[0]);
            _steps[0].Start();
            return;
        }

        if (_current >= _steps.Count) return;
        if (!_steps[_current].IsComplete) return;

        _current++;
        if (_current >= _steps.Count)
        {
            Done();
            return;
        }

        ApplySuspension(_steps[_current]);
        _steps[_current].Start();
    }

    private void ApplySuspension(IEventStep step)
    {
        if (!step.IsInterruptable)
        {
            _scene.SuspendSceneInput();
            _weDidSuspend = true;
        }
        else if (_weDidSuspend)
        {
            _scene.UnsuspendSceneInput();
            _weDidSuspend = false;
        }
    }

    private void Done()
    {
        bool wasSuspended = _weDidSuspend;
        Clear();
        if (wasSuspended)
            _scene.UnsuspendSceneInput();
    }

    // -------------------------------------------------------------------------
    // AddEvent* helpers — create an IEventStep and append it to the queue.
    // Signatures are identical to the old Event-based helpers so call sites
    // (thing overrides, scene scripts, PopulateEventQueue) need no changes.
    // -------------------------------------------------------------------------

    public void AddEventWait(float seconds) =>
        _steps.Add(new SignalStep(
            () => _scene.GetTree().CreateTimer(seconds, true),
            "timeout",
            debugName: $"wait({seconds}s)"));

    public void AddEventMove(NPC actor, Vector2 dest, bool _interruptable = false) =>
        _steps.Add(new SignalStep(
            () => { actor.GoToLocation(dest); return actor; },
            "DestinationReached",
            _interruptable,
            debugName: "move"));

    public void AddEventChangeFacing(NPC actor, NPC.Direction facing, bool _interruptable = false) =>
        _steps.Add(new InstantStep(() => actor.ChangeFacing(facing), _interruptable));

    public void AddEventChangeFacingToLookAt(NPC actor, Node2D target, bool _interruptable = false) =>
        _steps.Add(new InstantStep(() => actor.ChangeFacingToLookAt(target as thing), _interruptable));

    public void AddEventPlayAnimation(AnimationPlayer player, string animName, bool _interruptable = false) =>
        _steps.Add(new InstantStep(() => player.Play(animName), _interruptable));

    public void AddEventAddToInventory(thing t) =>
        _steps.Add(new InstantStep(() => t.AddToInventory()));

    public void AddEventRemoveFromInventory(InventoryItem.ItemType itemType) =>
        _steps.Add(new InstantStep(() =>
        {
            var inv = _scene.mainScene.inventory;
            for (int i = 0; i < inv.Count; i++)
                if (inv[i].Type == itemType) { inv.RemoveAt(i); break; }
        }));

    public void AddEventAddFlag(Globals.SceneFlag flag) =>
        _steps.Add(new InstantStep(() => _scene.AddFlag(flag.Name, flag.Value)));

    public void AddEventToggleExist(thing t, bool? value = null) =>
        _steps.Add(new InstantStep(() => t?.ToggleExist(value)));

    public void AddEventToggleHide(thing t, bool? value = null) =>
        _steps.Add(new InstantStep(() => t?.ToggleHide(value)));

    public void AddEventExit(string dest, string arriveDir = "", string arriveArea = "", bool arriveWalk = true, string arriveThing = "") =>
        _steps.Add(new InstantStep(() =>
        {
            var arrival = new scene_script.ArrivalData(System.IO.Path.GetFileNameWithoutExtension(_scene.SceneFilePath), arriveDir, arriveArea, arriveWalk, arriveThing);
            _scene.mainScene.ChangeSceneToFile(dest, arrival);
        }));

    public void AddEventRemapFloor(string navRegionNodeName, string guidePolygonNodeName) =>
        _steps.Add(new InstantStep(() =>
        {
            var navReg = _scene.GetNodeOrNull<NavigationRegion2D>(navRegionNodeName ?? "NavigationRegion2D");
            if (navReg == null) return;
            var guide = navReg.GetNodeOrNull<Polygon2D>(guidePolygonNodeName);
            if (guide == null) return;
            var poly = new NavigationPolygon();
            poly.AddOutline(guide.Polygon);
#pragma warning disable CS0618
            poly.MakePolygonsFromOutlines();
#pragma warning restore CS0618
            navReg.NavigationPolygon = poly;
        }));

    public void AddEventSpeak(NPC actor, string phrase, Vector2? position = null,
                              bool _interruptable = false, bool strict = false,
                              DialogBox.TailStyle? tailStyle = null,
                              Globals.DialogTypes dialogType = Globals.DialogTypes.speaking,
                              Color? color = null) =>
        _steps.Add(new SignalStep(
            () => actor.parentScene.CreateDialog(
                dialogType, phrase, color ?? actor.dialogColor, position,
                actor.topPoint, actor.Facing, strict: strict, tailStyle: tailStyle),
            "DialogClosed", _interruptable, "speak"));

    public void AddEventThink(NPC actor, string phrase, Vector2? position = null,
                              bool _interruptable = false, bool strict = false,
                              Color? color = null) =>
        _steps.Add(new SignalStep(
            () => actor.parentScene.CreateDialog(
                Globals.DialogTypes.thinking, phrase, color ?? actor.dialogColor, position,
                new Vector2(actor.Position.X, actor.topPoint.Y), actor.Facing, strict: strict),
            "DialogClosed", _interruptable, "think"));

    public void AddEventSpeakFromAnchor(DialogAnchor anchor, string phrase,
                                        bool _interruptable = false, bool strict = false,
                                        DialogBox.TailStyle? tailStyle = null,
                                        Globals.DialogTypes dialogType = Globals.DialogTypes.speaking) =>
        _steps.Add(new SignalStep(
            () => _scene.CreateDialog(
                dialogType, phrase, anchor.dialogColor, null,
                anchor.GlobalPosition, anchor.Facing, strict: strict, tailStyle: tailStyle),
            "DialogClosed", _interruptable, "speak(anchor)"));

    public void AddEventThinkFromAnchor(DialogAnchor anchor, string phrase,
                                        bool _interruptable = false, bool strict = false) =>
        _steps.Add(new SignalStep(
            () => _scene.CreateDialog(
                Globals.DialogTypes.thinking, phrase, anchor.dialogColor, null,
                anchor.GlobalPosition, anchor.Facing, strict: strict),
            "DialogClosed", _interruptable, "think(anchor)"));

    public void AddEventNarrate(string phrase, Vector2? position = null,
                                bool _interruptable = false, bool strict = false,
                                Globals.NarrationCorner corner = Globals.NarrationCorner.Auto,
                                Globals.NarrationStyle  style  = Globals.NarrationStyle.Normal,
                                Color? color = null) =>
        _steps.Add(new SignalStep(
            () => _scene.CreateDialog(Globals.DialogTypes.narration, phrase,
                                      color ?? Colors.White, position, strict: strict, corner: corner,
                                      narrationStyle: style),
            "DialogClosed", _interruptable, "narrate"));

    public void AddEventConversation(string dialogFile, Vector2? position = null,
                                     bool _interruptable = false) =>
        _steps.Add(new ConversationStep(_scene, dialogFile, _interruptable));
}
