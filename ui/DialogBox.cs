using Godot;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

public partial class DialogBox : Control
{
    public enum TailStyle { Straight, Curved, Wavy, Lightning, NoTail }

    [Signal] public delegate void DialogClosedEventHandler();

    [Export(PropertyHint.Range, "1,60,1")] public float textSpeed = 35f;

    [Export] public Color dialogColor;

    [Export] public int   maxWidth   = 250;
    [Export] public float marginSize = 24;

    [ExportGroup("Tail")]
    [Export] public TailStyle SpeechTailStyle  = TailStyle.Curved;
    [Export(PropertyHint.Range, "0.5,8,0.5")]  public float TailLineWidth    = 3.5f;
    [Export(PropertyHint.Range, "6,28,1")]      public float CloudBumpRadius  = 12f;

    [ExportGroup("Shadow")]
    [Export] public bool    ShadowEnabled = true;
    [Export] public Color   ShadowColor   = new Color(0f, 0f, 0f, 0.45f);
    [Export] public Vector2 ShadowOffset  = new Vector2(4f, 5f);
    [Export(PropertyHint.Range, "0,16,1")] public int ShadowBlur = 5;

    public scene_script parentScene;
    public thing trackActor;
    public Vector2 tailPos = new Vector2(0,0);
    private Vector2 _actorScreenTailPos;

    public float offsetForFacing = 0f;

    public Globals.DialogTypes dialogType = Globals.DialogTypes.narration;
    public bool isStrict = false;

    private RichTextLabel dbText;
    private Timer dbTimer;
    private AudioStreamPlayer audioStreamPlayer;
    private NinePatchRect dbSpeechBubble;
    private NinePatchRect dbNarrationBubble;
    private NinePatchRect dbThoughtBubble;
    private NinePatchRect dbExclaimBubble;
    private Polygon2D dbSpeechBubbleTail;

    public DialogChoices dialogChoices;

    private enum PlacementSide { Above, Below, Left, Right }
    private PlacementSide _placement = PlacementSide.Above;

    private Rect2 drawRect;
    private bool  _tailIsBelow;  // used by exclaim only
    private (Vector2 center, float rx, float ry)[] _speechOvoids;
    private float[] _speechPhases;
    private float[] _speechFreqs;
    private float   _speechTime;
    private Rect2     _bubbleRect;
    private Vector2[] _narrationBasePts;
    private float     _narrationBaseShear;
    private float     _narrationShearAmp;
    private float     _narrationPhase;
    private float     _narrationTime;
    private float     _narrationCenterY;

    public Globals.NarrationCorner narrationCorner = Globals.NarrationCorner.Auto;
    public Globals.NarrationStyle  narrationStyle  = Globals.NarrationStyle.Normal;

    // ── Exclaim bubble ────────────────────────────────────────────────────────────
    private Vector2   _exclaimCenter;
    private float[]   _exclaimSpikeAngles;
    private float[]   _exclaimSpikePhases;
    private float[]   _exclaimSpikeFreqs;
    private float     _exclaimRxInner;      // spike-base radius x
    private float     _exclaimRyInner;      // spike-base radius y
    private float     _exclaimRxValley;     // valley radius x — guaranteed to contain text rect
    private float     _exclaimRyValley;     // valley radius y
    private float     _exclaimSpikeExt;
    private float     _exclaimTime;
    private Vector2[] _exclaimPolyCache;    // last-built (non-shadow) starburst poly for tail clipping

    private string phrase;
    private int _strippedPhraseLength;
    private int charsAlreadyDisplayed = 0;


    public string StripBbcode(string input) {
       return Regex.Replace(input, @"\[[^\]]+\]", "");
    }
    public void SetPhrase(string _phrase) { this.phrase = _phrase; }
    public string GetPhrase() { return phrase; }
    public string GetPhraseWithoutBbcode() { return StripBbcode(phrase); }
    public string GetCurrentCharacter() { return phrase[dbText.VisibleCharacters-1].ToString(); }

    public override void _Ready()
    {
        // DialogBox is now in a CanvasLayer (screen space). Full-Rect anchors would
        // make the control resize against the viewport, producing a negative height
        // (and malformed _Draw calls) when Position.Y is near the bottom of the screen.
        // Reset to TopLeft so Position is unconditionally free.
        AnchorLeft = AnchorRight = AnchorTop = AnchorBottom = 0f;
        OffsetLeft = OffsetTop = OffsetRight = OffsetBottom = 0f;

        dbText            = GetNode<RichTextLabel>("DB_Text");
        dbSpeechBubble    = dbText.GetNode<NinePatchRect>("DB_SpeechBubble");
        dbNarrationBubble = dbText.GetNode<NinePatchRect>("DB_NarrationBubble");
        dbThoughtBubble   = dbText.GetNode<NinePatchRect>("DB_ThoughtBubble");
        dbExclaimBubble   = dbText.GetNode<NinePatchRect>("DB_ExclaimBubble");
        dbSpeechBubbleTail= dbSpeechBubble.GetNode<Polygon2D>("DB_SpeechBubbleTail");
        dbTimer           = GetNode<Timer>("DB_TextTimer");
        audioStreamPlayer = GetNode<AudioStreamPlayer>("DB_AudioStreamPlayer");
        dialogChoices     = dbText.GetNode<DialogChoices>("DialogChoices");
    }

    public override void _Process(double delta)
    {
        if (trackActor != null)
        {
            Vector2 newScreenTailPos = WorldToScreen(trackActor.topPoint);
            if (newScreenTailPos != _actorScreenTailPos)
            {
                Position           += newScreenTailPos - _actorScreenTailPos;
                _actorScreenTailPos = newScreenTailPos;
                UpdateTailToActor(newScreenTailPos);
                QueueRedraw();
            }
        }

        if (dialogType == Globals.DialogTypes.thinking)
        {
            _cloudTime += (float)delta;
            QueueRedraw();
        }

        if (dialogType == Globals.DialogTypes.speaking)
        {
            _speechTime += (float)delta;
            QueueRedraw();
        }

        if (dialogType == Globals.DialogTypes.exclaim)
        {
            _exclaimTime += (float)delta;
            QueueRedraw();
        }

        if (dialogType == Globals.DialogTypes.narration || dialogType == Globals.DialogTypes.choice)
        {
            _narrationTime += (float)delta;
            // Re-pin to the cel border corner each frame so any viewport resize is corrected.
            if (narrationCorner != Globals.NarrationCorner.Auto)
                Position = GetSpotForNarrationCorner(drawRect);
            QueueRedraw();
        }
    }

    public void DealWithClick() {
        if (dialogType == Globals.DialogTypes.choice) {
            if (dialogChoices.currentChoice >= 0)
                CloseThisDialog(dialogChoices.currentChoice);
            return;
        }
        if (isStrict) return;
        if (dbText.VisibleCharacters >= GetPhrase().Length)
            CloseThisDialog();
        else
            dbText.VisibleCharacters = GetPhrase().Length;
    }

    public override void _Input(InputEvent @event) {
        if (@event is InputEventMouseButton mouseEvent &&
            mouseEvent.ButtonIndex == MouseButton.Left &&
            mouseEvent.Pressed)
        {
            DealWithClick();
            GetViewport().SetInputAsHandled();
        }
        else if (@event is InputEventScreenTouch touchEvent && touchEvent.Pressed)
        {
            GetViewport().SetInputAsHandled();
            if (dialogType == Globals.DialogTypes.choice && dialogChoices != null)
            {
                // Hit-test directly so the first tap selects immediately (no hover pre-req).
                Vector2 canvasPos = GetViewport().GetCanvasTransform().AffineInverse() * touchEvent.Position;
                for (int i = 0; i < dialogChoices.choices.Count; i++)
                {
                    if (dialogChoices.choices[i].GetGlobalRect().HasPoint(canvasPos))
                    {
                        dialogChoices.currentChoice = i;
                        CloseThisDialog(i);
                        return;
                    }
                }
                // Touch missed all choices — swallowed but no selection.
                return;
            }
            DealWithClick();
        }
    }

