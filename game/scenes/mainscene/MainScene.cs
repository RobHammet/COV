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

public enum TransitionType { NextPanel, PageTurn, FadeToBlack, CoverTurn }

public partial class MainScene : Node2D
{
    [Export] bool showDebugTools;
    [Export] bool forceMobile;

    public RichTextLabel debugText;
    public Panel         _debugPanel;
    private DebugMenu    _debugMenu;

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
    private CanvasLayer _comicPageLayer;  // ComicPageLayer CanvasLayer (hidden during transitions)

    public bool isInTransition = false;

    private Globals.InteractModes _previousInteractMode = Globals.InteractModes.walk;

    // Offset derived from CelBorderEdge — how far the viewable area's top-left is
    // from the viewport origin. Applied as Camera2D.Offset so the rendered view shifts
    // without affecting any world-space coordinates (nav, clicks, movement targets).
    private Vector2 _celBorderOffset;
    // Default cel border settings captured from OverlayScene's exported values in _Ready().
    // Used to restore the border when a scene doesn't specify overrides.
    private OverlayScene.CelBorderStyle _defaultBorderStyle;
    private float                        _defaultBorderWidth;

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
    private int                          _currentPanelIndex  = 0;
    public  int                           CurrentPanelIndex  => _currentPanelIndex;
    private ImageTexture[]               _panelTextures      = new ImageTexture[6];
    private OverlayScene.CelBorderStyle[] _panelBorderStyles  = new OverlayScene.CelBorderStyle[6];
    private float?[]                     _panelBorderWidths  = new float?[6];
    private string[]                     _currentPageSceneNames = new string[6];
    private int                          _currentPageNumber  = 1;

    private const float PAGE_ASPECT    = 8.5f / 11.0f; // fallback if no CoverArea found
    // Set once in _Ready() from the CoverArea shape (single source of truth).
    // All PageSize/PageOffset/panel helpers read this; change CoverArea in editor to update all transitions.
    private float _activePageAspect = PAGE_ASPECT;

    // Set by Load() before a scene transition; consumed by SwitchCurrentSceneForNext()
    // to place the ego at the saved position/facing instead of start_point.
    private Vector2?       _pendingLoadPosition;
    private NPC.Direction? _pendingLoadFacing;
    // Set by Load() so SwitchCurrentSceneForNext skips CaptureThingState — loaded
    // states must not be overwritten by the current (fresh/unmodified) scene.
    private bool           _skipNextCapture;

    // Per-session thing state store: scene SceneFilePath → (thing Name → ThingState).
    // Only contains scenes that have been visited and had at least one thing change.
    // Captured when leaving a scene; applied when re-entering, before thumbnail capture.
    private record ThingState(
        bool IsExist, bool IsHidden, Vector2 Position, NPC.Direction? Facing,
        string Animation, int? Frame, bool? AnimPlaying);
    private readonly Dictionary<string, Dictionary<string, ThingState>> _thingStates = new();
    private AudioStreamPlayer _pageFlipPlayer;
    private AudioStreamPlayer _coverFlipPlayer;

    private const float PAGE_MARGIN_H  = 16f;          // left/right page margin
    private const float PAGE_MARGIN_V  = 18f;          // top/bottom page margin
    private const float PANEL_GUTTER_H = 4f;           // thin horizontal gap between columns
    private const float PANEL_GUTTER_V = 12f;          // vertical gap between rows
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
        _debugPanel = GetNode<Panel>("DebugLayer/DebugPanel");
        debugText   = _debugPanel.GetNode<RichTextLabel>("DebugText");

        _debugMenu             = new DebugMenu { mainScene = this };
        GetNode<CanvasLayer>("DebugLayer").AddChild(_debugMenu);

        // Move cursor into its own CanvasLayer above the overlay (layer 10)
        // so it is always in front.
        RemoveChild(cursor);
        var cursorLayer = new CanvasLayer { Layer = 20 };
        AddChild(cursorLayer);
        cursorLayer.AddChild(cursor);

        currentSceneHolder = GetNode<Node2D>("CurrentSceneHolder");
        nextSceneHolder    = GetNode<Node2D>("NextSceneHolder");

        // Load the starting scene (or cover page if configured) from game_config.json.
        string configJson = FileAccess.GetFileAsString("res://game/game_config.json");
        var    config     = JsonNode.Parse(configJson).AsObject();
        string coverPath  = config["cover_scene"]?.GetValue<string>() ?? "";
        string startPath  = config["start_scene"].GetValue<string>();
        string initialPath = !string.IsNullOrEmpty(coverPath) ? coverPath : startPath;
        currentScene      = ResourceLoader.Load<PackedScene>(initialPath).Instantiate() as scene_script;
        currentSceneHolder.AddChild(currentScene);

        // Derive page aspect from CoverArea shape — single source of truth for all transitions.
        var coverAreaNode = currentScene.GetNodeOrNull<CollisionShape2D>("CoverArea");
        if (coverAreaNode?.Shape is RectangleShape2D coverShapeRect)
            _activePageAspect = coverShapeRect.Size.X / coverShapeRect.Size.Y;

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
        _comicPageLayer = GetNode<CanvasLayer>("ComicPageLayer");
        // The saved .tres material stores uv_noise_scale/uv_dirt_scale as float (old type).
        // Force them to vec2(1,1) so the shader doesn't receive a zero vec2 and black out.
        if (ComicPageMaterial != null)
        {
            ComicPageMaterial.SetShaderParameter("uv_noise_scale", Vector2.One);
            ComicPageMaterial.SetShaderParameter("uv_dirt_scale",  Vector2.One);
        }
        overlayScene = currentSceneHolder.GetNode<OverlayScene>("OverlayScene");
        currentSceneHolder.RemoveChild(overlayScene);
        AddChild(overlayScene);
        overlayScene.Layer   = 10;
        _defaultBorderStyle  = overlayScene.BorderStyle;
        _defaultBorderWidth  = overlayScene.BorderWidth;

        // Hide the verb/inventory overlay while the cover page is showing.
        if (!string.IsNullOrEmpty(coverPath))
            overlayScene.Hide();

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
                if (tl.X <= br.X && tl.Y <= br.Y)
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

        _pageFlipPlayer = new AudioStreamPlayer
        {
            Stream   = ResourceLoader.Load<AudioStream>("res://game/audio/pageflip1.mp3"),
            VolumeDb = 0f,
        };
        AddChild(_pageFlipPlayer);

