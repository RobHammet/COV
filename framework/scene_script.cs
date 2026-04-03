// scene_script.cs — base class for every room scene.
//
// Each room's .cs file extends this class. All input, event-queue ticking,
// light/shadow math, parallax, and dialog instantiation live here so
// individual room scripts stay small.
//
// ROOM SCRIPT PATTERN
//   public partial class kitchen : scene_script {
//       public override void _Ready() {
//           base._Ready();
//           // optional: connect signals, restore state from flags
//       }
//   }
//   Each interactive prop/NPC overrides thing.SpecificLook/Use/Talk/UseItem.
//
// EXTRAS vs Prog2 base version
//   - parallaxSprites  — export array of Sprite2D nodes, DoParallax() scrolls them
//   - darkMap          — per-pixel brightness texture; samples at thing/char position
//   - cameraClamp      — CollisionShape2D that limits camera travel
//   - hasCameraControl — set false to skip camera follow (e.g. title screen)
//   - AdvanceInteractMode() — right-click cycles verb in verbtray mode

using Godot;
using GC = Godot.Collections;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text.Json.Nodes;

public partial class scene_script : Node2D
{
    [Export] public string displayName;

    // Path to a JSON file (relative to res://) defining named entry points for
    // this scene. Each entry specifies charStartPos, charStartDir, and a sequence
    // of actions to run when the scene is entered via that entry point.
    // Set in the Godot editor. Leave empty if the scene needs no entry sequences.
    [Export] public string entryScriptFile;

    // Per-scene ambient darkness texture. Sampled at each thing's/ego's
    // position; the result drives the shader's overall_dark parameter.
    [Export] public Texture2D darkMap;

    // Sprites whose Offset is updated each frame for a parallax scroll effect.
    // Index 0 scrolls the least, higher indices scroll more.
    [Export] public Godot.Collections.Array<Sprite2D> parallaxSprites;

    public Image darkMapImage = null;

    // Set true to run camera follow logic in _Process(). False for scenes where
    // the camera is fixed or managed externally.
    public bool hasCameraControl = true;

    // Camera and optional clamp region. Populated in _EnterTree() if present.
    public Camera2D         camera      = null;
    public CollisionShape2D cameraClamp = null;

    public NPC   ego;
    private Color    egoOriginalModulate;

    public List<thing>            things     = new List<thing>();
    public List<Globals.SceneFlag> flags     = new List<Globals.SceneFlag>();
    public EventSequence           eventQueue;
    public MainScene               mainScene;
    public int                     numberOfOpenDialogs = 0;

    // Scene JSON data — loaded in _EnterTree (parent-first, before child _Ready()).
    // Used by exit area handling and thing interaction data lookup.
    public JsonObject _sceneData;

    // Arrival data passed from the source scene's exit definition.
    // SourceName — display name of the scene the ego came from (for auto-detecting arrive_area).
    // Dir        — direction to face and walk on arrival ("up"/"down"/"left"/"right").
    // Area       — CollisionShape2D node name in this scene to start at; empty = auto-detect.
    // Walk       — true: trigger a strict walk-out-of-zone move after the transition completes.
    // Sequence   — optional event sequence to run instead of auto-walk (no_walk implied).

    // Walk target stored here during staging; consumed by UnsuspendSceneInput so the
    // walk only becomes visible after the transition animation finishes.
    public Vector2? _arrivalWalkTarget;
    public record struct ArrivalData(
        string     SourceName,
        string     Dir,
        string     Area,
        bool       Walk,
        JsonArray  Sequence
    );

    private readonly record struct ExitZone(string Dest, Vector2 Center, Vector2 HalfSize, ArrivalData Arrival);
    private readonly List<(string Name, ExitZone Zone)> _exitZones   = new();
    private readonly HashSet<string>                    _insideExits  = new();
    private          bool                               _exitsSeeded  = false;

    private readonly record struct AreaZone(Vector2 Center, Vector2 HalfSize, JsonArray Actions, bool Through);
    private readonly List<(string Name, AreaZone Zone)> _areaZones   = new();
    private readonly HashSet<string>                    _insideAreas  = new();

    // Cached scene light — resolved once in _EnterTree() to avoid GetNode every frame.
    private PointLight2D _sceneLight;

    // Inventory panel loaded in _EnterTree(); shown/hidden by ToggleInventory().
    private Inventory inventoryUI = null;

