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

    public bool isInTransition = false;

    private Globals.InteractModes _previousInteractMode = Globals.InteractModes.walk;

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

    private const float PANEL_GUTTER   = 18f;
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
        overlayScene = currentSceneHolder.GetNode<OverlayScene>("OverlayScene");
        currentSceneHolder.RemoveChild(overlayScene);
        AddChild(overlayScene);
        overlayScene.Layer = 10;

        // MainScene has a Camera2D for legacy AnimationPlayer/CanvasLayer reasons,
        // but per-scene cameras handle all actual rendering. Disable it permanently
        // so it never steals the viewport when a scene camera is momentarily inactive.
        var cam = GetNode<Camera2D>("Camera2D");
        cam.Enabled  = false;
        cam.Position = GetViewportRect().Size / 2f;

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
        string entryPoint             = "default",
        TransitionType transitionType = TransitionType.NextPanel)
    {
        if (isInTransition) return;
        isInTransition = true;

        switch (transitionType)
        {
            case TransitionType.NextPanel:
                NextPanelTransition(roomPath, entryPoint);
                break;
            case TransitionType.PageTurn:
                PageTurnTransition(roomPath, entryPoint);
                break;
            default:
                FadeTransition(roomPath, entryPoint);
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
    private async void NextPanelTransition(string roomPath, string entryPoint)
    {
        int fromIndex = _currentPanelIndex;
        int toIndex   = (fromIndex + 1) % 6;

        if (fromIndex == 5)
        {
            PageTurnTransition(roomPath, entryPoint);
            return;
        }

        Vector2 vp = GetViewportRect().Size;

        // 1. Capture the departing scene (without cursor/overlay) and store it.
        _panelTextures[fromIndex] = await CaptureViewportClean();

        // 2. Instantiate the next scene into an offscreen SubViewport, capture
        //    the thumbnail from there, then move the same instance to nextSceneHolder.
        //    One _Ready() call = one consistent random/flag state, never visible.
        PrepareNextScene(roomPath, entryPoint);
        _panelTextures[toIndex] = await CaptureNextSceneThumbnail();

        // 3. Build the 6-panel page overlay, starting zoomed into fromIndex.
        var overlay = new CanvasLayer { Layer = 100 };
        AddChild(overlay);
        overlay.AddChild(new ColorRect
        {
            Color    = new Color(0.96f, 0.93f, 0.82f),
            Size     = vp,
            Position = Vector2.Zero,
        });

        Control page      = BuildPageContainer(vp);
        overlay.AddChild(page);

        Vector2 panelSize = PanelSize(vp);
        float   zoomIn    = ZoomInScale(panelSize, vp);
        page.Scale    = new Vector2(zoomIn, zoomIn);
        page.Position = ContainerPosForPanel(fromIndex, panelSize, zoomIn, vp);

        // 4. Zoom out — ease-in so it feels like the camera is being pulled back.
        float dur      = RandomisedDuration(0.50f);
        Tween tweenOut = CreateTween().SetParallel(true);
        tweenOut.TweenProperty(page, "scale",    new Vector2(PAGE_SCALE_OUT, PAGE_SCALE_OUT), dur)
            .SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Sine);
        tweenOut.TweenProperty(page, "position", ContainerPosForFullPage(vp), dur)
            .SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Sine);
        await ToSignal(tweenOut, Tween.SignalName.Finished);

        // 5. Pause — vary the beat length so it never feels mechanical.
        Tween pause = CreateTween();
        pause.TweenInterval(RandomisedDuration(0.22f, 0.20f));
        await ToSignal(pause, Tween.SignalName.Finished);

        // 6. Zoom in — ease-out with slight overshoot (TransitionType.Back).
        dur = RandomisedDuration(0.45f);
        Tween tweenIn = CreateTween().SetParallel(true);
        tweenIn.TweenProperty(page, "scale",    new Vector2(zoomIn, zoomIn), dur)
            .SetEase(Tween.EaseType.Out).SetTrans(RandomZoomInCurve());
        tweenIn.TweenProperty(page, "position", ContainerPosForPanel(toIndex, panelSize, zoomIn, vp), dur)
            .SetEase(Tween.EaseType.Out).SetTrans(RandomZoomInCurve());
        await ToSignal(tweenIn, Tween.SignalName.Finished);

        // 7. Finalise.
        _currentPanelIndex = toIndex;
        SwitchCurrentSceneForNext();
        overlay.QueueFree();
        currentScene.UnsuspendSceneInput();
        isInTransition = false;
    }

    // ---------------------------------------------------------------------------
    // PageTurn transition — full comic page curl.
    // ---------------------------------------------------------------------------
    private async void PageTurnTransition(string roomPath, string entryPoint)
    {
        Vector2 vp = GetViewportRect().Size;

        // 1. Save the current scene at the correct panel slot.
        _panelTextures[_currentPanelIndex] = await CaptureViewportClean();

        // 2. Instantiate into SubViewport, capture thumbnail, move to nextSceneHolder.
        PrepareNextScene(roomPath, entryPoint);
        ImageTexture nextTex = await CaptureNextSceneThumbnail();

        // 3. Build the 6-panel page overlay, zoomed into the current panel.
        var pageOverlay = new CanvasLayer { Layer = 100 };
        AddChild(pageOverlay);
        pageOverlay.AddChild(new ColorRect
        {
            Color    = new Color(0.96f, 0.93f, 0.82f),
            Size     = vp,
            Position = Vector2.Zero,
        });

        Control page      = BuildPageContainer(vp);
        pageOverlay.AddChild(page);

        Vector2 panelSize = PanelSize(vp);
        float   zoomIn    = ZoomInScale(panelSize, vp);
        page.Scale    = new Vector2(zoomIn, zoomIn);
        page.Position = ContainerPosForPanel(_currentPanelIndex, panelSize, zoomIn, vp);

        // 4. Zoom out to reveal the full page.
        float dur      = RandomisedDuration(0.40f);
        Tween tweenOut = CreateTween().SetParallel(true);
        tweenOut.TweenProperty(page, "scale",    new Vector2(PAGE_SCALE_OUT, PAGE_SCALE_OUT), dur)
            .SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Sine);
        tweenOut.TweenProperty(page, "position", ContainerPosForFullPage(vp), dur)
            .SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Sine);
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
            Color    = new Color(0.96f, 0.93f, 0.82f),
            Size     = vp,
            Position = Vector2.Zero,
        });

        float   newZoomIn    = ZoomInScale(panelSize, vp);
        Control newPage      = BuildPageContainer(vp);
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

        // 9. Zoom into panel 0.
        dur = RandomisedDuration(0.45f);
        Tween tweenIn = CreateTween().SetParallel(true);
        tweenIn.TweenProperty(newPage, "scale",    new Vector2(newZoomIn, newZoomIn), dur)
            .SetEase(Tween.EaseType.Out).SetTrans(RandomZoomInCurve());
        tweenIn.TweenProperty(newPage, "position", ContainerPosForPanel(0, panelSize, newZoomIn, vp), dur)
            .SetEase(Tween.EaseType.Out).SetTrans(RandomZoomInCurve());
        await ToSignal(tweenIn, Tween.SignalName.Finished);

        // 10. Finalise.
        SwitchCurrentSceneForNext();
        newPageOverlay.QueueFree();
        currentScene.UnsuspendSceneInput();
        isInTransition = false;
    }

    // ---------------------------------------------------------------------------
    // Fade-to-black transition.
    // ---------------------------------------------------------------------------
    private void FadeTransition(string roomPath, string entryPoint)
    {
        PrepareNextScene(roomPath, entryPoint);
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

    private void PrepareNextScene(string roomPath, string entryPoint = "default")
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

        ApplyEntryPoint(nextScene, roomPath, entryPoint);

        // For camera-following scenes: snap the camera to the character's start
        // position (clamped), set the SubViewport CanvasTransform to match so the
        // thumbnail renders the correct view, then prime parallax to match.
        if (nextScene.hasCameraControl && nextScene.camera != null && nextScene.character != null)
        {
            Vector2 snapped = nextScene.character.Position;

            if (nextScene.cameraClamp != null)
            {
                Vector2 tl = nextScene.cameraClamp.Position - nextScene.cameraClamp.Shape.GetRect().Size / 2f;
                Vector2 br = nextScene.cameraClamp.Position + nextScene.cameraClamp.Shape.GetRect().Size / 2f;
                tl += vp / 2f;
                br -= vp / 2f;
                snapped = snapped.Clamp(tl, br);
            }

            nextScene.camera.Position = snapped;
            _stagingViewport.CanvasTransform = new Transform2D(0f, vp / 2f - snapped);
            PrimeParallax(nextScene);
        }

        nextScene.SuspendSceneInput();
    }

    private void ApplyEntryPoint(scene_script scene, string roomPath, string entryPoint)
    {
        // Auto-derive JSON path from scene file name (e.g. "entryway.tscn" → "game/data/scenes/entryway.json").
        // The [Export] entryScriptFile on the scene takes priority if set.
        string basename  = System.IO.Path.GetFileNameWithoutExtension(roomPath);
        string relative  = !string.IsNullOrEmpty(scene.entryScriptFile)
            ? scene.entryScriptFile
            : $"game/data/scenes/{basename}.json";
        string jsonPath  = $"res://{relative}";

        if (!FileAccess.FileExists(jsonPath)) return;

        var doc = JsonNode.Parse(FileAccess.GetFileAsString(jsonPath))?.AsObject();
        if (doc == null) return;

        // Fall back to "default" if the named entry point doesn't exist.
        var ep = doc[entryPoint]?.AsObject() ?? doc["default"]?.AsObject();
        if (ep == null) return;

        var character = scene.character ?? scene.GetNodeOrNull<Character>("Character");
        if (character == null) return;

        if (ep.ContainsKey("charStartPos"))
        {
            var arr = ep["charStartPos"].AsArray();
            character.Position = new Vector2(arr[0].GetValue<float>(), arr[1].GetValue<float>());
        }

        if (ep.ContainsKey("charStartDir"))
        {
            character.ChangeFacing(ep["charStartDir"].GetValue<string>() switch
            {
                "up"    => NPC.Direction.up,
                "down"  => NPC.Direction.down,
                "left"  => NPC.Direction.left,
                "right" => NPC.Direction.right,
                _       => NPC.Direction.down,
            });
        }

        if (ep.ContainsKey("sequence"))
        {
            var seq = new EventSequence(scene);
            scene_script.PopulateEventQueue(seq, ep["sequence"].AsArray(), scene);
            scene.eventQueue = seq;
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

        // Prime parallax so the first live frame matches the thumbnail.
        PrimeParallax(currentScene);
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
        (vp.X - 3f * PANEL_GUTTER) / 2f,
        (vp.Y - 4f * PANEL_GUTTER) / 3f
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
            PANEL_GUTTER + (index % 2) * (cell.X + PANEL_GUTTER),
            PANEL_GUTTER + (index / 2) * (cell.Y + PANEL_GUTTER)
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
        if (overlayScene != null) overlayScene.Visible = false;

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
    private Control BuildPageContainer(Vector2 vp)
    {
        Vector2 panelSize = PanelSize(vp);

        var container = new Control
        {
            Size        = vp,
            PivotOffset = Vector2.Zero,
        };

        container.AddChild(new ColorRect
        {
            Color    = new Color(0.96f, 0.93f, 0.82f),
            Size     = vp,
            Position = Vector2.Zero,
        });

        var celTex = GD.Load<Texture2D>("res://game/art/cel_overlay.png");

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

            // Overlay the cel border image on each panel to match the in-game frame.
            if (celTex != null)
                container.AddChild(new TextureRect
                {
                    Texture     = celTex,
                    StretchMode = TextureRect.StretchModeEnum.Scale,
                    ExpandMode  = TextureRect.ExpandModeEnum.IgnoreSize,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                    Position    = pos,
                    Size        = panelSize,
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
