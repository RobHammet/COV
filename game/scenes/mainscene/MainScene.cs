// MainScene.cs — root scene controller. Persists across all room transitions.
//
// Owns:
//   inventory    — list of held InventoryItem nodes
//   sceneFlags   — persistent named flags, saved/loaded as JSON
//   cursor       — custom Cursor sprite, updated each frame by UpdateCursor()
//   overlayScene — verb tray, inventory button, debug label
//
// OVERLAY SCENE
//   OverlayScene starts as a child of CurrentSceneHolder in the .tscn (index 2),
//   but _Ready() immediately reparents it to MainScene directly so it is never
//   affected by scene-transition mechanics. The Black ColorRect (index 1) stays
//   in CurrentSceneHolder because AnimationPlayer.root_node targets it there.
//
// NEXT SCENE HOLDER
//   NextSceneHolder sits at position (640, 0) in the .tscn so that a loading
//   flash during LoadNextScene is off-screen. PageTurnTransition temporarily
//   moves it to (0, 0) just before the curl shader starts (so transparent curl
//   pixels reveal the new scene at the correct position), then restores it.
//
// TRANSITIONS
//   NextPanel (default) — zoom-out of 6-panel comic page → brief pause → zoom-in
//                         to next panel. Saves scene screenshots for page continuity.
//                         Escalates to PageTurn automatically at panel 5 → 0.
//   PageTurn            — shows the full saved page, then curls the whole thing away
//                         (all six panels distort together) to reveal the new scene.
//                         Resets the panel screenshot history for a fresh page.
//   FadeToBlack         — classic fade; driven by the AnimationPlayer.
//
// SCREENSHOTS
//   CaptureViewportClean() hides the cursor and OverlayScene for one GPU frame
//   before capturing so neither appears in panel thumbnails.

using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Json.Nodes;

public enum TransitionType { NextPanel, PageTurn, FadeToBlack }

public partial class MainScene : Node2D
{
    [Export] bool showDebugTools;
    [Export] bool forceMobile;

    public RichTextLabel debugText;

    public List<Globals.SceneFlag> sceneFlags = [];
    public List<InventoryItem>     inventory  = [];

    public InventoryItem.ItemType usingItem = InventoryItem.ItemType.none;

    public Globals.InteractModes currentInteractMode = Globals.InteractModes.walk;
    public Globals.InputModes    currentInputMode    = Globals.InputModes.verbtray;

    public Node2D       currentSceneHolder;
    public Node2D       nextSceneHolder;
    public scene_script currentScene;
    public scene_script nextScene;
    public OverlayScene overlayScene;
    public Cursor       cursor;
    private CanvasLayer _halftoneLayer;

    public bool isInTransition = false;

    private Globals.InteractModes _previousInteractMode = Globals.InteractModes.walk;

    // Offset derived from CelBorderEdge — how far the viewable area's top-left is
    // from the viewport origin. Applied as Camera2D.Offset so the rendered view shifts
    // without affecting any world-space coordinates (nav, clicks, movement targets).
    private Vector2 _celBorderOffset;

    // ---------------------------------------------------------------------------
    // Comic page state.
    //
    // Panel layout (2 cols × 3 rows, left-to-right then top-to-bottom):
    //   0 = top-left    1 = top-right
    //   2 = mid-left    3 = mid-right
    //   4 = bot-left    5 = bot-right
    //
    // _panelTextures is filled as the player moves through panels. Each entry is
    // either a captured screenshot (visited) or null (not yet seen → placeholder).
    // PageTurn resets the array for the new page and seeds slot 0 with the
    // incoming scene thumbnail.
    // ---------------------------------------------------------------------------
    private int            _currentPanelIndex = 0;
    public  int             CurrentPanelIndex => _currentPanelIndex;
    private ImageTexture[] _panelTextures     = new ImageTexture[6];

    private const float PANEL_GUTTER_H = 8f;   // horizontal gap between panels
    private const float PANEL_GUTTER_V = 18f;  // vertical gap between panels
    private const float PAGE_SCALE_OUT = 0.88f;

    // Placeholder fill colors for unseen panels.
    private static readonly Color[] _panelColors =
    [
        new(0.84f, 0.74f, 0.62f),
        new(0.62f, 0.74f, 0.84f),
        new(0.74f, 0.84f, 0.62f),
        new(0.84f, 0.62f, 0.74f),
        new(0.68f, 0.78f, 0.68f),
        new(0.78f, 0.68f, 0.78f),
    ];

