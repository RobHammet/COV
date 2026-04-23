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
        string fullPath  = _dialogFile.StartsWith("res://") ? _dialogFile : $"res://{_dialogFile}";
        string fileText  = FileAccess.GetFileAsString(fullPath);
        var doc = fullPath.EndsWith(".covscript")
            ? CovScript.Parse(fileText)
            : JsonNode.Parse(fileText)?.AsObject();
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

    // Parsing/resolution helpers are in ScriptParser (public static) — no duplicates here.

    private async Task<string> RunDialogArray(JsonObject allNodes, JsonArray actions)
    {
        foreach (var item in actions)
        {
            var obj = item?.AsObject();
            if (obj == null) continue;
            if (!ScriptParser.ConditionMet(obj, _scene)) continue;

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
                        if (!ScriptParser.ConditionMet(o, _scene)) continue;
                        labels.Add(o["label"].GetValue<string>());
                        nodeIds.Add(o["node"].GetValue<string>());
                    }
                    if (labels.Count == 0) break;
                    string        choiceActor = obj.ContainsKey("actor") ? obj["actor"].GetValue<string>() : "ego";
                    var           ca          = ScriptParser.ResolveActorAnchor(choiceActor, _scene);
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
                    string speakActorName = obj["actor"].GetValue<string>();
                    var a = ScriptParser.ResolveActorAnchor(speakActorName, _scene);
                    if (a == null) break;
                    thing speakActor = ScriptParser.ResolveThing(speakActorName, _scene);
                    bool   strict   = obj["strict"]?.GetValue<bool>()  ?? false;
                    float? forSecs  = obj.ContainsKey("for") ? obj["for"].GetValue<float>() : null;
                    (Globals.DialogTypes type, DialogBox.TailStyle? tail) = ScriptParser.ParseSpeakStyle(obj["style"]?.GetValue<string>());
                    Color color = ScriptParser.ParseColor(obj["color"]?.GetValue<string>()) ?? a.Value.color;
                    DialogBox db = _scene.CreateDialog(
                        type, obj["text"].GetValue<string>(),
                        color, null, a.Value.tail, a.Value.facing,
                        strict: strict, tailStyle: tail, actor: speakActor);
                    if (forSecs.HasValue)
                    {
                        if (strict) _scene.mainScene.SetInteractMode(Globals.InteractModes.wait);
                        _scene.GetTree().CreateTimer(forSecs.Value, true).Connect("timeout",
                            Callable.From(() => {
                                if (GodotObject.IsInstanceValid(db)) db.CloseThisDialog();
                                if (strict) _scene.mainScene.SetInteractMode(Globals.InteractModes.walk);
                            }));
                    }
                    else
                        await _scene.ToSignal(db, "DialogClosed");
                    break;
                }

                case "think": {
                    string thinkActorName = obj["actor"].GetValue<string>();
                    var a = ScriptParser.ResolveActorAnchor(thinkActorName, _scene);
                    if (a == null) break;
                    thing thinkActor = ScriptParser.ResolveThing(thinkActorName, _scene);
                    bool   strict  = obj["strict"]?.GetValue<bool>()  ?? false;
                    float? forSecs = obj.ContainsKey("for") ? obj["for"].GetValue<float>() : null;
                    Color color = ScriptParser.ParseColor(obj["color"]?.GetValue<string>()) ?? a.Value.color;
                    DialogBox db = _scene.CreateDialog(
                        Globals.DialogTypes.thinking, obj["text"].GetValue<string>(),
                        color, null, a.Value.tail, a.Value.facing, strict: strict, actor: thinkActor);
                    if (forSecs.HasValue)
                    {
                        if (strict) _scene.mainScene.SetInteractMode(Globals.InteractModes.wait);
                        _scene.GetTree().CreateTimer(forSecs.Value, true).Connect("timeout",
                            Callable.From(() => {
                                if (GodotObject.IsInstanceValid(db)) db.CloseThisDialog();
                                if (strict) _scene.mainScene.SetInteractMode(Globals.InteractModes.walk);
                            }));
                    }
                    else
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
                    NPC   actor  = ScriptParser.ResolveNPC(obj["actor"].GetValue<string>(), _scene);
                    thing target = ScriptParser.ResolveThing(obj["target"].GetValue<string>(), _scene);
                    if (actor != null && target != null)
                        actor.ChangeFacingToLookAt(target);
                    break;
                }

                case "change_facing": {
                    NPC actor = ScriptParser.ResolveNPC(obj["actor"].GetValue<string>(), _scene);
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

                case "play_animation": {
                    thing actor = ScriptParser.ResolveThing(obj["actor"].GetValue<string>(), _scene);
                    actor?.animationPlayer?.Play(obj["animation"].GetValue<string>());
                    break;
                }

                case "move": {
                    NPC actor = ScriptParser.ResolveNPC(obj["actor"].GetValue<string>(), _scene);
                    if (actor == null) break;
                    actor.GoToLocation(ScriptParser.ResolveMoveDest(obj, _scene, actor));
                    await _scene.ToSignal(actor, "DestinationReached");
                    break;
                }

                case "fly": {
                    NPC actor = ScriptParser.ResolveNPC(obj["actor"].GetValue<string>(), _scene);
                    if (actor == null) break;
                    actor.FlyToLocation(ScriptParser.ResolveMoveDest(obj, _scene, actor));
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
                    thing  t     = tname != null ? ScriptParser.ResolveThing(tname, _scene) : null;
                    bool?  v     = obj.ContainsKey("value") ? obj["value"].GetValue<bool>() : null;
                    t?.ToggleExist(v);
                    break;
                }

                case "toggle_hide": {
                    string tname = obj.ContainsKey("target") ? obj["target"].GetValue<string>() : null;
                    thing  t     = tname != null ? ScriptParser.ResolveThing(tname, _scene) : null;
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

                case "add_to_inventory": {
                    string targetName = obj["target"]?.GetValue<string>();
                    thing  target     = targetName != null ? ScriptParser.ResolveThing(targetName, _scene) : null;
                    target?.AddToInventory();
                    break;
                }

                case "remap_floor": {
                    ScriptParser.ExecuteRemapFloor(
                        obj["nav_region"]?.GetValue<string>() ?? "NavigationRegion2D",
                        obj["polygon"].GetValue<string>(),
                        _scene);
                    break;
                }

                case "exit": {
                    string dest = Scenes.Resolve(obj["destination"]?.GetValue<string>() ?? "");
                    if (string.IsNullOrEmpty(dest)) break;
                    string arriveDir   = obj["arrive_dir"]?.GetValue<string>()   ?? "";
                    string arriveArea  = obj["arrive_area"]?.GetValue<string>()  ?? "";
                    string arriveThing = obj["arrive_thing"]?.GetValue<string>() ?? "";
                    bool   noWalk      = obj["no_walk"]?.GetValue<bool>() ?? false;
                    var arrival = new scene_script.ArrivalData(
                        System.IO.Path.GetFileNameWithoutExtension(_scene.SceneFilePath),
                        arriveDir, arriveArea, !noWalk, arriveThing);
                    _scene.mainScene.ChangeSceneToFile(dest, arrival);
                    break;
                }

                case "run_sequence": {
                    string seqName = obj["name"]?.GetValue<string>() ?? "";
                    var    seq     = _scene.GetNamedSequence(seqName);
                    if (seq != null)
                    {
                        string result = await RunDialogArray(allNodes, seq);
                        if (result != null) return result;
                    }
                    break;
                }

                case "finish_and_run": {
                    string seqName = obj["name"]?.GetValue<string>() ?? "";
                    var    seq     = _scene.GetNamedSequence(seqName);
                    if (seq != null)
                        ScriptParser.PopulateEventQueue(_scene.eventQueue, seq, _scene);
                    return null;
                }

                case "camera_pan": {
                    if (_scene.camera == null) break;
                    var camTarget = ScriptParser.ResolveCameraTarget(obj, _scene);
                    if (!camTarget.HasValue) break;
                    float duration = obj["duration"]?.GetValue<float>() ?? 0.5f;
                    bool strict = obj["strict"]?.GetValue<bool>() ?? false;
                    _scene.hasCameraControl = false;
                    var tween = _scene.CreateTween();
                    tween.TweenProperty(_scene.camera, "global_position", _scene.ClampCameraPos(camTarget.Value), duration)
                         .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
                    if (strict) await _scene.ToSignal(tween, "finished");
                    break;
                }

                case "camera_zoom": {
                    if (_scene.camera == null) break;
                    float zoom     = obj["zoom"]?.GetValue<float>() ?? 1f;
                    float duration = obj["duration"]?.GetValue<float>() ?? 0.5f;
                    bool strict    = obj["strict"]?.GetValue<bool>() ?? false;
                    _scene.hasCameraControl = false;
                    var zoomVec   = new Vector2(zoom, zoom);
                    var tween     = _scene.CreateTween();
                    var camTarget = ScriptParser.ResolveCameraTarget(obj, _scene);
                    if (camTarget.HasValue)
                    {
                        tween.TweenProperty(_scene.camera, "global_position", _scene.ClampCameraPos(camTarget.Value, zoomVec), duration)
                             .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
                        tween.Parallel().TweenProperty(_scene.camera, "zoom", zoomVec, duration)
                             .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
                    }
                    else
                    {
                        tween.TweenProperty(_scene.camera, "zoom", zoomVec, duration)
                             .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
                    }
                    if (strict) await _scene.ToSignal(tween, "finished");
                    break;
                }

                case "return_camera": {
                    if (_scene.camera == null) break;
                    float duration = obj["duration"]?.GetValue<float>() ?? 0.5f;
                    bool strict    = obj["strict"]?.GetValue<bool>() ?? false;
                    _scene.hasCameraControl = true;
                    if (_scene.camera.Zoom != _scene.cameraOriginalZoom)
                    {
                        var tween = _scene.CreateTween();
                        tween.TweenProperty(_scene.camera, "zoom", _scene.cameraOriginalZoom, duration)
                             .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
                        if (strict) await _scene.ToSignal(tween, "finished");
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

    public void AddEventFly(NPC actor, Vector2 dest, bool _interruptable = false) =>
        _steps.Add(new SignalStep(
            () => { actor.FlyToLocation(dest); return actor; },
            "DestinationReached",
            _interruptable,
            debugName: "fly"));

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
            ScriptParser.ExecuteRemapFloor(navRegionNodeName, guidePolygonNodeName, _scene)));

    public void AddEventSpeak(thing actor, string phrase, Vector2? position = null,
                              bool _interruptable = false, bool strict = false,
                              DialogBox.TailStyle? tailStyle = null,
                              Globals.DialogTypes dialogType = Globals.DialogTypes.speaking,
                              Color? color = null) =>
        _steps.Add(new SignalStep(
            () => actor.parentScene.CreateDialog(
                dialogType, phrase, color ?? actor.dialogColor, position,
                actor.topPoint, actor.DialogFacing, strict: strict, tailStyle: tailStyle, actor: actor),
            "DialogClosed", _interruptable, "speak"));

    public void AddEventThink(thing actor, string phrase, Vector2? position = null,
                              bool _interruptable = false, bool strict = false,
                              Color? color = null) =>
        _steps.Add(new SignalStep(
            () => actor.parentScene.CreateDialog(
                Globals.DialogTypes.thinking, phrase, color ?? actor.dialogColor, position,
                new Vector2(actor.Position.X, actor.topPoint.Y), actor.DialogFacing, strict: strict),
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

    public void AddEventCameraPan(Vector2 targetPos, float duration = 0.5f, bool strict = false) =>
        _steps.Add(new SignalStep(
            () => {
                _scene.hasCameraControl = false;
                var tween = _scene.CreateTween();
                tween.TweenProperty(_scene.camera, "global_position", _scene.ClampCameraPos(targetPos), duration)
                     .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
                return tween;
            },
            "finished", !strict, "camera_pan"));

    public void AddEventCameraZoom(float zoom, Vector2? targetPos, float duration = 0.5f, bool strict = false) =>
        _steps.Add(new SignalStep(
            () => {
                _scene.hasCameraControl = false;
                var zoomVec = new Vector2(zoom, zoom);
                var tween   = _scene.CreateTween();
                if (targetPos.HasValue)
                {
                    tween.TweenProperty(_scene.camera, "global_position", _scene.ClampCameraPos(targetPos.Value, zoomVec), duration)
                         .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
                    tween.Parallel().TweenProperty(_scene.camera, "zoom", zoomVec, duration)
                         .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
                }
                else
                {
                    tween.TweenProperty(_scene.camera, "zoom", zoomVec, duration)
                         .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
                }
                return tween;
            },
            "finished", !strict, "camera_zoom"));

    public void AddEventReturnCamera(float duration = 0.5f, bool strict = false) =>
        _steps.Add(new SignalStep(
            () => {
                _scene.hasCameraControl = true;
                var tween = _scene.CreateTween();
                if (_scene.camera != null && _scene.camera.Zoom != _scene.cameraOriginalZoom)
                    tween.TweenProperty(_scene.camera, "zoom", _scene.cameraOriginalZoom, duration)
                         .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
                else
                    tween.TweenInterval(0.001f);
                return tween;
            },
            "finished", !strict, "return_camera"));
}
