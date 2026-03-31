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
    }

    // Set to true in the editor or at startup to show debug overlays.
    public static bool showDebugTools = false;

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