    public override void _Draw() {
        if (dialogType == Globals.DialogTypes.speaking || dialogType == Globals.DialogTypes.exclaim) {
            bool isExclaim = dialogType == Globals.DialogTypes.exclaim;
            float animTime = isExclaim ? _exclaimTime : _speechTime;
            if (SpeechTailStyle == TailStyle.Wavy)     ComputeTailWavy();
            if (SpeechTailStyle == TailStyle.Lightning) ComputeTailLightning(animTime);
            if (SpeechTailStyle == TailStyle.Curved)    ComputeTailCurved();
            if (SpeechTailStyle == TailStyle.Straight)  ComputeTailStraight();
            if (ShadowEnabled) {
                if (isExclaim) DrawExclaimBubble(ShadowOffset, ShadowColor, 0f);
                else           DrawSpeechBubble(ShadowOffset, ShadowColor, 0f);
                DrawTailShadow();
            }
            if (isExclaim) DrawExclaimBubble(Vector2.Zero, dialogColor, 4f);
            else           DrawSpeechBubble(Vector2.Zero, dialogColor, 2.5f);
            if (SpeechTailStyle != TailStyle.NoTail) DrawTail();
        } else if (dialogType == Globals.DialogTypes.narration || dialogType == Globals.DialogTypes.choice) {
            if (ShadowEnabled)
                DrawNarrationBubble(ShadowOffset, ShadowColor, 0f);
            DrawNarrationBubble(Vector2.Zero, dialogColor, 2.5f);
        } else if (dialogType == Globals.DialogTypes.thinking) {
            if (ShadowEnabled) {
                DrawCloudShape(ShadowOffset, ShadowColor, 0f);
                if (SpeechTailStyle != TailStyle.NoTail) DrawThoughtTrail(ShadowOffset, ShadowColor);
            }
            DrawCloudShape(Vector2.Zero, dialogColor, 2.5f);
            if (SpeechTailStyle != TailStyle.NoTail) DrawThoughtTrail(Vector2.Zero, dialogColor);
        }
    }

    // ── Speech bubble (superellipse ovoid geometry) ───────────────────────────────
    //
    // Shape: superellipse with exponent 3.5 — flatter sides, rounded ends.
    // Stacking: 1/2/3 ovoids based on line count; large overlap (~55% of height).
    // Outline: each ovoid draws only the portion of its outline that is NOT
    //   covered by an adjacent ovoid (Y-clip at midpoint between centers), so the
    //   overlapping region has no internal black lines.
    // Animation: each ovoid has independent phase/freq for subtle breathing.

    private const float SpeechExp     = 3.5f;
    private const float SpeechExp2    = 2f / SpeechExp;  // exponent for parametrization
    private const float SpeechAnimAmp = 2.2f;             // max vertical shift (px)
    private const float SpeechAnimBase= 0.28f;           // base speed (rad/s)
    private const float SpeechGrowAmp = 0.033f;          // max radius growth fraction

    private void ComputeSpeechBubble() {
        float rxPad = marginSize * 0.85f;
        float ryPad = marginSize * 0.55f;

        int lines = Mathf.Max(1, dbText.GetLineCount());
        int n     = lines <= 1 ? 1 : lines <= 3 ? 2 : 3;

        float rx   = dbText.Size.X / 2f + rxPad;
        float step = (dbText.Size.Y + ryPad * 2f) / n;
        float ry   = step * 0.78f;   // 56% overlap between adjacent ovoids

        // Bubble bounding box starts at (0, 0) — DialogBox is sized to fit.
        float bubbleW = rx * 2f;
        float bubbleH = 2f * ry + (n - 1) * step;

        _speechOvoids = new (Vector2, float, float)[n];
        for (int i = 0; i < n; i++)
            _speechOvoids[i] = (new Vector2(rx, ry + i * step), rx, ry);

        _bubbleRect = new Rect2(0f, 0f, bubbleW, bubbleH);

        // Center the RichTextLabel in the merged bubble.
        dbText.Position = new Vector2(
            rx - dbText.Size.X / 2f,
            bubbleH / 2f - dbText.Size.Y / 2f
        );

        // Per-ovoid animation params, seeded from phrase for stability.
        uint seed = 0;
        if (phrase != null) foreach (char c in phrase) seed = seed * 31u + c;
        var rng = new RandomNumberGenerator { Seed = seed };
        _speechPhases = new float[n];
        _speechFreqs  = new float[n];
        for (int i = 0; i < n; i++) {
            _speechPhases[i] = rng.Randf() * Mathf.Tau;
            _speechFreqs[i]  = rng.RandfRange(0.5f, 1.3f);
        }
        _speechTime = 0f;
    }

    // Returns the animated center/radii for ovoid[oi] at the current _speechTime.
    private (Vector2 center, float rx, float ry) AnimatedOvoid(int oi) {
        var (center, rx, ry) = _speechOvoids[oi];
        float s   = Mathf.Sin(_speechTime * SpeechAnimBase * _speechFreqs[oi] + _speechPhases[oi]);
        float grow = 1f + s * SpeechGrowAmp;
        Vector2 shift = new Vector2(0f, s * SpeechAnimAmp);
        return (center + shift, rx * grow, ry * grow);
    }

    // Generates points for one superellipse (used for fill polygon).
    private static Vector2[] SuperEllipsePoints(Vector2 center, float rx, float ry, int steps = 56) {
        var pts = new Vector2[steps];
        for (int i = 0; i < steps; i++) {
            float t  = (float)i / steps * Mathf.Tau;
            float ct = Mathf.Cos(t), st = Mathf.Sin(t);
            pts[i] = center + new Vector2(
                Mathf.Sign(ct) * Mathf.Pow(Mathf.Abs(ct), SpeechExp2) * rx,
                Mathf.Sign(st) * Mathf.Pow(Mathf.Abs(st), SpeechExp2) * ry
            );
        }
        return pts;
    }

    private void DrawSpeechBubble(Vector2 offset, Color fill, float outlineW) {
        if (_speechOvoids == null) return;

        // Fill pass — expand by half the outline width so the fill meets the outer edge.
        float fillGrow = outlineW * 0.5f;
        for (int oi = 0; oi < _speechOvoids.Length; oi++) {
            var (c, rx, ry) = AnimatedOvoid(oi);
            DrawPolygon(SuperEllipsePoints(c + offset, rx + fillGrow, ry + fillGrow), new[] { fill });
        }

        // Outline pass — only draw the outer contour of each ovoid.
        if (outlineW > 0f)
            DrawSpeechBubbleContour(offset, outlineW);
    }

    // Draws only the visible (non-overlapping) portion of each ovoid's outline.
    // Each ovoid is clipped to the Y band it "owns": above/below midpoints to neighbors.
    private void DrawSpeechBubbleContour(Vector2 offset, float outlineW) {
        int n = _speechOvoids.Length;
        const int steps = 64;

        for (int oi = 0; oi < n; oi++) {
            var (center, rx, ry) = AnimatedOvoid(oi);
            Vector2 c = center + offset;

            // Y range (in local-to-this-ovoid coords) that this ovoid "owns".
            float yMin = (oi == 0)     ? float.NegativeInfinity
                                       : (_speechOvoids[oi-1].center.Y + _speechOvoids[oi].center.Y) / 2f - center.Y;
            float yMax = (oi == n - 1) ? float.PositiveInfinity
                                       : (_speechOvoids[oi].center.Y + _speechOvoids[oi+1].center.Y) / 2f - center.Y;

            var seg = new List<Vector2>();
            for (int i = 0; i <= steps; i++) {
                float t  = (float)i / steps * Mathf.Tau;
                float ct = Mathf.Cos(t), st = Mathf.Sin(t);
                float lx = Mathf.Sign(ct) * Mathf.Pow(Mathf.Abs(ct), SpeechExp2) * (rx + outlineW);
                float ly = Mathf.Sign(st) * Mathf.Pow(Mathf.Abs(st), SpeechExp2) * (ry + outlineW);

                if (ly >= yMin && ly <= yMax) {
                    seg.Add(c + new Vector2(lx, ly));
                } else {
                    if (seg.Count > 1) DrawPolyline(seg.ToArray(), Colors.Black, outlineW, false);
                    seg.Clear();
                }
            }
            if (seg.Count > 1) DrawPolyline(seg.ToArray(), Colors.Black, outlineW, false);
        }
    }

    // ── Narration bubble (notched rectangle with animated shear) ──────────────────
    //
    // Shape: rectangle with outward corner spikes + triangular notch cuts along
    // each edge. A gentle horizontal shear is applied, animated over time, so the
    // box appears to lean slightly back and forth like a caption card.