    // ---------------------------------------------------------------------------
    // Pause — stops _Process() logic without pausing the Godot scene tree.
    // Used by ToggleInventory() and can be used by MainScene during transitions.
    // ---------------------------------------------------------------------------
    private bool isPaused = false;
    public void  Pause()    => isPaused = true;
    public void  Resume()   => isPaused = false;
    public bool  IsPaused() => isPaused;

    // ---------------------------------------------------------------------------
    // Input suspension — set by EventSequence when a non-interruptable event runs.
    // ---------------------------------------------------------------------------
    public bool              isSceneInputSuspended;
    public Globals.InteractModes prevInteractMode = Globals.InteractModes.walk;

    public void SuspendSceneInput()
    {
        prevInteractMode = mainScene.GetInteractMode();
        mainScene.SetInteractMode(Globals.InteractModes.wait);
        isSceneInputSuspended = true;
    }

    public void UnsuspendSceneInput()
    {
        if (prevInteractMode == Globals.InteractModes.wait)
            prevInteractMode = Globals.InteractModes.walk;
        mainScene.SetInteractMode(prevInteractMode);
        isSceneInputSuspended = false;

        // Kick off the arrival walk now that the transition is complete and the
        // scene is live. This keeps the walk visible to the player rather than
        // happening invisibly during the staging SubViewport phase.
        if (_arrivalWalkTarget.HasValue && ego != null)
        {
            eventQueue.AddEventMove(ego, _arrivalWalkTarget.Value, _interruptable: false);
            _arrivalWalkTarget = null;
        }
    }

    // ---------------------------------------------------------------------------
    // _EnterTree
    // ---------------------------------------------------------------------------
    public override void _EnterTree()
    {
        base._EnterTree();

        // Resolve MainScene — always the second root child (index 1) because
        // it is the main scene and our room is instantiated as its child.
        // NOTE: breaks if the scene tree order changes. Safer alternative:
        // use a group ("main_scene") and GetTree().GetFirstNodeInGroup().
        if (GetTree().Root.GetChild(1).Name == "MainScene")
            mainScene = (MainScene)GetTree().Root.GetChild(1);
        else
            GD.PrintErr("scene_script: MainScene not found at root child index 1.");

        GD.Print($"Entering scene '{displayName}' ({Name})");

        // Camera and optional clamp shape.
        // Disable immediately in _EnterTree — before Godot's own tree-entry
        // processing can auto-activate it and steal the viewport. MainScene
        // re-enables it in SwitchCurrentSceneForNext() when the scene goes live.
        try
        {
            camera         = GetNode<Camera2D>("Camera2D");
            camera.Enabled = false;
            cameraClamp    = GetNode<CollisionShape2D>("CameraClamp");
        }
        catch { camera = null; cameraClamp = null; }

        // Cache the scene light for _Process().
        try { _sceneLight = GetNode<PointLight2D>("PointLight2D"); }
        catch { _sceneLight = null; }

        // Dark map image for per-pixel brightness sampling.
        if (darkMap != null)
            darkMapImage = darkMap.GetImage();

        // Spawn ego from GameConfig.EgoScene at the start_point position.
        // Guard: _EnterTree fires again on reparent (staging → main holder).
        if (ego == null)
        {
            var startPoint = GetNodeOrNull<Node2D>("start_point");
            var egoPacked  = ResourceLoader.Load<PackedScene>(GameConfig.EgoScene);
            if (egoPacked != null)
            {
                ego = egoPacked.Instantiate() as NPC;
                if (ego != null)
                {
                    ego.parentScene = this;
                    AddChild(ego);
                    ego.Position    = startPoint?.Position ?? Vector2.Zero;
                    egoOriginalModulate = ego.Modulate;
                }
            }
        }

        // Collect thing children.
        things.Clear();
        foreach (var o in GetChildren())
            if (o is thing t)
                things.Add(t);

        foreach (thing t in things)
        {
            t.parentScene = this;
            GD.Print($"  thing: {t.displayName}");
        }

        // Load inventory panel. Guard against re-running when the node is
        // reparented (thumbnail capture moves it to a SubViewport and back),
        // and against crashes when instantiated in a SubViewport context.
        if (inventoryUI == null)
        {
            try
            {
                var packedInv = GD.Load<PackedScene>("res://ui/Inventory.tscn");
                inventoryUI = packedInv.Instantiate() as Inventory;
                AddChild(inventoryUI);
                inventoryUI.Hide();
            }
            catch { inventoryUI = null; }
        }

        eventQueue = new EventSequence(this);

        // Load scene JSON — must happen in _EnterTree (parent-first) so child
        // thing._Ready() calls can read _sceneData for interaction definitions.
        string jsonPath = !string.IsNullOrEmpty(entryScriptFile)
            ? $"res://{entryScriptFile}"
            : SceneFilePath.Replace(".tscn", ".json");
        if (FileAccess.FileExists(jsonPath))
            _sceneData = JsonNode.Parse(FileAccess.GetFileAsString(jsonPath))?.AsObject();
    }