        _coverFlipPlayer = new AudioStreamPlayer
        {
            Stream   = ResourceLoader.Load<AudioStream>("res://game/audio/pageflip2.mp3"),
            VolumeDb = 0f,
        };
        AddChild(_coverFlipPlayer);

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
        else if (Globals.showDebugTools &&
                 inputEvent is InputEventKey key &&
                 key.Pressed && !key.Echo)
        {
            switch (key.Keycode)
            {
                case Key.F1:
                    Globals.showDebugPanel = !Globals.showDebugPanel;
                    GetViewport().SetInputAsHandled();
                    break;
                case Key.F2:
                    Globals.showDebugGraphics = !Globals.showDebugGraphics;
                    GetViewport().SetInputAsHandled();
                    break;
                case Key.F3:
                    _debugMenu.Toggle();
                    GetViewport().SetInputAsHandled();
                    break;
            }
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

        switch (transitionType)
        {
            case TransitionType.NextPanel:
                NextPanelTransition(roomPath, arrival);
                break;
            case TransitionType.PageTurn:
                PageTurnTransition(roomPath, arrival);
                break;
            case TransitionType.CoverTurn:
                CoverTurnTransition(roomPath, arrival);
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
        _panelTextures[fromIndex]       = await CaptureViewportClean();
        _panelBorderStyles[fromIndex]   = overlayScene?.BorderStyle ?? OverlayScene.CelBorderStyle.Normal;
        _panelBorderWidths[fromIndex]   = overlayScene?.BorderWidth;
        _currentPageSceneNames[fromIndex] = SceneBaseName(currentScene);

        // 2. Instantiate the next scene into an offscreen SubViewport, capture
        //    the thumbnail from there, then move the same instance to nextSceneHolder.
        //    One _Ready() call = one consistent random/flag state, never visible.
        PrepareNextScene(roomPath, arrival);
        _panelTextures[toIndex]         = await CaptureNextSceneThumbnail();
        _panelBorderStyles[toIndex]     = nextScene?.SceneBorderStyle != null ? OverlayScene.ParseBorderStyle(nextScene.SceneBorderStyle) : _defaultBorderStyle;
        _panelBorderWidths[toIndex]     = nextScene?.SceneBorderWidth ?? _defaultBorderWidth;
        _currentPageSceneNames[toIndex] = SceneBaseName(nextScene);

        // 3. Build the 6-panel page overlay, starting zoomed into fromIndex.
        if (_comicPageLayer != null) _comicPageLayer.Visible = false;
        var overlay = new CanvasLayer { Layer = 100 };
        AddChild(overlay);
        overlay.AddChild(new ColorRect
        {
            Color    = new Color(0.12f, 0.12f, 0.12f),
            Size     = vp,
            Position = Vector2.Zero,
        });

        Control page      = BuildPageContainer(vp);
        overlay.AddChild(page);

        Vector2 panelSize  = PanelSize(vp);
        float   zoomIn     = ZoomInScale(panelSize, vp);
        Vector2 startPos   = ContainerPosForPanel(fromIndex, panelSize, zoomIn, vp);
        page.Scale    = new Vector2(zoomIn, zoomIn);
        page.Position = startPos;

        // Match effectUV == SCREEN_UV so there is no jump from ComicPageLayer.
        ShaderMaterial pageMat = GetPageShaderMat(page);
        InitPageUVUniforms(pageMat, zoomIn, startPos, vp);

        Vector2 fullPagePos        = ContainerPosForFullPage(vp);
        Vector2 startNoiseScale    = NoiseScale(zoomIn,          vp);
        Vector2 fullPageNoiseScale = NoiseScale(PAGE_SCALE_OUT,  vp);
        Vector2 startNoiseOffset   = NoiseOffset(zoomIn,         startPos,    vp);
        Vector2 fullPageNoiseOffset = NoiseOffset(PAGE_SCALE_OUT, fullPagePos, vp);

        // 4. Zoom out — ease-in-out for a smooth pull-back, not a whip.
        float dur      = RandomisedDuration(0.50f);
        Tween tweenOut = CreateTween().SetParallel(true);
        tweenOut.TweenProperty(page, "scale",    new Vector2(PAGE_SCALE_OUT, PAGE_SCALE_OUT), dur)
            .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
        tweenOut.TweenProperty(page, "position", fullPagePos, dur)
            .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
        if (pageMat != null)
        {
            tweenOut.TweenMethod(Callable.From<Vector2>(v => pageMat.SetShaderParameter("uv_noise_scale",  v)),
                startNoiseScale, fullPageNoiseScale, dur).SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
            tweenOut.TweenMethod(Callable.From<Vector2>(o => pageMat.SetShaderParameter("uv_noise_offset", o)),
                startNoiseOffset, fullPageNoiseOffset, dur).SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
        }
        await ToSignal(tweenOut, Tween.SignalName.Finished);

        // 5. Pause — vary the beat length so it never feels mechanical.
        Tween pause = CreateTween();
        pause.TweenInterval(RandomisedDuration(0.22f, 0.20f));
        await ToSignal(pause, Tween.SignalName.Finished);

        // 6. Zoom in — cubic ease-out, no overshoot.
        Vector2 toPos           = ContainerPosForPanel(toIndex, panelSize, zoomIn, vp);
        Vector2 toNoiseScale    = NoiseScale(zoomIn, vp);
        Vector2 toNoiseOffset   = NoiseOffset(zoomIn, toPos, vp);
        dur = RandomisedDuration(0.45f);
        Tween tweenIn = CreateTween().SetParallel(true);
        tweenIn.TweenProperty(page, "scale",    new Vector2(zoomIn, zoomIn), dur)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tweenIn.TweenProperty(page, "position", toPos, dur)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        if (pageMat != null)
        {
            tweenIn.TweenMethod(Callable.From<Vector2>(v => pageMat.SetShaderParameter("uv_noise_scale",  v)),
                fullPageNoiseScale, toNoiseScale, dur).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
            tweenIn.TweenMethod(Callable.From<Vector2>(o => pageMat.SetShaderParameter("uv_noise_offset", o)),
                fullPageNoiseOffset, toNoiseOffset, dur).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        }
        await ToSignal(tweenIn, Tween.SignalName.Finished);

        // 7. Finalise — align ComicPageLayer dirt UV to match the zoomed page container.
        AlignDirtUV(zoomIn, toPos, vp);
        _currentPanelIndex = toIndex;
        SwitchCurrentSceneForNext();
        overlay.QueueFree();
        currentScene.UnsuspendSceneInput();
        isInTransition = false;
        overlayScene?.ShowAfterTransition();
        if (_comicPageLayer != null) _comicPageLayer.Visible = true;
    }

    // ---------------------------------------------------------------------------
    // PageTurn transition — full comic page curl.
    // ---------------------------------------------------------------------------
    private async void PageTurnTransition(string roomPath, scene_script.ArrivalData arrival)
    {
        Vector2 vp = GetViewportRect().Size;

        // 1. Save the current scene at the correct panel slot.
        _panelTextures[_currentPanelIndex]         = await CaptureViewportClean();
        _panelBorderStyles[_currentPanelIndex]     = overlayScene?.BorderStyle ?? OverlayScene.CelBorderStyle.Normal;
        _panelBorderWidths[_currentPanelIndex]     = overlayScene?.BorderWidth;
        _currentPageSceneNames[_currentPanelIndex] = SceneBaseName(currentScene);

        // 2. Instantiate into SubViewport, capture thumbnail, move to nextSceneHolder.
        PrepareNextScene(roomPath, arrival);
        ImageTexture nextTex = await CaptureNextSceneThumbnail();

        // 3. Build the 6-panel page overlay, zoomed into the current panel.
        if (_comicPageLayer != null) _comicPageLayer.Visible = false;
        var pageOverlay = new CanvasLayer { Layer = 100 };
        AddChild(pageOverlay);
        pageOverlay.AddChild(new ColorRect
        {
            Color    = new Color(0.12f, 0.12f, 0.12f),
            Size     = vp,
            Position = Vector2.Zero,
        });

        Control page      = BuildPageContainer(vp);
        pageOverlay.AddChild(page);

        Vector2 panelSize = PanelSize(vp);
        float   zoomIn    = ZoomInScale(panelSize, vp);
        Vector2 startPos  = ContainerPosForPanel(_currentPanelIndex, panelSize, zoomIn, vp);
        page.Scale    = new Vector2(zoomIn, zoomIn);
        page.Position = startPos;

        ShaderMaterial pageMat = GetPageShaderMat(page);
        InitPageUVUniforms(pageMat, zoomIn, startPos, vp);

        Vector2 fullPagePos         = ContainerPosForFullPage(vp);
        Vector2 startNoiseScale     = NoiseScale(zoomIn,         vp);
        Vector2 fullPageNoiseScale  = NoiseScale(PAGE_SCALE_OUT, vp);
        Vector2 startNoiseOffset    = NoiseOffset(zoomIn,         startPos,    vp);
        Vector2 fullPageNoiseOffset = NoiseOffset(PAGE_SCALE_OUT, fullPagePos, vp);

        // 4. Zoom out to reveal the full page — ease-in-out, not a whip.
        float dur      = RandomisedDuration(0.40f);
        Tween tweenOut = CreateTween().SetParallel(true);
        tweenOut.TweenProperty(page, "scale",    new Vector2(PAGE_SCALE_OUT, PAGE_SCALE_OUT), dur)
            .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
        tweenOut.TweenProperty(page, "position", fullPagePos, dur)
            .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
        if (pageMat != null)
        {
            tweenOut.TweenMethod(Callable.From<Vector2>(v => pageMat.SetShaderParameter("uv_noise_scale",  v)),
                startNoiseScale, fullPageNoiseScale, dur).SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
            tweenOut.TweenMethod(Callable.From<Vector2>(o => pageMat.SetShaderParameter("uv_noise_offset", o)),
                startNoiseOffset, fullPageNoiseOffset, dur).SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
        }
        await ToSignal(tweenOut, Tween.SignalName.Finished);

        Tween prePagePause = CreateTween();
        prePagePause.TweenInterval(RandomisedDuration(0.14f, 0.08f));
        await ToSignal(prePagePause, Tween.SignalName.Finished);

        // 5. Capture the full-page view — crop to the portrait page area for the curl.
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Vector2 pageSize2 = PageSize(vp);
        Vector2 pageOff2  = PageOffset(vp);
        Image   fullImg   = GetViewport().GetTexture().GetImage();
        Image   pageImg   = fullImg.GetRegion(new Rect2I(
            (int)pageOff2.X, (int)pageOff2.Y, (int)pageSize2.X, (int)pageSize2.Y));
        ImageTexture pageTex = ImageTexture.CreateFromImage(pageImg);

        // 6. Build the new page at Layer 99 — it sits behind the curl so the
        //    shader reveals it as it peels. No blank frame ever appears.
        _panelTextures           = new ImageTexture[6];
        _panelTextures[0]        = nextTex;
        _panelBorderStyles       = new OverlayScene.CelBorderStyle[6];
        _panelBorderWidths       = new float?[6];
        _panelBorderStyles[0]    = nextScene?.SceneBorderStyle != null ? OverlayScene.ParseBorderStyle(nextScene.SceneBorderStyle) : _defaultBorderStyle;
        _panelBorderWidths[0]    = nextScene?.SceneBorderWidth ?? _defaultBorderWidth;
        _currentPageSceneNames   = new string[6];
        _currentPageSceneNames[0] = SceneBaseName(nextScene);
        _currentPageNumber++;
        _currentPanelIndex       = 0;

        var newPageOverlay = new CanvasLayer { Layer = 99 };
        AddChild(newPageOverlay);
        newPageOverlay.AddChild(new ColorRect
        {
            Color    = new Color(0.12f, 0.12f, 0.12f),
            Size     = vp,
            Position = Vector2.Zero,
        });

        float   newZoomIn    = ZoomInScale(panelSize, vp);
        Control newPage      = BuildPageContainer(vp);
        newPageOverlay.AddChild(newPage);
        newPage.Scale    = new Vector2(PAGE_SCALE_OUT, PAGE_SCALE_OUT);
        newPage.Position = fullPagePos;
        // New page starts at full-page scale — noise uniforms default (vec2.One / Zero) are correct.
        ShaderMaterial newPageMat = GetPageShaderMat(newPage);

        // 7. Curl the old page (Layer 100) away on the portrait rect only.
        pageOverlay.QueueFree();

        var curlOverlay = new CanvasLayer { Layer = 100 };
        AddChild(curlOverlay);

        var rect = new TextureRect
        {
            Texture     = pageTex,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Size        = pageSize2,
            Position    = pageOff2,
        };
        curlOverlay.AddChild(rect);

        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/page_turn.gdshader") };
        mat.SetShaderParameter("progress", 0.0f);
        rect.Material = mat;

        PlayFlip(_pageFlipPlayer);
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
        Vector2 panel0Pos        = ContainerPosForPanel(0, panelSize, newZoomIn, vp);
        Vector2 newFullNoiseSc   = NoiseScale(PAGE_SCALE_OUT, vp);
        Vector2 newFullNoiseOff  = NoiseOffset(PAGE_SCALE_OUT, fullPagePos, vp);
        Vector2 panel0NoiseSc    = NoiseScale(newZoomIn, vp);
        Vector2 panel0NoiseOff   = NoiseOffset(newZoomIn, panel0Pos, vp);
        dur = RandomisedDuration(0.45f);
        Tween tweenIn = CreateTween().SetParallel(true);
        tweenIn.TweenProperty(newPage, "scale",    new Vector2(newZoomIn, newZoomIn), dur)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tweenIn.TweenProperty(newPage, "position", panel0Pos, dur)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        if (newPageMat != null)
        {
            tweenIn.TweenMethod(Callable.From<Vector2>(v => newPageMat.SetShaderParameter("uv_noise_scale",  v)),
                newFullNoiseSc, panel0NoiseSc, dur).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
            tweenIn.TweenMethod(Callable.From<Vector2>(o => newPageMat.SetShaderParameter("uv_noise_offset", o)),
                newFullNoiseOff, panel0NoiseOff, dur).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        }
        await ToSignal(tweenIn, Tween.SignalName.Finished);

        // 10. Finalise — align ComicPageLayer dirt UV to match the zoomed page container.
        AlignDirtUV(newZoomIn, panel0Pos, vp);
        SwitchCurrentSceneForNext();
        newPageOverlay.QueueFree();
        currentScene.UnsuspendSceneInput();
        isInTransition = false;
        overlayScene?.ShowAfterTransition();
        if (_comicPageLayer != null) _comicPageLayer.Visible = true;
    }

    // ---------------------------------------------------------------------------
    // CoverTurn transition — full cover page curls away to reveal the first game page.
    //
    // Skips the panel zoom-out used by PageTurnTransition; the cover IS the full
    // page, so we capture it, curl it directly, then zoom into panel 0 of the
    // incoming scene's fresh page layout.
    // ---------------------------------------------------------------------------
    private async void RunTransition(string name, Func<Task> impl)
    {
        try { await impl(); }
        catch (Exception ex)
        {
            GD.PrintErr($"{name} failed: {ex.Message}\n{ex.StackTrace}");
            isInTransition = false;
        }
    }

    private void CoverTurnTransition(string roomPath, scene_script.ArrivalData arrival) =>
        RunTransition("CoverTurnTransition", () => CoverTurnTransitionImpl(roomPath, arrival));

    private async Task CoverTurnTransitionImpl(string roomPath, scene_script.ArrivalData arrival)
    {
        Vector2 vp        = GetViewportRect().Size;
        Rect2   coverRect = new Rect2(Vector2.Zero, vp);  // fallback: full viewport

        // ── 1. Zoom cover camera to show the full CoverArea + background margin ──
        var coverArea = currentScene.GetNodeOrNull<CollisionShape2D>("CoverArea");
        if (coverArea?.Shape is RectangleShape2D coverShape)
        {
            var coverCam = currentScene.camera;
            if (coverCam != null)
            {
                coverCam.Enabled = true;
                coverCam.MakeCurrent();

                Vector2 coverCenter = coverArea.GlobalPosition;
                float   targetZoom  = Mathf.Min(
                    vp.X / (coverShape.Size.X * 1.15f),
                    vp.Y / (coverShape.Size.Y * 1.15f));

                Tween zoomOut = CreateTween().SetParallel(true);
                zoomOut.TweenProperty(coverCam, "zoom",     new Vector2(targetZoom, targetZoom), 0.55f)
                       .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Cubic);
                zoomOut.TweenProperty(coverCam, "position", coverCenter, 0.55f)
                       .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Cubic);
                await ToSignal(zoomOut, Tween.SignalName.Finished);

                // Compute coverRect directly from the known final camera state.
                // After the tween: camera is centred on CoverArea, so the CoverArea
                // is screen-centred.  No GetFinalTransform() needed.
                Vector2 screenHalf = coverShape.Size / 2f * targetZoom;
                coverRect = new Rect2(vp / 2f - screenHalf, screenHalf * 2f);
            }
        }