    private void ComputeNarrationBubble() {
        float pad    = marginSize;
        bool  jagged = narrationStyle == Globals.NarrationStyle.Jagged;
        Rect2 r = new Rect2(
            dbText.Position - Vector2.One * pad,
            dbText.Size + Vector2.One * pad * 2f
        );
        float px = r.Position.X, py = r.Position.Y;
        float qx = r.End.X,      qy = r.End.Y;

        uint seed = 0;
        if (phrase != null) foreach (char c in phrase) seed = seed * 31u + c;
        var rng = new RandomNumberGenerator { Seed = seed };

        var pts = new List<Vector2>();

        if (jagged) {
            // Jagged: plain right-angle corners, dense notches covering the full left and
            // right edges (corner-to-corner) so jaggies appear near the top and base too.
            pts.Add(new Vector2(px, py));               // TL corner
            pts.Add(new Vector2(qx, py));               // TR corner  (top: no notches)
            AddJaggedEdgeNotches(pts, new Vector2(qx, py), new Vector2(qx, qy), rng);
            pts.Add(new Vector2(qx, qy));               // BR corner
            pts.Add(new Vector2(px, qy));               // BL corner  (bottom: no notches)
            AddJaggedEdgeNotches(pts, new Vector2(px, qy), new Vector2(px, py), rng);
            // polygon closes back to TL
        } else {
            // Normal: outward spikes on the left corners (TL, BL); inward bends on the
            // right corners (TR, BR) so the right side folds inward instead of jutting out.
            const float spike = 1.5f;
            const float near  = spike * 2.2f;

            pts.Add(new Vector2(px - spike, py));       // TL — outward left spike
            pts.Add(new Vector2(px + near,  py));       // depart TL along top

            pts.Add(new Vector2(qx - near,  py));       // arrive TR along top
            pts.Add(new Vector2(qx - spike, py));       // TR — inward bend (leftward)
            pts.Add(new Vector2(qx,         py + near));// depart TR along right

            pts.Add(new Vector2(qx,         qy - near));// arrive BR along right
            pts.Add(new Vector2(qx - spike, qy));       // BR — inward bend (leftward)
            pts.Add(new Vector2(qx - near,  qy));       // depart BR along bottom

            pts.Add(new Vector2(px + near,  qy));       // arrive BL along bottom
            pts.Add(new Vector2(px - spike, qy));       // BL — outward left spike
            pts.Add(new Vector2(px,         qy - near));// depart BL along left

            pts.Add(new Vector2(px,         py + near));// arrive TL along left
            // polygon closes to (px - spike, py)
        }

        _narrationBasePts  = pts.ToArray();
        _narrationCenterY  = py + r.Size.Y / 2f;

        _narrationBaseShear = rng.RandfRange(-0.06f, 0.06f);
        _narrationShearAmp  = rng.RandfRange(0.03f, 0.05f);
        _narrationPhase     = rng.Randf() * Mathf.Tau;
        _narrationTime      = 0f;
    }

    // Dense randomised inward notches along one full edge (Jagged style).
    // Covers fromPt → toPt exclusively; caller adds the endpoint.
    // Inward direction is the left-hand normal of dir (correct for CW polygon).
    private static void AddJaggedEdgeNotches(List<Vector2> pts, Vector2 fromPt, Vector2 toPt,
                                              RandomNumberGenerator rng) {
        Vector2 dir    = (toPt - fromPt).Normalized();
        Vector2 inward = new(-dir.Y, dir.X);
        float   len    = (toPt - fromPt).Length();
        if (len < 10f) return;

        // One notch per 8–12 px — denser than before, and now covers the full edge height.
        int count = Mathf.Max(3, Mathf.RoundToInt(len / rng.RandfRange(10f, 15f)));
        float slotSize = 1f / count;
        float cursor   = 0f;
        for (int i = 0; i < count; i++) {
            float lo     = Mathf.Max(0.01f, cursor);
            float hi     = Mathf.Min(0.99f, cursor + slotSize);
            float center = rng.RandfRange(lo + slotSize * 0.05f, hi - slotSize * 0.05f);
            float depth  = rng.RandfRange(3f, 18f);
            float hw     = rng.RandfRange(1.5f, 4f) * 0.5f / len;

            pts.Add(fromPt.Lerp(toPt, Mathf.Max(0.005f, center - hw)));
            pts.Add(fromPt.Lerp(toPt, center) + inward * depth);
            pts.Add(fromPt.Lerp(toPt, Mathf.Min(0.995f, center + hw)));
            cursor += slotSize;
        }
    }

    private void DrawNarrationBubble(Vector2 offset, Color fill, float outlineW) {
        if (_narrationBasePts == null) return;

        float shear = _narrationBaseShear
                    + _narrationShearAmp * Mathf.Sin(_narrationTime * 0.18f + _narrationPhase);
        var pts = ShearPolygon(_narrationBasePts, shear, _narrationCenterY);
        for (int i = 0; i < pts.Length; i++) pts[i] += offset;

        Vector2 fanCenter = Vector2.Zero;
        foreach (var p in pts) fanCenter += p;
        fanCenter /= pts.Length;
        var c3 = new[] { fill, fill, fill };
        for (int i = 0; i < pts.Length; i++)
            DrawPrimitive(new[] { fanCenter, pts[i], pts[(i + 1) % pts.Length] }, c3, null);
        if (outlineW > 0f) {
            var closed = new Vector2[pts.Length + 1];
            Array.Copy(pts, closed, pts.Length);
            closed[pts.Length] = pts[0];
            DrawPolyline(closed, Colors.Black, outlineW, false);
        }
    }

    private static Vector2[] ShearPolygon(Vector2[] pts, float shear, float pivotY) {
        var result = new Vector2[pts.Length];
        for (int i = 0; i < pts.Length; i++)
            result[i] = new Vector2(pts[i].X + shear * (pts[i].Y - pivotY), pts[i].Y);
        return result;
    }

    // ── Exclaim bubble (starburst) ────────────────────────────────────────────────
    //
    // Shape: N radiating spikes (8–12, seeded per phrase). Each spike has a sharp
    // tip at r_inner + spike_ext, shoulder points slightly bulged at the base, and
    // concave valleys between spikes at r_valley < r_inner.
    // Animation: each spike tip oscillates independently (±15% extension).

    private void ComputeExclaimBubble() {
        // Body ellipse tracks text size; spikes are fixed pixel height above the body.
        float rxBase   = dbText.Size.X / 2f + marginSize;
        float ryBase   = dbText.Size.Y / 2f + marginSize;
        const float spikeRise = 12f;  // fixed px from body surface to spike base
        const float spikeExt  = 7f;   // fixed px for pointed tip beyond spike base

        float rxInner  = rxBase + spikeRise;
        float ryInner  = ryBase + spikeRise;

        uint seed = 0;
        if (phrase != null) foreach (char c in phrase) seed = seed * 31u + c;
        var rng = new RandomNumberGenerator { Seed = seed };

        int nSpikes = rng.RandiRange(9, 13);
        _exclaimSpikeAngles    = new float[nSpikes];
        _exclaimSpikePhases    = new float[nSpikes];
        _exclaimSpikeFreqs     = new float[nSpikes];
        _exclaimRxInner  = rxInner;
        _exclaimRyInner  = ryInner;
        _exclaimRxValley = rxBase;    // valley IS the body ellipse — always shallow
        _exclaimRyValley = ryBase;
        _exclaimSpikeExt = spikeExt;

        float step = Mathf.Tau / nSpikes;
        float jMax = step * 0.18f;
        float angle = rng.RandfRange(0f, step);
        for (int i = 0; i < nSpikes; i++) {
            _exclaimSpikeAngles[i] = angle + rng.RandfRange(-jMax, jMax);
            _exclaimSpikePhases[i] = rng.Randf() * Mathf.Tau;
            _exclaimSpikeFreqs[i]  = rng.RandfRange(0.5f, 1.5f);
            angle += step;
        }

        float bubbleW  = (rxInner + spikeExt) * 2f;
        float bubbleH  = (ryInner + spikeExt) * 2f;
        _exclaimCenter = new Vector2(bubbleW / 2f, bubbleH / 2f);
        _bubbleRect    = new Rect2(0f, 0f, bubbleW, bubbleH);
        _exclaimTime   = 0f;

        dbText.Position = new Vector2(
            _exclaimCenter.X - dbText.Size.X / 2f,
            _exclaimCenter.Y - dbText.Size.Y / 2f);
    }

    // Builds the animated starburst polygon (rebuilt each frame).
    // Shape: N sharp tips at r_inner+spikeExt connected by quadratic Bezier valley arcs.
    // Each arc passes through the valley apex (midAngle, at r_valley) at t=0.5.
    private Vector2[] BuildExclaimPoly(Vector2 offset) {
        int n = _exclaimSpikeAngles.Length;
        var pts = new List<Vector2>(n * 10);
        Vector2 c = _exclaimCenter + offset;

        // Slow global breath — shifts tips and valleys together outward/inward.
        float breath   = Mathf.Sin(_exclaimTime * 1.1f) * 1.8f;
        float rxInnerA = _exclaimRxInner + breath;
        float ryInnerA = _exclaimRyInner + breath;
        float rxValleyA = _exclaimRxValley + breath * 0.6f;
        float ryValleyA = _exclaimRyValley + breath * 0.6f;

        // Pre-compute animated tip positions.
        var tips = new Vector2[n];
        for (int i = 0; i < n; i++) {
            float s   = Mathf.Sin(_exclaimTime * 1.2f * _exclaimSpikeFreqs[i] + _exclaimSpikePhases[i]);
            float ext = _exclaimSpikeExt * (1f + s * 0.18f);
            float a   = _exclaimSpikeAngles[i];
            tips[i] = c + new Vector2(Mathf.Cos(a) * (rxInnerA + ext),
                                      Mathf.Sin(a) * (ryInnerA + ext));
        }

        // For each spike: Bezier valley arc from prev tip → valley apex → this tip,
        // then the sharp tip itself. k=0 (= prev tip) is skipped — added by prev iteration.
        const int arcSteps = 8;
        for (int i = 0; i < n; i++) {
            int     prev  = (i - 1 + n) % n;
            Vector2 p0    = tips[prev];
            Vector2 p2    = tips[i];

            float   prevA = _exclaimSpikeAngles[prev];
            float   thisA = _exclaimSpikeAngles[i];
            float   midA  = prevA + Mathf.AngleDifference(prevA, thisA) * 0.5f;
            Vector2 apex  = c + new Vector2(Mathf.Cos(midA) * rxValleyA,
                                            Mathf.Sin(midA) * ryValleyA);
            // Control point so Bezier passes through apex at t=0.5.
            Vector2 ctrl  = 2f * apex - (p0 + p2) * 0.5f;

            for (int k = 1; k <= arcSteps; k++) {
                float t = (float)k / arcSteps, u = 1f - t;
                pts.Add(u * u * p0 + 2f * u * t * ctrl + t * t * p2);
            }
        }
        return pts.ToArray();
    }

