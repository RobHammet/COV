using Godot;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

public partial class DialogBox : Control
{

    [Signal] public delegate void DialogClosedEventHandler();
    
    [Export(PropertyHint.Range, "1,60,1")] public float textSpeed = 35f;
    
    [Export] public Color dialogColor;

    [Export] public int maxWidth = 250;
    [Export] public float marginSize = 24;

    public scene_script parentScene;
    public NPC trackActor;
    public Vector2 tailPos = new Vector2(0,0);
    private Vector2 _actorWorldTailPos;

    public float offsetForFacing = 0f; //place dialog a little left for left facing, or right for right facing character

    public Globals.DialogTypes dialogType = Globals.DialogTypes.narration;
    public bool isStrict = false;

    private RichTextLabel dbText;
    private Timer dbTimer;
    private AudioStreamPlayer audioStreamPlayer;
    private NinePatchRect dbSpeechBubble;
    private NinePatchRect dbNarrationBubble;
    private NinePatchRect dbThoughtBubble;
    private Polygon2D dbSpeechBubbleTail;
    private Node2D dbNode2D;

    public DialogChoices dialogChoices;

    private Rect2 drawRect;

    private string phrase;
    private int charsAlreadyDisplayed = 0;


    public string StripBbcode(string input)
    {        
       string pattern = @"\[[^\]]+\]";
       return Regex.Replace(input, pattern, "");
    }
    public void SetPhrase(string _phrase) {
        this.phrase = _phrase;
    }

    public string GetPhrase() {
        return phrase;
    }

    public string GetPhraseWithoutBbcode() {
        return StripBbcode(phrase);
    }

    public string GetCurrentCharacter() {
        return phrase[dbText.VisibleCharacters-1].ToString();
    }

    // public bool IsCurrentCharInBbcode(int currentNum) {
    //     bool[] isCharBbcode = new bool[GetPhrase().Length];
    //     bool isCurrentlyBbcode = false;
    //     for (int i = 0; i < GetPhrase().Length; i++) {
    //         isCharBbcode[i] = false;

    //         if (isCurrentlyBbcode)
    //             isCharBbcode[i] = true;
    //         if (GetPhrase()[i] == '[') {
    //             isCharBbcode[i] = true;
    //             isCurrentlyBbcode = true;
    //         }
    //         if (GetPhrase()[i] == ']') {
    //             isCharBbcode[i] = true;
    //             isCurrentlyBbcode = false;
    //         }
    //     }
        
    //     return isCharBbcode[currentNum];
    // }

    public override void _EnterTree() {

      //  GD.Print ("ENTERED");
    }

    public override void _Ready()
    {
        // set references to nodes
       // dbNode2D = this.GetNode<Node2D>("DB_Node2D");
        dbText = GetNode<RichTextLabel>("DB_Text");
        dbSpeechBubble = dbText.GetNode<NinePatchRect>("DB_SpeechBubble");
        dbNarrationBubble = dbText.GetNode<NinePatchRect>("DB_NarrationBubble");
        dbThoughtBubble = dbText.GetNode<NinePatchRect>("DB_ThoughtBubble");
        dbSpeechBubbleTail = dbSpeechBubble.GetNode<Polygon2D>("DB_SpeechBubbleTail");
        dbTimer = GetNode<Timer>("DB_TextTimer");
        audioStreamPlayer = GetNode<AudioStreamPlayer>("DB_AudioStreamPlayer");
        dialogChoices = dbText.GetNode<DialogChoices>("DialogChoices");


    }

    public override void _Process(double delta)
    {
        ManageAudio();

        if (trackActor != null)
        {
            Vector2 newTailPos = trackActor.topPoint;
            Vector2 actorDelta = newTailPos - _actorWorldTailPos;
            if (actorDelta != Vector2.Zero)
            {
                Position += actorDelta;
                _actorWorldTailPos = newTailPos;
                QueueRedraw();
            }
        }
    }


    public void DealWithClick() {
        if (dialogType == Globals.DialogTypes.choice) {
            if (dialogChoices.currentChoice >= 0)
                CloseThisDialog(dialogChoices.currentChoice);
            return;
        }

        if (dbText.VisibleCharacters >= GetPhrase().Length) {
            CloseThisDialog();
        } else if (!isStrict) {
            dbText.VisibleCharacters = GetPhrase().Length;
        }
    }

