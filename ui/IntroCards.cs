using Godot;

public partial class IntroCards : Node
{
    private const string MainScenePath = "res://game/scenes/mainscene/mainScene.tscn";
    private const float  FadeInDur     = 1.0f;
    private const float  HoldDur       = 2.5f;
    private const float  FadeOutDur    = 0.9f;
    private const float  GapDur        = 0.25f;
    private const float  ViewW         = 1280f;
    private const float  ViewH         = 720f;

    // MainScene reads this to resume BGM from the right position.
    public static (string Path, float Position) BgmHandoff;

    private CanvasLayer        _layer;
    private Control            _card;
    private Tween              _activeTween;
    private AudioStreamPlayer  _bgmPlayer;
    private string             _menuBgm = "";
    private int                _cardIndex = 0;
    private bool               _finishing = false;

    private readonly struct CardDef(string tex = null, string title = null, string sub = null)
    {
        public readonly string TexturePath = tex;
        public readonly string TitleText   = title;
        public readonly string SubText     = sub;
    }

    private static readonly CardDef[] Cards =
    [
        new(title: "COV", sub: "A Creepovision Game"),
        new(tex: "res://game/art/creepovision_logo.png"),
    ];

    public override void _Ready()
    {
        _layer = new CanvasLayer { Layer = 100 };
        AddChild(_layer);

        var bg = new ColorRect { Color = Colors.Black, Size = new Vector2(ViewW, ViewH) };
        _layer.AddChild(bg);

        if (ReadConfig(out _menuBgm)) { Finish(); return; }

        if (!string.IsNullOrEmpty(_menuBgm))
        {
            _bgmPlayer = new AudioStreamPlayer
            {
                Stream = ResourceLoader.Load<AudioStream>(_menuBgm),
            };
            AddChild(_bgmPlayer);
            _bgmPlayer.Play();
        }

        ShowCard(0);
    }

    public override void _Input(InputEvent @event)
    {
        bool isRelease =
            (@event is InputEventMouseButton mb && !mb.Pressed && mb.ButtonIndex == MouseButton.Left) ||
            (@event is InputEventScreenTouch st && !st.Pressed);
        if (!isRelease || _finishing) return;
        GetViewport().SetInputAsHandled();
        ShowCard(_cardIndex + 1);
    }

    private void ShowCard(int idx)
    {
        _activeTween?.Kill();
        _card?.QueueFree();
        _card = null;
        _cardIndex = idx;

        if (idx >= Cards.Length)
        {
            Finish();
            return;
        }

        _card = BuildCard(Cards[idx]);
        _card.Modulate = Colors.Transparent;
        _layer.AddChild(_card);

        _activeTween = CreateTween();
        _activeTween.TweenProperty(_card, "modulate", Colors.White, FadeInDur)
                    .SetTrans(Tween.TransitionType.Sine);
        _activeTween.TweenInterval(HoldDur);
        _activeTween.TweenProperty(_card, "modulate", Colors.Transparent, FadeOutDur)
                    .SetTrans(Tween.TransitionType.Sine);
        _activeTween.TweenInterval(GapDur);
        _activeTween.TweenCallback(Callable.From(() => ShowCard(idx + 1)));
    }

    private void Finish()
    {
        _finishing = true;
        if (_bgmPlayer != null && _bgmPlayer.Playing)
            BgmHandoff = (_menuBgm, _bgmPlayer.GetPlaybackPosition());
        GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, MainScenePath);
    }

    private static bool ReadConfig(out string menuBgm)
    {
        menuBgm = "";
        using var f = FileAccess.Open("res://game/game_config.json", FileAccess.ModeFlags.Read);
        if (f == null) return false;
        var json = new Json();
        json.Parse(f.GetAsText());
        var config = json.Data.AsGodotDictionary();
        if (config.TryGetValue("menu_bgm", out var bgmVal))
            menuBgm = bgmVal.AsString();
        return config.TryGetValue("skip_intro", out var val) && val.AsBool();
    }

    private Control BuildCard(in CardDef def)
    {
        var container = new CenterContainer { Size = new Vector2(ViewW, ViewH) };

        if (def.TexturePath != null)
        {
            var img = new TextureRect
            {
                Texture           = GD.Load<Texture2D>(def.TexturePath),
                ExpandMode        = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode       = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(640, 360),
            };
            container.AddChild(img);
        }
        else
        {
            var vbox = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
            container.AddChild(vbox);

            if (def.TitleText != null)
            {
                var lbl = new Label
                {
                    Text                = def.TitleText,
                    HorizontalAlignment = HorizontalAlignment.Center,
                };
                lbl.AddThemeFontOverride("font", GD.Load<Font>("res://fonts/KOMIKA_BOLD.tres"));
                lbl.AddThemeFontSizeOverride("font_size", 120);
                vbox.AddChild(lbl);
            }

            if (def.SubText != null)
            {
                var sub = new Label
                {
                    Text                = def.SubText,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Modulate            = new Color(0.65f, 0.65f, 0.65f),
                };
                sub.AddThemeFontOverride("font", GD.Load<Font>("res://fonts/KOMIKA_NORMAL.tres"));
                sub.AddThemeFontSizeOverride("font_size", 48);
                vbox.AddChild(sub);
            }
        }

        return container;
    }
}