        // ── 2. Capture the viewport (cover scene, no comicPageLayer interference) ─
        // Hide _comicPageLayer first — it sits at layer 15 and would pollute the
        // snapshot if left visible.  Use the raw FramePostDraw+GetImage path
        // (same technique as PageTurnTransition step 5) rather than CaptureViewportClean.
        if (_comicPageLayer != null) _comicPageLayer.Visible = false;
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image rawImg = GetViewport().GetTexture().GetImage();

        // Crop to the CoverArea screen rect.
        Vector2I vpI   = rawImg.GetSize();
        Rect2I   cropI = new Rect2I((int)coverRect.Position.X, (int)coverRect.Position.Y,
                                     (int)coverRect.Size.X,     (int)coverRect.Size.Y)
                         .Intersection(new Rect2I(Vector2I.Zero, vpI));
        if (cropI.Size.X <= 0 || cropI.Size.Y <= 0)
            cropI = new Rect2I(Vector2I.Zero, vpI);
        ImageTexture pageTex = ImageTexture.CreateFromImage(rawImg.GetRegion(cropI));

        // ── 3. Stage the incoming scene ───────────────────────────────────────────
        PrepareNextScene(roomPath, arrival);
        ImageTexture nextTex = await CaptureNextSceneThumbnail();
        if (_comicPageLayer != null) _comicPageLayer.Visible = false;