    public override void _Input (InputEvent @event) {

        if (@event is InputEventMouseButton mouseEvent) {
            if (mouseEvent.ButtonIndex is Godot.MouseButton.Left && mouseEvent.Pressed)
            DealWithClick();
            GetViewport().SetInputAsHandled(); 
        }

        // if (@event is InputEventScreenTouch touchEvent) {
        //     if (touchEvent.Pressed) {
        //         DealWithClick();
        //     }
        // }
        
    }
    public override void _Draw() {
        if(this.dialogType == Globals.DialogTypes.speaking) {
            DrawTail();            
        } else if (this.dialogType == Globals.DialogTypes.thinking || this.dialogType == Globals.DialogTypes.choice ) {
            DrawThoughtTrail();
        }
       // DrawRect(drawRect, Colors.Red, false, 4);
    }

    public Vector2[] tailPolygon;
    public void DrawTail(){ 
        // // outline needs adjustment to stay with bubble
        Vector2[] newPolygon = new Vector2[3];
        newPolygon[0] = tailPolygon[0];// + dbSpeechBubble.GlobalPosition;
        newPolygon[1] = tailPolygon[1];// + dbSpeechBubble.GlobalPosition;
        newPolygon[2] = tailPolygon[2];// + dbSpeechBubble.GlobalPosition;
        this.DrawPolyline(newPolygon, Colors.Black, 4, false);
        
    }


    public void DrawThoughtTrail() {

        Sprite2D trailSprite = this.GetNode<Sprite2D>("ThoughtBubbleTrail");
        // trailSprite.DrawSetTransform(Vector2.Zero,0f,new Vector2(0.25f,0.25f));
        // trailSprite.Scale = new Vector2(0.25f,0.25f);
        Texture2D trailTexture = trailSprite.Texture;
        Image trailImage = trailTexture.GetImage();

        
        int numberOfIterations = 12;

        for (int i = 1; i <= numberOfIterations; i+=3) {

            RandomNumberGenerator rng = new RandomNumberGenerator();
            rng.Randomize();
            float r = (rng.Randf() * i/2);
            r -= (i/4);
            // int ri = (int)r;     

            Vector2 trailpos = (tailPos * (numberOfIterations - i) + anchorAvg * i) / numberOfIterations;

            trailpos += new Vector2(r, 0f);


            Vector2 trailsize = trailImage.GetSize() / (numberOfIterations - i);
            if (i >= numberOfIterations - 4) {
                trailsize /= 1.5f;
                trailpos = (anchorAvg * 2 + trailpos) / 3;
            }
            this.DrawTextureRectRegion(trailTexture, new Rect2(trailpos - trailsize/2, trailsize), new Rect2(Vector2.Zero, trailImage.GetSize()));       
        }




        // Vector2 trail1 = (tailPos * 2 + anchorAvg) / 3;
        // Vector2 trail1size = trailImage.GetSize() / 8;


        // Vector2 trail2 = (anchorAvg * 2 + tailPos) / 3;
        // Vector2 trail2size = trailImage.GetSize() / 4;


        // this.DrawCircle(trail1, 5, Colors.Blue);
        // this.DrawCircle(trail2, 10, Colors.Blue);        
        
        // this.DrawTextureRectRegion(trailTexture, new Rect2(trail1 - trail1size/2, trail1size), new Rect2(Vector2.Zero, trailImage.GetSize()));       
        // this.DrawTextureRectRegion(trailTexture, new Rect2(trail2 - trail2size/2, trail2size), new Rect2(Vector2.Zero, trailImage.GetSize()));     

        // Color[] clrs = new Color[]{Colors.Salmon};
        // this.DrawPolygon(tailPolygon, clrs);

        // dbThoughtBubble.Visible = false;
        // this.DrawTexture(trailTexture, trail1);
        // this.DrawTexture(trailTexture, trail2);

    }
    public void _on_DB_TextTimer_timeout() {
      //  GD.Print(GetPhrase().Length);
      //  GD.Print(dbText.VisibleCharacters + " / " + GetPhrase().Length);
      
        if (dbText.VisibleCharacters < GetPhraseWithoutBbcode().Length) {            
            dbText.VisibleCharacters++;   
        } else {
            dbText.VisibleCharacters = GetPhrase().Length;
        }
        
    }