    // _Ready is empty so subclasses can override freely.
    public override void _Ready()
    {
        base._Ready();
        SetupAreaZones();
    }

    private static bool InsideZone(Vector2 pos, in ExitZone zone) =>
        Mathf.Abs(pos.X - zone.Center.X) <= zone.HalfSize.X &&
        Mathf.Abs(pos.Y - zone.Center.Y) <= zone.HalfSize.Y;

    private static bool InsideZone(Vector2 pos, in AreaZone zone) =>
        Mathf.Abs(pos.X - zone.Center.X) <= zone.HalfSize.X &&
        Mathf.Abs(pos.Y - zone.Center.Y) <= zone.HalfSize.Y;

    // ---------------------------------------------------------------------------
    // Area zone setup — handles exits, move_into zones, and move_through zones.
    // JSON: "areas": { "exit_to_X":   { "move_into":   [ { "action": "exit", ... } ] },
    //                  "some_zone":    { "move_into":   [ ...actions... ] },
    //                  "through_zone": { "move_through": [ ...actions... ] } }
    // ---------------------------------------------------------------------------
    private void SetupAreaZones()
    {
        if (_sceneData == null) return;
        var areas = _sceneData["areas"]?.AsObject();
        if (areas == null) return;

        foreach (var kv in areas)
        {
            bool through = false;
            JsonArray actions = kv.Value?["move_into"]?.AsArray();
            if (actions == null)
            {
                actions = kv.Value?["move_through"]?.AsArray();
                if (actions != null) through = true;
            }
            if (actions == null) continue;

            CollisionShape2D shape = GetNodeOrNull<CollisionShape2D>(kv.Key);
            if (shape?.Shape is not RectangleShape2D rect) continue;

            // Check for an "exit" action — if found, register as an exit zone.
            JsonObject exitAction = null;
            foreach (var item in actions)
            {
                var obj = item?.AsObject();
                if (obj?["action"]?.GetValue<string>() == "exit") { exitAction = obj; break; }
            }

            if (exitAction != null)
            {
                string dest = Scenes.Resolve(exitAction["destination"]?.GetValue<string>() ?? "");
                if (string.IsNullOrEmpty(dest)) continue;
                string arriveDir  = exitAction["arrive_dir"]?.GetValue<string>()  ?? "";
                string arriveArea = exitAction["arrive_area"]?.GetValue<string>() ?? "";
                var    arriveSeq  = exitAction["arrive_sequence"]?.AsArray();
                bool   noWalk     = exitAction["no_walk"]?.GetValue<bool>() ?? false;
                bool   arriveWalk = !noWalk && arriveSeq == null;
                var    arrival    = new ArrivalData(displayName, arriveDir, arriveArea, arriveWalk, arriveSeq);
                _exitZones.Add((kv.Key, new ExitZone(dest, shape.Position, rect.Size / 2f, arrival)));
            }
            else
            {
                _areaZones.Add((kv.Key, new AreaZone(shape.Position, rect.Size / 2f, actions, through)));
            }
        }
    }

    // ---------------------------------------------------------------------------
    // Scene flags
    // ---------------------------------------------------------------------------

    public void AddFlag(string flagName, Godot.Variant value)
    {
        foreach (var f in mainScene.sceneFlags)
        {
            if (f.sceneName == Name && f.Name == flagName)
            {
                SetFlag(flagName, value, f.sceneName);
                return;
            }
        }
        mainScene.sceneFlags.Add(new Globals.SceneFlag(Name, flagName, value));
    }

    public void SetFlag(string flagName, Godot.Variant value, string sceneName = "")
    {
        foreach (var f in mainScene.sceneFlags)
        {
            if (f.Name == flagName && (sceneName == "" || sceneName == f.sceneName))
            {
                f.Value = value;
                return;
            }
        }
    }

