using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;


[GlobalClass]
public partial class DialogTree : Node2D
{
    [Signal] public delegate void DialogTreeClosedEventHandler();
    public scene_script parentScene;
    public List<DialogNode> nodes = new List<DialogNode>();

    [Export] public string dialogFile;

    private Globals.InteractModes prevInteractMode = Globals.InteractModes.walk;
    private JsonObject _dialogDoc;

    public override void _Ready()
    {
        this.parentScene = (scene_script)this.GetParent();

        nodes.Clear();
        foreach (var n in this.GetChildren()) {
            if (n is DialogNode) {
                nodes.Add((DialogNode)n);
            }
        }

        this.Connect("DialogTreeClosed", new Callable(parentScene, "_on_DialogTree_DialogTreeClosed"));

        if (!string.IsNullOrEmpty(dialogFile)) {
            string json = FileAccess.GetFileAsString($"res://{dialogFile}");
            _dialogDoc = JsonNode.Parse(json)?.AsObject();
        }
    }

    public override void _Process(double delta) { }

    public virtual async void DoDialogs()
    {
        prevInteractMode = this.parentScene.mainScene.GetInteractMode();
        this.parentScene.mainScene.SetInteractMode(Globals.InteractModes.walk);

        if (_dialogDoc != null)
            await DoDialogsFromFile();
        else
            await DoDialogsFromNodes();

        this.parentScene.mainScene.SetInteractMode(prevInteractMode);
        GD.Print("EMITTING SIGNAL DialogTreeClosed");
        this.EmitSignal("DialogTreeClosed");
    }

    // -------------------------------------------------------------------------
    // File-based dialog path
    // -------------------------------------------------------------------------

    // Returns true if the item's condition (if any) is satisfied.
    // A condition is the name of a scene flag that must be truthy.
    private bool ConditionMet(JsonObject obj)
    {
        if (!obj.ContainsKey("condition")) return true;
        return (bool)parentScene.GetFlag(obj["condition"].GetValue<string>()).Value;
    }