    private int characterAudioPlayed = 0;
    public void ManageAudio() {
        //dont play sound if this is a choice
        if (dialogType == Globals.DialogTypes.choice)
            return;


        if (characterAudioPlayed < dbText.VisibleCharacters) {
            characterAudioPlayed = dbText.VisibleCharacters;

            audioStreamPlayer.Stop();

            // GD.Print(GetCurrentCharacter()+ " ( "+dbText.VisibleCharacters+" / "+GetPhrase().Length+" )");

            if (GetCurrentCharacter() != " ") {      
                
            
				// RandomNumberGenerator rng = new RandomNumberGenerator();
				// rng.Randomize();
				// float r = rng.Randf() * 4;
				// //int ri = (int)r;     
                // float newPitchScale = 0.76f + r/100;
                // audioStreamPlayer.PitchScale = newPitchScale;
                // audioStreamPlayer.Seek(0.21f);


                audioStreamPlayer.Play();
            } 

        } else {
        //     audioStreamPlayer.Playing = false;
        //     audioStreamPlayer.Stop();
        //   //  audioStreamPlayer.QueueFree();
        }
    }

    public void CloseThisDialog(int choice = -1) {
       
        this.QueueFree();
        // GetViewport().SetInputAsHandled(); // not sure if i should be using this
        if (choice == -1) {
            this.EmitSignal("DialogClosed");     
        } else {
            GD.Print("Closedialog with " + choice);
            this.EmitSignal("DialogClosed", choice);     
        }
    }


    public Vector2 GetSpotForDialog(Rect2 rect) {
        
        // try {
        // ColorRect black = parentScene.mainScene.currentSceneHolder.GetNode<ColorRect>("Black"); //parentScene.GetParent().GetNode<ColorRect>("Black");
        Vector2 cam = new Vector2( GetViewport().GetCamera2D().Position.X - GetViewport().GetVisibleRect().End.X / 2,
                                      GetViewport().GetCamera2D().Position.Y - GetViewport().GetVisibleRect().End.Y / 2);
        // float vpVisX = GetViewport().GetVisibleRect().End.X;
        // GD.Print("camX: " + camX);
        // GD.Print("vpVisX: " + vpVisX);

        // Sprite2D bg = parentScene.GetNode<Sprite2D>("background");
        // GD.Print("bgwidth: " + bg.GetRect().End.X);
        // GD.Print("parentscenewidth: " + parentScene.GetViewportRect().End.X);
        // GD.Print("size:" +rect.Size);
        RandomNumberGenerator rng = new RandomNumberGenerator();
        rng.Randomize();
        float x = 0, y = 0;
        int xi = 0;
        int yi = 0;
        // GD.Print("fromPos: " + tailPos);

        // x = bg.GetRect().End.X - rect.Size.X - marginSize*2 ; 
        // y = bg.GetRect().End.Y - rect.Size.Y - marginSize*2 ; 

        // Inner cel boundary in screen space → convert to world space via cam offset.
        Rect2 inner = parentScene.mainScene.CelBorderInnerRect;
        float minX = inner.Position.X + cam.X;
        float minY = inner.Position.Y + cam.Y;
        float maxX = inner.End.X      + cam.X - rect.Size.X - marginSize * 2;
        float maxY = inner.End.Y      + cam.Y - rect.Size.Y - marginSize * 2;

        //try to find spot above tailpos
        if (tailPos.Y > rect.Size.Y + marginSize*4) {
            y = tailPos.Y - marginSize*8;
        } else {
            y = tailPos.Y + marginSize*8;
        }

        x = tailPos.X - (rect.Size.X / 2);

        // add offset for facing
        x += this.offsetForFacing;

        x = (float)Mathf.Clamp((int)x, (int)minX, (int)maxX);
        y = (float)Mathf.Clamp((int)y, (int)minY, (int)maxY);


        //clamp to cameraClamp
        // Vector2 topLeft = parentScene.cameraClamp.Position - parentScene.cameraClamp.Shape.GetRect().Size/2;
        // Vector2 bottomRight = parentScene.cameraClamp.Position + parentScene.cameraClamp.Shape.GetRect().Size/2 ;        
        // topLeft += GetViewportRect().Size / 2;
        // bottomRight -= GetViewportRect().Size / 2;            
        // topLeft -= this.Position;
        // bottomRight -= this.Position;
        // x = (float)Mathf.Clamp((int)x,(int)topLeft.X,(int)bottomRight.X);
        // y = (float)Mathf.Clamp((int)x,(int)topLeft.Y,(int)bottomRight.Y);
        


      //  y = GetViewportRect().End.Y - rect.Size.Y - marginSize*2 ; 


        // if (fromPos.X > parentScene.GetViewportRect().End.X / 2) {
        //     //put in right side
        //     GD.Print("put in right side");
        //     x = rng.Randf() * ( (parentScene.GetViewportRect().End.X - parentScene.GetViewportRect().Position.X) / 2 + parentScene.GetViewportRect().End.X/2 - rect.Size.X - marginSize*2 ) ; 
        // } else {
        //     //put in left side
        //     GD.Print("put in left side");
        //     x = rng.Randf() * ( (parentScene.GetViewportRect().End.X - parentScene.GetViewportRect().Position.X) / 2 - rect.Size.X - marginSize*2 ) ; 
        // }
        // if (fromPos.Y > parentScene.GetViewportRect().End.Y / 2) {
        //     // put in top
        //     GD.Print("put in top");
        //     y = rng.Randf() * ( (parentScene.GetViewportRect().End.Y - parentScene.GetViewportRect().Position.Y) / 2 - rect.Size.Y - marginSize*2) ; 
        // } else {
        //     // put in bottom
        //     GD.Print("put in bottom");
        //     y = rng.Randf() * ( (parentScene.GetViewportRect().End.Y - parentScene.GetViewportRect().Position.Y) / 2 + parentScene.GetViewportRect().End.Y/2 - rect.Size.Y - marginSize*2) ; 
        // }
        
        xi = (int)x + (int)parentScene.GetViewportRect().Position.X;
        
        yi = (int)y + (int)parentScene.GetViewportRect().Position.Y;
        

        return new Vector2(x,y);
        // } catch {
        //     GD.Print("No black");
        //     return new Vector2(50,50);
        // }
    }

