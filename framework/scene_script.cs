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

    // Same file as currently playing → seamless continuation; different file → switches; empty → fade out.
    [Export(PropertyHint.File, "*.mp3,*.ogg,*.wav")] public string bgmFile;

    // Mark this scene as an insert (close-up / puzzle / interstitial).
    // Insert scenes have no ego: walking is disabled and the ego is not spawned.
    [Export] public bool isInsert = false;

    // Sprites whose Offset is updated each frame for a parallax scroll effect.
    // Index 0 scrolls the least, higher indices scroll more.
    [Export] public Godot.Collections.Array<Sprite2D> parallaxSprites;
    // Optional per-sprite scroll factors. If shorter than parallaxSprites,
    // remaining entries fall back to (index + 0.075).
    [Export] public Godot.Collections.Array<float> parallaxFactors;

    private List<DarkmapZone> _darkmapZones = new();

    // Set true to run camera follow logic in _Process(). False for scenes where
    // the camera is fixed or managed externally.
    public bool hasCameraControl = true;

    // Camera and optional clamp region. Populated in _EnterTree() if present.
    public Camera2D         camera             = null;
    public CollisionShape2D cameraClamp        = null;
    public Vector2          cameraOriginalZoom = Vector2.One;

    // Clamps a world-space camera position to the cameraClamp bounds at the given zoom
    // (defaults to camera.Zoom). Matches the logic used by the ego-follow path in _Process.
    public Vector2 ClampCameraPos(Vector2 pos, Vector2? zoom = null)
    {
        if (cameraClamp == null || camera == null) return pos;
        Vector2 z        = zoom ?? camera.Zoom;
        Vector2 tl       = cameraClamp.Position - cameraClamp.Shape.GetRect().Size / 2f;
        Vector2 br       = cameraClamp.Position + cameraClamp.Shape.GetRect().Size / 2f;
        Vector2 halfView = GetViewportRect().Size / 2f / z;
        tl += halfView;
        br -= halfView;
        if (tl.X <= br.X && tl.Y <= br.Y)
            return pos.Clamp(tl, br);
        return pos;
    }

    public NPC   ego;
    private Color    egoOriginalModulate;

    public List<thing>   things     = new List<thing>();
    public EventSequence eventQueue;
    public MainScene               mainScene;
    public bool HasOpenDialog => _dialogLayer?.GetChildren().OfType<DialogBox>().Any() ?? false;

    private PackedScene  _dialogBoxScene;
    private CanvasLayer  _dialogLayer;

    // Scene JSON data — loaded in _EnterTree (parent-first, before child _Ready()).
    // Used by exit area handling and thing interaction data lookup.
    public JsonObject _sceneData;

    // Arrival data passed from the source scene's exit definition.
    // SourceName — display name of the scene the ego came from (for auto-detecting arrive_area).
    // Dir        — direction to face and walk on arrival ("up"/"down"/"left"/"right").
    // Area  — CollisionShape2D node name in this scene to start at; empty = auto-detect.
    // Walk  — true: trigger a strict walk-out-of-zone move after the transition completes.

    // Arrival state stored during staging; consumed by UnsuspendSceneInput so effects
    // only become visible after the transition animation finishes.
    public Vector2?  _arrivalWalkTarget;
    public JsonArray _arrivalSequence;

    // Returns a named sequence from this scene's JSON "sequences" block, or null.
    public JsonArray GetNamedSequence(string name) =>
        _sceneData?["sequences"]?[name]?.AsArray();

    // Optional per-scene border overrides (read from scene JSON "border_style" / "border_width").
    public string SceneBorderStyle => _sceneData?["border_style"]?.GetValue<string>();
    public float? SceneBorderWidth => _sceneData?.ContainsKey("border_width") == true
        ? _sceneData["border_width"].GetValue<float>() : null;
    public record struct ArrivalData(
        string SourceName,
        string Dir,
        string Area,
        bool   Walk,
        string Thing = ""
    );

    private readonly record struct ExitZone(string Dest, Vector2 Center, Vector2 HalfSize, ArrivalData Arrival);
    private readonly List<(string Name, ExitZone Zone)> _exitZones    = new();
    private readonly HashSet<string>                    _insideExits  = new();
    private          bool                               _exitsSeeded  = false;
    // Name of the CollisionShape2D the player arrived through. Protected from re-triggering
    // while the arrival event sequence (walk-out + scripted moves) is still running.
    public string _arrivalZoneName = null;

    private readonly record struct AreaZone(Vector2 Center, Vector2 HalfSize, JsonArray Actions, bool Through, Vector2[] Polygon = null);
    private readonly List<(string Name, AreaZone Zone)> _areaZones   = new();
    private readonly HashSet<string>                    _insideAreas  = new();

    // Interactive areas — CollisionShape2D or CollisionPolygon2D regions in the
    // scene JSON that respond to use/look/talk clicks rather than walk triggers.
    private readonly record struct InteractiveArea(Vector2[] Polygon, JsonArray WalkActions, JsonArray UseActions, JsonArray LookActions, JsonArray TalkActions);
    private readonly List<InteractiveArea> _interactiveAreas = new();

    // Cached scene light — resolved once in _EnterTree() to avoid GetNode every frame.
    private PointLight2D _sceneLight;

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
        // In verbcoin mode the selected verb is one-shot; always restore to the
        // neutral base mode so a tap after the event doesn't re-trigger the same verb.
        prevInteractMode = mainScene.currentInputMode == Globals.InputModes.verbcoin
            ? (ego != null ? Globals.InteractModes.walk : Globals.InteractModes.look)
            : mainScene.GetInteractMode();
        mainScene.SetInteractMode(Globals.InteractModes.wait);
        isSceneInputSuspended = true;
    }

    public void UnsuspendSceneInput()
    {
        if (prevInteractMode == Globals.InteractModes.wait)
            prevInteractMode = ego != null ? Globals.InteractModes.walk : Globals.InteractModes.look;
        mainScene.SetInteractMode(prevInteractMode);
        isSceneInputSuspended = false;

        // Kick off arrival effects now that the transition is complete and the scene is live.
        // _arrivalSequence is set by on_arrive_from; "entry" always runs regardless of source.
        var entrySeq = _sceneData?["entry"]?.AsArray();
        if (entrySeq != null) PopulateEventQueue(eventQueue, entrySeq, this);
        // Walk out of arrival zone first (always), then run the arrival sequence.
        if (_arrivalWalkTarget.HasValue && ego != null)
        {
            eventQueue.AddEventMove(ego, _arrivalWalkTarget.Value, _interruptable: false);
            _arrivalWalkTarget = null;
        }
        if (_arrivalSequence != null)
        {
            PopulateEventQueue(eventQueue, _arrivalSequence, this);
            _arrivalSequence = null;
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
            camera             = GetNode<Camera2D>("Camera2D");
            cameraOriginalZoom = camera.Zoom;
            camera.Enabled     = false;
            cameraClamp        = GetNode<CollisionShape2D>("CameraClamp");
        }
        catch { camera = null; cameraClamp = null; }

        // Cache the scene light for _Process().
        try { _sceneLight = GetNode<PointLight2D>("PointLight2D"); }
        catch { _sceneLight = null; }

        // Collect darkmap zones for ambient darkness sampling.
        _darkmapZones = FindChildren("*", "", true, false).OfType<DarkmapZone>().ToList();

        // Spawn ego from GameConfig.EgoScene at the start_point position.
        // Guard: _EnterTree fires again on reparent (staging → main holder).
        if (ego == null && !isInsert)
        {
            var startPoint = GetNodeOrNull<Node2D>("start_point");
            var egoPacked  = ResourceLoader.Load<PackedScene>(GameConfig.EgoScene);
            if (egoPacked != null)
            {
                ego = egoPacked.Instantiate() as NPC;
                if (ego != null)
                {
                    ego.parentScene = this;
                    ego.Name        = "ego";
                    AddChild(ego);
                    ego.Position    = startPoint?.Position ?? NavCentroid();
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

        if (_dialogLayer == null)
        {
            _dialogLayer = new CanvasLayer { Layer = 13 };
            AddChild(_dialogLayer);
        }

        eventQueue = new EventSequence(this);

        // Load scene script — .covscript preferred, .json as fallback.
        // Must happen in _EnterTree (parent-first) so child thing._Ready() calls
        // can read _sceneData for interaction definitions.
        string basePath = !string.IsNullOrEmpty(entryScriptFile)
            ? $"res://{entryScriptFile.Replace(".json", "").Replace(".covscript", "")}"
            : SceneFilePath.Replace(".tscn", "");
        string covPath  = basePath + ".covscript";
        string jsonPath = basePath + ".json";
        if (FileAccess.FileExists(covPath))
            _sceneData = CovScript.Parse(FileAccess.GetFileAsString(covPath));
        else if (FileAccess.FileExists(jsonPath))
            _sceneData = JsonNode.Parse(FileAccess.GetFileAsString(jsonPath))?.AsObject();

        FlushDarkmapToThings();
    }

    // _Ready is empty so subclasses can override freely.
    public override void _Ready()
    {
        base._Ready();
        _dialogBoxScene = GD.Load<PackedScene>("res://ui/DialogBox.tscn");
        SetupAreaZones();
    }

    private Vector2 NavCentroid()
    {
        var nav = GetNodeOrNull<NavigationRegion2D>("NavigationRegion2D");
        if (nav?.NavigationPolygon == null || nav.NavigationPolygon.GetOutlineCount() == 0)
            return Vector2.Zero;
        var outline = nav.NavigationPolygon.GetOutline(0);
        if (outline.Length == 0) return Vector2.Zero;
        Vector2 sum = Vector2.Zero;
        foreach (var pt in outline) sum += pt;
        return sum / outline.Length;
    }

    private static bool InsideZone(Vector2 pos, in ExitZone zone) =>
        Mathf.Abs(pos.X - zone.Center.X) <= zone.HalfSize.X &&
        Mathf.Abs(pos.Y - zone.Center.Y) <= zone.HalfSize.Y;

    private static bool InsideZone(Vector2 pos, in AreaZone zone) =>
        zone.Polygon != null
            ? Geometry2D.IsPointInPolygon(pos, zone.Polygon)
            : Mathf.Abs(pos.X - zone.Center.X) <= zone.HalfSize.X &&
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
            var areaData = kv.Value?.AsObject();
            if (areaData == null) continue;

            // Resolve the node — accept either CollisionShape2D or CollisionPolygon2D.
            // Polygon is stored in world space for hit-testing.
            Vector2   center   = Vector2.Zero;
            Vector2   halfSize = Vector2.Zero;
            Vector2[] polygon  = null;

            CollisionShape2D shape = GetNodeOrNull<CollisionShape2D>(kv.Key);
            if (shape?.Shape is RectangleShape2D rect)
            {
                center   = shape.Position;
                halfSize = rect.Size / 2f;
            }
            else
            {
                CollisionPolygon2D cpoly = GetNodeOrNull<CollisionPolygon2D>(kv.Key);
                if (cpoly == null) continue;
                // Transform polygon points to parent (scene) space.
                polygon = new Vector2[cpoly.Polygon.Length];
                for (int i = 0; i < cpoly.Polygon.Length; i++)
                    polygon[i] = cpoly.ToGlobal(cpoly.Polygon[i]) - GlobalPosition;
            }

            // Walk / navigation verbs.
            bool through = false;
            JsonArray walkActions = areaData["move_into"]?.AsArray();
            if (walkActions == null)
            {
                walkActions = areaData["move_through"]?.AsArray();
                if (walkActions != null) through = true;
            }

            if (walkActions != null)
            {
                // Check for an "exit" action — if found, register as an exit zone.
                JsonObject exitAction = null;
                foreach (var item in walkActions)
                {
                    var obj = item?.AsObject();
                    if (obj?["action"]?.GetValue<string>() == "exit") { exitAction = obj; break; }
                }

                if (exitAction != null)
                {
                    if (shape == null) continue; // exits require RectangleShape2D
                    string dest = Scenes.Resolve(exitAction["destination"]?.GetValue<string>() ?? "");
                    if (string.IsNullOrEmpty(dest)) continue;
                    string arriveDir   = exitAction["arrive_dir"]?.GetValue<string>()   ?? "";
                    string arriveArea  = exitAction["arrive_area"]?.GetValue<string>()  ?? "";
                    string arriveThing = exitAction["arrive_thing"]?.GetValue<string>() ?? "";
                    bool   noWalk      = exitAction["no_walk"]?.GetValue<bool>() ?? false;
                    bool   arriveWalk  = !noWalk;
                    var    arrival     = new ArrivalData(System.IO.Path.GetFileNameWithoutExtension(SceneFilePath), arriveDir, arriveArea, arriveWalk, arriveThing);
                    _exitZones.Add((kv.Key, new ExitZone(dest, center, halfSize, arrival)));
                }
                else
                {
                    _areaZones.Add((kv.Key, new AreaZone(center, halfSize, walkActions, through, polygon)));
                }
                continue;
            }

            // Interaction verbs — walk / use / look / talk.
            // Keys may be pipe-separated to share actions across modes, e.g. "walk|use".
            static JsonArray VerbActions(JsonObject d, string verb) {
                if (d.ContainsKey(verb)) return d[verb]?.AsArray();
                foreach (var pair in d)
                    if (pair.Key.Contains('|') && Array.IndexOf(pair.Key.Split('|'), verb) >= 0)
                        return pair.Value?.AsArray();
                return null;
            }
            JsonArray walkActions2 = VerbActions(areaData, "walk");
            JsonArray useActions   = VerbActions(areaData, "use");
            JsonArray lookActions  = VerbActions(areaData, "look");
            JsonArray talkActions  = VerbActions(areaData, "talk");
            if (walkActions2 == null && useActions == null && lookActions == null && talkActions == null) continue;

            // Build world-space polygon for hit-testing if we only have a rect.
            if (polygon == null)
            {
                polygon =
                [
                    new Vector2(center.X - halfSize.X, center.Y - halfSize.Y),
                    new Vector2(center.X + halfSize.X, center.Y - halfSize.Y),
                    new Vector2(center.X + halfSize.X, center.Y + halfSize.Y),
                    new Vector2(center.X - halfSize.X, center.Y + halfSize.Y),
                ];
            }
            _interactiveAreas.Add(new InteractiveArea(polygon, walkActions2, useActions, lookActions, talkActions));
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
    private CanvasLayer _invCoinLayer;

    // ---------------------------------------------------------------------------
    // _UnhandledInput — raw input collection only; actions taken in _Process().
    // ---------------------------------------------------------------------------
    public override void _UnhandledInput(InputEvent @event)
    {
        // Block while suspended, paused, dialog open, or inventory visible.
        if (IsPaused() || isSceneInputSuspended || HasOpenDialog || (mainScene?.inventoryScene?.Visible ?? false))
            return;

        if (@event is InputEventMouseButton mouse)
        {
            isTouch = false;

            if (mouse.ButtonIndex == MouseButton.Left)
            {
                if (mouse.Pressed)
                {
                    isMouseLeftButtonClicked = false;
                    if (mouse.DoubleClick)
                        isMouseLeftButtonDoubleClicked = true;
                    else
                    {
                        isMouseLeftButtonDown = true;
                        if (verbCoinControl == null)
                            mousePosOnPress = GetLocalMousePosition();
                    }
                }
                else
                {
                    // Only count as a click if we also saw the press in _UnhandledInput.
                    // If the press was consumed by a dialog, isMouseLeftButtonDown was
                    // never set — so the release must be ignored to prevent a spurious
                    // re-interaction on the frame after the dialog closes.
                    bool wasPressing = isMouseLeftButtonDown;
                    isMouseLeftButtonDown = false;
                    if (wasPressing)
                    {
                        if (isMouseLeftButtonHeld)
                            isMouseLeftButtonHeld = false;
                        else
                            isMouseLeftButtonClicked = true;
                    }
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
                isMouseLeftButtonClicked = false;
                isMouseLeftButtonDown = true;
                if (verbCoinControl == null)
                    mousePosOnPress = ToLocal(GetViewport().GetCanvasTransform().AffineInverse() * touch.Position);
            }
            else
            {
                bool wasPressing = isMouseLeftButtonDown;
                isMouseLeftButtonDown = false;
                if (wasPressing)
                {
                    if (isMouseLeftButtonHeld)
                        isMouseLeftButtonHeld = false;
                    else
                        isMouseLeftButtonClicked = true;
                }
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
        if (next == 0 && ego == null && !isInsert) next = 1;
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

    public void ShowInventoryVerbCoin(Vector2 screenPos, InventoryItem.ItemType targetSlotType, Action<Globals.InteractModes> onCommit, Action onCleanup = null)
    {
        if (verbCoinControl != null) return;

        if (_invCoinLayer == null)
        {
            _invCoinLayer = new CanvasLayer { Layer = 14 };
            AddChild(_invCoinLayer);
        }

        var packed = GD.Load<PackedScene>("res://ui/VerbCoin.tscn");
        var coin = packed.Instantiate<VerbCoin>();
        coin.parentScene         = this;
        coin.showInventoryButton = false;
        coin.targetSlotType      = targetSlotType;
        coin.OnCommit = onCommit;
        coin.OnClose  = () => {
            verbCoinControl = null;
            mousePosOnPress = new Vector2(-1, -1);
            mainScene.SetInteractMode(ego != null ? Globals.InteractModes.walk : Globals.InteractModes.look);
            onCleanup?.Invoke();
            coin.QueueFree();
        };
        coin.Position   = screenPos;   // CanvasLayer children use screen space directly
        verbCoinControl = coin;
        _invCoinLayer.AddChild(coin);
    }

    public void HideVerbCoin()
    {
        var coin = verbCoinControl as VerbCoin;
        if (coin != null)
            mainScene.SetInteractMode(mainScene.GetInteractMode());

        verbCoinControl.QueueFree();
        verbCoinControl = null;

        if (mainScene.GetInteractMode() == Globals.InteractModes.inventory)
            ToggleInventory();
        else if (mousePosOnPress.X >= 0 && mousePosOnPress.Y >= 0)
            IterateThingsToInteract(mousePosOnPress, mainScene.GetInteractMode());

        mousePosOnPress = new Vector2(-1, -1);
        mainScene.SetInteractMode(ego != null ? Globals.InteractModes.walk : Globals.InteractModes.look);
    }

    // Called by VerbCoin when the player releases (mouse or touch) to commit the action.
    // VerbCoin marks the event as handled so _UnhandledInput doesn't double-process it.
    public void OnVerbCoinRelease()
    {
        if (verbCoinControl == null) return;
        isMouseLeftButtonDown = false;
        isMouseLeftButtonHeld = false;
        HideVerbCoin();
    }

    // ---------------------------------------------------------------------------
    // ToggleInventory
    // ---------------------------------------------------------------------------

    public void ToggleInventory()
    {
        var inv = mainScene?.inventoryScene;
        if (inv == null) return;
        if (!inv.Visible)
        {
            if (ego != null) ego.StopWalking();
            inv.Open();
        }
        else
        {
            inv.Close();
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
                Vector2 tl       = cameraClamp.Position - cameraClamp.Shape.GetRect().Size / 2f;
                Vector2 br       = cameraClamp.Position + cameraClamp.Shape.GetRect().Size / 2f;
                Vector2 halfView = GetViewportRect().Size / 2f / camera.Zoom;
                tl += halfView;
                br -= halfView;
                if (tl.X <= br.X && tl.Y <= br.Y)
                    newPos = newPos.Clamp(tl, br);
                else
                    GD.PushWarning($"[{Name}] CameraClamp too small for viewport: clamp={cameraClamp.Shape.GetRect().Size}, halfView={halfView * 2f}");
            }

            camera.Position = newPos;

            // Parallax scroll.
            if (parallaxSprites != null)
                for (int i = 0; i < parallaxSprites.Count; i++)
                {
                    float factor = (parallaxFactors != null && i < parallaxFactors.Count)
                        ? parallaxFactors[i]
                        : (float)i + 0.075f;
                    DoParallax(parallaxSprites[i], factor);
                }
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

                    // Protect the arrival zone from re-firing while the arrival event sequence
                    // (walk-out + scripted moves) is still running. Cleared once the queue drains.
                    if (name == _arrivalZoneName)
                    {
                        if (!isSceneInputSuspended && eventQueue.Count() == 0)
                            _arrivalZoneName = null; // sequence done — fall through to normal logic
                        else
                        {
                            if (inside) _insideExits.Add(name);
                            else        _insideExits.Remove(name);
                            continue;
                        }
                    }

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
            mainScene._debugPanel.Visible = Globals.showDebugTools && Globals.showDebugPanel;
            if (Globals.showDebugTools)
            {
                var sb = new System.Text.StringBuilder();

                sb.AppendLine($"[b]SCENE[/b]  {Name}");
                if (ego != null)
                {
                    sb.AppendLine($"x: {ego.Position.X,7:F1}  y: {ego.Position.Y,7:F1}  scale: {ego.Scale.X:F2}");
                    sb.AppendLine($"facing: {ego.Facing,-5}  fastwalk: {ego.isFastWalking}");
                    sb.AppendLine($"mode: {mainScene.GetInteractMode()}  item: {mainScene.usingItem}");
                }

                sb.AppendLine();
                sb.AppendLine("[b]EVENTS[/b]");
                if (eventQueue.Steps.Count == 0)
                    sb.AppendLine("  (none)");
                else
                    foreach (var s in eventQueue.Steps)
                        sb.AppendLine($"  {s.DebugName}");

                sb.AppendLine();
                sb.AppendLine("[b]INVENTORY[/b]");
                if (mainScene.inventory.Count == 0)
                    sb.AppendLine("  (empty)");
                else
                    foreach (var item in mainScene.inventory)
                        sb.AppendLine($"  {item.Type}");

                sb.AppendLine();
                sb.AppendLine("[b]FLAGS[/b]");
                if (mainScene.sceneFlags.Count == 0)
                    sb.AppendLine("  (none)");
                else
                    foreach (var f in mainScene.sceneFlags)
                        sb.AppendLine($"  [{f.sceneName}] {f.Name} = {f.Value}");

                sb.AppendLine();
                sb.AppendLine("[b]THING STATES[/b]");
                string lastThingScene = null;
                foreach (var (tScene, tThing, tSummary) in mainScene.GetThingStateDebugLines())
                {
                    if (tScene != lastThingScene)
                    {
                        sb.AppendLine($"  [{tScene}]");
                        lastThingScene = tScene;
                    }
                    sb.AppendLine($"    {tThing}  {tSummary}");
                }
                if (lastThingScene == null)
                    sb.AppendLine("  (none)");

                mainScene.debugText.Text = sb.ToString();
                QueueRedraw();
            }
        }

        // Tick event queue.
        eventQueue?.Tick();

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

                    // Ambient darkness from darkmap zones.
                    if (_darkmapZones.Count > 0 && tsprite.Material != null)
                        ((ShaderMaterial)tsprite.Material).SetShaderParameter("overall_dark", SampleDarkness(things[i].Position));

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

                        // Radial hatch lines: compute the light's position in the sprite's
                        // frame UV space so the shader can fan lines out from the exact
                        // light position. Uses ToLocal() so scale/rotation are handled.
                        Vector2 lightInSpriteLocal = tsprite.ToLocal(_sceneLight.GlobalPosition);
                        if (tsprite.Texture == null) continue;
                        Vector2 texSize   = new Vector2(tsprite.Texture.GetWidth(), tsprite.Texture.GetHeight());
                        Vector2 frameSize = texSize / new Vector2(tsprite.Hframes, tsprite.Vframes);
                        Vector2 frameTL   = tsprite.Offset - frameSize / 2f;
                        Vector2 lightUV   = (lightInSpriteLocal - frameTL) / frameSize;
                        ((ShaderMaterial)tsprite.Material).SetShaderParameter("light_pos_uv", lightUV);
                        ((ShaderMaterial)tsprite.Material).SetShaderParameter("hframes",      tsprite.Hframes);
                        ((ShaderMaterial)tsprite.Material).SetShaderParameter("vframes",      tsprite.Vframes);
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
            else if (!isMouseLeftButtonHeld && verbCoinControl != null
                     && verbCoinControl is not VerbCoin { OnCommit: not null })
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

        // Fall through to interactive areas (CollisionPolygon2D / CollisionShape2D
        // regions in the scene JSON that declare use/look/talk rather than move verbs).
        foreach (var area in _interactiveAreas)
        {
            if (!Geometry2D.IsPointInPolygon(pos, area.Polygon)) continue;
            JsonArray actions = mode switch
            {
                Globals.InteractModes.walk => area.WalkActions,
                Globals.InteractModes.use  => area.UseActions,
                Globals.InteractModes.look => area.LookActions,
                Globals.InteractModes.talk => area.TalkActions,
                _                          => null,
            };
            if (actions == null) break;
            eventQueue = new EventSequence(this);
            PopulateEventQueue(eventQueue, actions, this);
            break;
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

    // Called by VerbCoin to fire the selected action on whatever is at the press position.
    // Clears mousePosOnPress so HideVerbCoin doesn't dispatch a second time.
    public void TriggerInteractAtPressPos()
    {
        if (eventQueue != null && eventQueue.Count() > 0)
        {
            eventQueue.TryInterrupt();
            if (eventQueue.isInterrupted)
                eventQueue = new EventSequence(this);
        }
        IterateThingsToInteract(mousePosOnPress, mainScene.GetInteractMode());
        mousePosOnPress = new Vector2(-1, -1);
    }

    // Called by VerbCoin during the confirm phase when the user taps to interrupt.
    // Closes the verbcoin immediately and dispatches a fresh walk/interact at the new position.
    public void InterruptVerbCoin(Vector2 localPos)
    {
        mousePosOnPress       = new Vector2(-1, -1); // skip HideVerbCoin dispatch
        isMouseLeftButtonDown = false;
        isMouseLeftButtonHeld = false;
        HideVerbCoin();
        mousePosOnPress          = localPos; // set after HideVerbCoin clears it
        isMouseLeftButtonClicked = true;     // dispatches walk next _Process frame
    }

    // Forwarding shim — kept for backward compatibility with existing callers.
    // New code should call ScriptParser.PopulateEventQueue directly.
    public static void PopulateEventQueue(EventSequence queue, JsonArray actions, scene_script scene, thing self = null)
        => ScriptParser.PopulateEventQueue(queue, actions, scene, self);

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
        thing actor         = null,
        bool strict         = false,
        DialogBox.TailStyle? tailStyle = null,
        Globals.NarrationCorner corner = Globals.NarrationCorner.Auto,
        Globals.NarrationStyle  narrationStyle = Globals.NarrationStyle.Normal)
    {
        bool hasTail = tailPos != null;

        float facingOffset = 0f;
        if (egoFacing == NPC.Direction.left)
            facingOffset = -96f;
        else if (egoFacing == NPC.Direction.right)
            facingOffset = 64f;

        DialogBox db = _dialogBoxScene.Instantiate() as DialogBox;

        db.dialogColor     = color ?? Colors.White;
        db.offsetForFacing = facingOffset;
        db.dialogType      = _dialogType;
        db.parentScene     = this;
        db.trackActor      = actor;
        db.isStrict        = strict;
        db.narrationCorner = corner;
        db.narrationStyle  = narrationStyle;
        if (tailStyle.HasValue) db.SpeechTailStyle = tailStyle.Value;

        if (_dialogType == Globals.DialogTypes.choice)
        {
            db.dialogColor     = Colors.Yellow;
            db.narrationStyle  = Globals.NarrationStyle.Jagged;
            db.narrationCorner = Globals.NarrationCorner.BottomCenter;
        }

        if (hasTail) db.tailPos = tailPos.Value;

        if (_dialogType == Globals.DialogTypes.choice || _dialogType == Globals.DialogTypes.thinking)
            db.offsetForFacing = 0f;

        if (_dialogType != Globals.DialogTypes.choice)
            db.SetPhrase(text);
        else
            db.SetPhrase(string.Join("\n", _dialogChoices ?? new string[0]));

        _dialogLayer.AddChild(db); // _Ready fires here — safe to access nodes after this point

        if (_dialogType == Globals.DialogTypes.choice && _dialogChoices != null)
        {
            db.dialogChoices.InitDialogChoices(_dialogChoices);
            db.GetNode<Timer>("DB_TextTimer").Autostart = false;
        }

        db.InitDialogBox(pos ?? Vector2.Zero);
        return db;
    }

    // ---------------------------------------------------------------------------
    // Darkmap sampling
    // ---------------------------------------------------------------------------

    // Pre-applies darkmap to all things immediately on tree entry so there is no
    // one-frame bright flash before _Process() runs its first darkness pass.
    private void FlushDarkmapToThings()
    {
        if (_darkmapZones.Count == 0 || things == null) return;
        foreach (var t in things)
        {
            var spr = t.GetNodeOrNull<Sprite2D>("Sprite2D");
            if (spr?.Material is ShaderMaterial mat)
                mat.SetShaderParameter("overall_dark", SampleDarkness(t.Position));
        }
    }

    // Returns overall_dark value for the shader: 1.0 = fully lit, 0.0 = fully dark.
    private float SampleDarkness(Vector2 worldPos)
    {
        float darkness = 0f;
        foreach (var zone in _darkmapZones)
        {
            Vector2 local = zone.ToLocal(worldPos);
            if (!Geometry2D.IsPointInPolygon(local, zone.Polygon)) continue;
            float contribution = zone.Darkness;
            if (zone.Falloff > 0f)
            {
                float dist = DistanceToPolygonEdge(local, zone.Polygon);
                contribution *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp(dist / zone.Falloff, 0f, 1f));
            }
            darkness = Mathf.Max(darkness, contribution);
        }
        return 1f - darkness;
    }

    private static float DistanceToPolygonEdge(Vector2 p, Vector2[] polygon)
    {
        float min = float.MaxValue;
        for (int i = 0; i < polygon.Length; i++)
        {
            Vector2 a  = polygon[i];
            Vector2 ab = polygon[(i + 1) % polygon.Length] - a;
            float   t  = Mathf.Clamp(ab.Dot(p - a) / ab.LengthSquared(), 0f, 1f);
            min = Mathf.Min(min, p.DistanceTo(a + ab * t));
        }
        return min;
    }

    // ---------------------------------------------------------------------------
    // Signal handlers — override in room subclass to react to ego events.
    // ---------------------------------------------------------------------------

    public virtual void _on_Ego_DestinationReached() { }
    public virtual void _on_Ego_FacingChanged()      { }
}