        // ── 4. Build panel page at Layer 99 (behind the curl) ────────────────────
        // Layer 99's dark ColorRect provides the surround visible outside CoverArea
        // and is also what shows through the curl's transparent pixels as it peels.
        _panelTextures            = new ImageTexture[6];
        _panelTextures[0]         = nextTex;
        _panelBorderStyles        = new OverlayScene.CelBorderStyle[6];
        _panelBorderWidths        = new float?[6];
        _panelBorderStyles[0]     = nextScene?.SceneBorderStyle != null ? OverlayScene.ParseBorderStyle(nextScene.SceneBorderStyle) : _defaultBorderStyle;
        _panelBorderWidths[0]     = nextScene?.SceneBorderWidth ?? _defaultBorderWidth;
        _currentPageSceneNames    = new string[6];
        _currentPageSceneNames[0] = SceneBaseName(nextScene);
        _currentPanelIndex        = 0;

        var newPageOverlay = new CanvasLayer { Layer = 99 };
        AddChild(newPageOverlay);
        newPageOverlay.AddChild(new ColorRect
            { Color = new Color(0.12f, 0.12f, 0.12f), Size = vp, Position = Vector2.Zero });

        // Scale the panel page so PageSize height matches the CoverArea screen height.
        float   coverPageScale = coverRect.Size.Y / vp.Y;
        Vector2 coverPagePos   = coverRect.GetCenter() - vp * coverPageScale * 0.5f;

        Vector2 panelSize   = PanelSize(vp);
        float   newZoomIn   = ZoomInScale(panelSize, vp);
        Control newPage     = BuildPageContainer(vp);
        newPageOverlay.AddChild(newPage);
        newPage.Scale    = new Vector2(coverPageScale, coverPageScale);
        newPage.Position = coverPagePos;
        ShaderMaterial newPageMat = GetPageShaderMat(newPage);

        // ── 5. Curl Layer (100): CoverArea portrait only ─────────────────────────
        // Transparent shader pixels reveal Layer 99's panel layout underneath.
        var curlOverlay = new CanvasLayer { Layer = 100 };
        AddChild(curlOverlay);

        var curlRect = new TextureRect
        {
            Texture     = pageTex,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Size        = coverRect.Size,
            Position    = coverRect.Position,
        };
        curlOverlay.AddChild(curlRect);

        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/page_turn.gdshader") };
        mat.SetShaderParameter("progress", 0.0f);
        curlRect.Material = mat;

