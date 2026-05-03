using Godot;
using System.Text.Json.Nodes;

public partial class Cover : scene_script
{
    private enum CoverPage { Cover, Menu, Settings }

    private static bool _introPlayed = false;
    private static bool _menuOpened  = false;

    private CoverPage _activePage     = CoverPage.Cover;
    private bool      _coverClickable = false;
    private bool      _flipping       = false;

    private string    _startScene      = "";
    private Polygon2D _coverPoly;
    private Polygon2D _pagesShape;
    private Rect2     _coverScreenRect;
    private Vector2   _naturalCoverSize;   // cover size at design zoom; reference for scaling

    private CanvasLayer _menuPage;
    private CanvasLayer _settingsPage;

    private ColorRect _menuPageBg;
    private ColorRect _settingsPageBg;

    private Button _newGameBtn;
    private Button _saveGameBtn;
    private Button _loadGameBtn;
    private Button _resumeGameBtn;
    private Button _settingsBtn;
    private Button _exitGameBtn;

    private Button _fontSizeBtn;
    private Button _backBtn;
    private Label  _settingsTitleLabel;

    public Rect2 CoverScreenRect => _coverScreenRect;

    private const float CurlOverhang = 0.25f;

    public override void _EnterTree()
    {
        isInsert         = true;
        hasCameraControl = false;
        base._EnterTree();
    }

    public override void _Ready()
    {
        base._Ready();

        string json = FileAccess.GetFileAsString("res://game/game_config.json");
        _startScene = JsonNode.Parse(json)?.AsObject()?["start_scene"]?.GetValue<string>() ?? "";

        _coverPoly    = GetNodeOrNull<Polygon2D>("Cover");
        _pagesShape   = GetNodeOrNull<Polygon2D>("Pages");
        _menuPage     = GetNodeOrNull<CanvasLayer>("MenuPage");
        _settingsPage = GetNodeOrNull<CanvasLayer>("SettingsPage");

        SetupMenuPage();
        SetupSettingsPage();

        if (_menuOpened)
        {
            // Cover was already opened in a prior visit — skip cover, go straight to menu.
            // Keep cover polygon hidden and defer page setup until after the first render
            // when the canvas transform is valid.
            if (_coverPoly  != null) _coverPoly.Visible  = false;
            if (_pagesShape != null) _pagesShape.Visible = false;
            Callable.From(() => { SetupPages(); ShowPage(CoverPage.Menu); }).CallDeferred();
        }
        else
        {
            // SetupPages needs the camera canvas transform, which isn't valid until after
            // the first render cycle. Defer so it runs before the first frame is drawn.
            Callable.From(SetupPages).CallDeferred();
            AnimateCoverIn();
        }
    }

    // Computes the cover's screen-space rect from the polygon vertices and canvas transform,
    // then aligns both CanvasLayers to that rect with the correct scale.
    public void SetupPages()
    {
        if (_coverPoly == null) return;
        _coverScreenRect = GetCoverScreenRect();
        var pos  = new Vector2(Mathf.Round(_coverScreenRect.Position.X), Mathf.Round(_coverScreenRect.Position.Y));
        var size = new Vector2(Mathf.Round(_coverScreenRect.Size.X),     Mathf.Round(_coverScreenRect.Size.Y));

        // Record natural size once (at design zoom). Used as the denominator for scale.
        if (_naturalCoverSize == Vector2.Zero && size != Vector2.Zero)
            _naturalCoverSize = size;
        if (_naturalCoverSize == Vector2.Zero) return;

        float sx = size.X / _naturalCoverSize.X;
        float sy = size.Y / _naturalCoverSize.Y;
        var transform = new Transform2D(0f, new Vector2(sx, sy), 0f, pos);

        if (_menuPage     != null) { _menuPage.Transform     = transform; if (_menuPageBg     != null) _menuPageBg.Size     = _naturalCoverSize; }
        if (_settingsPage != null) { _settingsPage.Transform = transform; if (_settingsPageBg != null) _settingsPageBg.Size = _naturalCoverSize; }

        LayoutButtons();
    }

