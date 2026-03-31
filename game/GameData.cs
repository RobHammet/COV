// GameData.cs — game-specific constants and types for COV.
//
// Replace this file (along with /game/ content) to target a different game.
// The framework (framework/, ui/, shaders/, fonts/) needs no changes.

using System.Runtime.CompilerServices;

// Runs automatically at assembly load, before any Godot _Ready() calls.
static class GameInteractionDefaults
{
    [ModuleInitializer]
    internal static void Init()
    {
        thing.TalkFallbacks    = ["I CAN'T TALK TO THAT.", "IT DOESN'T SAY MUCH.", "I MIGHT BE LOSING IT."];
        thing.UseFallbacks     = ["I CAN'T DO ANYTHING WITH THAT.", "IT'S USELESS.", "NOT SURE WHAT I'D DO THAT FOR."];
        thing.UseItemFallbacks = ["I CAN'T USE THESE THINGS TOGETHER.", "THAT DOESN'T MAKE SENSE.", "I DON'T THINK THAT WILL WORK."];
    }
}

// ---------------------------------------------------------------------------
// Scenes — all room scene paths as constants.
// Use these in ChangeSceneToFile() calls:
//   mainScene.ChangeSceneToFile(Scenes.KITCHEN);
// ---------------------------------------------------------------------------
public static class Scenes
{
    public const string THEROAD      = "res://game/scenes/theroad/theroad.tscn";
    public const string KITCHEN      = "res://game/scenes/kitchen/kitchen.tscn";
    public const string SITTINGROOM  = "res://game/scenes/sittingroom/sittingroom.tscn";
    public const string ENTRYWAY     = "res://game/scenes/entryway/entryway.tscn";
    public const string THEPORCH     = "res://game/scenes/theporch/theporch.tscn";
    public const string HOUSEFRONT   = "res://game/scenes/housefront/housefront.tscn";
    public const string BARNFRONT    = "res://game/scenes/barnfront/barnfront.tscn";
    public const string BARNINTERIOR = "res://game/scenes/barninterior/barninterior.tscn";
    public const string UPSTAIRS     = "res://game/scenes/upstairs/upstairs.tscn";
    public const string TREE         = "res://game/scenes/tree/tree.tscn";
    public const string BASEMENT     = "res://game/scenes/basement/basement.tscn";
}

// ---------------------------------------------------------------------------
// InventoryItem — wraps a single carried item.
// Add or remove ItemType values here to match the game's inventory.
// ItemType.none must remain at 0 — the framework uses it as a sentinel.
// ---------------------------------------------------------------------------
public partial class InventoryItem : Godot.Node
{
    public enum ItemType
    {
        none   = 0,
        key    = 1,
        candle = 2,
        bell   = 3,
        book   = 4,
        axe    = 5,
    }

    public ItemType Type { get; set; }

    public InventoryItem(ItemType type) { Type = type; }
}