    private void DrawExclaimBubble(Vector2 offset, Color fill, float outlineW, bool fillEnabled = true) {
        if (_exclaimSpikeAngles == null) return;
        Vector2 center = _exclaimCenter + offset;
        var poly = BuildExclaimPoly(offset);
        if (offset == Vector2.Zero) _exclaimPolyCache = poly;

        // Outline: dilate each vertex outward from center by outlineW, draw in black first.
        if (outlineW > 0f) {
            var outer = new Vector2[poly.Length];
            for (int i = 0; i < poly.Length; i++) {
                Vector2 d = poly[i] - center;
                float   r = d.Length();
                outer[i]  = r > 0.01f ? center + d * ((r + outlineW) / r) : poly[i];
            }
            var black = new[] { Colors.Black, Colors.Black, Colors.Black };
            for (int i = 0; i < outer.Length; i++)
                DrawPrimitive(new[] { center, outer[i], outer[(i + 1) % outer.Length] }, black, null);
        }

        if (fillEnabled) {
            var c3 = new[] { fill, fill, fill };
            for (int i = 0; i < poly.Length; i++)
                DrawPrimitive(new[] { center, poly[i], poly[(i + 1) % poly.Length] }, c3, null);
        }
    }

    // ── Speech tail ───────────────────────────────────────────────────────────────

    public Vector2[] tailPolygon;
    public void DrawTail() {
        if (_tailFillPolygon == null) return;
        // Wavy: draw as explicit triangle strip to avoid ear-clip failure when animated curves align.
        if (SpeechTailStyle == TailStyle.Wavy && _tailLeftCurve != null && _tailRightRaw != null) {
            var c3 = new[] { dialogColor, dialogColor, dialogColor };
            int n = _tailLeftCurve.Length;
            for (int i = 0; i < n - 1; i++) {
                DrawPrimitive(new[] { _tailLeftCurve[i],     _tailLeftCurve[i + 1], _tailRightRaw[i + 1] }, c3, null);
                DrawPrimitive(new[] { _tailLeftCurve[i],     _tailRightRaw[i + 1],  _tailRightRaw[i]     }, c3, null);
            }
        } else {
            DrawPolygon(_tailFillPolygon, new[] { dialogColor });
        }
        float   inset = SpeechAnimAmp + TailLineWidth + 3f;
        Vector2 inDir = (dialogType == Globals.DialogTypes.exclaim)
            ? (_tailIsBelow ? new Vector2(0f, inset) : new Vector2(0f, -inset))
            : _placement switch {
                PlacementSide.Below => new Vector2(0f,    inset),
                PlacementSide.Left  => new Vector2(-inset, 0f),
                PlacementSide.Right => new Vector2( inset, 0f),
                _                   => new Vector2(0f,   -inset),
            };
        DrawPolygon(new Vector2[] { _tailBaseL, _tailBaseR, _tailBaseR + inDir, _tailBaseL + inDir },
                    new[] { dialogColor });
        DrawTailOutline(_tailLeftCurve,  tipAtEnd: true);
        DrawTailOutline(_tailRightCurve, tipAtEnd: false);
    }

