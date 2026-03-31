// Character.cs — the player character. Extends NPC with footstep audio.
//
// SpecificTalk() is called by scene_script when the player talks to themselves.
// _Process() drives the footstep audio system: plays a random seek position
// from the audio stream on walk-cycle frames 2 and 6 (mod 8), then stops
// the clip after 0.25 s.

using Godot;

public partial class Character : NPC
{
    public AudioStreamPlayer audioStreamPlayer;
    public thing             interactThing;

    public override void _Ready()
    {
        base._Ready();
        audioStreamPlayer = GetNode<AudioStreamPlayer>("AudioStreamPlayer");
        interactThing     = null;
    }

    public override bool SpecificTalk(Character character, EventSequence eventSequence)
    {
        eventSequence.AddEventSpeak(this, "TALKING TO ONESELF IS THE FIRST SIGN OF INSANITY...", new Vector2(0, 0));
        return true;
    }

    // --- Footstep audio ----------------------------------------------------

    private int   currentFrame       = 0;
    private float playingStepFrom    = 0f;
    private float playingStepLength  = 0f;
    private bool  playFootsteps      = false;

    public override void _Process(double delta)
    {
        if (playFootsteps)
        {
            if (sprite.Frame != currentFrame)
            {
                currentFrame = sprite.Frame;
                if (currentFrame % 8 == 2 || currentFrame % 8 == 6)
                {
                    var rng = new RandomNumberGenerator();
                    rng.Randomize();
                    float rf = (int)(rng.Randf() * 4) * 0.55f;
                    audioStreamPlayer.Play();
                    audioStreamPlayer.Seek(rf);
                    playingStepFrom   = rf;
                    playingStepLength = rf == 0 ? 0.001f : rf;
                }
            }

            if (playingStepLength != 0)
            {
                playingStepLength = audioStreamPlayer.GetPlaybackPosition();
                if (playingStepLength >= playingStepFrom + 0.25f)
                {
                    audioStreamPlayer.Stop();
                    playingStepLength = 0;
                }
            }
        }

        base._Process(delta);
    }
}