    public Globals.SceneFlag GetFlag(string flagName)
    {
        foreach (var f in mainScene.sceneFlags)
            if (f.Name == flagName)
                return f;
        return new Globals.SceneFlag(null, "false", false);
    }

    // ---------------------------------------------------------------------------
    // Input state
    // ---------------------------------------------------------------------------
    public bool    isTouch                        = false;
    public bool    isMouseLeftButtonDown          = false;
    public bool    isMouseLeftButtonHeld          = false;
    public bool    isMouseLeftButtonClicked       = false;
    public bool    isMouseLeftButtonDoubleClicked  = false;
    public Vector2 mousePosOnPress                = new Vector2(-1, -1);

    // Hold timer: increments while button is held; triggers isMouseLeftButtonHeld.
    private float _holdTimer          = 0f;
    private const float HoldThreshold = 0.3f;

    // Active VerbCoin control, or null when not shown.
    public Control verbCoinControl;

    // ---------------------------------------------------------------------------
    // _UnhandledInput — raw input collection only; actions taken in _Process().
    // ---------------------------------------------------------------------------
    public override void _UnhandledInput(InputEvent @event)
    {
        // Block while suspended, paused, dialog open, or inventory visible.
        if (IsPaused() || isSceneInputSuspended || numberOfOpenDialogs > 0 || (inventoryUI?.Visible ?? false))
        {
            // Still pass input to open dialogs.
            foreach (var child in GetChildren())
                if (child is DialogBox db)
                    db._Input(@event);
            return;
        }

        isMouseLeftButtonClicked = false;

        if (@event is InputEventMouseButton mouse)
        {
            isTouch = false;

            if (mouse.ButtonIndex == MouseButton.Left)
            {
                if (mouse.Pressed)
                {
                    if (mouse.DoubleClick)
                        isMouseLeftButtonDoubleClicked = true;
                    else
                    {
                        isMouseLeftButtonDown = true;
                        mousePosOnPress       = GetLocalMousePosition();
                    }
                }
                else
                {
                    isMouseLeftButtonDown = false;
                    if (isMouseLeftButtonHeld)
                        isMouseLeftButtonHeld = false;
                    else
                        isMouseLeftButtonClicked = true;
                }
            }
            else if (mouse.ButtonIndex == MouseButton.Right && mouse.Pressed)
            {
                AdvanceInteractMode();
            }
        }
        else if (@event is InputEventScreenTouch touch)
        {
            isTouch = true;
            if (touch.Pressed)
            {
                isMouseLeftButtonDown = true;
                mousePosOnPress       = ToLocal(touch.Position);
            }
            else
            {
                isMouseLeftButtonDown = false;
                if (isMouseLeftButtonHeld)
                    isMouseLeftButtonHeld = false;
                else
                    isMouseLeftButtonClicked = true;
            }
        }
        else if (@event is InputEventKey key && key.Pressed)
        {
            if (key.Keycode == Key.I)
                ToggleInventory();

            if (Globals.showDebugTools)
            {
                if (key.Keycode == Key.S) mainScene.Save();
                if (key.Keycode == Key.L) mainScene.Load();
            }
        }
    }

    // Advance the interact mode by one step (wraps after item).
    // Only active in verbtray mode; verbcoin mode sets mode via VerbCoin.
    public void AdvanceInteractMode()
    {
        if (mainScene.currentInputMode != Globals.InputModes.verbtray) return;
        int next = (int)mainScene.GetInteractMode() + 1;
        if (next > 4) next = 0;
        // Skip walk if scene has no ego.
        if (next == 0 && ego == null) next = 1;
        mainScene.SetInteractMode((Globals.InteractModes)next);
    }

    // ---------------------------------------------------------------------------
    // VerbCoin
    // ---------------------------------------------------------------------------

    public void ShowVerbCoin()
    {
        var packed = GD.Load<PackedScene>("res://ui/VerbCoin.tscn");
        verbCoinControl          = packed.Instantiate<Control>();
        verbCoinControl.Position = mousePosOnPress;
        AddChild(verbCoinControl);
    }

    public void HideVerbCoin()
    {
        var coin = verbCoinControl as VerbCoin;
        if (coin != null)
            mainScene.SetInteractMode(mainScene.GetInteractMode());

        verbCoinControl.QueueFree();
        verbCoinControl = null;

        if (mousePosOnPress.X >= 0 && mousePosOnPress.Y >= 0)
            IterateThingsToInteract(mousePosOnPress, mainScene.GetInteractMode());

        mousePosOnPress = new Vector2(-1, -1);
        mainScene.SetInteractMode(Globals.InteractModes.walk);
    }

