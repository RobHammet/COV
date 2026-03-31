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
    }

    [Signal] public delegate void EventFinishedEventHandler();

    public string               Phrase               { get; set; }
    public string               dialogTreeNameInScene;
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
    public Character.Direction  NewFacing;
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
        }
    }

    // --- Instant events (emit EventFinished immediately) --------------------

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
        DialogTree instance = ParentScene.GetNode<DialogTree>(dialogTreeNameInScene);
        instance.DoDialogs();
        await ToSignal(instance, "DialogTreeClosed");
        IsFinished = true;
        IsInProgress = false;
        EmitSignal(SignalName.EventFinished);
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
            case Event.EventType.conversation:          AddEventConversation(e.dialogTreeNameInScene, _interruptable: e.IsInterruptable); break;
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

    public void AddEventChangeFacing(NPC character, Character.Direction facing, bool _interruptable = false)
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

    public void AddEventConversation(string dialogTreeNameInScene, Vector2? position = null, bool _interruptable = false)
    {
        var e = NewEvent(Event.EventType.conversation, _interruptable);
        e.dialogTreeNameInScene = dialogTreeNameInScene;
        e.Color                 = Colors.White;
        e.Position              = position ?? Vector2.Zero;
    }

    public void AddEventPlayAnimation(AnimationPlayer player, string animName, bool _interruptable = false)
    {
        var e = NewEvent(Event.EventType.play_animation, _interruptable);
        e.animationPlayer = player;
        e.animationToPlay = animName;
    }
}
