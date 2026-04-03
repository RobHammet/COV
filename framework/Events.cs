// Events.cs — the event system: Event and EventSequence.
//
// EventSequence is the async command queue used by every room scene.
// Room scripts and thing overrides push events onto scene_script.eventQueue;
// scene_script._Process() calls eventQueue.ExecuteAll() once per frame when
// events are waiting.
//
// USAGE
//   // In a thing override:
//   parentScene.eventQueue.AddEventMove(character, doorPos);
//   parentScene.eventQueue.AddEventSpeak(character, "IT'S LOCKED.");
//   // scene_script._Process() will run them in order the next frame.
//
// SUSPEND TYPE
//   normal       — suspend input for non-interruptable events, allow for interruptable ones
//   suspend_all  — always suspend (used during transitions)
//   unsuspend_all — always unsuspend (reserved)

using Godot;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

// ---------------------------------------------------------------------------
// Event — a single step in a sequence. Created by EventSequence.AddEvent*
// helpers; never instantiated directly.
// ---------------------------------------------------------------------------
public partial class Event : Node
{
    public enum EventType
    {
        move                  = 0,
        narrate               = 1,
        character_speak       = 2,
        change_facing         = 3,
        play_animation        = 4,
        add_to_inventory      = 5,
        remove_from_inventory = 6,
        character_think       = 7,
        change_facing_lookat  = 8,
        add_scene_flag        = 9,
        conversation          = 10,
        none                  = 11,
        wait                  = 12,
        remap_floor           = 13,
    }

    [Signal] public delegate void EventFinishedEventHandler();

    public string               Phrase               { get; set; }
    public string               dialogFile;
    public string               navRegionNodeName;
    public string               guidePolygonNodeName;
    public Vector2              Position             { get; set; }
    public EventType            Type;
    public Color                Color;
    public bool                 IsFinished           { get; set; }
    public bool                 IsInProgress         { get; set; }
    public bool                 IsInterruptable      { get; set; }
    public NPC                  ActingCharacter;
    public AnimationPlayer      animationPlayer;
    public string               animationToPlay;
    public scene_script         ParentScene;
    public NPC.Direction  NewFacing;
    public thing                interactThing;
    public InventoryItem.ItemType itemType;
    public Globals.SceneFlag    sceneFlag;
    public float                duration;

    // ---------------------------------------------------------------------------
    // Execute — dispatch to the appropriate Execute* method.
    // ---------------------------------------------------------------------------
    public void Execute()
    {
        switch (Type)
        {
            case EventType.move:                  ExecuteMovement();          break;
            case EventType.narrate:               ExecuteNarration();         break;
            case EventType.character_speak:       ExecuteDialog();            break;
            case EventType.character_think:       ExecuteThought();           break;
            case EventType.conversation:          ExecuteConversation();      break;
            case EventType.change_facing:         ExecuteChangeFacing();      break;
            case EventType.change_facing_lookat:  ExecuteChangeFacingToLookAt(); break;
            case EventType.play_animation:        ExecutePlayAnimation();     break;
            case EventType.add_to_inventory:      ExecuteAddToInventory();    break;
            case EventType.remove_from_inventory: ExecuteRemoveFromInventory(); break;
            case EventType.add_scene_flag:        ExecuteAddFlag();           break;
            case EventType.wait:                  ExecuteWait();              break;
            case EventType.remap_floor:           ExecuteRemapFloor();        break;
        }
    }

    // --- Instant events (emit EventFinished immediately) --------------------

    public void ExecuteRemapFloor()
    {
        var navReg = ParentScene.GetNodeOrNull<NavigationRegion2D>(navRegionNodeName ?? "NavigationRegion2D");
        if (navReg != null)
        {
            var guide = navReg.GetNodeOrNull<Polygon2D>(guidePolygonNodeName);
            if (guide != null)
            {
                var poly = new NavigationPolygon();
                poly.AddOutline(guide.Polygon);
#pragma warning disable CS0618
                poly.MakePolygonsFromOutlines();
#pragma warning restore CS0618
                navReg.NavigationPolygon = poly;
            }
        }
        EmitSignal(SignalName.EventFinished);
    }