    private async Task DoDialogsFromFile()
    {
        var allNodes = _dialogDoc["nodes"]?.AsObject();
        string currentId = _dialogDoc["start"]?.GetValue<string>();

        // Initialise hidden state: scene flags take priority over JSON "hidden".
        var hiddenState = new Dictionary<string, bool>();
        foreach (var (id, nodeVal) in allNodes)
        {
            var flag = parentScene.GetFlag(id + "_hidden");
            // sceneName == null means the flag was never set; fall back to JSON.
            hiddenState[id] = flag.sceneName != null
                ? (bool)flag.Value
                : nodeVal?["hidden"]?.GetValue<bool>() ?? false;
        }

        while (currentId != null) {
            var node = allNodes[currentId]?.AsObject();
            if (node == null) break;

            // Default next node (may be overridden by a choice).
            string nextId = node["next"]?.GetValue<string>();

            // Skip this node entirely if its condition is not met.
            if (!ConditionMet(node)) {
                currentId = nextId;
                continue;
            }

            var content = node["content"]?.AsArray();
            if (content == null) {
                currentId = nextId;
                continue;
            }

            foreach (var item in content) {
                var obj = item?.AsObject();
                if (obj == null) continue;

                // Skip any content item whose condition is not met.
                if (!ConditionMet(obj)) continue;

                if (obj.ContainsKey("speaker")) {
                    // --- Dialog line ---
                    string speakerName = obj["speaker"].GetValue<string>();
                    string text        = obj["text"].GetValue<string>();
                    bool   isThought   = obj["thought"]?.GetValue<bool>() ?? false;

                    NPC npc = ResolveNPC(speakerName);
                    var dialogType = isThought ? Globals.DialogTypes.thinking : Globals.DialogTypes.speaking;
                    var topPoint   = isThought
                        ? new Vector2(parentScene.character.Position.X, parentScene.character.topPoint.Y)
                        : npc.topPoint;

                    NPC trackNpc = isThought ? parentScene.character : npc;
                    var db = parentScene.CreateDialog(dialogType, text, npc.dialogColor, null, topPoint, npc.Facing, actor: trackNpc);
                    await ToSignal(db, "DialogClosed");

                } else if (obj.ContainsKey("choices")) {
                    // --- Choice block ---
                    var choiceArray = obj["choices"].AsArray();
                    var visible = new List<(string id, string label)>();
                    foreach (var opt in choiceArray) {
                        string id = opt["id"].GetValue<string>();
                        // A choice is visible if not hidden AND its condition (if any) is met.
                        if (!hiddenState.GetValueOrDefault(id, false) && ConditionMet(opt.AsObject()))
                            visible.Add((id, opt["label"].GetValue<string>()));
                    }

                    string[] labels = visible.Select(c => c.label).ToArray();
                    var tp = new Vector2(parentScene.character.Position.X, parentScene.character.topPoint.Y);
                    var choiceDb = parentScene.CreateDialog(
                        Globals.DialogTypes.choice, null, null, null, tp, parentScene.character.Facing, labels,
                        actor: parentScene.character);
                    var returnVal = await ToSignal(choiceDb, "DialogClosed");

                    nextId = visible[(int)returnVal[0]].id;

                } else if (obj.ContainsKey("action")) {
                    // --- Action ---
                    switch (obj["action"].GetValue<string>()) {
                        case "set_flag": {
                            string name = obj["name"].GetValue<string>();
                            string val  = obj["value"]?.GetValue<string>() ?? "true";
                            if (val == "true" || val == "false")
                                parentScene.AddFlag(name, val == "true");
                            else
                                parentScene.AddFlag(name, val);
                            break;
                        }
                        case "hide": {
                            string choiceId = obj["choice"].GetValue<string>();
                            hiddenState[choiceId] = true;
                            parentScene.AddFlag(choiceId + "_hidden", true);
                            break;
                        }
                        case "show": {
                            string choiceId = obj["choice"].GetValue<string>();
                            hiddenState[choiceId] = false;
                            parentScene.AddFlag(choiceId + "_hidden", false);
                            break;
                        }
                        case "move": {
                            // Fire-and-forget: actor starts walking immediately while
                            // the dialog continues with the next line.
                            NPC actor = ResolveNPC(obj["actor"].GetValue<string>());
                            Vector2 dest;
                            if (obj.ContainsKey("target")) {
                                thing target = ResolveThing(obj["target"].GetValue<string>());
                                dest = target?.interactPoint ?? actor.Position;
                            } else {
                                var to = obj["to"].AsArray();
                                dest = new Vector2(to[0].GetValue<float>(), to[1].GetValue<float>());
                            }
                            actor?.GoToLocation(dest);
                            break;
                        }
                        case "speak": {
                            // Inline: awaited in sequence with the surrounding dialog lines.
                            NPC actor = ResolveNPC(obj["actor"].GetValue<string>());
                            if (actor != null) {
                                var db = parentScene.CreateDialog(
                                    Globals.DialogTypes.speaking, obj["text"].GetValue<string>(),
                                    actor.dialogColor, null, actor.topPoint, actor.Facing, actor: actor);
                                await ToSignal(db, "DialogClosed");
                            }
                            break;
                        }
                        case "think": {
                            // Inline: awaited in sequence with the surrounding dialog lines.
                            NPC actor   = ResolveNPC(obj["actor"].GetValue<string>());
                            if (actor != null) {
                                var topPoint = new Vector2(parentScene.character.Position.X, parentScene.character.topPoint.Y);
                                var db = parentScene.CreateDialog(
                                    Globals.DialogTypes.thinking, obj["text"].GetValue<string>(),
                                    actor.dialogColor, null, topPoint, actor.Facing, actor: parentScene.character);
                                await ToSignal(db, "DialogClosed");
                            }
                            break;
                        }
                        case "narrate": {
                            // Inline: awaited in sequence with the surrounding dialog lines.
                            var db = parentScene.CreateDialog(
                                Globals.DialogTypes.narration, obj["text"].GetValue<string>());
                            await ToSignal(db, "DialogClosed");
                            break;
                        }
                        case "wait": {
                            // Suspend input for a number of seconds, showing hourglass cursor.
                            float seconds = obj["seconds"]?.GetValue<float>() ?? 1f;
                            parentScene.mainScene.SetInteractMode(Globals.InteractModes.wait);
                            await ToSignal(GetTree().CreateTimer(seconds, true), "timeout");
                            parentScene.mainScene.SetInteractMode(Globals.InteractModes.walk);
                            break;
                        }
                        case "look_at": {
                            // Instant: actor turns to face the target immediately.
                            NPC actor   = ResolveNPC(obj["actor"].GetValue<string>());
                            thing target = ResolveThing(obj["target"].GetValue<string>());
                            if (actor != null && target != null)
                                actor.ChangeFacingToLookAt(target);
                            break;
                        }
                        case "play_animation": {
                            // Instant: animation starts and dialog continues immediately.
                            NPC actor = ResolveNPC(obj["actor"].GetValue<string>());
                            actor?.GetNodeOrNull<AnimationPlayer>("AnimationPlayer")?.Play(obj["animation"].GetValue<string>());
                            break;
                        }
                    }
                }
            }

            currentId = nextId;
        }
    }

