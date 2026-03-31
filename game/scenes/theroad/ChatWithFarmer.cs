using Godot;
using System;

public partial class ChatWithFarmer : DialogTree
{

 	// public async override void DoDialogs() {


	// 	bool exitDialogs = false;

	// 	string[] newstring = new string[3]{"1111.", "222222!", "3333333333...."};

	// 	// then one by one, create dialogs for each step in order, waiting for the choice parts and keeping 
	// 	// track of position in tree

	// 	while (!exitDialogs) {
	// 	DialogBox prompt = CreateDialog(Globals.DialogTypes.narration, "WHICH ONE?");
	// 	DialogBox db = CreateDialog(Globals.DialogTypes.choice,null,null,null,null, newstring);
	// 	Godot.Variant[] returnVal = await this.ToSignal(db, "DialogClosed");
    //     GD.Print("returnVal: " + returnVal[0]);
		
	// 	prompt.QueueFree();
	// 	parentScene.numberOfOpenDialogs--;
	// 	// db.QueueFree();
	// 	DialogBox resp;
	// 	switch ((int)returnVal[0]) {
	// 		case 1:
	// 			resp = CreateDialog(Globals.DialogTypes.narration, "1111111111111111");
	// 			await this.ToSignal(resp, "DialogClosed");
	// 			//resp.QueueFree();
	// 			break;
	// 		case 2:
	// 			resp = CreateDialog(Globals.DialogTypes.narration, "A22222222222222222");
	// 			await this.ToSignal(resp, "DialogClosed");
	// 			//resp.QueueFree();
	// 			break;
	// 		case 3:
	// 			resp = CreateDialog(Globals.DialogTypes.narration, "3333333333333333. BYE!");
	// 			await this.ToSignal(resp, "DialogClosed");
	// 			//resp.QueueFree();
	// 			exitDialogs = true;
	// 			break;

	// 	}

	// 	}

    //     this.EmitSignal("DialogTreeClosed");     


    // }
}