    public override void _Ready()
    {
        Input.MouseMode = Input.MouseModeEnum.Hidden;

        cursor    = GetNode<Cursor>("Cursor");
        debugText = GetNode<RichTextLabel>("DebugText");

        // Move cursor into its own CanvasLayer above the overlay (layer 10)
        // so it is always in front.
        RemoveChild(cursor);
        var cursorLayer = new CanvasLayer { Layer = 20 };
        AddChild(cursorLayer);
        cursorLayer.AddChild(cursor);

        currentSceneHolder = GetNode<Node2D>("CurrentSceneHolder");
        nextSceneHolder    = GetNode<Node2D>("NextSceneHolder");

        // Load the starting scene from game_config.json.
        string configJson = FileAccess.GetFileAsString("res://game/game_config.json");
        var    config     = JsonNode.Parse(configJson).AsObject();
        string startPath  = config["start_scene"].GetValue<string>();
        currentScene      = ResourceLoader.Load<PackedScene>(startPath).Instantiate() as scene_script;
        currentSceneHolder.AddChild(currentScene);

        // The starting scene's camera is disabled in _EnterTree (same as all
        // scenes) — re-enable it here now that it is the live scene.
        if (currentScene?.camera != null)
        {
            currentScene.camera.Enabled = true;
            currentScene.camera.MakeCurrent();
        }

        // Detach OverlayScene from CurrentSceneHolder so it is never touched by
        // scene-transition code (scene frees, AddChild calls, etc.).
        // Black (the fade ColorRect) stays in CurrentSceneHolder for AnimationPlayer.
        _halftoneLayer = GetNode<CanvasLayer>("HalftoneLayer");
        overlayScene = currentSceneHolder.GetNode<OverlayScene>("OverlayScene");
        currentSceneHolder.RemoveChild(overlayScene);
        AddChild(overlayScene);
        overlayScene.Layer = 10;

        // Shift the camera view so scene content starts at the CelBorderEdge inner
        // boundary. Camera2D.Offset displaces what the camera renders in screen space
        // without touching world-space coordinates, so click detection, nav mesh, and
        // character movement are all unaffected.
        // Negated: Camera2D.Offset moves what the camera *looks at*, so a positive X
        // offset shifts rendered content left. We negate to shift content right/down.
        _celBorderOffset = -overlayScene.GetInnerRect().Position;
        ApplyCelBorderOffset(currentScene);

        // Snap the starting scene's camera to the ego's initial position, clamped
        // to the camera bounds — identical to what PrepareNextScene does for every
        // scene loaded via transition. Without this the camera starts at the .tscn
        // default position and pans toward the ego over many frames; clicks during
        // that pan land at world positions based on the lagging camera, so the walk
        // target appears offset from what the player actually clicked.
        if (currentScene.hasCameraControl && currentScene.camera != null && currentScene.ego != null)
        {
            Vector2 snapped  = currentScene.ego.Position;
            Vector2 halfView = GetViewportRect().Size / 2f / currentScene.camera.Zoom;
            if (currentScene.cameraClamp != null)
            {
                Vector2 tl = currentScene.cameraClamp.Position - currentScene.cameraClamp.Shape.GetRect().Size / 2f;
                Vector2 br = currentScene.cameraClamp.Position + currentScene.cameraClamp.Shape.GetRect().Size / 2f;
                tl += halfView;
                br -= halfView;
                snapped = snapped.Clamp(tl, br);
            }
            currentScene.camera.Position = snapped;
        }

        // MainScene has a Camera2D for legacy AnimationPlayer/CanvasLayer reasons,
        // but per-scene cameras handle all actual rendering. Disable it permanently
        // so it never steals the viewport when a scene camera is momentarily inactive.
        var cam = GetNode<Camera2D>("Camera2D");
        cam.Enabled  = false;
        cam.Position = GetViewportRect().Size / 2f;

        // Suspend input for one deferred frame so the NavigationServer2D has time
        // to process the initial navmesh bake before the player can click.
        // Every transition scene gets this via SuspendSceneInput → UnsuspendSceneInput;
        // the starting scene needs the same treatment or the first path query may use
        // a stale/incomplete map.
        currentScene.SuspendSceneInput();
        Callable.From(() => currentScene?.UnsuspendSceneInput()).CallDeferred();

        cursor.Frame = 0;

        Globals.showDebugTools = showDebugTools;

        bool isMobile = forceMobile || OS.HasFeature("mobile");
        currentInputMode = isMobile ? Globals.InputModes.verbcoin : Globals.InputModes.verbtray;

        if (currentInputMode == Globals.InputModes.verbcoin)
            overlayScene?.GetNode<Control>("VerbPanel")?.Hide();
    }

    public override void _Process(double delta)
    {
        UpdateCursor();

        string inventoryStr = "";
        foreach (var item in inventory)
            inventoryStr += item.Type.ToString() + ", ";
        SetOverlayLeftLabelText(inventoryStr);
    }

    public override void _Input(InputEvent inputEvent)
    {
        if (inputEvent.IsActionPressed("ui_save"))
        {
            GD.Print("saving...");
            Save();
        }
        else if (inputEvent.IsActionPressed("ui_load"))
        {
            GD.Print("loading...");
            Load();
        }
    }