    private static bool PointInPoly(Vector2 p, Vector2[] poly) {
        bool inside = false;
        int j = poly.Length - 1;
        for (int i = 0; i < poly.Length; j = i++)
            if ((poly[i].Y > p.Y) != (poly[j].Y > p.Y) &&
                p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
                inside = !inside;
        return inside;
    }

    private void DrawTailShadow() {
        if (SpeechTailStyle == TailStyle.NoTail) return;
        if (SpeechTailStyle == TailStyle.Wavy && _tailLeftCurve != null && _tailRightRaw != null) {
            var s3 = new[] { ShadowColor, ShadowColor, ShadowColor };
            int n = _tailLeftCurve.Length;
            for (int i = 0; i < n - 1; i++) {
                DrawPrimitive(new[] { _tailLeftCurve[i] + ShadowOffset,     _tailLeftCurve[i + 1] + ShadowOffset, _tailRightRaw[i + 1] + ShadowOffset }, s3, null);
                DrawPrimitive(new[] { _tailLeftCurve[i] + ShadowOffset,     _tailRightRaw[i + 1] + ShadowOffset,  _tailRightRaw[i] + ShadowOffset     }, s3, null);
            }
        } else if (_tailFillPolygon != null) {
            var shadowTail = Array.ConvertAll(_tailFillPolygon, p => p + ShadowOffset);
            DrawPolygon(shadowTail, new[] { ShadowColor });
        }
    }

    // All t values in (0,1) where segment a→b crosses the polygon, sorted ascending.
    private static List<float> SegmentPolyCrossings(Vector2 a, Vector2 b, Vector2[] poly) {
        var ts = new List<float>();
        Vector2 r = b - a;
        int n = poly.Length;
        for (int i = 0; i < n; i++) {
            Vector2 c = poly[i], s = poly[(i + 1) % n] - c;
            float denom = r.X * s.Y - r.Y * s.X;
            if (Mathf.Abs(denom) < 1e-6f) continue;
            Vector2 diff = c - a;
            float t = (diff.X * s.Y - diff.Y * s.X) / denom;
            float u = (diff.X * r.Y - diff.Y * r.X) / denom;
            if (t > 1e-4f && t < 1f - 1e-4f && u >= 0f && u <= 1f) ts.Add(t);
        }
        ts.Sort();
        return ts;
    }

    // All t values in (0,1) where segment a→b crosses the rect, sorted ascending.
    private static List<float> SegmentRectCrossings(Vector2 a, Vector2 b, Rect2 r) {
        var ts = new List<float>();
        Vector2 d = b - a;
        void CheckEdge(float t, float coord, float lo, float hi) {
            if (t > 1e-4f && t < 1f - 1e-4f && coord >= lo && coord <= hi) ts.Add(t);
        }
        if (Mathf.Abs(d.X) > 1e-6f) {
            float tx0 = (r.Position.X - a.X) / d.X; CheckEdge(tx0, a.Y + tx0 * d.Y, r.Position.Y, r.End.Y);
            float tx1 = (r.End.X      - a.X) / d.X; CheckEdge(tx1, a.Y + tx1 * d.Y, r.Position.Y, r.End.Y);
        }
        if (Mathf.Abs(d.Y) > 1e-6f) {
            float ty0 = (r.Position.Y - a.Y) / d.Y; CheckEdge(ty0, a.X + ty0 * d.X, r.Position.X, r.End.X);
            float ty1 = (r.End.Y      - a.Y) / d.Y; CheckEdge(ty1, a.X + ty1 * d.X, r.Position.X, r.End.X);
        }
        ts.Sort();
        return ts;
    }

    private void DrawTailOutline(Vector2[] pts, bool tipAtEnd) {
        if (pts == null || pts.Length < 2) return;
        int last = pts.Length - 1;
        bool usePolyClip = dialogType == Globals.DialogTypes.exclaim && _exclaimPolyCache != null;
        Rect2 clip = _bubbleRect.Grow(-2f);
        for (int i = 0; i < last; i++) {
            Vector2 a = pts[i], b = pts[i + 1];
            float tTaper = tipAtEnd ? (float)i / last : 1f - (float)i / last;
            float width  = Mathf.Lerp(TailLineWidth, 0.5f, tTaper);
            // Collect all boundary crossings along a→b, walk them toggling inside/outside.
            List<float> crossings = usePolyClip
                ? SegmentPolyCrossings(a, b, _exclaimPolyCache)
                : SegmentRectCrossings(a, b, clip);
            bool inside = usePolyClip ? PointInPoly(a, _exclaimPolyCache) : clip.HasPoint(a);
            float tPrev = 0f;
            foreach (float t in crossings) {
                if (!inside) DrawLine(a.Lerp(b, tPrev), a.Lerp(b, t), Colors.Black, width);
                inside = !inside;
                tPrev  = t;
            }
            if (!inside) DrawLine(a.Lerp(b, tPrev), b, Colors.Black, width);
        }
    }

    public void DrawThoughtTrail(Vector2 offset, Color fill) {
        if (_thoughtDotCenters == null) return;
        const int blobVerts = 14;
        bool isShadow = fill != Colors.White;
        Color[] outline3 = { Colors.Black, Colors.Black, Colors.Black };
        Color[] fill3    = { fill, fill, fill };
        for (int i = 0; i < _thoughtDotCenters.Length; i++) {
            float phase = _cloudTime * 2.8f + i * 1.4f;
            Vector2 pos = _thoughtDotCenters[i] + offset
                        + new Vector2(Mathf.Sin(phase) * 0.6f, Mathf.Sin(phase) * 1.2f);
            float rx = _thoughtDotRx[i], ry = _thoughtDotRy[i];
            float tilt = _thoughtDotAngle[i];
            float outlineW = 2f;
            var poly = new Vector2[blobVerts];
            for (int v = 0; v < blobVerts; v++) {
                float a  = v * Mathf.Tau / blobVerts;
                float nr = 1f + _thoughtDotNoise[i][v];
                float lx = Mathf.Cos(a) * rx * nr;
                float ly = Mathf.Sin(a) * ry * nr;
                poly[v]  = pos + new Vector2(lx * Mathf.Cos(tilt) - ly * Mathf.Sin(tilt),
                                             lx * Mathf.Sin(tilt) + ly * Mathf.Cos(tilt));
            }
            if (!isShadow) {
                var outer = new Vector2[blobVerts];
                for (int v = 0; v < blobVerts; v++) {
                    Vector2 d = poly[v] - pos;
                    float   r = d.Length();
                    outer[v]  = r > 0.01f ? pos + d * ((r + outlineW) / r) : poly[v];
                }
                for (int v = 0; v < blobVerts; v++)
                    DrawPrimitive(new[] { pos, outer[v], outer[(v + 1) % blobVerts] }, outline3, null);
            }
            for (int v = 0; v < blobVerts; v++)
                DrawPrimitive(new[] { pos, poly[v], poly[(v + 1) % blobVerts] }, fill3, null);
        }
    }

    public void _on_DB_TextTimer_timeout() {
        if (dbText.VisibleCharacters < _strippedPhraseLength) {
            dbText.VisibleCharacters++;
        } else {
            dbText.VisibleCharacters = GetPhrase().Length;
        }
        ManageAudio();
    }

    private int characterAudioPlayed = 0;
    public void ManageAudio() {
        if (dialogType == Globals.DialogTypes.choice) return;
        if (characterAudioPlayed < dbText.VisibleCharacters) {
            characterAudioPlayed = dbText.VisibleCharacters;
            audioStreamPlayer.Stop();
            if (GetCurrentCharacter() != " ")
                audioStreamPlayer.Play();
        }
    }

    public void CloseThisDialog(int choice = -1) {
        this.QueueFree();
        if (choice == -1) this.EmitSignal("DialogClosed");
        else              this.EmitSignal("DialogClosed", choice);
    }

    // Projects a world-space position onto screen pixels, matching the inverse of
    // the old ScreenToWorld formula. Used to anchor dialog boxes (CanvasLayer,
    // screen-space) to world-space NPC positions.
    private Vector2 WorldToScreen(Vector2 worldPos)
    {
        var cam = parentScene?.camera;
        if (cam == null) return worldPos;
        Vector2 vpHalf = GetViewport().GetVisibleRect().Size / 2f;
        return vpHalf + (worldPos - cam.Position - cam.Offset) * cam.Zoom;
    }

    private Vector2 GetSpotForNarrationCorner(Rect2 rect) {
        Rect2 inner = parentScene.mainScene.CelBorderInnerRect;
        const float spikeW = 6f;
        const float gap    = 10f;
        Vector2 tl = inner.Position + new Vector2(spikeW + gap, gap);
        Vector2 br = inner.End - rect.Size - new Vector2(spikeW + gap, gap);
        return narrationCorner switch {
            Globals.NarrationCorner.TopLeft      => new Vector2(tl.X, tl.Y),
            Globals.NarrationCorner.TopRight     => new Vector2(br.X, tl.Y),
            Globals.NarrationCorner.BottomLeft   => new Vector2(tl.X, br.Y),
            Globals.NarrationCorner.BottomRight  => new Vector2(br.X, br.Y),
            Globals.NarrationCorner.BottomCenter => new Vector2((tl.X + br.X) / 2f, br.Y),
            _                                    => GetSpotForDialog(rect),
        };
    }

    public Vector2 GetSpotForDialog(Rect2 rect) {
        Rect2 inner = parentScene.mainScene.CelBorderInnerRect;
        float minX = inner.Position.X;
        float minY = inner.Position.Y;
        float maxX = inner.End.X - rect.Size.X - marginSize * 2;
        float maxY = inner.End.Y - rect.Size.Y - marginSize * 2;

        float camZoomY  = parentScene?.camera?.Zoom.Y ?? 1f;
        float clearance = trackActor != null
            ? trackActor.Position.DistanceTo(trackActor.topPoint) * 0.5f * camZoomY
            : marginSize * 8f;
        float sideGap   = marginSize * 2f;
        float xCentered = Mathf.Clamp(tailPos.X - rect.Size.X / 2f + offsetForFacing, minX, maxX);
        float yMid      = Mathf.Clamp(tailPos.Y - rect.Size.Y / 2f, minY, maxY);

        bool  isThought = dialogType == Globals.DialogTypes.thinking;
        float yAbove = tailPos.Y - clearance - rect.Size.Y;
        if (isThought) yAbove = Mathf.Max(yAbove, minY);

        // Collect other NPC head positions (screen space) to avoid covering them.
        var otherHeads = new System.Collections.Generic.List<Vector2>();
        foreach (var node in parentScene.FindChildren("*", "NPC", true, false))
        {
            if (node is NPC npc && npc != trackActor)
                otherHeads.Add(WorldToScreen(npc.topPoint));
        }

        bool OverlapsNPC(Vector2 pos) {
            var candidate = new Rect2(pos, rect.Size);
            foreach (var head in otherHeads)
                if (candidate.HasPoint(head)) return true;
            return false;
        }

        // Tail would cross the actor's head if the box is placed on the same side they face.
        NPC.Direction? actorFacing = (trackActor as NPC)?.Facing;
        bool canLeft  = actorFacing != NPC.Direction.right;
        bool canRight = actorFacing != NPC.Direction.left;

        // Build candidates in preference order; pick first in-bounds one, preferring NPC-free.
        (PlacementSide side, Vector2 pos, bool valid)[] candidates = {
            (PlacementSide.Above, new Vector2(xCentered, yAbove),
                yAbove >= minY && yAbove + rect.Size.Y <= tailPos.Y),
            (PlacementSide.Left,  new Vector2(tailPos.X - rect.Size.X - sideGap, yMid),
                canLeft  && tailPos.X - rect.Size.X - sideGap >= minX),
            (PlacementSide.Right, new Vector2(tailPos.X + sideGap, yMid),
                canRight && tailPos.X + sideGap <= maxX),
            (PlacementSide.Below, new Vector2(xCentered, Mathf.Clamp(tailPos.Y + sideGap, minY, maxY)),
                true),
        };

        // First pass: valid + no NPC overlap.
        foreach (var (side, pos, valid) in candidates)
            if (valid && !OverlapsNPC(pos))
            { _placement = side; return pos; }

        // Second pass: valid regardless of NPC overlap.
        foreach (var (side, pos, valid) in candidates)
            if (valid)
            { _placement = side; return pos; }

        _placement = PlacementSide.Below;
        return candidates[3].pos;
    }

    // ── Live tail tracking ────────────────────────────────────────────────────────

    // Called each frame when trackActor moves or the camera pans. The dialog box
    // stays in place; only the tail tip is repositioned so it always points at the
    // actor's topPoint. Tail geometry is recomputed from the new tip.
    private void UpdateTailToActor(Vector2 screenTailPos)
    {
        Vector2 rawLocal = screenTailPos - Position;

        if (dialogType == Globals.DialogTypes.speaking)
        {
            if (tailPolygon != null)
            {
                tailPolygon[1] = rawLocal * 0.75f + anchorAvg * 0.25f;
                ComputeTailBezier();
            }
        }
        else if (dialogType == Globals.DialogTypes.exclaim)
        {
            Vector2 toActor    = rawLocal - _exclaimCenter;
            Vector2 dir        = toActor.Length() > 0.1f ? toActor.Normalized() : Vector2.Down;
            float   angle      = Mathf.Atan2(dir.Y, dir.X);
            Vector2 perp       = new Vector2(-dir.Y, dir.X);
            Vector2 baseCenter = _exclaimCenter + new Vector2(
                Mathf.Cos(angle) * _exclaimRxValley * 0.85f,
                Mathf.Sin(angle) * _exclaimRyValley * 0.85f);
            float spread = Mathf.Max(_exclaimRxValley, _exclaimRyValley) * 0.20f;
            tailPolygon = [
                baseCenter - perp * spread,
                rawLocal * 0.75f + baseCenter * 0.25f,
                baseCenter + perp * spread,
            ];
            ComputeTailBezier();
        }
        else if (dialogType == Globals.DialogTypes.thinking)
        {
            tailPos = rawLocal * 0.75f + anchorAvg * 0.25f;
            ComputeThoughtDots();
        }
    }

    // ── Tail / thought-trail geometry ─────────────────────────────────────────────

    private Vector2[] _tailFillPolygon;
    private Vector2   _tailBaseL, _tailBaseR;  // actual base endpoints used by the cover rect
    private Vector2[] _tailLeftCurve;
    private Vector2[] _tailRightCurve;
    private Vector2[] _tailRightRaw;   // right curve in b→tip order (before reverse), for triangle strip
    private Vector2[] _thoughtDotCenters;
    private float[]   _thoughtDotRadii;
    private float[]   _thoughtDotRx;
    private float[]   _thoughtDotRy;
    private float[]   _thoughtDotAngle;
    private float[][] _thoughtDotNoise;   // per-dot, per-vertex radius fraction noise

    private static Vector2 QuadBezier(Vector2 p0, Vector2 cp, Vector2 p2, float t) {
        float u = 1f - t;
        return u * u * p0 + 2f * u * t * cp + t * t * p2;
    }

    private void ComputeTailBezier() {
        switch (SpeechTailStyle) {
            case TailStyle.Straight:  ComputeTailStraight();  break;
            case TailStyle.Wavy:      ComputeTailWavy();      break;
            case TailStyle.Lightning: ComputeTailLightning(); break;
            default:                  ComputeTailCurved();    break;
        }
    }

    // Lightning-bolt (zigzag) tail — two kinks alternating left/right of the
    // direct base-to-tip line. Width tapers from base spread to a sharp tip.
    private void ComputeTailLightning(float animTime = 0f) {
        Vector2 a = tailPolygon[0], tip = tailPolygon[1], b = tailPolygon[2];
        Vector2 baseCenter = (a + b) * 0.5f;
        Vector2 dir = tip - baseCenter;
        float length = dir.Length();
        if (length < 1f) { _tailFillPolygon = null; return; }
        dir /= length;
        Vector2 perp = new Vector2(-dir.Y, dir.X);
        float kink = length * 0.26f;
        // Animated width: pulse gently around base size.
        float baseW = 10f + Mathf.Sin(animTime * 4.5f) * 0.6f;

        Vector2 wp0 = baseCenter;
        Vector2 wp1 = baseCenter + dir * (length * 0.30f) + perp * kink;
        Vector2 wp2 = baseCenter + dir * (length * 0.62f) - perp * kink;
        // Re-approach tip along main axis so the final segment is always diagonal.
        Vector2 wp3 = tip - dir * (length * 0.10f);
        Vector2 wp4 = tip;

        static Vector2 Perp(Vector2 from, Vector2 to)
            => new Vector2(-(to - from).Normalized().Y, (to - from).Normalized().X);

        // n01 uses overall perp so the base is a flat horizontal line.
        Vector2 n01 = perp;
        Vector2 n12 = (perp + Perp(wp1, wp2)).Normalized();
        Vector2 n23 = (Perp(wp1, wp2) + Perp(wp2, wp3)).Normalized();
        Vector2 n34 = Perp(wp3, wp4);

        float w1 = baseW * 0.60f, w2 = baseW * 0.28f, w3 = baseW * 0.10f;

        _tailBaseL = wp0 - n01 * baseW;
        _tailBaseR = wp0 + n01 * baseW;
        _tailLeftCurve  = new[] { wp0 + n01*baseW, wp1 + n12*w1, wp2 + n23*w2, wp3 + n34*w3, wp4 };
        _tailRightCurve = new[] { wp0 - n01*baseW, wp1 - n12*w1, wp2 - n23*w2, wp3 - n34*w3, wp4 };
        _tailFillPolygon = new[] {
            wp0 + n01*baseW, wp1 + n12*w1, wp2 + n23*w2, wp3 + n34*w3, wp4,
            wp3 - n34*w3,    wp2 - n23*w2, wp1 - n12*w1, wp0 - n01*baseW,
        };
    }

    private void ComputeTailStraight() {
        Vector2 tip     = tailPolygon[1];
        Vector2 midBase = (tailPolygon[0] + tailPolygon[2]) * 0.5f;
        Vector2 dir     = tip - midBase;
        Vector2 perp    = dir.Length() > 0.1f ? new Vector2(-dir.Y, dir.X).Normalized() : Vector2.Right;
        const float baseHalf = 10f;
        Vector2 a = midBase - perp * baseHalf;
        Vector2 b = midBase + perp * baseHalf;
        float   skew    = Mathf.Sin(_speechTime * 1.3f) * 1.5f;
        Vector2 animTip = tip + perp * skew;
        _tailBaseL = a; _tailBaseR = b;
        _tailLeftCurve   = new[] { a, animTip };
        _tailRightCurve  = new[] { animTip, b };
        _tailFillPolygon = new[] { a, animTip, b };
    }

    private void ComputeTailCurved() {
        Vector2 tip     = tailPolygon[1];
        Vector2 midBase = (tailPolygon[0] + tailPolygon[2]) * 0.5f;
        Vector2 dir     = tip - midBase;
        Vector2 perp    = dir.Length() > 0.1f ? new Vector2(-dir.Y, dir.X).Normalized() : Vector2.Right;
        const float baseHalf = 10f;
        Vector2 a = midBase - perp * baseHalf;
        Vector2 b = midBase + perp * baseHalf;
        _tailBaseL = a; _tailBaseR = b;
        float   skew = Mathf.Sin(_speechTime * 1.3f) * 1.5f;
        Vector2 ctrl = ((a + b) * 0.5f).Lerp(tip, 0.5f) + perp * skew;
        const int steps = 18;
        _tailLeftCurve  = new Vector2[steps + 1];
        _tailRightCurve = new Vector2[steps + 1];
        for (int i = 0; i <= steps; i++) {
            float t = (float)i / steps;
            _tailLeftCurve[i]  = QuadBezier(a,   ctrl, tip, t);
            _tailRightCurve[i] = QuadBezier(tip, ctrl, b,   t);
        }
        _tailFillPolygon = new Vector2[steps * 2 + 1];
        Array.Copy(_tailLeftCurve,  0, _tailFillPolygon, 0,         steps + 1);
        Array.Copy(_tailRightCurve, 1, _tailFillPolygon, steps + 1, steps);
    }

    private void ComputeTailWavy() {
        Vector2 tip = tailPolygon[1];
        Vector2 midBase = (tailPolygon[0] + tailPolygon[2]) * 0.5f;
        Vector2 dir  = (tip - midBase).Normalized();
        Vector2 perp = new Vector2(-dir.Y, dir.X);
        const float baseHalf = 10f;
        Vector2 a = midBase - perp * baseHalf;
        Vector2 b = midBase + perp * baseHalf;
        _tailBaseL = a; _tailBaseR = b;
        const int steps = 28; const float waves = 2.5f, amp = 4f;
        _tailLeftCurve  = new Vector2[steps + 1];
        _tailRightCurve = new Vector2[steps + 1];
        for (int i = 0; i <= steps; i++) {
            float t    = (float)i / steps;
            float wave = Mathf.Sin(t * waves * Mathf.Tau - _speechTime * 1.5f) * (1f - t) * amp;
            _tailLeftCurve[i]  = a.Lerp(tip, t) + perp * wave;
            _tailRightCurve[i] = b.Lerp(tip, t) + perp * wave;
        }
        // Keep pre-reversed copy for triangle-strip fill drawing.
        _tailRightRaw = (Vector2[])_tailRightCurve.Clone();
        // Reverse right curve so index 0 = tip (matches tipAtEnd:false convention in DrawTailOutline).
        Array.Reverse(_tailRightCurve);
        // Fill polygon: left a→tip, right tip→b, no duplicate tip vertex.
        _tailFillPolygon = new Vector2[steps * 2 + 1];
        for (int i = 0; i <= steps; i++) _tailFillPolygon[i]         = _tailLeftCurve[i];
        for (int i = 1; i <= steps; i++) _tailFillPolygon[steps + i] = _tailRightCurve[i];
    }

    private void ComputeThoughtDots() {
        float dist = anchorAvg.DistanceTo(tailPos);
        int count  = Mathf.Clamp(Mathf.RoundToInt(dist / 22f), 2, 5);

        // Bezier control point: sweeps horizontally toward the dialog's X first,
        // then arcs up to the dialog anchor — gives a natural floating curve.
        Vector2 ctrl = new Vector2(anchorAvg.X, tailPos.Y);

        _thoughtDotCenters = new Vector2[count];
        _thoughtDotRadii   = new float[count];
        _thoughtDotRx      = new float[count];
        _thoughtDotRy      = new float[count];
        _thoughtDotAngle   = new float[count];
        _thoughtDotNoise   = new float[count][];

        const int blobVerts = 14;
        uint seed = 0;
        if (phrase != null) foreach (char c in phrase) seed = seed * 31u + c;
        var rng = new RandomNumberGenerator { Seed = seed + 99u };

        for (int i = 0; i < count; i++) {
            // t=0 → tailPos (character head, small), t=1 → anchorAvg (dialog, large)
            float t = (i + 1f) / (count + 1f);
            _thoughtDotCenters[i] = QuadBezier(tailPos, ctrl, anchorAvg, t);
            float baseR = Mathf.Lerp(3.5f, 9f, t);
            _thoughtDotRadii[i] = baseR;
            float aspect = rng.RandfRange(0.55f, 0.82f);
            _thoughtDotRx[i]    = baseR;
            _thoughtDotRy[i]    = baseR * aspect;
            _thoughtDotAngle[i] = rng.RandfRange(0f, Mathf.Tau);
            _thoughtDotNoise[i] = new float[blobVerts];
            for (int v = 0; v < blobVerts; v++)
                _thoughtDotNoise[i][v] = rng.RandfRange(-0.1f, 0.1f);
        }
    }

    // ── Cloud bubble geometry ─────────────────────────────────────────────────────

    private struct CloudBump {
        public Vector2 Center;
        public float   Radius;
        public float   OutAngle;
        public float   Phase;
        public float   Freq;
    }
    private struct CloudValley {
        public Vector2 Center;
        public float   RadiusAlong;
        public float   RadiusOut;
        public float   OutAngle;
        public float   Phase;
        public float   Freq;
    }
    private CloudBump[]   _cloudBumps;
    private CloudValley[] _cloudValleys;
    private Rect2         _cloudInnerRect;
    private float         _cloudTime;

    // Measures choice labels and resizes dbText to contain them. Called before ComputeNarrationBubble.
    private void MeasureChoiceContent() {
        float w = 0f, h = 0f;
        foreach (var child in dialogChoices.choices) {
            child.CustomMinimumSize = Vector2.Zero;
            child.AutowrapMode     = TextServer.AutowrapMode.Off;
            child.Size             = new Vector2(9999f, 9999f);
            var ms = child.GetMinimumSize();
            w  = Mathf.Max(w, ms.X);
            h += ms.Y;
        }
        int sep = dialogChoices.GetThemeConstant("separation");
        h += Mathf.Max(0, dialogChoices.choices.Count - 1) * sep;
        var contentSize = new Vector2(Mathf.Max(w, 80f), Mathf.Max(h, 20f));
        dialogChoices.Size = contentSize;
        // Ensure dbText is tall enough to contain the VBox so Godot's GUI
        // traversal doesn't clip the last choice out of mouse-event reach.
        dbText.Size = new Vector2(
            Mathf.Max(dbText.Size.X, contentSize.X),
            Mathf.Max(dbText.Size.Y, dialogChoices.Position.Y + contentSize.Y));
    }

    private void ComputeCloudBubble() {
        Vector2 contentPos  = dbText.Position;
        Vector2 contentSize = dbText.Size;

        float pad = marginSize * 0.6f;
        _cloudInnerRect = new Rect2(contentPos - Vector2.One * pad,
                                    contentSize + Vector2.One * pad * 2f);
        Rect2 r       = _cloudInnerRect;
        float bR      = CloudBumpRadius;
        float spacing = bR * 1.45f;

        uint seed = 0;
        if (phrase != null) foreach (char c in phrase) seed = seed * 31u + c;
        var rng = new RandomNumberGenerator { Seed = seed };

        var bumps   = new List<CloudBump>();
        var valleys = new List<CloudValley>();

        void AddEdge(float length, float outAngle, System.Func<int, int, float, CloudBump> make) {
            int   n        = Mathf.Max(2, (int)Mathf.Round(length / spacing));
            float slot     = length / n;
            int   edgeStart = bumps.Count;
            for (int i = 0; i < n; i++) {
                float jitter = (rng.Randf() - 0.5f) * slot * 0.28f;
                var   b      = make(i, n, jitter);
                b.OutAngle   = outAngle;
                b.Phase      = rng.Randf() * Mathf.Tau;
                b.Freq       = rng.RandfRange(0.6f, 1.4f);
                bumps.Add(b);
            }
            Vector2 outDir = new Vector2(Mathf.Cos(outAngle), Mathf.Sin(outAngle));
            for (int i = edgeStart; i < bumps.Count - 1; i++) {
                if (rng.Randf() > 0.15f) continue;
                Vector2 mid    = (bumps[i].Center + bumps[i + 1].Center) * 0.5f;
                float   rOut   = bR * rng.RandfRange(0.28f, 0.40f);
                float   rAlong = rOut * rng.RandfRange(1.8f, 2.3f);
                valleys.Add(new CloudValley {
                    Center      = mid + outDir * rOut * 0.6f,
                    RadiusAlong = rAlong,
                    RadiusOut   = rOut,
                    OutAngle    = outAngle,
                    Phase       = rng.Randf() * Mathf.Tau,
                    Freq        = rng.RandfRange(0.5f, 1.3f),
                });
            }
        }

        AddEdge(r.Size.X, -Mathf.Pi / 2f, (i, n, j) => new CloudBump {
            Center = new Vector2(r.Position.X + (i + 0.5f) * r.Size.X / n + j, r.Position.Y),
            Radius = bR * rng.RandfRange(0.55f, 1.30f),
        });
        AddEdge(r.Size.X, Mathf.Pi / 2f, (i, n, j) => new CloudBump {
            Center = new Vector2(r.Position.X + (i + 0.5f) * r.Size.X / n + j, r.End.Y),
            Radius = bR * rng.RandfRange(0.55f, 1.30f),
        });
        AddEdge(r.Size.Y, Mathf.Pi, (i, n, j) => new CloudBump {
            Center = new Vector2(r.Position.X, r.Position.Y + (i + 0.5f) * r.Size.Y / n + j),
            Radius = bR * rng.RandfRange(0.55f, 1.30f),
        });
        AddEdge(r.Size.Y, 0f, (i, n, j) => new CloudBump {
            Center = new Vector2(r.End.X, r.Position.Y + (i + 0.5f) * r.Size.Y / n + j),
            Radius = bR * rng.RandfRange(0.55f, 1.30f),
        });

        _cloudTime    = 0f;
        _cloudBumps   = bumps.ToArray();
        _cloudValleys = valleys.ToArray();
    }

    private void DrawCloudShape(Vector2 offset, Color fill, float outlineWidth) {
        if (_cloudBumps == null) return;
        Rect2 ir = new Rect2(_cloudInnerRect.Position + offset, _cloudInnerRect.Size);
        const float animAmp  = 2.2f;
        const float animBase = 0.4f;
        const float growAmp  = 0.06f;

        if (outlineWidth > 0f) {
            DrawRect(ir.Grow(outlineWidth), Colors.Black);
            foreach (var b in _cloudBumps) {
                float   s    = Mathf.Sin(_cloudTime * animBase * b.Freq + b.Phase);
                Vector2 anim = new Vector2(Mathf.Cos(b.OutAngle), Mathf.Sin(b.OutAngle)) * s * animAmp;
                DrawCircle(b.Center + offset + anim, b.Radius * (1f + s * growAmp) + outlineWidth, Colors.Black);
            }
        }
        DrawRect(ir, fill);
        foreach (var b in _cloudBumps) {
            float   s    = Mathf.Sin(_cloudTime * animBase * b.Freq + b.Phase);
            Vector2 anim = new Vector2(Mathf.Cos(b.OutAngle), Mathf.Sin(b.OutAngle)) * s * animAmp;
            DrawCircle(b.Center + offset + anim, b.Radius * (1f + s * growAmp), fill);
        }

        if (_cloudValleys != null && outlineWidth > 0f) {
            const int ellipseSteps = 16;
            foreach (var v in _cloudValleys) {
                float   s      = Mathf.Sin(_cloudTime * animBase * v.Freq + v.Phase);
                float   grow   = 1f + s * growAmp;
                Vector2 vanim  = new Vector2(Mathf.Cos(v.OutAngle), Mathf.Sin(v.OutAngle)) * s * animAmp;
                Vector2 c      = v.Center + offset + vanim;
                Vector2 axOut  = new Vector2(Mathf.Cos(v.OutAngle),  Mathf.Sin(v.OutAngle));
                Vector2 axAlong= new Vector2(-Mathf.Sin(v.OutAngle), Mathf.Cos(v.OutAngle));
                float   rA     = v.RadiusAlong * grow;
                float   rO     = v.RadiusOut   * grow;
                var pts = new Vector2[ellipseSteps];
                for (int i = 0; i < ellipseSteps; i++) {
                    float a = i * Mathf.Tau / ellipseSteps;
                    pts[i]  = c + axAlong * Mathf.Cos(a) * rA + axOut * Mathf.Sin(a) * rO;
                }
                DrawPolygon(pts, new[] { fill });
                const int arcSteps = ellipseSteps / 2 + 1;
                var arc = new Vector2[arcSteps];
                for (int i = 0; i < arcSteps; i++) {
                    float a = i * Mathf.Pi / (arcSteps - 1);
                    arc[i]  = c + axAlong * Mathf.Cos(a) * rA + axOut * Mathf.Sin(a) * rO;
                }
                DrawPolyline(arc, Colors.Black, 1.8f, false);
            }
        }
    }

    // ── InitDialogBox ─────────────────────────────────────────────────────────────

    private Vector2 anchorAvg = Vector2.Zero;
    public void InitDialogBox(Vector2 pos) {
        dbText.Modulate   = Colors.White;
        dbText.AddThemeColorOverride("default_color", new Color(0.08f, 0.08f, 0.08f));
        dbTimer.WaitTime  = 1f / textSpeed;

        // DialogBox lives in a CanvasLayer (screen space). Convert world-space tailPos
        // to screen pixels so all subsequent placement math is in the same coordinate space.
        if (tailPos != Vector2.Zero)
            tailPos = WorldToScreen(tailPos);

        if (phrase == null) phrase = "error: phrase missing";
        phrase = phrase.Trim();
        _strippedPhraseLength = GetPhraseWithoutBbcode().Length;

        dbText.Clear();
        dbText.AppendText(phrase);
        dbText.VisibleCharacters = GetPhrase().Length;

        // For narration, fill the full cel border width minus margins so long captions
        // span the readable area edge-to-edge. Other dialog types respect maxWidth.
        int effectiveMaxWidth = maxWidth;
        if ((dialogType == Globals.DialogTypes.narration || dialogType == Globals.DialogTypes.choice) && parentScene?.mainScene != null)
        {
            Rect2 inner = parentScene.mainScene.CelBorderInnerRect;
            effectiveMaxWidth = Mathf.Max(100, (int)(inner.Size.X - (marginSize + 16f) * 2f));
        }

        // Shrink to natural single-line width; wrap only if it exceeds effectiveMaxWidth.
        dbText.CustomMinimumSize = Vector2.Zero;
        dbText.AutowrapMode = TextServer.AutowrapMode.Off;
        dbText.Size = new Vector2(9999f, 9999f);
        Vector2 naturalSize = dbText.GetMinimumSize();
        if (naturalSize.X > effectiveMaxWidth) {
            dbText.AutowrapMode = TextServer.AutowrapMode.Word;
            dbText.Size = new Vector2(effectiveMaxWidth, 9999f);
            dbText.Size = new Vector2(effectiveMaxWidth, dbText.GetContentHeight());
        } else {
            dbText.Size = naturalSize;
        }

        // All NinePatchRect nodes are hidden — geometry is drawn dynamically.
        dbSpeechBubble.Hide();
        dbNarrationBubble.Hide();
        dbThoughtBubble.Hide();
        dbExclaimBubble.Hide();
        dbSpeechBubbleTail.Polygon = Array.Empty<Vector2>();

        // Compute bubble geometry; this also sets dbText.Position for speaking/exclaim.
        // For narration/thought we position text first, then compute geometry.
        if (dialogType == Globals.DialogTypes.speaking) {
            ComputeSpeechBubble();
            drawRect = _bubbleRect;
        } else if (dialogType == Globals.DialogTypes.exclaim) {
            ComputeExclaimBubble();
            drawRect = _bubbleRect;
        } else if (dialogType == Globals.DialogTypes.choice) {
            dbText.Position = new Vector2(marginSize, marginSize);
            MeasureChoiceContent();
            drawRect = new Rect2(0f, 0f,
                dbText.Size.X + marginSize * 2f,
                dbText.Size.Y + marginSize * 2f);
        } else {
            dbText.Position = new Vector2(marginSize, marginSize);
            drawRect = new Rect2(0f, 0f,
                dbText.Size.X + marginSize * 2f,
                dbText.Size.Y + marginSize * 2f);
        }

        if (pos == Vector2.Zero) {
            if ((dialogType == Globals.DialogTypes.narration || dialogType == Globals.DialogTypes.choice) &&
                narrationCorner != Globals.NarrationCorner.Auto)
                pos = GetSpotForNarrationCorner(drawRect);
            else
                pos = GetSpotForDialog(drawRect);
        }

        this.Position = pos;  // _placement already set by GetSpotForDialog above
        dbText.VisibleCharacters = 0;

        if (dialogType == Globals.DialogTypes.choice) {
            dialogChoices.Show();
            dbText.VisibleCharacters = GetPhrase().Length;
            dbText.AddThemeColorOverride("default_color", Colors.Transparent);
        } else {
            dialogChoices.Hide();
        }

        _actorScreenTailPos = tailPos;

        if (dialogType == Globals.DialogTypes.speaking) {
            float   bcx    = _bubbleRect.Size.X / 2f;
            float   bcy    = _bubbleRect.Size.Y / 2f;
            float   spread = _bubbleRect.Size.X / 16f;
            const float tuck = 8f;
            bool sidePlacement = _placement == PlacementSide.Left || _placement == PlacementSide.Right;
            Vector2 anchor = _placement switch {
                PlacementSide.Below => new Vector2(bcx, _bubbleRect.Position.Y + tuck),
                PlacementSide.Left  => new Vector2(_bubbleRect.End.X - tuck, bcy),
                PlacementSide.Right => new Vector2(_bubbleRect.Position.X + tuck, bcy),
                _                   => new Vector2(bcx, _bubbleRect.End.Y - tuck),
            };

            tailPos   = (tailPos - Position) * 0.75f + anchor * 0.25f;
            anchorAvg = anchor;

            tailPolygon = sidePlacement
                ? new Vector2[] { anchor + new Vector2(0f, -spread), tailPos, anchor + new Vector2(0f, spread) }
                : new Vector2[] { anchor + new Vector2(-spread, 0f), tailPos, anchor + new Vector2(spread, 0f) };
            ComputeTailBezier();

        } else if (dialogType == Globals.DialogTypes.exclaim) {
            Vector2 center     = _exclaimCenter;
            Vector2 actorLocal = tailPos - Position;
            Vector2 toActor    = actorLocal - center;
            Vector2 dir        = toActor.Length() > 0.1f ? toActor.Normalized() : Vector2.Down;
            float   actorAngle = Mathf.Atan2(dir.Y, dir.X);

            // Tail base tucked inside the starburst (85% of valley radius).
            Vector2 perp       = new Vector2(-dir.Y, dir.X);
            Vector2 baseCenter = center + new Vector2(
                Mathf.Cos(actorAngle) * _exclaimRxValley * 0.85f,
                Mathf.Sin(actorAngle) * _exclaimRyValley * 0.85f);
            float   spread     = Mathf.Max(_exclaimRxValley, _exclaimRyValley) * 0.20f;
            Vector2 blendedTip = actorLocal * 0.75f + baseCenter * 0.25f;

            tailPolygon = new Vector2[] {
                baseCenter - perp * spread,
                blendedTip,
                baseCenter + perp * spread,
            };
            ComputeTailBezier();

            anchorAvg    = center;
            _tailIsBelow = dir.Y > 0f;

        } else if (dialogType == Globals.DialogTypes.thinking) {
            ComputeCloudBubble();
            // Anchor on whichever cloud face is nearest to the character, matching speech bubble logic.
            float ccx = _cloudInnerRect.GetCenter().X;
            float ccy = _cloudInnerRect.GetCenter().Y;
            anchorAvg = _placement switch {
                PlacementSide.Left  => new Vector2(_cloudInnerRect.End.X      + CloudBumpRadius * 0.6f, ccy),
                PlacementSide.Right => new Vector2(_cloudInnerRect.Position.X - CloudBumpRadius * 0.6f, ccy),
                PlacementSide.Below => new Vector2(ccx, _cloudInnerRect.Position.Y - CloudBumpRadius * 0.6f),
                _                   => new Vector2(ccx, _cloudInnerRect.End.Y  + CloudBumpRadius * 0.6f),
            };
            tailPos = (tailPos - Position) * 0.75f + anchorAvg * 0.25f;
            ComputeThoughtDots();

        } else if (dialogType == Globals.DialogTypes.narration || dialogType == Globals.DialogTypes.choice) {
            ComputeNarrationBubble();
        }
    }
}