    public void ExecuteAddFlag()
    {
        ParentScene.AddFlag(sceneFlag.Name, sceneFlag.Value);
        EmitSignal(SignalName.EventFinished);
    }

    public void ExecuteAddToInventory()
    {
        IsInProgress = true;
        interactThing.AddToInventory();
        IsFinished = true;
        IsInProgress = false;
        EmitSignal(SignalName.EventFinished);
    }

    public void ExecuteRemoveFromInventory()
    {
        IsInProgress = true;
        var inv = ParentScene.mainScene.inventory;
        for (int i = 0; i < inv.Count; i++)
        {
            if (inv[i].Type == itemType)
            {
                inv.RemoveAt(i);
                break;
            }
        }
        IsFinished = true;
        IsInProgress = false;
        EmitSignal(SignalName.EventFinished);
    }

    public void ExecuteChangeFacing()
    {
        ActingCharacter.ChangeFacing(NewFacing);
        EmitSignal(SignalName.EventFinished);
    }

    public void ExecuteChangeFacingToLookAt()
    {
        ActingCharacter.ChangeFacingToLookAt(interactThing);
        EmitSignal(SignalName.EventFinished);
    }

    public void ExecutePlayAnimation()
    {
        animationPlayer.Play(animationToPlay);
        EmitSignal(SignalName.EventFinished);
    }

    // --- Async events (emit EventFinished after awaiting a signal) ----------

    public async void ExecuteNarration()
    {
        IsInProgress = true;
        DialogBox db = ParentScene.CreateDialog(Globals.DialogTypes.narration, Phrase, Color, Position);
        await ToSignal(db, "DialogClosed");
        IsFinished = true;
        IsInProgress = false;
        EmitSignal(SignalName.EventFinished);
    }

    public async void ExecuteDialog()
    {
        IsInProgress = true;
        DialogBox db = ActingCharacter.parentScene.CreateDialog(
            Globals.DialogTypes.speaking, Phrase, Color, Position,
            ActingCharacter.topPoint, ActingCharacter.Facing);
        await ToSignal(db, "DialogClosed");
        IsFinished = true;
        IsInProgress = false;
        EmitSignal(SignalName.EventFinished);
    }

    public async void ExecuteThought()
    {
        IsInProgress = true;
        var thinkPos = new Vector2(ActingCharacter.Position.X, ActingCharacter.topPoint.Y);
        DialogBox db = ActingCharacter.parentScene.CreateDialog(
            Globals.DialogTypes.thinking, Phrase, Color, Position,
            thinkPos, ActingCharacter.Facing);
        await ToSignal(db, "DialogClosed");
        IsFinished = true;
        IsInProgress = false;
        EmitSignal(SignalName.EventFinished);
    }

    public async void ExecuteMovement()
    {
        IsInProgress = true;
        ActingCharacter.GoToLocation(Position);
        await ToSignal(ActingCharacter, "DestinationReached");
        IsFinished = true;
        IsInProgress = false;
        EmitSignal(SignalName.EventFinished);
    }

    public async void ExecuteWait()
    {
        IsInProgress = true;
        await ToSignal(ParentScene.GetTree().CreateTimer(duration, true), "timeout");
        IsFinished   = true;
        IsInProgress = false;
        EmitSignal(SignalName.EventFinished);
    }

    public async void ExecuteConversation()
    {
        IsInProgress = true;

        string jsonText = FileAccess.GetFileAsString($"res://{dialogFile}");
        var doc = JsonNode.Parse(jsonText)?.AsObject();
        if (doc == null) { EmitSignal(SignalName.EventFinished); return; }

        var allNodes  = doc["nodes"]?.AsObject();
        string nodeId = doc["start"]?.GetValue<string>();

        var prevMode = ParentScene.mainScene.GetInteractMode();
        ParentScene.mainScene.SetInteractMode(Globals.InteractModes.walk);

        while (nodeId != null && allNodes != null)
        {
            var nodeArray = allNodes[nodeId]?.AsArray();
            if (nodeArray == null) break;
            nodeId = await RunDialogArray(allNodes, nodeArray);
        }

        ParentScene.mainScene.SetInteractMode(prevMode);
        IsFinished   = true;
        IsInProgress = false;
        EmitSignal(SignalName.EventFinished);
    }

