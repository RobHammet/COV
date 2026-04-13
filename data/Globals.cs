using Godot;

// ---------------------------------------------------------------------------
// Globals — autoloaded singleton. Shared enums and debug flag.
// Game-specific types (Scenes, InventoryItem) live in game/GameData.cs.
// ---------------------------------------------------------------------------
public partial class Globals : Node
{
    // How the player interacts with the world.
    public enum InteractModes
    {
        walk = 0,
        look = 1,
        talk = 2,
        use  = 3,
        item = 4,
        wait = 5,
    }

    // Input scheme: verb tray (desktop) or verb coin (touch / mobile).
    public enum InputModes
    {
        verbtray = 0,
        verbcoin = 1,
    }

    // Dialog bubble style.
    public enum DialogTypes
    {
        narration = 0,
        speaking  = 1,
        thinking  = 2,
        choice    = 3,
        exclaim   = 4,
    }

    // Corner anchor for narration boxes.
    public enum NarrationCorner
    {
        Auto        = 0,
        TopLeft     = 1,
        TopRight    = 2,
        BottomLeft  = 3,
        BottomRight = 4,
    }

    public enum NarrationStyle
    {
        Normal = 0,   // subdued corner bends only, no edge notches
        Jagged = 1,   // dense randomised notches on left and right edges only
    }

    // Set to true in the editor or at startup to enable all debug features.
    public static bool showDebugTools    = false;
    // Sub-toggles (only meaningful when showDebugTools is true):
    public static bool showDebugGraphics = false;   // F2 — polygons/gizmos on things & NPCs
    public static bool showDebugPanel    = false;   // F3 — info panel (pos/events/flags/inventory)

    // One saved flag per scene: tracks puzzle state across scene changes.
    // Saved as JSON by MainScene.Save() / Load().
    public class SceneFlag
    {
        public string         sceneName;
        public string         Name;
        public Godot.Variant  Value;

        public SceneFlag(string scene, string name, Godot.Variant value)
        {
            sceneName = scene ?? "";
            Name      = name;
            Value     = value;
        }
    }
}