    // ---------------------------------------------------------------------------
    // ToggleInventory
    // ---------------------------------------------------------------------------

    public void ToggleInventory()
    {
        if (inventoryUI == null) return;
        if (!inventoryUI.Visible)
        {
            Pause();
            if (ego != null) ego.StopWalking();
            inventoryUI.Show();
            inventoryUI.InventoryOpened(mainScene.GetInteractMode());
        }
        else
        {
            inventoryUI.Hide();
            Resume();
        }
    }

    // ---------------------------------------------------------------------------
    // _Process
    // Order: camera follow + parallax → pause guard → debug → hold timer →
    //        event queue → light/shadow → verbcoin → click dispatch
    // ---------------------------------------------------------------------------
    public override void _Process(double delta)
    {
        // Camera follow and parallax run even when paused so the camera stays
        // correctly positioned while the inventory is open.
        // Guard on camera.Enabled: while a scene sits in nextSceneHolder (off-screen,
        // camera disabled), GlobalPosition includes the holder's x=640 offset. Assigning
        // that back to camera.Position (a local field) drifts the camera every frame and
        // corrupts parallax offsets by the time the scene goes live.
        if (hasCameraControl && camera != null && camera.Enabled && ego != null)
        {
            if (!camera.IsCurrent() && !isSceneInputSuspended)
                camera.MakeCurrent();

            // Smooth follow — track the ego's head position so close-up
            // scenes frame the ego rather than his feet.
            Vector2 followTarget = (ego.GlobalPosition + ego.topPoint) / 2f;
            Vector2 newPos = camera.GlobalPosition;
            float   speed  = camera.GlobalPosition.DistanceTo(followTarget) * (float)delta;
            newPos = newPos.MoveToward(followTarget, speed);

            // Clamp to bounds if a CameraClamp shape is present.
            if (cameraClamp != null)
            {
                Vector2 tl = cameraClamp.Position - cameraClamp.Shape.GetRect().Size / 2f;
                Vector2 br = cameraClamp.Position + cameraClamp.Shape.GetRect().Size / 2f;
                tl += GetViewportRect().Size / 2f;
                br -= GetViewportRect().Size / 2f;
                newPos = newPos.Clamp(tl, br);
            }

            camera.Position = newPos;

            // Parallax scroll.
            if (parallaxSprites != null)
                for (int i = 0; i < parallaxSprites.Count; i++)
                    DoParallax(parallaxSprites[i], (float)i + 0.075f);
        }

        if (IsPaused()) return;

        // Exit zone polling — fires only on outside→inside transition.
        if (ego?.charBody != null)
        {
            var pos = ego.Position;
            if (!_exitsSeeded)
            {
                // First frame: record which zones the ego already occupies
                // (post-ApplyEntryPoint) so they don't immediately re-trigger.
                foreach (var (name, zone) in _exitZones)
                    if (InsideZone(pos, zone)) _insideExits.Add(name);
                foreach (var (name, zone) in _areaZones)
                    if (zone.Through && InsideZone(pos, zone)) _insideAreas.Add(name);
                _exitsSeeded = true;
            }
            else
            {
                foreach (var (name, zone) in _exitZones)
                {
                    bool inside = InsideZone(pos, zone);
                    if (inside && !_insideExits.Contains(name))
                    {
                        mainScene.ChangeSceneToFile(zone.Dest, zone.Arrival);
                        return;
                    }
                    if (inside) _insideExits.Add(name);
                    else        _insideExits.Remove(name);
                }

                foreach (var (name, zone) in _areaZones)
                {
                    bool inside        = InsideZone(pos, zone);
                    bool alreadyInside = _insideAreas.Contains(name);
                    if (inside && !alreadyInside)
                    {
                        if (zone.Through)
                        {
                            // Interrupt any interruptable walk in progress.
                            eventQueue.TryInterrupt();
                            if (eventQueue.isInterrupted || !eventQueue.isRunning)
                            {
                                if (eventQueue.isInterrupted)
                                {
                                    ego?.StopWalking();
                                    ego?.PathEndReached();
                                    eventQueue = new EventSequence(this);
                                }
                                PopulateEventQueue(eventQueue, zone.Actions, this);
                                _insideAreas.Add(name);
                            }
                            // else: non-interruptable sequence running — skip this frame
                        }
                        else if (!eventQueue.isRunning && !isSceneInputSuspended)
                        {
                            PopulateEventQueue(eventQueue, zone.Actions, this);
                            _insideAreas.Add(name);
                        }
                    }
                    else if (!inside)
                        _insideAreas.Remove(name);
                }
            }
        }

        // Debug overlay.
        if (mainScene != null)
        {
            mainScene.debugText.Visible = Globals.showDebugTools;
            if (Globals.showDebugTools && ego != null)
            {
                mainScene.debugText.Text  = $"pos: {ego.Position}";
                mainScene.debugText.Text += $"\nfastwalk: {ego.isFastWalking}";
                mainScene.debugText.Text += $"\nitem: {mainScene.usingItem}";
                mainScene.debugText.Text += "\nevents:";
                foreach (Event e in eventQueue.eventList)
                    mainScene.debugText.Text += $"\n  {e.Type}";
                QueueRedraw();
            }
        }

        // Tick event queue.
        if (eventQueue != null && eventQueue.HasEventsWaiting())
        {
            eventQueue.ExecuteAll(
                isSceneInputSuspended
                    ? EventSequence.SuspendType.suspend_all
                    : EventSequence.SuspendType.normal);
        }

        // Hold timer.
        if (isMouseLeftButtonDown && !isMouseLeftButtonHeld)
        {
            _holdTimer += (float)delta;
            if (_holdTimer >= HoldThreshold)
            {
                isMouseLeftButtonHeld = true;
                _holdTimer = 0f;
            }
        }
        else if (!isMouseLeftButtonDown)
        {
            _holdTimer = 0f;
        }

        // Light and shadow math.
        if (_sceneLight != null)
        {
            // Character shadow.
            if (ego != null && ego.castsShadow)
            {
                Vector2 lightDir = _sceneLight.Position.DirectionTo(ego.Position);
                float   angle    = Mathf.Atan2(lightDir.Y, lightDir.X);
                float   height   = Mathf.Clamp(_sceneLight.Height / 500f, 0f, 1f);
                ego.shadow.GlobalScale = new Vector2(ego.shadow.GlobalScale.X, 1f - height);
                ego.shadow.GlobalSkew  = angle - (float)Math.PI / 2f;
                ego.shadow.GlobalRotationDegrees = angle > 0 ? 180f : 0f;
            }

            // Things: shader params + shadow geometry.
            if (things != null)
            {
                for (int i = 0; i < things.Count; i++)
                {
                    if (!things[i].hasSprite) continue;

                    Sprite2D tsprite   = things[i].GetNode<Sprite2D>("Sprite2D");
                    Vector2  lightDir  = _sceneLight.Position.DirectionTo(things[i].Position);
                    float    lightDist = _sceneLight.Position.DistanceTo(things[i].Position);

                    // Ambient darkness from the dark map.
                    if (darkMapImage != null && tsprite.Material != null)
                    {
                        Color  sample = darkMapImage.GetPixelv((Vector2I)things[i].Position);
                        float  avg    = (sample.R + sample.G + sample.B) / 3f;
                        ((ShaderMaterial)tsprite.Material).SetShaderParameter("overall_dark", avg);
                    }

                    // Directional light for toon/outline shaders.
                    if (tsprite.Material != null)
                    {
                        float energy   = Mathf.Clamp(_sceneLight.Energy / 16f, 0f, 1f);
                        float distStr  = 1f - Mathf.Clamp(lightDist / GetViewport().GetVisibleRect().Size.X, 0f, 1f);
                        float finalStr = (energy * 3f + distStr) / 4f;
                        ((ShaderMaterial)tsprite.Material).SetShaderParameter("light_direction",
                            new Vector3(-lightDir.X, lightDir.Y, _sceneLight.Height));
                        ((ShaderMaterial)tsprite.Material).SetShaderParameter("light_strength", finalStr);
                        ((ShaderMaterial)tsprite.Material).SetShaderParameter("is_flipped", tsprite.FlipH);
                    }

                    // Shadow geometry.
                    if (things[i].castsShadow)
                    {
                        float angle  = Mathf.Atan2(lightDir.Y, lightDir.X);
                        float height = Mathf.Clamp(_sceneLight.Height / 500f, 0f, 1f);
                        things[i].shadow.GlobalScale = new Vector2(things[i].shadow.GlobalScale.X, 1f - height);
                        things[i].shadow.GlobalSkew  = angle - (float)Math.PI / 2f;
                        things[i].shadow.GlobalRotationDegrees = angle > 0 ? 180f : 0f;
                    }
                }
            }
        }

        // VerbCoin show/hide.
        if (mainScene.currentInputMode == Globals.InputModes.verbcoin)
        {
            if (isMouseLeftButtonHeld && verbCoinControl == null)
            {
                if (ego != null) ego.StopWalking();
                ShowVerbCoin();
            }
            else if (!isMouseLeftButtonHeld && verbCoinControl != null)
            {
                HideVerbCoin();
            }
        }

        // Click dispatch.
        if (!isMouseLeftButtonClicked) return;

        if (eventQueue != null && eventQueue.Count() > 0)
        {
            eventQueue.TryInterrupt();
            if (eventQueue.isInterrupted)
                eventQueue = new EventSequence(this);
        }

        if (mainScene.GetInteractMode() == Globals.InteractModes.walk && ego != null)
        {
            ego.StopFastWalking();
            eventQueue.AddEventMove(ego, mousePosOnPress, _interruptable: true);
        }
        else
        {
            IterateThingsToInteract(mousePosOnPress, mainScene.GetInteractMode());
        }

        if (isMouseLeftButtonDoubleClicked)
        {
            if (ego != null) ego.StartFastWalking();
            isMouseLeftButtonDoubleClicked = false;
        }

        isMouseLeftButtonClicked = false;
    }