    // ---------------------------------------------------------------------------
    // Interact mode.
    // ---------------------------------------------------------------------------

    public void SetInteractMode(Globals.InteractModes mode)
    {
        currentInteractMode = mode;
        UpdateCursor();
    }

    public Globals.InteractModes GetInteractMode() => currentInteractMode;

    // ---------------------------------------------------------------------------
    // Scene transition — entry point.
    // ---------------------------------------------------------------------------
    public void ChangeSceneToFile(
        string roomPath,
        scene_script.ArrivalData arrival = default,
        TransitionType transitionType    = TransitionType.NextPanel)
    {
        if (isInTransition) return;
        isInTransition = true;
        overlayScene?.HideForTransition();
        if (_halftoneLayer != null) _halftoneLayer.Visible = false;

        switch (transitionType)
        {
            case TransitionType.NextPanel:
                NextPanelTransition(roomPath, arrival);
                break;
            case TransitionType.PageTurn:
                PageTurnTransition(roomPath, arrival);
                break;
            default:
                FadeTransition(roomPath, arrival);
                break;
        }
    }

    // ---------------------------------------------------------------------------
    // NextPanel transition — comic-book focus shift.
    //
    // Captures and stores the current and incoming scene screenshots, builds the
    // 6-panel page, then animates: zoom-out (reveals full page) → pause → zoom-in
    // to the next panel. Previously visited panels keep their saved screenshots.
    //
    // Panel 5 → 0: escalates to PageTurnTransition (full page curl).
    // NOTE: _currentPanelIndex is NOT reset here on escalation — PageTurnTransition
    // reads it to save the screenshot to the correct panel slot (5).
    // ---------------------------------------------------------------------------
    private async void NextPanelTransition(string roomPath, scene_script.ArrivalData arrival)
    {
        int fromIndex = _currentPanelIndex;
        int toIndex   = (fromIndex + 1) % 6;

        if (fromIndex == 5)
        {
            PageTurnTransition(roomPath, arrival);
            return;
        }

        Vector2 vp = GetViewportRect().Size;

        // 1. Capture the departing scene (without cursor/overlay) and store it.
        _panelTextures[fromIndex] = await CaptureViewportClean();

        // 2. Instantiate the next scene into an offscreen SubViewport, capture
        //    the thumbnail from there, then move the same instance to nextSceneHolder.
        //    One _Ready() call = one consistent random/flag state, never visible.
        PrepareNextScene(roomPath, arrival);
        _panelTextures[toIndex] = await CaptureNextSceneThumbnail();

        // 3. Build the 6-panel page overlay, starting zoomed into fromIndex.
        var overlay = new CanvasLayer { Layer = 100 };
        AddChild(overlay);
        overlay.AddChild(new ColorRect
        {
            Color    = Colors.White,
            Size     = vp,
            Position = Vector2.Zero,
        });

        Control page      = BuildPageContainer(vp, fromIndex);
        overlay.AddChild(page);

        Vector2 panelSize = PanelSize(vp);
        float   zoomIn    = ZoomInScale(panelSize, vp);
        page.Scale    = new Vector2(zoomIn, zoomIn);
        page.Position = ContainerPosForPanel(fromIndex, panelSize, zoomIn, vp);

        // 4. Zoom out — ease-in-out for a smooth pull-back, not a whip.
        float dur      = RandomisedDuration(0.50f);
        Tween tweenOut = CreateTween().SetParallel(true);
        tweenOut.TweenProperty(page, "scale",    new Vector2(PAGE_SCALE_OUT, PAGE_SCALE_OUT), dur)
            .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
        tweenOut.TweenProperty(page, "position", ContainerPosForFullPage(vp), dur)
            .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
        await ToSignal(tweenOut, Tween.SignalName.Finished);

        // 5. Pause — vary the beat length so it never feels mechanical.
        Tween pause = CreateTween();
        pause.TweenInterval(RandomisedDuration(0.22f, 0.20f));
        await ToSignal(pause, Tween.SignalName.Finished);

        // 6. Zoom in — cubic ease-out, no overshoot.
        dur = RandomisedDuration(0.45f);
        Tween tweenIn = CreateTween().SetParallel(true);
        tweenIn.TweenProperty(page, "scale",    new Vector2(zoomIn, zoomIn), dur)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tweenIn.TweenProperty(page, "position", ContainerPosForPanel(toIndex, panelSize, zoomIn, vp), dur)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        await ToSignal(tweenIn, Tween.SignalName.Finished);

        // 7. Finalise.
        _currentPanelIndex = toIndex;
        SwitchCurrentSceneForNext();
        overlay.QueueFree();
        currentScene.UnsuspendSceneInput();
        isInTransition = false;
        overlayScene?.ShowAfterTransition();
        if (_halftoneLayer != null) _halftoneLayer.Visible = true;
    }