    // Returns true if both flag conditions on obj are satisfied.
    private bool DialogConditionMet(JsonObject obj)
    {
        if (obj.ContainsKey("if_flag")     &&  !ParentScene.GetFlag(obj["if_flag"].GetValue<string>()).Value.As<bool>())
            return false;
        if (obj.ContainsKey("if_not_flag") &&   ParentScene.GetFlag(obj["if_not_flag"].GetValue<string>()).Value.As<bool>())
            return false;
        return true;
    }

    private NPC   ResolveDialogNPC(string name)   => name == "character"
        ? ParentScene.ego
        : ParentScene.FindChild(name, true, false) as NPC;

    private thing ResolveDialogThing(string name) => name == "character"
        ? ParentScene.ego
        : ParentScene.FindChild(name, true, false) as thing;

    // Executes one node array. Returns the next node id (from "call" or
    // "choices"), or null when the array runs to completion.
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
                    NPC ch = ParentScene.ego;
                    Vector2   tp = new Vector2(ch.Position.X, ch.topPoint.Y);
                    DialogBox db = ParentScene.CreateDialog(
                        Globals.DialogTypes.choice, null, null, null, tp, ch.Facing, labels.ToArray());
                    Godot.Variant[] ret = await ToSignal(db, "DialogClosed");
                    int idx = (int)ret[0];
                    if (idx >= 0 && idx < nodeIds.Count)
                        return nodeIds[idx];
                    break;
                }

                case "speak": {
                    NPC actor = ResolveDialogNPC(obj["actor"].GetValue<string>());
                    if (actor == null) break;
                    DialogBox db = ParentScene.CreateDialog(
                        Globals.DialogTypes.speaking, obj["text"].GetValue<string>(),
                        actor.dialogColor, null, actor.topPoint, actor.Facing);
                    await ToSignal(db, "DialogClosed");
                    break;
                }

                case "think": {
                    NPC     actor = ResolveDialogNPC(obj["actor"].GetValue<string>());
                    if (actor == null) break;
                    Vector2 tp    = new Vector2(actor.Position.X, actor.topPoint.Y);
                    DialogBox db  = ParentScene.CreateDialog(
                        Globals.DialogTypes.thinking, obj["text"].GetValue<string>(),
                        actor.dialogColor, null, tp, actor.Facing);
                    await ToSignal(db, "DialogClosed");
                    break;
                }

                case "narrate": {
                    DialogBox db = ParentScene.CreateDialog(
                        Globals.DialogTypes.narration, obj["text"].GetValue<string>(), Colors.Yellow);
                    await ToSignal(db, "DialogClosed");
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
                        dest   = new Vector2(to[0].GetValue<float>(), to[1].GetValue<float>());
                    }
                    actor.GoToLocation(dest);
                    await ToSignal(actor, "DestinationReached");
                    break;
                }

                case "wait": {
                    float secs = obj["seconds"]?.GetValue<float>() ?? 1f;
                    await ToSignal(ParentScene.GetTree().CreateTimer(secs, true), "timeout");
                    break;
                }

                case "set_flag": {
                    string name = obj["name"].GetValue<string>();
                    string val  = obj["value"]?.GetValue<string>() ?? "true";
                    Variant v   = (val == "true" || val == "false")
                        ? Variant.From(val == "true")
                        : Variant.From(val);
                    ParentScene.AddFlag(name, v);
                    break;
                }

                case "branch_on_flag": {
                    string flagName = obj["name"].GetValue<string>();
                    bool   expected = obj["is"]?.GetValue<bool>() ?? true;
                    bool   actual   = ParentScene.GetFlag(flagName).Value.As<bool>();
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
// EventSequence — ordered list of Events that runs one at a time.
// Instantiate with a parent scene, push events with the AddEvent* helpers,
// then call ExecuteAll(). scene_script._Process() calls this automatically.
// ---------------------------------------------------------------------------
public partial class EventSequence
{
    private scene_script parentScene;
    private int          currentEvent = 0;
    public  bool         isRunning    = false;
    public  bool         isInterrupted = false;
    public  List<Event>  eventList;

    // Events whose EventFinished signal is NOT awaited (they complete synchronously).
    private static readonly HashSet<Event.EventType> instantTypes = new()
    {
        Event.EventType.add_scene_flag,
        Event.EventType.change_facing,
        Event.EventType.change_facing_lookat,
        Event.EventType.play_animation,
        Event.EventType.add_to_inventory,
        Event.EventType.remove_from_inventory,
        Event.EventType.remap_floor,
    };

    public enum SuspendType { normal = 0, suspend_all = 1, unsuspend_all = 2 }

    public EventSequence(scene_script _parentScene)
    {
        parentScene = _parentScene;
        eventList   = new List<Event>();
    }

    public EventSequence(scene_script _parentScene, List<Event> newEvents)
    {
        parentScene = _parentScene;
        eventList   = new List<Event>();
        foreach (Event e in newEvents)
            AddEvent(e);
    }

    public int  Count()           => eventList.Count;
    public Event GetCurrentEvent() => eventList[currentEvent];

    public bool HasEventsWaiting() =>
        eventList.Count > 0 && !isRunning;

    public void Halt() => isRunning = false;

    public void Clear()
    {
        eventList.Clear();
        eventList     = new List<Event>();
        isRunning     = false;
        isInterrupted = false;
        currentEvent  = 0;
    }

    public void TryInterrupt()
    {
        if (eventList.Count == 0 || !eventList[currentEvent].IsInterruptable)
            return;
        isInterrupted = true;
    }

    public async void ExecuteAll(SuspendType suspendType = SuspendType.normal)
    {
        isRunning    = true;
        currentEvent = 0;

        for (int i = 0; i < eventList.Count; i++)
        {
            if (!isRunning)    return;
            if (isInterrupted) { Halt(); Clear(); return; }

            currentEvent = i;

            switch (suspendType)
            {
                case SuspendType.normal:
                    if (!eventList[i].IsInterruptable)
                        parentScene.SuspendSceneInput();
                    else if (parentScene.isSceneInputSuspended)
                        parentScene.UnsuspendSceneInput();
                    break;
                case SuspendType.suspend_all:
                    if (!parentScene.isSceneInputSuspended)
                        parentScene.SuspendSceneInput();
                    break;
                case SuspendType.unsuspend_all:
                    if (parentScene.isSceneInputSuspended)
                        parentScene.UnsuspendSceneInput();
                    break;
            }

            eventList[i].Execute();
            if (!instantTypes.Contains(eventList[i].Type))
                await eventList[i].ToSignal(eventList[i], "EventFinished");
        }

        Halt();
        Clear();

        if (parentScene.isSceneInputSuspended && suspendType == SuspendType.normal)
            parentScene.UnsuspendSceneInput();
    }

    // -------------------------------------------------------------------------
    // AddEvent — dispatch an existing Event object to the right typed helper.
    // -------------------------------------------------------------------------
    public void AddEvent(Event e)
    {
        switch (e.Type)
        {
            case Event.EventType.add_scene_flag:        AddEventAddFlag(e.sceneFlag); break;
            case Event.EventType.wait:                  AddEventWait(e.duration); break;
            case Event.EventType.move:                  AddEventMove(e.ActingCharacter, e.Position, e.IsInterruptable); break;
            case Event.EventType.narrate:               AddEventNarrate(e.Phrase, e.Position, e.IsInterruptable); break;
            case Event.EventType.character_speak:       AddEventSpeak(e.ActingCharacter, e.Phrase, e.Position, e.IsInterruptable); break;
            case Event.EventType.character_think:       AddEventThink(e.ActingCharacter, e.Phrase, e.Position, e.IsInterruptable); break;
            case Event.EventType.conversation:          AddEventConversation(e.dialogFile, _interruptable: e.IsInterruptable); break;
            case Event.EventType.change_facing:         AddEventChangeFacing(e.ActingCharacter, e.NewFacing, e.IsInterruptable); break;
            case Event.EventType.change_facing_lookat:  AddEventChangeFacingToLookAt(e.ActingCharacter, e.interactThing, e.IsInterruptable); break;
            case Event.EventType.play_animation:        AddEventPlayAnimation(e.animationPlayer, e.animationToPlay, e.IsInterruptable); break;
            case Event.EventType.add_to_inventory:      AddEventAddToInventory(e.interactThing); break;
            case Event.EventType.remove_from_inventory: AddEventRemoveFromInventory(e.itemType); break;
        }
    }

    // -------------------------------------------------------------------------
    // AddEvent* typed helpers — each creates an Event, fills its fields, and
    // appends it to eventList.
    // -------------------------------------------------------------------------

    private Event NewEvent(Event.EventType type, bool interruptable = false)
    {
        var e = new Event
        {
            ParentScene    = parentScene,
            Type           = type,
            IsFinished     = false,
            IsInProgress   = false,
            IsInterruptable = interruptable,
        };
        eventList.Add(e);
        return e;
    }

    public void AddEventAddFlag(Globals.SceneFlag flag)
    {
        var e = NewEvent(Event.EventType.add_scene_flag, interruptable: true);
        e.sceneFlag = flag;
    }

    public void AddEventWait(float seconds)
    {
        var e = NewEvent(Event.EventType.wait);
        e.duration = seconds;
    }

    public void AddEventAddToInventory(thing t)
    {
        var e = NewEvent(Event.EventType.add_to_inventory, interruptable: true);
        e.interactThing = t;
    }

    public void AddEventRemoveFromInventory(InventoryItem.ItemType itemType)
    {
        var e = NewEvent(Event.EventType.remove_from_inventory, interruptable: true);
        e.itemType = itemType;
    }

    public void AddEventMove(NPC character, Vector2 position, bool _interruptable = false)
    {
        var e = NewEvent(Event.EventType.move, _interruptable);
        e.ActingCharacter = character;
        e.Position        = position;
    }

    public void AddEventChangeFacing(NPC character, NPC.Direction facing, bool _interruptable = false)
    {
        var e = NewEvent(Event.EventType.change_facing, _interruptable);
        e.ActingCharacter = character;
        e.NewFacing       = facing;
    }

    public void AddEventChangeFacingToLookAt(NPC character, Node2D target, bool _interruptable = false)
    {
        var e = NewEvent(Event.EventType.change_facing_lookat, _interruptable);
        e.ActingCharacter = character;
        e.interactThing   = target as thing;
    }

    public void AddEventSpeak(NPC character, string phrase, Vector2? position = null, bool _interruptable = false)
    {
        var e = NewEvent(Event.EventType.character_speak, _interruptable);
        e.ActingCharacter = character;
        e.Color           = character.dialogColor;
        e.Phrase          = phrase;
        e.Position        = position ?? Vector2.Zero;
    }

    public void AddEventThink(NPC character, string phrase, Vector2? position = null, bool _interruptable = false)
    {
        var e = NewEvent(Event.EventType.character_think, _interruptable);
        e.ActingCharacter = character;
        e.Color           = character.dialogColor;
        e.Phrase          = phrase;
        e.Position        = position ?? Vector2.Zero;
    }

    public void AddEventNarrate(string phrase, Vector2? position = null, bool _interruptable = false)
    {
        var e = NewEvent(Event.EventType.narrate, _interruptable);
        e.Color    = Colors.Yellow;
        e.Phrase   = phrase;
        e.Position = position ?? Vector2.Zero;
    }

    public void AddEventConversation(string dialogFile, Vector2? position = null, bool _interruptable = false)
    {
        var e = NewEvent(Event.EventType.conversation, _interruptable);
        e.dialogFile = dialogFile;
        e.Color                 = Colors.White;
        e.Position              = position ?? Vector2.Zero;
    }

    public void AddEventRemapFloor(string navRegionNodeName, string guidePolygonNodeName)
    {
        var e = new Event
        {
            ParentScene          = parentScene,
            Type                 = Event.EventType.remap_floor,
            IsFinished           = false,
            IsInProgress         = false,
            IsInterruptable      = true,
            navRegionNodeName    = navRegionNodeName,
            guidePolygonNodeName = guidePolygonNodeName,
        };
        eventList.Add(e);
    }

    public void AddEventPlayAnimation(AnimationPlayer player, string animName, bool _interruptable = false)
    {
        var e = NewEvent(Event.EventType.play_animation, _interruptable);
        e.animationPlayer = player;
        e.animationToPlay = animName;
    }
}