    // ---------------------------------------------------------------------------
    // IterateThingsToInteract — z-sorted polygon hit test.
    // ---------------------------------------------------------------------------
    public void IterateThingsToInteract(Vector2 pos, Globals.InteractModes mode)
    {
        List<thing> sorted = things.OrderByDescending(o => o.ZIndex).ToList();
        foreach (thing t in sorted)
        {
            if (Geometry2D.IsPointInPolygon(pos, t.clickPolygon))
            {
                t.InitInteract(mode);
                return;
            }
        }
    }

    // ---------------------------------------------------------------------------
    // Parallax helper — called from _Process() for each parallax sprite.
    // ---------------------------------------------------------------------------
    public void DoParallax(Sprite2D sprite, float factor)
    {
        float xDiff = sprite.Position.X - camera.Position.X;
        float yDiff = sprite.Position.Y - camera.Position.Y;
        sprite.Offset = new Vector2(xDiff * factor, yDiff * factor);
    }

    // ---------------------------------------------------------------------------
    // PopulateEventQueue — converts a JSON action array into EventSequence events.
    // Shared by the entry-script system, thing interactions, and any other caller.
    // Supports: move, look_at, change_facing, narrate, speak, think,
    //           set_flag, wait, play_animation, add_to_inventory.
    // self — the thing being interacted with (for "self" actor references and
    //         add_to_inventory). Pass null when not applicable.
    // ---------------------------------------------------------------------------
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
                    NPC actor = ResolveNPCInScene(obj["actor"].GetValue<string>(), scene, self);
                    Vector2 dest;
                    if (obj.ContainsKey("target"))
                    {
                        string targetName = obj["target"].GetValue<string>();
                        thing  target     = ResolveThingInScene(targetName, scene, self);
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
                    NPC   actor  = ResolveNPCInScene(obj["actor"].GetValue<string>(), scene, self);
                    thing target = ResolveThingInScene(obj["target"].GetValue<string>(), scene, self);
                    if (actor != null && target != null)
                        queue.AddEventChangeFacingToLookAt(actor, target);
                    break;
                }
                case "narrate":
                    queue.AddEventNarrate(obj["text"].GetValue<string>(), Vector2.Zero);
                    break;
                case "speak": {
                    NPC actor = ResolveNPCInScene(obj["actor"].GetValue<string>(), scene, self);
                    if (actor != null)
                        queue.AddEventSpeak(actor, obj["text"].GetValue<string>(), Vector2.Zero);
                    break;
                }
                case "think": {
                    NPC actor = ResolveNPCInScene(obj["actor"].GetValue<string>(), scene, self);
                    if (actor != null)
                        queue.AddEventThink(actor, obj["text"].GetValue<string>(), Vector2.Zero);
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
                    NPC actor = ResolveNPCInScene(obj["actor"].GetValue<string>(), scene, self);
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
                    thing  actor     = ResolveThingInScene(actorName, scene, self);
                    if (actor?.animationPlayer != null)
                        queue.AddEventPlayAnimation(actor.animationPlayer, obj["animation"].GetValue<string>());
                    break;
                }
                case "add_to_inventory": {
                    if (self != null) queue.AddEventAddToInventory(self);
                    break;
                }
                case "wait": {
                    float seconds = obj["seconds"]?.GetValue<float>() ?? 1f;
                    queue.AddEventWait(seconds);
                    break;
                }
                case "remap_floor": {
                    string navReg = obj["nav_region"]?.GetValue<string>() ?? "NavigationRegion2D";
                    string polygon = obj["polygon"].GetValue<string>();
                    queue.AddEventRemapFloor(navReg, polygon);
                    break;
                }
                case "branch_on_flag": {
                    string flagName   = obj["name"].GetValue<string>();
                    bool   expected   = obj["is"]?.GetValue<bool>() ?? true;
                    bool   actual     = scene.GetFlag(flagName).Value.As<bool>();
                    var    branch     = (actual == expected ? obj["then"] : obj["else"])?.AsArray();
                    if (branch != null)
                        PopulateEventQueue(queue, branch, scene, self);
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

    private static thing ResolveThingInScene(string name, scene_script scene, thing self = null)
    {
        if (name == "self")      return self;
        if (name == "ego") return scene.ego;
        return scene.FindChild(name, true, false) as thing;
    }

    private static NPC ResolveNPCInScene(string name, scene_script scene, thing self = null)
    {
        if (name == "self")      return self as NPC;
        if (name == "ego") return scene.ego;
        return scene.FindChild(name, true, false) as NPC;
    }

    // Returns the center of a named CollisionShape2D exit node in scene-local space.
    private static Vector2 ResolveAreaCenter(string shapeName, scene_script scene)
    {
        var shape = scene.GetNodeOrNull<CollisionShape2D>(shapeName);
        return shape?.Position ?? Vector2.Zero;
    }

    // ---------------------------------------------------------------------------
    // CreateDialog — instantiate a DialogBox and add it as a child.
    // ---------------------------------------------------------------------------
    public DialogBox CreateDialog(
        Globals.DialogTypes _dialogType = Globals.DialogTypes.narration,
        string text         = "[empty]",
        Color? color        = null,
        Vector2? pos        = null,
        Vector2? tailPos    = null,
        NPC.Direction? egoFacing = null,
        string[] _dialogChoices = null,
        NPC actor           = null)
    {
        bool hasTail = tailPos != null;

        float facingOffset = 0f;
        if (egoFacing == NPC.Direction.left)
            facingOffset = -96f;
        else if (egoFacing == NPC.Direction.right)
            facingOffset = 64f;

        var packed = GD.Load<PackedScene>("res://ui/DialogBox.tscn");
        DialogBox db = packed.Instantiate() as DialogBox;

        db.dialogColor     = color ?? Colors.White;
        db.offsetForFacing = facingOffset;
        db.dialogType      = _dialogType;
        db.parentScene     = this;
        db.trackActor      = actor;

        if (hasTail) db.tailPos = tailPos.Value;

        if (_dialogType == Globals.DialogTypes.choice || _dialogType == Globals.DialogTypes.thinking)
            db.offsetForFacing = 0f;

        if (_dialogType != Globals.DialogTypes.choice)
            db.SetPhrase(text);
        else
            db.SetPhrase(string.Join("\n", _dialogChoices ?? new string[0]));

        AddChild(db); // _Ready fires here — safe to access nodes after this point

        if (_dialogType == Globals.DialogTypes.choice && _dialogChoices != null)
        {
            db.dialogChoices.InitDialogChoices(_dialogChoices);
            db.GetNode<Timer>("DB_TextTimer").Autostart = false;
        }

        db.InitDialogBox(pos ?? Vector2.Zero);
        return db;
    }

    // ---------------------------------------------------------------------------
    // Signal handlers
    // ---------------------------------------------------------------------------

    public void _on_DialogBox_DialogClosed()       => WaitAfterDialog();
    public void _on_Ego_DestinationReached() { }
    public void _on_Ego_FacingChanged()      { }

    public async void WaitAfterDialog()
    {
        await ToSignal(GetTree().CreateTimer(0.2f, true), "timeout");
        numberOfOpenDialogs--;
    }
}