    private thing ResolveThing(string name)
    {
        if (name == "character") return parentScene.character;
        return parentScene.FindChild(name, true, false) as thing;
    }

    private NPC ResolveNPC(string name)
    {
        if (name == "character") return parentScene.character;
        return parentScene.FindChild(name, true, false) as NPC;
    }

    // -------------------------------------------------------------------------
    // Node-tree dialog path (original behaviour, preserved unchanged)
    // -------------------------------------------------------------------------

    private async Task DoDialogsFromNodes()
    {
        DialogNode currentNode = nodes[0];
        bool exitDialogs = false;
        bool hasChosen   = false;

        while (!exitDialogs) {
            foreach (var c in currentNode.GetChildren()) {
                if (c is DialogLine) {
                    hasChosen = false;
                    DialogLine line = c as DialogLine;
                    Globals.DialogTypes dialogType = Globals.DialogTypes.speaking;
                    Vector2 topPoint = line.speakingCharacter.topPoint;
                    if (line.isThought) {
                        dialogType = Globals.DialogTypes.thinking;
                        topPoint   = new Vector2(parentScene.character.Position.X, parentScene.character.topPoint.Y);
                    }
                    DialogBox prompt = parentScene.CreateDialog(
                        dialogType, line.phrase, line.speakingCharacter.dialogColor,
                        null, topPoint, line.speakingCharacter.Facing);
                    await this.ToSignal(prompt, "DialogClosed");
                }
                if (c is DialogChoice) {
                    DialogChoice dialogChoice = c as DialogChoice;
                    var choiceDict = new Godot.Collections.Dictionary<int, DialogNode>();
                    var choiceList = new List<string>();
                    int j = 0;
                    for (int i = 0; i < dialogChoice.choices.Length; i++) {
                        if (!dialogChoice.choices[i].isHidden) {
                            choiceList.Add(dialogChoice.choices[i].displayAsChoice);
                            choiceDict.Add(j, dialogChoice.choices[i]);
                            j++;
                        }
                    }
                    Vector2 tp = new Vector2(parentScene.character.Position.X, parentScene.character.topPoint.Y);
                    DialogBox db = parentScene.CreateDialog(
                        Globals.DialogTypes.choice, null, null, null, tp,
                        parentScene.character.Facing, choiceList.ToArray());
                    Godot.Variant[] returnVal = await this.ToSignal(db, "DialogClosed");
                    GD.Print("returnVal: " + returnVal[0]);

                    DialogNode chosenNode = null;
                    choiceDict.TryGetValue((int)returnVal[0], out chosenNode);
                    currentNode = chosenNode;
                    hasChosen   = true;
                }
                if (c is DialogAction) {
                    DialogAction action = c as DialogAction;
                    GD.Print("doing action....");
                    action.DoAction();
                }
            }

            GD.Print("finished going through children");
            GD.Print("hasChosen:" + hasChosen);

            if (!hasChosen) {
                GD.Print("currentNode:" + currentNode);
                GD.Print("currentNode.toNode:" + currentNode.toNode);
                currentNode = currentNode.toNode;
            }

            if (currentNode == null)
                exitDialogs = true;
        }
    }
}