    private Vector2 anchorAvg = Vector2.Zero;
    public void InitDialogBox(Vector2 pos) {


        
       
        // set the dialog properties
        dbText.Modulate = this.dialogColor;
        dbTimer.WaitTime = 1f / textSpeed;

        if (phrase == null)
            phrase =  "error: phrase missing";
        
        phrase = phrase.Trim();

        GD.Print("The phrase is: " + phrase);
        GD.Print("The tail position is: " + tailPos);


		dbText.Clear();
		dbText.AppendText(phrase);
        dbText.VisibleCharacters = GetPhrase().Length;
		dbText.Size = dbText.GetMinimumSize();
		
		
		
		dbSpeechBubble.Size = new Vector2(dbText.Size.X + marginSize*2,
										 dbText.Size.Y + marginSize*2);
        dbSpeechBubble.Position -= new Vector2 (marginSize, marginSize);
		dbText.Position = new Vector2(marginSize,marginSize);


		drawRect = new Rect2(dbText.Position.X,dbText.Position.Y,
							 dbText.Size.X,dbText.Size.Y);



        if (pos == Vector2.Zero) {
            pos = GetSpotForDialog(drawRect);
        }

        bool isBelow  = pos.Y < tailPos.Y ? false : true;


        this.Position = pos;


      //  dbText.AutowrapMode = TextServer.AutowrapMode.Off;

        dbText.VisibleCharacters = 0;


        // Vector2 maxRectSize = dbText.GetThemeFont("normal_font").GetMultilineStringSize(dbText.Text);
        // Vector2 oneLineOfText = dbText.GetThemeFont("normal_font").GetMultilineStringSize(dbText.Text);
        // if ((oneLineOfText.X < maxWidth) && (maxRectSize.Y == oneLineOfText.Y))
        //     dbText.Size = oneLineOfText + new Vector2(1,0);
        // else
        //     dbText.Size = maxRectSize;

        // resize the speech bubble
        // dbSpeechBubble.Size = new Vector2(dbText.Size.X + marginSize*2, dbText.Size.Y + marginSize*2);
        // dbSpeechBubble.Position = new Vector2(dbSpeechBubble.Position.X - marginSize, dbSpeechBubble.Position.Y - marginSize);
        // dbText.Position = new Vector2(dbText.Position.X + marginSize, dbText.Position.Y + marginSize);

        // narration match size and pos of speech bubble

        dbNarrationBubble.Size = dbSpeechBubble.Size;
        dbNarrationBubble.Position = dbSpeechBubble.Position;

        dbThoughtBubble.Size = dbSpeechBubble.Size;
        dbThoughtBubble.Position = dbSpeechBubble.Position;

        // hide the one not using
        if (this.dialogType == Globals.DialogTypes.narration) {
            GD.Print("NARRATE");
            dbSpeechBubble.Hide();
            dbThoughtBubble.Hide();
            dbNarrationBubble.Show();
            dialogChoices.Hide();
        } else if (this.dialogType == Globals.DialogTypes.speaking) {
            GD.Print("SPEAK");
            dbNarrationBubble.Hide();
            dbThoughtBubble.Hide();
            dbSpeechBubble.Show();
            dialogChoices.Hide();
        } else if (this.dialogType == Globals.DialogTypes.thinking) {
            GD.Print("THINK");
            dbNarrationBubble.Hide();
            dbSpeechBubble.Hide();
            dbThoughtBubble.Show();
            dialogChoices.Hide();

        } else if (this.dialogType == Globals.DialogTypes.choice) {
            GD.Print("CHOICE");
            dbNarrationBubble.Hide();
            dbSpeechBubble.Hide();
            dbThoughtBubble.Show();
            dialogChoices.Show();
            dbText.VisibleCharacters = GetPhrase().Length;
			dbText.AddThemeColorOverride("default_color", Colors.Transparent);
        }
       
        // align bubble tail
        dbSpeechBubbleTail.Position = new Vector2(0,0);

        float oneSixteenthOfBase = (dbSpeechBubble.Size.X/16);
        Vector2 baseCenterPoint, firstAnchorPoint, secondAnchorPoint;
        
        baseCenterPoint = new Vector2((dbSpeechBubble.Size.X - dbSpeechBubble.Position.X)/2, dbSpeechBubble.Size.Y);

        if (isBelow) {
            firstAnchorPoint = new Vector2(baseCenterPoint.X - oneSixteenthOfBase, marginSize);        
            secondAnchorPoint = new Vector2(baseCenterPoint.X + oneSixteenthOfBase, marginSize);
        } else {
            firstAnchorPoint = new Vector2(baseCenterPoint.X - oneSixteenthOfBase, dbSpeechBubble.Size.Y);        
            secondAnchorPoint = new Vector2(baseCenterPoint.X + oneSixteenthOfBase, dbSpeechBubble.Size.Y);
        }
        

        // Store world-space tail position before converting to dialog-relative,
        // so _Process can track how far the actor has moved.
        _actorWorldTailPos = tailPos;

        //adjust tailpos to minus this dialog box's pos
        this.tailPos = this.tailPos - this.Position;

        // make tailpos farther away
        this.tailPos = (this.tailPos * 3 + baseCenterPoint) / 4;

        // make tail origin halfway point between origin and anchorpoints
        anchorAvg = new Vector2((firstAnchorPoint.X + secondAnchorPoint.X)/2, (firstAnchorPoint.Y + secondAnchorPoint.Y)/2); 
        // Vector2 newTail = new Vector2((anchorAvg.X + this.tailPos.X)/2, (anchorAvg.Y + this.tailPos.Y)/2); 
        // tailPos = newTail;


        Vector2[] newPolygon = new Vector2[3];
        newPolygon[0] = firstAnchorPoint;
        newPolygon[1] = this.tailPos;  // - this.Position          // + dbSpeechBubbleTail.Position; // - this.GlobalPosition;
        newPolygon[2] = secondAnchorPoint;

        //move anchor points up a bit so line continues through
       newPolygon[0] = new Vector2(newPolygon[0].X, newPolygon[0].Y - 5);
       newPolygon[2] = new Vector2(newPolygon[2].X, newPolygon[2].Y - 5);

        //shift to match bubble location
      //  newPolygon[1] -= dbText.Position;

        dbSpeechBubbleTail.Polygon = newPolygon;

        // pass to local var (outline to be drawn in drawn func)



        tailPolygon = newPolygon;      
    
      
    }

    
}