    private void LayoutButtons()
    {
        if (_naturalCoverSize == Vector2.Zero) return;
        const float refW = 324f, refH = 466f;
        float sx = _naturalCoverSize.X / refW;
        float sy = _naturalCoverSize.Y / refH;

        float bLeft   = 12f  * sx;
        float bRight  = 312f * sx;
        float bHeight = 50f  * sy;
        float bStep   = 62f  * sy;
        float bTop0   = 53f  * sy;
        int   bFont   = Mathf.RoundToInt(28f * sx);

        var menuBtns = new (Button b, int row)[]
        {
            (_newGameBtn, 0), (_loadGameBtn, 1), (_saveGameBtn, 2),
            (_resumeGameBtn, 3), (_settingsBtn, 4), (_exitGameBtn, 5),
        };
        foreach (var (b, row) in menuBtns)
        {
            if (b == null) continue;
            b.OffsetLeft   = bLeft;
            b.OffsetRight  = bRight;
            b.OffsetTop    = bTop0 + row * bStep;
            b.OffsetBottom = bTop0 + row * bStep + bHeight;
            b.AddThemeFontSizeOverride("font_size", bFont);
        }

        void ScaleBtn(Button b, float refTop)
        {
            if (b == null) return;
            b.OffsetLeft   = bLeft;
            b.OffsetRight  = bRight;
            b.OffsetTop    = refTop * sy;
            b.OffsetBottom = refTop * sy + bHeight;
            b.AddThemeFontSizeOverride("font_size", bFont);
        }
        ScaleBtn(_fontSizeBtn, 160f);
        ScaleBtn(_backBtn,     250f);

        if (_settingsTitleLabel != null)
        {
            _settingsTitleLabel.OffsetLeft   = 12f  * sx;
            _settingsTitleLabel.OffsetRight  = 312f * sx;
            _settingsTitleLabel.OffsetTop    = 50f  * sy;
            _settingsTitleLabel.OffsetBottom = 110f * sy;
            _settingsTitleLabel.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(40f * sx));
        }
    }

    private void SetupMenuPage()
    {
        if (_menuPage == null) return;
        _menuPage.Visible = false;

        _menuPageBg    = _menuPage.GetNodeOrNull<ColorRect>("PageBg");
        if (_menuPageBg != null) _menuPageBg.Color = Globals.PageBgColor;

        _newGameBtn    = _menuPage.GetNodeOrNull<Button>("NewGameButton");
        _saveGameBtn   = _menuPage.GetNodeOrNull<Button>("SaveGameButton");
        _loadGameBtn   = _menuPage.GetNodeOrNull<Button>("LoadGameButton");
        _resumeGameBtn = _menuPage.GetNodeOrNull<Button>("ResumeGameButton");
        _settingsBtn   = _menuPage.GetNodeOrNull<Button>("SettingsButton");
        _exitGameBtn   = _menuPage.GetNodeOrNull<Button>("ExitGameButton");

        if (_newGameBtn    != null) _newGameBtn.Pressed    += () => mainScene.NewGame(_startScene);
        if (_saveGameBtn   != null) _saveGameBtn.Pressed   += () => { if (mainScene.HasResumeState) mainScene.Save(); };
        if (_loadGameBtn   != null) _loadGameBtn.Pressed   += mainScene.Load;
        if (_resumeGameBtn != null) _resumeGameBtn.Pressed += () => { if (mainScene.HasResumeState) mainScene.ResumeGame(); };
        if (_settingsBtn   != null) _settingsBtn.Pressed   += () => FlipPage(CoverPage.Settings);
        if (_exitGameBtn   != null) _exitGameBtn.Pressed   += () => GetTree().Quit();

        RefreshButtonStates();
    }

    private void SetupSettingsPage()
    {
        if (_settingsPage == null) return;
        _settingsPage.Visible = false;

        _settingsPageBg = _settingsPage.GetNodeOrNull<ColorRect>("PageBg");
        if (_settingsPageBg != null) _settingsPageBg.Color = Globals.PageBgColor;

        _settingsTitleLabel = _settingsPage.GetNodeOrNull<Label>("TitleLabel");
        _fontSizeBtn        = _settingsPage.GetNodeOrNull<Button>("FontSizeButton");
        _backBtn            = _settingsPage.GetNodeOrNull<Button>("BackButton");

        _settingsTitleLabel?.AddThemeColorOverride("font_color", Colors.Black);
        if (_fontSizeBtn != null) _fontSizeBtn.Pressed += ToggleFontSize;
        if (_backBtn     != null) _backBtn.Pressed     += () => FlipPage(CoverPage.Menu);

        SyncFontSizeLabel();
    }

    private void ToggleFontSize()
    {
        Globals.SetFontSize(Globals.FontSize == Globals.FontSizePreset.Normal
            ? Globals.FontSizePreset.Large
            : Globals.FontSizePreset.Normal);
        SyncFontSizeLabel();
    }

    private void SyncFontSizeLabel()
    {
        if (_fontSizeBtn == null) return;
        _fontSizeBtn.Text = Globals.FontSize == Globals.FontSizePreset.Normal
            ? "FONT SIZE: NORMAL"
            : "FONT SIZE: LARGE";
    }

    private void AnimateCoverIn()
    {
        if (_introPlayed) { _coverClickable = true; return; }
        _introPlayed = true;

        if (_coverPoly == null) { _coverClickable = true; return; }

        const float startXAngle = 1.30f;   // ~74° — book nearly flat, rotates up to face viewer
        const float startYAngle = -0.18f;  // right side slightly toward viewer, shows page-stack on right
        const float standDur    = 2.8f;

        // Compute polygon bounding-box centre so the rotation pivots on the book centre,
        // not on the top-left corner of the polygon.
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        foreach (var v in _coverPoly.Polygon)
        {
            if (v.X < minX) minX = v.X; if (v.X > maxX) maxX = v.X;
            if (v.Y < minY) minY = v.Y; if (v.Y > maxY) maxY = v.Y;
        }
        var polyCenter = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);

        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://ui/cover_spin.gdshader") };
        mat.SetShaderParameter("x_angle", startXAngle);
        mat.SetShaderParameter("y_angle", startYAngle);
        mat.SetShaderParameter("depth",   2500f);
        mat.SetShaderParameter("center",  polyCenter);
        _coverPoly.Material = mat;

        if (_pagesShape != null)
            _pagesShape.Material = mat;

        var tween = CreateTween().SetParallel(true);

        tween.TweenMethod(
                 Callable.From((float v) => mat.SetShaderParameter("x_angle", v)),
                 startXAngle, 0f, standDur)
             .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);

        tween.TweenMethod(
                 Callable.From((float v) => mat.SetShaderParameter("y_angle", v)),
                 startYAngle, 0f, standDur)
             .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);

        GetTree().CreateTimer(standDur + 0.15f).Connect("timeout",
            Callable.From(() => _coverClickable = true));
    }

    public void RefreshButtonStates()
    {
        var dim  = new Color(1, 1, 1, 0.35f);
        var full = new Color(1, 1, 1, 1f);
        bool has = mainScene?.HasResumeState == true;
        if (_saveGameBtn   != null) _saveGameBtn.Modulate   = has ? full : dim;
        if (_resumeGameBtn != null) _resumeGameBtn.Modulate = has ? full : dim;
    }

    public override void _Input(InputEvent @event)
    {
        if (mainScene?.isInTransition == true || _flipping) return;

        bool isRelease =
            (@event is InputEventMouseButton mb && !mb.Pressed && mb.ButtonIndex == MouseButton.Left) ||
            (@event is InputEventScreenTouch  st && !st.Pressed);

        if (!isRelease || _activePage != CoverPage.Cover || !_coverClickable) return;

        GetViewport().SetInputAsHandled();
        FlipPage(CoverPage.Menu);
    }

    private void ShowPage(CoverPage page)
    {
        _activePage = page;
        bool showCover = page == CoverPage.Cover;
        if (_coverPoly    != null) _coverPoly.Visible    = showCover;
        if (_pagesShape   != null) _pagesShape.Visible   = showCover;
        if (_menuPage     != null) _menuPage.Visible     = page == CoverPage.Menu;
        if (_settingsPage != null) _settingsPage.Visible = page == CoverPage.Settings;
    }

    private Rect2 GetCoverScreenRect()
    {
        var ct   = GetViewport().GetCanvasTransform() * _coverPoly.GlobalTransform;
        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        foreach (var v in _coverPoly.Polygon)
        {
            var s = ct * v;
            minX = Mathf.Min(minX, s.X); minY = Mathf.Min(minY, s.Y);
            maxX = Mathf.Max(maxX, s.X); maxY = Mathf.Max(maxY, s.Y);
        }
        return new Rect2(minX, minY, maxX - minX, maxY - minY);
    }

    private async void FlipPage(CoverPage to)
    {
        if (_flipping) return;
        _flipping = true;

        bool backward = to == CoverPage.Menu && _activePage == CoverPage.Settings;

        if (to == CoverPage.Menu && _activePage == CoverPage.Cover)
            _menuOpened = true;

        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

        // Re-run after FramePostDraw: canvas transform is valid here, so cover rect is correct.
        SetupPages();

        var rawImg  = GetViewport().GetTexture().GetImage();
        var rectI   = new Rect2I(
            (int)_coverScreenRect.Position.X, (int)_coverScreenRect.Position.Y,
            (int)_coverScreenRect.Size.X,     (int)_coverScreenRect.Size.Y);
        var pageTex = ImageTexture.CreateFromImage(rawImg.GetRegion(rectI));

        ShowPage(to);

        float coverW = _coverScreenRect.Size.X;
        float coverH = _coverScreenRect.Size.Y;
        float ow     = coverW * CurlOverhang / (1f - CurlOverhang);

        var curlLayer = new CanvasLayer { Layer = 100 };
        AddChild(curlLayer);

        var mat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/page_turn.gdshader") };
        mat.SetShaderParameter("progress",      0.0f);
        mat.SetShaderParameter("left_overhang", CurlOverhang);
        mat.SetShaderParameter("backward",      backward ? 1 : 0);

        curlLayer.AddChild(new TextureRect
        {
            Texture     = pageTex,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            Size        = new Vector2(coverW + ow, coverH),
            Position    = backward
                ? new Vector2(_coverScreenRect.Position.X,      _coverScreenRect.Position.Y)
                : new Vector2(_coverScreenRect.Position.X - ow, _coverScreenRect.Position.Y),
            Material    = mat,
        });

        var tween = CreateTween();
        tween.TweenMethod(
            Callable.From<float>(p => mat.SetShaderParameter("progress", p)),
            0.0f, 1.0f, 0.85f)
            .SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Cubic);
        await ToSignal(tween, Tween.SignalName.Finished);

        curlLayer.QueueFree();
        _flipping = false;
    }
}