        PlayFlip(_coverFlipPlayer);
        Tween curl = CreateTween();
        curl.TweenMethod(Callable.From<float>(p => mat.SetShaderParameter("progress", p)),
            0.0f, 1.0f, 0.85)
            .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Cubic);
        await ToSignal(curl, Tween.SignalName.Finished);
        curlOverlay.QueueFree();

        // ── 6. Pause on the full panel page ──────────────────────────────────────
        await ToSignal(CreateTween().TweenInterval(RandomisedDuration(0.50f, 0.10f)),
            Tween.SignalName.Finished);

        // ── 7. Zoom into panel 0 ──────────────────────────────────────────────────
        Vector2 panel0Pos = ContainerPosForPanel(0, panelSize, newZoomIn, vp);
        float   dur       = RandomisedDuration(0.45f);
        Tween tweenIn = CreateTween().SetParallel(true);
        tweenIn.TweenProperty(newPage, "scale",    new Vector2(newZoomIn, newZoomIn), dur)
               .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        tweenIn.TweenProperty(newPage, "position", panel0Pos, dur)
               .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        if (newPageMat != null)
        {
            tweenIn.TweenMethod(Callable.From<Vector2>(v => newPageMat.SetShaderParameter("uv_noise_scale",  v)),
                NoiseScale(coverPageScale, vp), NoiseScale(newZoomIn, vp), dur)
                .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
            tweenIn.TweenMethod(Callable.From<Vector2>(o => newPageMat.SetShaderParameter("uv_noise_offset", o)),
                NoiseOffset(coverPageScale, coverPagePos, vp), NoiseOffset(newZoomIn, panel0Pos, vp), dur)
                .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        }
        await ToSignal(tweenIn, Tween.SignalName.Finished);

        // ── 8. Finalise ───────────────────────────────────────────────────────────
        AlignDirtUV(newZoomIn, panel0Pos, vp);
        SwitchCurrentSceneForNext();
        newPageOverlay.QueueFree();
        currentScene.UnsuspendSceneInput();
        isInTransition = false;
        overlayScene?.ShowAfterTransition();
        if (_comicPageLayer != null) _comicPageLayer.Visible = true;
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
        if (_comicPageLayer != null) _comicPageLayer.Visible = true;
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
                Vector2 tl = nextScene.cameraClamp.Position - nextScene.cameraClamp.Shape.GetRect().Size / 2f;
                Vector2 br = nextScene.cameraClamp.Position + nextScene.cameraClamp.Shape.GetRect().Size / 2f;
                Vector2 halfView = vp / 2f / nextScene.camera.Zoom;
                tl += halfView;
                br -= halfView;
                if (tl.X <= br.X && tl.Y <= br.Y)
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

        ApplyThingState(nextScene);
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

        // Place character at arrive_thing's interactPoint, or arrive_area's centre.
        CollisionShape2D arriveShape = null;
        if (!string.IsNullOrEmpty(arrival.Thing))
        {
            if (scene.FindChild(arrival.Thing, true, false) is thing t)
                character.Position = t.interactPoint;
        }
        else if (!string.IsNullOrEmpty(areaName))
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
        if (_skipNextCapture)
            _skipNextCapture = false;
        else
            CaptureThingState(currentScene);
        nextSceneHolder.RemoveChild(nextScene);
        currentSceneHolder.AddChild(nextScene);
        currentScene.QueueFree();
        currentScene = nextScene;
        nextScene    = null;

        // Re-show overlay in case it was hidden by the cover page.
        overlayScene?.Show();

        // Re-enable the camera — it was disabled in _EnterTree to prevent
        // Godot from auto-stealing the viewport during transition.
        if (currentScene.camera != null)
        {
            currentScene.camera.Enabled = true;
            currentScene.camera.MakeCurrent();
        }

        ApplyCelBorderOffset(currentScene);
        ApplySceneBorderSettings(currentScene);

        // Apply saved ego state from Load(), overriding the scene's start_point.
        if (_pendingLoadPosition.HasValue && currentScene.ego != null)
        {
            currentScene.ego.Position = _pendingLoadPosition.Value;
            _pendingLoadPosition = null;
        }
        if (_pendingLoadFacing.HasValue && currentScene.ego != null)
        {
            currentScene.ego.ChangeFacing(_pendingLoadFacing.Value);
            _pendingLoadFacing = null;
        }

        // Prime parallax so the first live frame matches the thumbnail.
        PrimeParallax(currentScene);
    }

    private static string SceneBaseName(scene_script s) =>
        System.IO.Path.GetFileNameWithoutExtension(s?.SceneFilePath ?? "");

    private static void PlayFlip(AudioStreamPlayer player)
    {
        if (player == null) return;
        player.PitchScale = (float)GD.RandRange(0.88, 1.12);
        player.Play();
    }

    private void ApplyCelBorderOffset(scene_script scene)
    {
        if (scene?.camera != null)
            scene.camera.Offset = _celBorderOffset;
    }

    private void ApplySceneBorderSettings(scene_script scene)
    {
        if (overlayScene == null) return;
        overlayScene.SetBorderStyle(scene?.SceneBorderStyle != null
            ? OverlayScene.ParseBorderStyle(scene.SceneBorderStyle)
            : _defaultBorderStyle);
        float bw = scene?.SceneBorderWidth ?? _defaultBorderWidth;
        if (!Mathf.IsEqualApprox(overlayScene.BorderWidth, bw))
        {
            overlayScene.SetBorderWidth(bw);
            _celBorderOffset = -overlayScene.GetInnerRect().Position;
            ApplyCelBorderOffset(scene);
        }
    }

    // ---------------------------------------------------------------------------
    // Thing state persistence (session-only).
    //
    // CaptureThingState — called when leaving a scene. Walks scene.things, skips
    // the ego, and records any thing whose isExist/isHidden/Position (or NPC Facing)
    // differs from the defaults captured at _Ready(). Stored under the scene's
    // SceneFilePath so re-entry can restore the exact state the player left it in.
    //
    // ApplyThingState — called in PrepareNextScene after instantiation, before
    // thumbnail capture. Restores stored state to matching things by name.
    // ---------------------------------------------------------------------------
    private void CaptureThingState(scene_script scene)
    {
        if (scene?.things == null) return;

        _thingStates.TryGetValue(scene.SceneFilePath, out var prevStored);

        var next = new Dictionary<string, ThingState>();
        foreach (var t in scene.things)
        {
            if (t == scene.ego) continue;

            bool existChanged  = t.isExist  != t._defaultIsExist;
            bool hiddenChanged = t.isHidden != t._defaultIsHidden;
            bool posChanged    = !t.Position.IsEqualApprox(t._defaultPosition);
            NPC.Direction? facing = (t is NPC npc && npc.Facing != npc._defaultFacing)
                                    ? npc.Facing : null;

            string anim     = t.isAnimated ? t.animationPlayer.CurrentAnimation : "";
            int?   frame    = t.hasSprite  ? t.sprite.Frame                     : null;
            bool?  animPlay = t.isAnimated ? t.animationPlayer.IsPlaying()       : null;

            bool anyChanged = existChanged || hiddenChanged || posChanged || facing.HasValue
                           || (t.isAnimated && anim    != t._defaultAnimation)
                           || (t.isAnimated && animPlay != t._defaultAnimPlaying)
                           || (t.hasSprite  && frame    != t._defaultFrame);

            // Store if state differs from defaults, OR if we had a stored entry for this
            // thing from a prior visit — so a value reset back to default overwrites the
            // stale entry rather than leaving it to be re-applied on the next visit.
            bool prevHadEntry = prevStored != null && prevStored.ContainsKey(t.Name);
            if (anyChanged || prevHadEntry)
                next[t.Name] = new ThingState(
                    t.isExist, t.isHidden, t.Position, facing,
                    anim, frame, animPlay);
        }

        if (prevStored != null || next.Count > 0)
            _thingStates[scene.SceneFilePath] = next;
    }

    // Returns one line per stored thing for the debug menu visualizer.
    // Format: "  thingName  [gone]  [hidden]  animName▶/▮  facing"
    public IEnumerable<(string scene, string thing, string summary)>
        GetThingStateDebugLines()
    {
        foreach (var (scenePath, things) in _thingStates)
        {
            string sceneName = System.IO.Path.GetFileNameWithoutExtension(scenePath);
            foreach (var (thingName, s) in things)
            {
                string line = "";
                if (!s.IsExist)  line += " [gone]";
                if (s.IsHidden)  line += " [hidden]";
                if (!string.IsNullOrEmpty(s.Animation))
                    line += $" {s.Animation}{(s.AnimPlaying == true ? "▶" : "▮")}";
                if (s.Facing.HasValue)
                    line += $" {s.Facing.Value}";
                if (s.Frame.HasValue && string.IsNullOrEmpty(s.Animation))
                    line += $" f{s.Frame.Value}";
                yield return (sceneName, thingName, line.TrimStart());
            }
        }
    }

    private void ApplyThingState(scene_script scene)
    {
        if (scene?.things == null) return;
        if (!_thingStates.TryGetValue(scene.SceneFilePath, out var stored)) return;

        foreach (var t in scene.things)
        {
            if (t == scene.ego) continue;
            if (!stored.TryGetValue(t.Name, out var state)) continue;

            t.Position = state.Position;
            t.ToggleExist(state.IsExist);
            t.ToggleHide(state.IsHidden);
            if (state.Facing.HasValue && t is NPC npc)
                npc.ChangeFacing(state.Facing.Value);
            if (!string.IsNullOrEmpty(state.Animation) && t.isAnimated)
            {
                if (state.AnimPlaying == true)
                    t.animationPlayer.Play(state.Animation);  // plays from start
                else
                    t.animationPlayer.Stop(false);            // stop without reset; frame set below
            }
            // Set frame after Play so a stopped animation lands on the right frame.
            // For a playing animation this is overridden by the animation system anyway.
            if (state.Frame.HasValue && t.hasSprite)
                t.sprite.Frame = state.Frame.Value;
        }
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

    // PageSize — portrait-book rect (8.5:11) fitted inside the viewport.
    private Vector2 PageSize(Vector2 vp)
    {
        float h = vp.Y, w = h * _activePageAspect;
        if (w > vp.X) { w = vp.X; h = w / _activePageAspect; }
        return new Vector2(w, h);
    }

    // PageOffset — top-left of the page rect within the container (centred).
    private Vector2 PageOffset(Vector2 vp) => (vp - PageSize(vp)) * 0.5f;

    // CellSize — the grid slot each panel occupies within the portrait page.
    private Vector2 CellSize(Vector2 vp)
    {
        Vector2 page = PageSize(vp);
        return new Vector2(
            (page.X - 2f * PAGE_MARGIN_H - PANEL_GUTTER_H) / 2f,
            (page.Y - 2f * PAGE_MARGIN_V - 2f * PANEL_GUTTER_V) / 3f
        );
    }

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

    // PanelTopLeft — top-left of the panel, centred within its cell.
    private Vector2 PanelTopLeft(int index, Vector2 panelSize, Vector2 vp)
    {
        Vector2 cell    = CellSize(vp);
        Vector2 pageOff = PageOffset(vp);
        Vector2 cellPos = new(
            pageOff.X + PAGE_MARGIN_H + (index % 2) * (cell.X + PANEL_GUTTER_H),
            pageOff.Y + PAGE_MARGIN_V + (index / 2) * (cell.Y + PANEL_GUTTER_V)
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
    // CaptureViewportClean — hides cursor for one GPU frame, then captures.
    // CelBorder is intentionally left visible: HideForTransition only hides
    // _celBorder (not the CanvasLayer), so the old restore of overlayScene.Visible
    // was always a no-op — meaning _celBorder stayed hidden for the entire
    // transition and caused a visible flash at the start.
    // ---------------------------------------------------------------------------
    private async Task<ImageTexture> CaptureViewportClean()
    {
        cursor.Visible = false;

        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        Image img = GetViewport().GetTexture().GetImage();

        cursor.Visible = true;

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
    private ShaderMaterial ComicPageMaterial =>
        _comicPageLayer?.GetNodeOrNull<ColorRect>("ColorRect")?.Material as ShaderMaterial;

    private float GetComicPageParam(string name, float fallback) =>
        ComicPageMaterial?.GetShaderParameter(name).AsSingle() ?? fallback;

    private Control BuildPageContainer(Vector2 vp)
    {
        Vector2 panelSize = PanelSize(vp);
        Vector2 pageSize  = PageSize(vp);
        Vector2 pageOff   = PageOffset(vp);

        var container = new Control
        {
            Size        = vp,
            PivotOffset = Vector2.Zero,
        };

        // Dark background fills the container; the portrait page sits centred within it.
        container.AddChild(new ColorRect
        {
            Color    = new Color(0.12f, 0.12f, 0.12f),
            Size     = vp,
            Position = Vector2.Zero,
        });
        container.AddChild(new ColorRect
        {
            Color    = Colors.White,
            Size     = pageSize,
            Position = pageOff,
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

            container.AddChild(overlayScene.MakePanelBorderOverlay(pos, panelSize,
                _panelBorderStyles[i], _panelBorderWidths[i]));
        }

        // Shader covers only the portrait page area so effects scale with the page,
        // not the full landscape viewport.
        // Halftone uses effectUV (= SCREEN_UV), so frequency and uv_offset need no
        // per-panel adjustment — they match ComicPageLayer directly.
        var comicPageShader = GD.Load<Shader>("res://shaders/comic_page.gdshader");
        if (comicPageShader != null)
        {
            var mat = new ShaderMaterial { Shader = comicPageShader };
            mat.SetShaderParameter("radius_c",         GetComicPageParam("radius_c",         4.0f));
            mat.SetShaderParameter("radius_m",         GetComicPageParam("radius_m",         5.0f));
            mat.SetShaderParameter("radius_y",         GetComicPageParam("radius_y",         6.0f));
            mat.SetShaderParameter("radius_k",         GetComicPageParam("radius_k",         7.0f));
            mat.SetShaderParameter("frequency",        GetComicPageParam("frequency",        330.0f));
            mat.SetShaderParameter("halftone_blend",   GetComicPageParam("halftone_blend",   0.3f));
            mat.SetShaderParameter("noise_scale",      GetComicPageParam("noise_scale",      2.256f));
            mat.SetShaderParameter("vignette_strength",GetComicPageParam("vignette_strength",0.1f));
            mat.SetShaderParameter("sepia_strength",   GetComicPageParam("sepia_strength",   0.0f));
            mat.SetShaderParameter("contrast",         GetComicPageParam("contrast",         1.0f));
            mat.SetShaderParameter("glow_range",       GetComicPageParam("glow_range",       2.783f));
            mat.SetShaderParameter("glow_strength",    GetComicPageParam("glow_strength",    0.184f));
            mat.SetShaderParameter("glow_falloff",     GetComicPageParam("glow_falloff",     2.16f));
            mat.SetShaderParameter("dirt_strength",    GetComicPageParam("dirt_strength",    0.559f));
            mat.SetShaderParameter("dirt_scale",       GetComicPageParam("dirt_scale",       61.721f));
            mat.SetShaderParameter("uv_noise_scale",   Vector2.One);
            mat.SetShaderParameter("uv_noise_offset",  Vector2.Zero);
            container.AddChild(new ColorRect
            {
                Material    = mat,
                Size        = pageSize,
                Position    = pageOff,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Color       = Colors.Black,
            });
        }

        return container;
    }

    // Returns the ShaderMaterial on the comic-page ColorRect overlay (last child).
    private ShaderMaterial GetPageShaderMat(Control page)
    {
        int n = page.GetChildCount();
        return n > 0 && page.GetChild(n - 1) is ColorRect cr ? cr.Material as ShaderMaterial : null;
    }

    // Noise UV helpers — page-space UV requires vec2 scale because portrait page X/Y
    // ratios differ from the landscape viewport.
    //
    // For a shader ColorRect at pageOff (size=pageSize) in a container at (scale, pos):
    //   UV.xy = ((SCREEN_UV * vp - pos) / scale - pageOff) / pageSize
    //   effectUV = UV * noiseScale + noiseOffset  ==  SCREEN_UV
    //   → noiseScale  = scale * pageSize / vp   (component-wise)
    //   → noiseOffset = (pos + scale * pageOff) / vp   (component-wise)
    private Vector2 NoiseScale(float containerScale, Vector2 vp)
    {
        Vector2 ps = PageSize(vp);
        return new Vector2(containerScale * ps.X / vp.X, containerScale * ps.Y / vp.Y);
    }

    private Vector2 NoiseOffset(float containerScale, Vector2 containerPos, Vector2 vp)
    {
        Vector2 po = PageOffset(vp);
        return new Vector2((containerPos.X + containerScale * po.X) / vp.X,
                           (containerPos.Y + containerScale * po.Y) / vp.Y);
    }

    // AlignDirtUV — sets ComicPageLayer dirt uniforms so dirtUV matches the page
    // container's UV at the given zoom/position, keeping the pattern continuous.
    // UV_container = ((SCREEN_UV * vp - pos) / scale - pageOff) / pageSize
    // → dirtScale = vp / (scale * pageSize),  dirtOffset = -pos/(scale*pageSize) - pageOff/pageSize
    private void AlignDirtUV(float scale, Vector2 pos, Vector2 vp)
    {
        if (ComicPageMaterial == null) return;
        Vector2 ps = PageSize(vp);
        Vector2 po = PageOffset(vp);
        ComicPageMaterial.SetShaderParameter("uv_dirt_scale",
            new Vector2(vp.X / (scale * ps.X), vp.Y / (scale * ps.Y)));
        ComicPageMaterial.SetShaderParameter("uv_dirt_offset",
            new Vector2(-pos.X / (scale * ps.X) - po.X / ps.X,
                        -pos.Y / (scale * ps.Y) - po.Y / ps.Y));
    }

    private void InitPageUVUniforms(ShaderMaterial mat, float scale, Vector2 pos, Vector2 vp)
    {
        if (mat == null) return;
        mat.SetShaderParameter("uv_noise_scale",  NoiseScale(scale, vp));
        mat.SetShaderParameter("uv_noise_offset", NoiseOffset(scale, pos, vp));
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
    //
    // Save() writes one JSON object to user://savegame.save with:
    //   save_name    — display name for the slot (caller can pass one; default provided)
    //   scene_path   — res:// path of the current scene
    //   ego_position — { x, y } local position of the ego node
    //   ego_facing   — NPC.Direction int (0=up 1=down 3=left 4=right)
    //   inventory    — array of InventoryItem.ItemType ints
    //   scene_flags  — array of { scene_name, name, value } objects
    //   thing_states — object: scene_path → object: thing_name → state fields
    //
    // Load() restores all of the above and transitions to the saved scene via Fade.
    // ---------------------------------------------------------------------------
    public void Save(string saveName = "Save 1")
    {
        // Capture current scene's thing state before saving so it's up to date.
        CaptureThingState(currentScene);

        var ego    = currentScene?.ego;
        var posObj = new Godot.Collections.Dictionary<string, Variant>
        {
            { "x", ego?.Position.X ?? 0f },
            { "y", ego?.Position.Y ?? 0f }
        };

        var invArr = new Godot.Collections.Array<Variant>();
        foreach (var item in inventory)
            invArr.Add((int)item.Type);

        var flagsArr = new Godot.Collections.Array<Variant>();
        foreach (var flag in sceneFlags)
        {
            var fd = new Godot.Collections.Dictionary<string, Variant>
            {
                { "scene_name", flag.sceneName },
                { "name",       flag.Name      },
                { "value",      flag.Value     }
            };
            flagsArr.Add(Json.ParseString(Json.Stringify(fd)));
        }

        // thing_states: { "res://...tscn": { "thingName": { fields }, ... }, ... }
        var thingStatesObj = new Godot.Collections.Dictionary<string, Variant>();
        foreach (var (scenePath, things) in _thingStates)
        {
            var sceneObj = new Godot.Collections.Dictionary<string, Variant>();
            foreach (var (thingName, state) in things)
            {
                var td = new Godot.Collections.Dictionary<string, Variant>
                {
                    { "is_exist",    state.IsExist  },
                    { "is_hidden",   state.IsHidden },
                    { "pos_x",       state.Position.X },
                    { "pos_y",       state.Position.Y },
                    { "facing",       state.Facing.HasValue ? (int)state.Facing.Value : -1 },
                    { "animation",    state.Animation ?? ""                               },
                    { "frame",        state.Frame     ?? -1                               },
                    { "anim_playing", state.AnimPlaying ?? false                          }
                };
                sceneObj[thingName] = Json.ParseString(Json.Stringify(td));
            }
            thingStatesObj[scenePath] = Json.ParseString(Json.Stringify(sceneObj));
        }

        var pagePanelsArr = new Godot.Collections.Array<Variant>();
        foreach (var name in _currentPageSceneNames)
            pagePanelsArr.Add(name ?? "");

        var save = new Godot.Collections.Dictionary<string, Variant>
        {
            { "save_name",           saveName                                  },
            { "scene_path",          currentScene?.SceneFilePath ?? ""        },
            { "ego_position",        posObj                                   },
            { "ego_facing",          (int)(ego?.Facing ?? NPC.Direction.down) },
            { "inventory",           invArr                                   },
            { "scene_flags",         flagsArr                                 },
            { "thing_states",        Json.ParseString(Json.Stringify(thingStatesObj)) },
            { "current_page",        _currentPageNumber  },
            { "current_panel",       _currentPanelIndex  },
            { "current_page_panels", pagePanelsArr       },
        };

        using var file = FileAccess.Open("user://savegame.save", FileAccess.ModeFlags.Write);
        file.StoreString(Json.Stringify(save));
    }

    public void Load()
    {
        const string path = "user://savegame.save";
        if (!FileAccess.FileExists(path)) return;

        var json = new Json();
        if (json.Parse(FileAccess.GetFileAsString(path)) != Error.Ok) return;

        var root = new Godot.Collections.Dictionary<string, Variant>(
            (Godot.Collections.Dictionary)json.Data);

        // ── Restore scene flags ───────────────────────────────────────────────
        sceneFlags.Clear();
        if (root.TryGetValue("scene_flags", out var flagsVar))
        {
            foreach (Variant fv in (Godot.Collections.Array)flagsVar)
            {
                var fd = new Godot.Collections.Dictionary<string, Variant>(
                    (Godot.Collections.Dictionary)fv);
                sceneFlags.Add(new Globals.SceneFlag(
                    fd["scene_name"].ToString(),
                    fd["name"].ToString(),
                    fd["value"]));
            }
        }

        // ── Restore inventory ─────────────────────────────────────────────────
        inventory.Clear();
        if (root.TryGetValue("inventory", out var invVar))
        {
            foreach (Variant iv in (Godot.Collections.Array)invVar)
                inventory.Add(new InventoryItem((InventoryItem.ItemType)(int)iv));
        }

        // ── Restore thing states ──────────────────────────────────────────────
        _thingStates.Clear();
        if (root.TryGetValue("thing_states", out var tsVar))
        {
            var scenesDict = new Godot.Collections.Dictionary<string, Variant>(
                (Godot.Collections.Dictionary)tsVar);
            foreach (var (savedScenePath, sceneVal) in scenesDict)
            {
                var thingsDict = new Godot.Collections.Dictionary<string, Variant>(
                    (Godot.Collections.Dictionary)sceneVal);
                var things = new Dictionary<string, ThingState>();
                foreach (var (thingName, thingVal) in thingsDict)
                {
                    var td = new Godot.Collections.Dictionary<string, Variant>(
                        (Godot.Collections.Dictionary)thingVal);
                    int    facingInt = (int)td["facing"];
                    int    frameInt  = (int)td["frame"];
                    things[thingName] = new ThingState(
                        IsExist:     (bool)td["is_exist"],
                        IsHidden:    (bool)td["is_hidden"],
                        Position:    new Vector2((float)td["pos_x"], (float)td["pos_y"]),
                        Facing:      facingInt >= 0 ? (NPC.Direction)facingInt : null,
                        Animation:   td["animation"].ToString(),
                        Frame:       frameInt  >= 0 ? frameInt : null,
                        AnimPlaying: (bool)td["anim_playing"]);
                }
                _thingStates[savedScenePath] = things;
            }
        }

        // ── Queue ego state for SwitchCurrentSceneForNext ─────────────────────
        if (root.TryGetValue("ego_position", out var posVar))
        {
            var pd = new Godot.Collections.Dictionary<string, Variant>(
                (Godot.Collections.Dictionary)posVar);
            _pendingLoadPosition = new Vector2((float)pd["x"], (float)pd["y"]);
        }
        if (root.TryGetValue("ego_facing", out var facingVar))
            _pendingLoadFacing = (NPC.Direction)(int)facingVar;

        // ── Transition to saved scene ─────────────────────────────────────────
        string scenePath = root.TryGetValue("scene_path", out var sp) ? sp.ToString() : "";
        if (string.IsNullOrEmpty(scenePath)) return;

        int pageNumber = root.TryGetValue("current_page",  out var cpv) ? (int)cpv : 1;
        int panelIndex = root.TryGetValue("current_panel", out var piv) ? (int)piv : 0;
        var pageSceneNames = new string[6];
        if (root.TryGetValue("current_page_panels", out var ppsv))
        {
            int idx = 0;
            foreach (Variant v in (Godot.Collections.Array)ppsv)
            {
                if (idx < 6) pageSceneNames[idx++] = v.ToString();
            }
        }

        _skipNextCapture = true;
        isInTransition   = true;
        LoadReplayTransition(scenePath, default, pageNumber, panelIndex, pageSceneNames);
    }

    // ---------------------------------------------------------------------------
    // LoadReplayTransition — restores comic-page state on game load.
    //
    // Mirrors CoverTurnTransition: the cover curls away to reveal the first
    // page. If pageNumber > 1 fake pages slide past, then the real current
    // page arrives and zooms into the saved panel.
    // ---------------------------------------------------------------------------
    private void LoadReplayTransition(
        string scenePath, scene_script.ArrivalData arrival,
        int pageNumber, int panelIndex, string[] pageSceneNames) =>
        RunTransition("LoadReplayTransition",
            () => LoadReplayTransitionImpl(scenePath, arrival, pageNumber, panelIndex, pageSceneNames));

    private async Task LoadReplayTransitionImpl(
        string scenePath,
        scene_script.ArrivalData arrival,
        int pageNumber,
        int panelIndex,
        string[] pageSceneNames)
    {
        Vector2 vp        = GetViewportRect().Size;
        Rect2   coverRect = new(Vector2.Zero, vp);

        if (_comicPageLayer != null) _comicPageLayer.Visible = false;

        // ── 1. Zoom cover camera (same as CoverTurnTransitionImpl) ────────────
        var coverArea = currentScene.GetNodeOrNull<CollisionShape2D>("CoverArea");
        if (coverArea?.Shape is RectangleShape2D coverShape)
        {
            var coverCam = currentScene.camera;
            if (coverCam != null)
            {
                coverCam.Enabled = true;
                coverCam.MakeCurrent();

                Vector2 coverCenter = coverArea.GlobalPosition;
                float   targetZoom  = Mathf.Min(
                    vp.X / (coverShape.Size.X * 1.15f),
                    vp.Y / (coverShape.Size.Y * 1.15f));

                Tween zoomOut = CreateTween().SetParallel(true);
                zoomOut.TweenProperty(coverCam, "zoom",     new Vector2(targetZoom, targetZoom), 0.55f)
                       .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Cubic);
                zoomOut.TweenProperty(coverCam, "position", coverCenter, 0.55f)
                       .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Cubic);
                await ToSignal(zoomOut, Tween.SignalName.Finished);

                Vector2 screenHalf = coverShape.Size / 2f * targetZoom;
                coverRect = new Rect2(vp / 2f - screenHalf, screenHalf * 2f);
            }
        }

        // ── 2. Capture cover viewport ─────────────────────────────────────────
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        Image rawImg = GetViewport().GetTexture().GetImage();
        Vector2I vpI   = rawImg.GetSize();
        Rect2I   cropI = new Rect2I((int)coverRect.Position.X, (int)coverRect.Position.Y,
                                     (int)coverRect.Size.X,     (int)coverRect.Size.Y)
                         .Intersection(new Rect2I(Vector2I.Zero, vpI));
        if (cropI.Size.X <= 0 || cropI.Size.Y <= 0)
            cropI = new Rect2I(Vector2I.Zero, vpI);
        ImageTexture coverTex = ImageTexture.CreateFromImage(rawImg.GetRegion(cropI));

        // ── 3. Reset panel arrays; build fake page containers ─────────────────
        // Fakes built before real textures so they get placeholder colours.
        _panelTextures         = new ImageTexture[6];
        _panelBorderStyles     = new OverlayScene.CelBorderStyle[6];
        _panelBorderWidths     = new float?[6];
        _currentPageSceneNames = new string[6];
        for (int i = 0; i < 6; i++)
        {
            _panelBorderStyles[i] = _defaultBorderStyle;
            _panelBorderWidths[i] = _defaultBorderWidth;
        }

        int fakeCount = Mathf.Min(pageNumber - 1, 4);
        var fakePages = new List<Control>();
        for (int fp = 0; fp < fakeCount; fp++)
            fakePages.Add(BuildPageContainer(vp));

        // ── 4. Capture thumbnails for current-page panels 0..panelIndex-1 ─────
        for (int i = 0; i < panelIndex; i++)
        {
            string name = i < pageSceneNames.Length ? pageSceneNames[i] : null;
            if (string.IsNullOrEmpty(name)) continue;
            string path = Scenes.Resolve(name);
            if (string.IsNullOrEmpty(path) || !ResourceLoader.Exists(path)) continue;

            PrepareNextScene(path);
            if (!IsInstanceValid(nextScene)) continue;
            _panelBorderStyles[i]     = nextScene.SceneBorderStyle != null
                ? OverlayScene.ParseBorderStyle(nextScene.SceneBorderStyle) : _defaultBorderStyle;
            _panelBorderWidths[i]     = nextScene.SceneBorderWidth ?? _defaultBorderWidth;
            _currentPageSceneNames[i] = name;
            _panelTextures[i]         = await CaptureNextSceneThumbnail();
            // CaptureNextSceneThumbnail moved nextScene into nextSceneHolder — free it.
            if (IsInstanceValid(nextScene))
            {
                nextSceneHolder.RemoveChild(nextScene);
                nextScene.QueueFree();
                nextScene = null;
            }
        }

        // ── 5. Prepare destination scene (stays in nextSceneHolder) ──────────
        PrepareNextScene(scenePath, arrival);
        if (IsInstanceValid(nextScene))
        {
            _panelBorderStyles[panelIndex]     = nextScene.SceneBorderStyle != null
                ? OverlayScene.ParseBorderStyle(nextScene.SceneBorderStyle) : _defaultBorderStyle;
            _panelBorderWidths[panelIndex]     = nextScene.SceneBorderWidth ?? _defaultBorderWidth;
            _currentPageSceneNames[panelIndex] = SceneBaseName(nextScene);
        }
        _panelTextures[panelIndex] = await CaptureNextSceneThumbnail();
        if (_comicPageLayer != null) _comicPageLayer.Visible = false;

        // ── 6. Build real current-page container (textures now populated) ──────
        Control realPage = BuildPageContainer(vp);
        ShaderMaterial realPageMat = GetPageShaderMat(realPage);

        // ── 7. Layer 99: page revealed behind the curl ────────────────────────
        Vector2 panelSize      = PanelSize(vp);
        float   coverPageScale = coverRect.Size.Y / vp.Y;
        Vector2 coverPagePos   = coverRect.GetCenter() - vp * coverPageScale * 0.5f;
        float   zoomIn         = ZoomInScale(panelSize, vp);
        Vector2 fullPos        = ContainerPosForFullPage(vp);

        var behindLayer = new CanvasLayer { Layer = 99 };
        AddChild(behindLayer);
        behindLayer.AddChild(new ColorRect
            { Color = new Color(0.12f, 0.12f, 0.12f), Size = vp, Position = Vector2.Zero });

        // First fake (if any) or the real page shows through the curl.
        Control firstReveal = fakeCount > 0 ? fakePages[0] : realPage;
        behindLayer.AddChild(firstReveal);
        firstReveal.Scale    = new Vector2(coverPageScale, coverPageScale);
        firstReveal.Position = coverPagePos;

        // ── 8. Layer 100: cover curl ──────────────────────────────────────────
        var curlOverlay = new CanvasLayer { Layer = 100 };
        AddChild(curlOverlay);
        var curlRect = new TextureRect
        {
            Texture     = coverTex,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Size        = coverRect.Size,
            Position    = coverRect.Position,
        };
        curlOverlay.AddChild(curlRect);
        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/page_turn.gdshader") };
        mat.SetShaderParameter("progress", 0.0f);
        curlRect.Material = mat;

        PlayFlip(_coverFlipPlayer);
        Tween curl = CreateTween();
        curl.TweenMethod(Callable.From<float>(p => mat.SetShaderParameter("progress", p)),
            0.0f, 1.0f, 0.85)
            .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Cubic);
        await ToSignal(curl, Tween.SignalName.Finished);
        curlOverlay.QueueFree();

        // ── 9. Post-curl: flip fake pages (if any) then arrive at real page ───
        Vector2 slideOff = new(vp.X * 0.2f, 0f);

        if (fakeCount > 0)
        {
            // Expand first fake from cover scale to full-page view.
            Tween expand = CreateTween().SetParallel(true);
            expand.TweenProperty(fakePages[0], "scale",    new Vector2(PAGE_SCALE_OUT, PAGE_SCALE_OUT), 0.20f)
                .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Cubic);
            expand.TweenProperty(fakePages[0], "position", fullPos, 0.20f)
                .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Cubic);
            await ToSignal(expand, Tween.SignalName.Finished);

            await ToSignal(CreateTween().TweenInterval(0.18f), Tween.SignalName.Finished);

            // Flip remaining fakes.
            Control lastFake = fakePages[0];
            for (int fp = 1; fp < fakeCount; fp++)
            {
                Control fakePage = fakePages[fp];
                behindLayer.AddChild(fakePage);
                fakePage.Scale    = new Vector2(PAGE_SCALE_OUT, PAGE_SCALE_OUT);
                fakePage.Position = fullPos + slideOff;

                Tween flip = CreateTween().SetParallel(true);
                flip.TweenProperty(fakePage, "position", fullPos,            0.14f)
                    .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
                flip.TweenProperty(lastFake, "position", fullPos - slideOff, 0.12f)
                    .SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Cubic);
                await ToSignal(flip, Tween.SignalName.Finished);
                lastFake.QueueFree();
                lastFake = fakePage;

                await ToSignal(CreateTween().TweenInterval(0.18f), Tween.SignalName.Finished);
            }

            // Slide real page in, last fake slides out.
            behindLayer.AddChild(realPage);
            realPage.Scale    = new Vector2(PAGE_SCALE_OUT, PAGE_SCALE_OUT);
            realPage.Position = fullPos + slideOff;
            Tween finalFlip = CreateTween().SetParallel(true);
            finalFlip.TweenProperty(lastFake, "position", fullPos - slideOff, 0.12f)
                .SetEase(Tween.EaseType.In).SetTrans(Tween.TransitionType.Cubic);
            finalFlip.TweenProperty(realPage, "position", fullPos,            0.14f)
                .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
            await ToSignal(finalFlip, Tween.SignalName.Finished);
            lastFake.QueueFree();
        }
        else
        {
            // No fakes: expand real page from cover scale to full-page view.
            Tween expand = CreateTween().SetParallel(true);
            expand.TweenProperty(realPage, "scale",    new Vector2(PAGE_SCALE_OUT, PAGE_SCALE_OUT), 0.25f)
                .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Cubic);
            expand.TweenProperty(realPage, "position", fullPos, 0.25f)
                .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Cubic);
            await ToSignal(expand, Tween.SignalName.Finished);
        }

        // ── 10. Pause, then zoom into saved panel ─────────────────────────────
        await ToSignal(CreateTween().TweenInterval(RandomisedDuration(0.40f, 0.10f)),
            Tween.SignalName.Finished);

        Vector2 toPos = ContainerPosForPanel(panelIndex, panelSize, zoomIn, vp);
        float   dur   = RandomisedDuration(0.45f);
        Tween tweenIn = CreateTween().SetParallel(true);
        tweenIn.TweenProperty(realPage, "scale",    new Vector2(zoomIn, zoomIn), dur)
            .SetEase(Tween.EaseType.Out).SetTrans(RandomZoomInCurve());
        tweenIn.TweenProperty(realPage, "position", toPos, dur)
            .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        if (realPageMat != null)
        {
            tweenIn.TweenMethod(Callable.From<Vector2>(v => realPageMat.SetShaderParameter("uv_noise_scale",  v)),
                NoiseScale(PAGE_SCALE_OUT, vp), NoiseScale(zoomIn, vp), dur)
                .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
            tweenIn.TweenMethod(Callable.From<Vector2>(o => realPageMat.SetShaderParameter("uv_noise_offset", o)),
                NoiseOffset(PAGE_SCALE_OUT, fullPos, vp), NoiseOffset(zoomIn, toPos, vp), dur)
                .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
        }
        await ToSignal(tweenIn, Tween.SignalName.Finished);

        // ── 11. Finalise ──────────────────────────────────────────────────────
        _currentPanelIndex = panelIndex;
        _currentPageNumber = pageNumber;
        AlignDirtUV(zoomIn, toPos, vp);
        SwitchCurrentSceneForNext();
        behindLayer.QueueFree();
        currentScene.UnsuspendSceneInput();
        isInTransition = false;
        overlayScene?.ShowAfterTransition();
        if (_comicPageLayer != null) _comicPageLayer.Visible = true;
    }
}