    // ---------------------------------------------------------------------------
    // PageTurn transition — full comic page curl.
    // ---------------------------------------------------------------------------
    private async void PageTurnTransition(string roomPath, scene_script.ArrivalData arrival)
    {
        Vector2 vp = GetViewportRect().Size;

        // 1. Save the current scene at the correct panel slot.
        _panelTextures[_currentPanelIndex] = await CaptureViewportClean();

        // 2. Instantiate into SubViewport, capture thumbnail, move to nextSceneHolder.
        PrepareNextScene(roomPath, arrival);
        ImageTexture nextTex = await CaptureNextSceneThumbnail();

        // 3. Build the 6-panel page overlay, zoomed into the current panel.
        var pageOverlay = new CanvasLayer { Layer = 100 };
        AddChild(pageOverlay);
        pageOverlay.AddChild(new ColorRect
        {
            Color    = Colors.White,
            Size     = vp,
            Position = Vector2.Zero,
        });

        Control page      = BuildPageContainer(vp, _currentPanelIndex);
        pageOverlay.AddChild(page);

        Vector2 panelSize = PanelSize(vp);
        float   zoomIn    = ZoomInScale(panelSize, vp);
        page.Scale    = new Vector2(zoomIn, zoomIn);
        page.Position = ContainerPosForPanel(_currentPanelIndex, panelSize, zoomIn, vp);

        // 4. Zoom out to reveal the full page — ease-in-out, not a whip.
        float dur      = RandomisedDuration(0.40f);
        Tween tweenOut = CreateTween().SetParallel(true);
        tweenOut.TweenProperty(page, "scale",    new Vector2(PAGE_SCALE_OUT, PAGE_SCALE_OUT), dur)
            .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
        tweenOut.TweenProperty(page, "position", ContainerPosForFullPage(vp), dur)
            .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
        await ToSignal(tweenOut, Tween.SignalName.Finished);

        Tween prePagePause = CreateTween();
        prePagePause.TweenInterval(RandomisedDuration(0.14f, 0.08f));
        await ToSignal(prePagePause, Tween.SignalName.Finished);

        // 5. Capture the full-page view.
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        ImageTexture pageTex = ImageTexture.CreateFromImage(GetViewport().GetTexture().GetImage());

        // 6. Build the new page at Layer 99 — it sits behind the curl so the
        //    shader reveals it as it peels. No blank frame ever appears.
        _panelTextures     = new ImageTexture[6];
        _panelTextures[0]  = nextTex;
        _currentPanelIndex = 0;

        var newPageOverlay = new CanvasLayer { Layer = 99 };
        AddChild(newPageOverlay);
        newPageOverlay.AddChild(new ColorRect
        {
            Color    = Colors.White,
            Size     = vp,
            Position = Vector2.Zero,
        });

        float   newZoomIn    = ZoomInScale(panelSize, vp);
        Control newPage      = BuildPageContainer(vp, 0);
        newPageOverlay.AddChild(newPage);
        newPage.Scale    = new Vector2(PAGE_SCALE_OUT, PAGE_SCALE_OUT);
        newPage.Position = ContainerPosForFullPage(vp);

        // 7. Curl the old page (Layer 100) away, revealing the new page beneath.
        pageOverlay.QueueFree();

        var curlOverlay = new CanvasLayer { Layer = 100 };
        AddChild(curlOverlay);

        var rect = new TextureRect
        {
            Texture     = pageTex,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Size        = vp,
            Position    = Vector2.Zero,
        };
        curlOverlay.AddChild(rect);

        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/page_turn.gdshader") };
        mat.SetShaderParameter("progress", 0.0f);
        rect.Material = mat;

        Tween curl = CreateTween();
        curl.TweenMethod(
            Callable.From<float>(p => mat.SetShaderParameter("progress", p)),
            0.0f, 1.0f, 0.85
        ).SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Cubic);
        await ToSignal(curl, Tween.SignalName.Finished);

        curlOverlay.QueueFree();

        // 8. Pause so the player can read the new page layout.
        Tween postPagePause = CreateTween();
        postPagePause.TweenInterval(RandomisedDuration(0.50f, 0.10f));
        await ToSignal(postPagePause, Tween.SignalName.Finished);

        // 9. Zoom into panel 0 — cubic ease-out, no overshoot.
        dur = RandomisedDuration(0.45f);
        Tween tweenIn = CreateTween().SetParallel(true);
        tweenIn.TweenProperty(newPage, "scale",    new Vector2(newZoomIn, newZoomIn), dur)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tweenIn.TweenProperty(newPage, "position", ContainerPosForPanel(0, panelSize, newZoomIn, vp), dur)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        await ToSignal(tweenIn, Tween.SignalName.Finished);

        // 10. Finalise.
        SwitchCurrentSceneForNext();
        newPageOverlay.QueueFree();
        currentScene.UnsuspendSceneInput();
        isInTransition = false;
        overlayScene?.ShowAfterTransition();
        if (_halftoneLayer != null) _halftoneLayer.Visible = true;
    }

    // ---------------------------------------------------------------------------
    // Fade-to-black transition.
    // ---------------------------------------------------------------------------
    private void FadeTransition(string roomPath, scene_script.ArrivalData arrival)
    {
        PrepareNextScene(roomPath, arrival);
        MoveNextSceneToHolder();
        GetNode<AnimationPlayer>("AnimationPlayer").Play("fadetoblack");
    }

    public void _on_AnimationPlayer_animation_finished(string animName)
    {
        if (animName == "fadetoblack")
        {
            SwitchCurrentSceneForNext();
            GetNode<AnimationPlayer>("AnimationPlayer").Play("fadeinfromblack");
            currentScene.UnsuspendSceneInput();
            isInTransition = false;
        overlayScene?.ShowAfterTransition();
        if (_halftoneLayer != null) _halftoneLayer.Visible = true;
        }
    }

    // ---------------------------------------------------------------------------
    // Shared scene-loading helpers.
    // ---------------------------------------------------------------------------

    // PrepareNextScene — instantiates the next scene inside a hidden SubViewport
    // so it never appears in the main viewport. CaptureNextSceneThumbnail() reads
    // back a screenshot from that SubViewport, then we reparent the already-live
    // scene node into nextSceneHolder. One _Ready() call, never visible on screen.
    private SubViewport _stagingViewport;

    private void PrepareNextScene(string roomPath, scene_script.ArrivalData arrival = default)
    {
        Vector2 vp = GetViewportRect().Size;

        _stagingViewport = new SubViewport
        {
            Size                   = new Vector2I((int)vp.X, (int)vp.Y),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            RenderTargetClearMode  = SubViewport.ClearMode.Always,
            TransparentBg          = false,
        };
        AddChild(_stagingViewport);

        var newSceneNode = ResourceLoader.Load<PackedScene>(roomPath).Instantiate() as Node2D;
        newSceneNode.Position = Vector2.Zero;
        _stagingViewport.AddChild(newSceneNode);
        nextScene = (scene_script)newSceneNode;

        ApplyArrival(nextScene, arrival);

        // For camera-following scenes: snap the camera to the character's start
        // position (clamped), set the SubViewport CanvasTransform to match so the
        // thumbnail renders the correct view, then prime parallax to match.
        if (nextScene.hasCameraControl && nextScene.camera != null)
        {
            Vector2 snapped = nextScene.ego?.Position ?? Vector2.Zero;

            if (nextScene.cameraClamp != null)
            {
                Vector2 clampZoom = nextScene.camera.Zoom;
                Vector2 halfView  = vp / 2f / clampZoom;
                Vector2 tl = nextScene.cameraClamp.Position - nextScene.cameraClamp.Shape.GetRect().Size / 2f;
                Vector2 br = nextScene.cameraClamp.Position + nextScene.cameraClamp.Shape.GetRect().Size / 2f;
                tl += halfView;
                br -= halfView;
                snapped = snapped.Clamp(tl, br);
            }

            nextScene.camera.Position = snapped;
            nextScene.camera.Offset   = _celBorderOffset;
            Vector2 zoom = nextScene.camera.Zoom;
            // CanvasTransform must replicate what Camera2D does at runtime:
            // scale by zoom, then translate so the camera position maps to vp/2.
            _stagingViewport.CanvasTransform = new Transform2D(
                new Vector2(zoom.X, 0f),
                new Vector2(0f, zoom.Y),
                vp / 2f - (snapped + _celBorderOffset) * zoom);
            PrimeParallax(nextScene);
        }

        nextScene.SuspendSceneInput();
    }

    private void ApplyArrival(scene_script scene, scene_script.ArrivalData arrival)
    {
        var character = scene.ego;
        if (character == null) return;

        // Resolve arrive_area: use explicit value or auto-detect from destination exits.
        string areaName = arrival.Area ?? "";
        if (string.IsNullOrEmpty(areaName) && !string.IsNullOrEmpty(arrival.SourceName))
        {
            var exits = scene._sceneData?["exits"]?.AsObject();
            if (exits != null)
            {
                foreach (var kv in exits)
                {
                    string exitDest = kv.Value?["destination"]?.GetValue<string>() ?? "";
                    if (string.Equals(exitDest, arrival.SourceName, StringComparison.OrdinalIgnoreCase))
                    {
                        areaName = kv.Key;
                        break;
                    }
                }
            }
        }

        // Place character at the centre of the arrive zone.
        CollisionShape2D arriveShape = null;
        if (!string.IsNullOrEmpty(areaName))
        {
            arriveShape = scene.GetNodeOrNull<CollisionShape2D>(areaName);
            if (arriveShape != null)
                character.Position = arriveShape.Position;
        }

        // Set facing direction.
        if (!string.IsNullOrEmpty(arrival.Dir))
        {
            character.ChangeFacing(arrival.Dir switch
            {
                "up"    => NPC.Direction.up,
                "down"  => NPC.Direction.down,
                "left"  => NPC.Direction.left,
                "right" => NPC.Direction.right,
                _       => NPC.Direction.down,
            });
        }

        // Check destination scene JSON for an on_arrive_from sequence keyed by source name.
        if (!string.IsNullOrEmpty(arrival.SourceName))
        {
            var onArriveFrom = scene._sceneData?["on_arrive_from"]?.AsObject();
            var seqJson      = onArriveFrom?[arrival.SourceName]?.AsArray()
                            ?? onArriveFrom?[arrival.SourceName.ToLower()]?.AsArray();
            if (seqJson != null)
                scene._arrivalSequence = seqJson;
        }

        // Store the walk target on the scene; UnsuspendSceneInput fires it after the
        // transition completes so the walk is visible to the player, not pre-run in staging.
        if (arrival.Walk && scene._arrivalSequence == null &&
            arriveShape?.Shape is RectangleShape2D rect && !string.IsNullOrEmpty(arrival.Dir))
        {
            Vector2 center = arriveShape.Position;
            Vector2 half   = rect.Size / 2f;
            const float margin = 20f;
            scene._arrivalWalkTarget = arrival.Dir switch
            {
                "right" => new Vector2(center.X + half.X + margin, center.Y),
                "left"  => new Vector2(center.X - half.X - margin, center.Y),
                "up"    => new Vector2(center.X, center.Y - half.Y - margin),
                "down"  => new Vector2(center.X, center.Y + half.Y + margin),
                _       => null,
            };
        }
    }

    // MoveNextSceneToHolder — call after thumbnail capture to move the scene
    // from the staging SubViewport into nextSceneHolder for the transition.
    private void MoveNextSceneToHolder()
    {
        // Push nextSceneHolder far off-screen before adding the scene.
        // The .tscn default of x=640 (one viewport-width right) is not
        // sufficient — a camera panned past centre can expose the left edge
        // of the incoming scene. x=100000 is safely unreachable.
        nextSceneHolder.Position = new Vector2(100000, 0);
        _stagingViewport.RemoveChild(nextScene);
        nextSceneHolder.AddChild(nextScene);
        _stagingViewport.QueueFree();
        _stagingViewport = null;
    }

    public void SwitchCurrentSceneForNext()
    {
        nextSceneHolder.RemoveChild(nextScene);
        currentSceneHolder.AddChild(nextScene);
        currentScene.QueueFree();
        currentScene = nextScene;
        nextScene    = null;

        // Re-enable the camera — it was disabled in _EnterTree to prevent
        // Godot from auto-stealing the viewport during transition.
        if (currentScene.camera != null)
        {
            currentScene.camera.Enabled = true;
            currentScene.camera.MakeCurrent();
        }

        ApplyCelBorderOffset(currentScene);

        // Prime parallax so the first live frame matches the thumbnail.
        PrimeParallax(currentScene);
    }

    private void ApplyCelBorderOffset(scene_script scene)
    {
        if (scene?.camera != null)
            scene.camera.Offset = _celBorderOffset;
    }

    // PrimeParallax — applies the parallax offset for every parallax sprite
    // in the given scene based on its current camera position. Call whenever
    // the camera is snapped (PrepareNextScene, SwitchCurrentSceneForNext) to
    // avoid a one-frame stale-offset jump.
    private static void PrimeParallax(scene_script scene)
    {
        if (scene.parallaxSprites == null) return;
        for (int i = 0; i < scene.parallaxSprites.Count; i++)
            scene.DoParallax(scene.parallaxSprites[i], i + 0.075f);
    }

    // ---------------------------------------------------------------------------
    // Panel layout helpers.
    // ---------------------------------------------------------------------------

    // CellSize — the grid slot each panel occupies (gutter-based, fills the page).
    private Vector2 CellSize(Vector2 vp) => new(
        (vp.X - 3f * PANEL_GUTTER_H) / 2f,
        (vp.Y - 4f * PANEL_GUTTER_V) / 3f
    );

    // PanelSize — the actual panel rect, aspect-ratio matched to the viewport,
    // fitted inside the cell (letter-boxed if necessary).
    private Vector2 PanelSize(Vector2 vp)
    {
        Vector2 cell   = CellSize(vp);
        float   aspect = vp.X / vp.Y;
        float   w      = cell.X;
        float   h      = w / aspect;
        if (h > cell.Y) { h = cell.Y; w = h * aspect; }
        return new Vector2(w, h);
    }

    // PanelTopLeft — top-left of the panel, centered within its cell.
    private Vector2 PanelTopLeft(int index, Vector2 panelSize, Vector2 vp)
    {
        Vector2 cell    = CellSize(vp);
        Vector2 cellPos = new(
            PANEL_GUTTER_H + (index % 2) * (cell.X + PANEL_GUTTER_H),
            PANEL_GUTTER_V + (index / 2) * (cell.Y + PANEL_GUTTER_V)
        );
        return cellPos + (cell - panelSize) * 0.5f;
    }

    private Vector2 PanelCenter(int index, Vector2 panelSize, Vector2 vp) =>
        PanelTopLeft(index, panelSize, vp) + panelSize * 0.5f;

    private float ZoomInScale(Vector2 panelSize, Vector2 vp) =>
        Mathf.Min(vp.X / panelSize.X, vp.Y / panelSize.Y);

    private Vector2 ContainerPosForPanel(int index, Vector2 panelSize, float scale, Vector2 vp) =>
        vp * 0.5f - PanelCenter(index, panelSize, vp) * scale;

    private Vector2 ContainerPosForFullPage(Vector2 vp) =>
        vp * (1f - PAGE_SCALE_OUT) * 0.5f;

    // ---------------------------------------------------------------------------
    // Organic timing helpers.
    // ---------------------------------------------------------------------------

    private static float RandomisedDuration(float baseDuration, float jitter = -1f)
    {
        if (jitter < 0f) jitter = baseDuration * 0.06f;
        return baseDuration + (GD.Randf() - 0.5f) * 2f * jitter;
    }

    private static Tween.TransitionType RandomZoomInCurve()
    {
        float r = GD.Randf();
        if (r < 0.55f) return Tween.TransitionType.Back;
        if (r < 0.85f) return Tween.TransitionType.Cubic;
        return Tween.TransitionType.Spring;
    }

    // ---------------------------------------------------------------------------
    // CaptureViewportClean — hides cursor and OverlayScene for one GPU frame.
    // ---------------------------------------------------------------------------
    private async Task<ImageTexture> CaptureViewportClean()
    {
        bool overlayWasVisible = overlayScene?.Visible ?? false;
        cursor.Visible = false;
        overlayScene?.HideForTransition();

        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        Image img = GetViewport().GetTexture().GetImage();

        cursor.Visible = true;
        if (overlayScene != null) overlayScene.Visible = overlayWasVisible;

        return ImageTexture.CreateFromImage(img);
    }

    // ---------------------------------------------------------------------------
    // CaptureNextSceneThumbnail — reads back a screenshot from the staging
    // SubViewport (scene is never visible in the main viewport), then moves the
    // scene into nextSceneHolder ready for the transition animation.
    // Returns null on failure (BuildPageContainer uses a placeholder colour).
    // ---------------------------------------------------------------------------
    private async Task<ImageTexture> CaptureNextSceneThumbnail()
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        ImageTexture result = null;
        try
        {
            result = ImageTexture.CreateFromImage(_stagingViewport.GetTexture().GetImage());
        }
        catch { }

        MoveNextSceneToHolder();
        return result;
    }

    // ---------------------------------------------------------------------------
    // BuildPageContainer — 6-panel comic page as a Control tree.
    // ---------------------------------------------------------------------------
    private ShaderMaterial HalftoneMaterial =>
        _halftoneLayer?.GetNodeOrNull<ColorRect>("ColorRect")?.Material as ShaderMaterial;

    private float GetHalftoneParam(string name, float fallback) =>
        HalftoneMaterial?.GetShaderParameter(name).AsSingle() ?? fallback;

    private Control BuildPageContainer(Vector2 vp, int startPanelIndex = 0)
    {
        Vector2 panelSize = PanelSize(vp);

        var container = new Control
        {
            Size        = vp,
            PivotOffset = Vector2.Zero,
        };

        container.AddChild(new ColorRect
        {
            Color    = Colors.White,
            Size     = vp,
            Position = Vector2.Zero,
        });

        for (int i = 0; i < 6; i++)
        {
            Vector2 pos = PanelTopLeft(i, panelSize, vp);

            if (_panelTextures[i] != null)
            {
                container.AddChild(new TextureRect
                {
                    Texture      = _panelTextures[i],
                    StretchMode  = TextureRect.StretchModeEnum.KeepAspectCentered,
                    ExpandMode   = TextureRect.ExpandModeEnum.IgnoreSize,
                    ClipContents = true,
                    Position     = pos,
                    Size         = panelSize,
                });
            }
            else
            {
                container.AddChild(new ColorRect
                {
                    Color    = _panelColors[i],
                    Position = pos,
                    Size     = panelSize,
                });
            }

            container.AddChild(overlayScene.MakePanelBorderOverlay(pos, panelSize));
        }

        // Halftone inside the container so it scales/moves with the page ("part of the paper").
        // frequency = HalftoneFrequency / panelScale so that when zoomed in (scale = 1/panelScale)
        // the on-screen dot size equals exactly HalftoneFrequency — matching the main scene.
        // uv_offset shifts the pattern origin to the start panel's position in page UV space,
        // so the dots are continuous with the main scene on entry.
        var halftoneShader = GD.Load<Shader>("res://shaders/halftone.gdshader");
        if (halftoneShader != null)
        {
            float   panelScale = panelSize.X / vp.X;
            Vector2 panelTL    = PanelTopLeft(startPanelIndex, panelSize, vp);
            Vector2 uvOffset   = -panelTL / vp;   // in page UV space, panel TL is at this fraction

            var mat = new ShaderMaterial { Shader = halftoneShader };
            mat.SetShaderParameter("radius_c",  GetHalftoneParam("radius_c",  0.2f));
            mat.SetShaderParameter("radius_m",  GetHalftoneParam("radius_m", -0.3f));
            mat.SetShaderParameter("radius_y",  GetHalftoneParam("radius_y",  0.0f));
            mat.SetShaderParameter("radius_k",  GetHalftoneParam("radius_k",  0.785f));
            mat.SetShaderParameter("frequency", GetHalftoneParam("frequency", 463.46f) / panelScale);
            mat.SetShaderParameter("uv_offset", uvOffset);
            container.AddChild(new ColorRect
            {
                Material    = mat,
                Size        = vp,
                Position    = Vector2.Zero,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Color       = Colors.Black,
            });
        }

        return container;
    }

    // ---------------------------------------------------------------------------
    // Cursor — updated every frame based on currentInteractMode.
    // ---------------------------------------------------------------------------
    public void UpdateCursor()
    {
        if (currentInputMode == Globals.InputModes.verbtray &&
            currentInteractMode != _previousInteractMode)
        {
            _previousInteractMode = currentInteractMode;

            cursor.Offset  = Vector2.Zero;
            cursor.Texture = cursor.cursorTexture;
            cursor.Hframes = cursor.cursorTextureSize.X;
            cursor.Vframes = cursor.cursorTextureSize.Y;

            switch (currentInteractMode)
            {
                case Globals.InteractModes.walk:
                    cursor.Frame = 0;
                    break;
                case Globals.InteractModes.look:
                    cursor.Frame = 1;
                    cursor.Offset = new Vector2(-32, -24);
                    break;
                case Globals.InteractModes.talk:
                    cursor.Frame = 2;
                    cursor.Offset = new Vector2(-32, -24);
                    break;
                case Globals.InteractModes.use:
                    cursor.Frame = 3;
                    break;
                case Globals.InteractModes.item:
                    if (usingItem == InventoryItem.ItemType.none)
                    {
                        cursor.Frame = 4;
                        cursor.Offset = new Vector2(-32, -24);
                    }
                    else
                    {
                        cursor.Texture = cursor.itemTexture;
                        cursor.Hframes = cursor.itemTextureSize.X;
                        cursor.Vframes = cursor.itemTextureSize.Y;
                        cursor.Frame   = (int)usingItem - 1;
                    }
                    break;
                case Globals.InteractModes.wait:
                    cursor.Frame = 5;
                    break;
            }
        }

        cursor.Position = GetViewport().GetMousePosition();
    }

    // ---------------------------------------------------------------------------
    // Overlay label.
    // ---------------------------------------------------------------------------
    public void SetOverlayLeftLabelText(string text)
    {
        overlayScene?.SetLabelText(text);
    }

    // Inner boundary of the cel border in screen space. Falls back to full
    // viewport rect if OverlayScene isn't present (e.g. during transitions).
    public Rect2 CelBorderInnerRect => overlayScene?.GetInnerRect() ?? GetViewportRect();

    // ---------------------------------------------------------------------------
    // Save / Load.
    // ---------------------------------------------------------------------------
    public void Save()
    {
        using var file = FileAccess.Open("user://savegame.save", FileAccess.ModeFlags.Write);
        foreach (var flag in sceneFlags)
        {
            var dict = new Godot.Collections.Dictionary<string, Variant>
            {
                { "sceneName", flag.sceneName },
                { "Name",      flag.Name      },
                { "Value",     flag.Value     }
            };
            file.StoreLine(Json.Stringify(dict));
        }
    }

    public void Load()
    {
        if (!FileAccess.FileExists("user://savegame.save")) return;

        using var file = FileAccess.Open("user://savegame.save", FileAccess.ModeFlags.Read);
        while (file.GetPosition() < file.GetLength())
        {
            var jsonString = file.GetLine();
            var json       = new Json();
            if (json.Parse(jsonString) != Error.Ok) continue;

            var data = new Godot.Collections.Dictionary<string, Variant>(
                (Godot.Collections.Dictionary)json.Data);
            sceneFlags.Add(new Globals.SceneFlag(
                data["sceneName"].ToString(),
                data["Name"].ToString(),
                data["Value"]));
        }
    }
}
