using Godot;
using System;
using System.Collections.Generic;

public partial class Inventory : Control
{
	private scene_script parentScene;
	// Called when the node enters the scene tree for the first time.
	private Globals.InteractModes prevInteractMode = Globals.InteractModes.walk;

	private List<InventoryItem> inventoryList = new List<InventoryItem>();
	private ItemList itemList;
	public override void _Ready()
	{
		parentScene = this.GetParent() as scene_script;
		itemList = this.GetNode<Panel>("Panel").GetNode<ItemList>("ItemList");
		itemList.Clear();
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		
	}

	public void _on_item_list_item_selected(int index) {
		if (parentScene.mainScene.GetInteractMode() == Globals.InteractModes.walk) {
			GD.Print("selected item is " + index + "-------");
			GD.Print("type is " + inventoryList[index].Type);
			parentScene.mainScene.usingItem = inventoryList[index].Type;
			parentScene.mainScene.SetInteractMode(Globals.InteractModes.item);
			UpdateVerbPanelItemButton();
		}
	}

	public void UpdateVerbPanelItemButton() {
		Image itemsImage = parentScene.mainScene.cursor.itemTexture.GetImage();
		ImageTexture itemTexture = null;

		int itemsTextureSizeX = parentScene.mainScene.cursor.itemTextureSize.X;
		int itemsTextureSizeY = parentScene.mainScene.cursor.itemTextureSize.Y;
		Image itemImage = Image.Create(itemsImage.GetSize().X/itemsTextureSizeX, itemsImage.GetSize().Y/itemsTextureSizeY,false, Image.Format.Rgba8);


		if (parentScene.mainScene.usingItem != InventoryItem.ItemType.none) {

			int itemNumber = (int)parentScene.mainScene.usingItem;
			int itemNumberInRow = itemNumber % itemsTextureSizeX;
			if (itemNumberInRow == 0)
				itemNumberInRow = itemsTextureSizeX;
			int itemRow = itemNumber % itemsTextureSizeX == 0 ? 
							((itemNumber - (itemNumber % itemsTextureSizeX)) / itemsTextureSizeX) - 1 :
							((itemNumber - (itemNumber % itemsTextureSizeX)) / itemsTextureSizeX)  ;
			itemRow++;

			
			for (int iy = 0; iy < itemImage.GetSize().Y; iy++ ) {
				for (int ix = 0; ix < itemImage.GetSize().X; ix++ ) {
					Vector2I itemsImagePixelStart = new Vector2I(itemImage.GetSize().X * (itemNumberInRow-1)  ,
																	itemImage.GetSize().Y * (itemRow - 1));
					Color pixelColor = itemsImage.GetPixel(itemsImagePixelStart.X + ix, itemsImagePixelStart.Y + iy); 
					itemImage.SetPixel(ix, iy, pixelColor);
				}
			}
			
			
			itemTexture = new ImageTexture();
			itemTexture.SetImage(itemImage);
		}


		parentScene.mainScene.overlayScene.verbPanel.itemButton.TextureNormal = itemTexture;
		if (parentScene.verbCoinControl is VerbCoin coin)
			coin.UpdateItemButton(itemTexture);
	}
	public void InventoryOpened(Globals.InteractModes _prevInteractMode) {
		
		// populate inventory
		itemList.Clear();
		inventoryList = parentScene.mainScene.inventory;
		
		foreach(InventoryItem i in inventoryList) {
			GD.Print(i.Type);
			Image itemsImage = parentScene.mainScene.cursor.itemTexture.GetImage();
			ImageTexture itemTexture = null;

			int itemsTextureSizeX = parentScene.mainScene.cursor.itemTextureSize.X;
			int itemsTextureSizeY = parentScene.mainScene.cursor.itemTextureSize.Y;
			Image itemImage = Image.Create(itemsImage.GetSize().X/itemsTextureSizeX, itemsImage.GetSize().Y/itemsTextureSizeY,false, Image.Format.Rgba8);


			if (i.Type != InventoryItem.ItemType.none) {

				int itemNumber = (int)i.Type;
				int itemNumberInRow = itemNumber % itemsTextureSizeX;
				if (itemNumberInRow == 0)
					itemNumberInRow = itemsTextureSizeX;
				int itemRow = itemNumber % itemsTextureSizeX == 0 ? 
								((itemNumber - (itemNumber % itemsTextureSizeX)) / itemsTextureSizeX) - 1 :
								((itemNumber - (itemNumber % itemsTextureSizeX)) / itemsTextureSizeX)  ;
				itemRow++;

				
				for (int iy = 0; iy < itemImage.GetSize().Y; iy++ ) {
					for (int ix = 0; ix < itemImage.GetSize().X; ix++ ) {
						Vector2I itemsImagePixelStart = new Vector2I(itemImage.GetSize().X * (itemNumberInRow-1)  ,
																	 itemImage.GetSize().Y * (itemRow - 1));
						Color pixelColor = itemsImage.GetPixel(itemsImagePixelStart.X + ix, itemsImagePixelStart.Y + iy); 
						itemImage.SetPixel(ix, iy, pixelColor);
					}
				}
				
				
				itemTexture = new ImageTexture();
				itemTexture.SetImage(itemImage);
			}

			itemList.AddItem(i.Type.ToString(), itemTexture);

		}
		
		
		


		this.prevInteractMode = _prevInteractMode;
      	parentScene.mainScene.SetInteractMode(Globals.InteractModes.walk);
        parentScene.Pause();
		GD.Print("opened: " + this);
		
	}

	public void InventoryClose() {
        GD.Print("closing inventory...");
	//	parentScene.mainScene.SetInteractMode(prevInteractMode);
		this.Hide();
		GetViewport().SetInputAsHandled();
		parentScene.Resume();				
        // parentScene.UnsuspendSceneInput();
	}
    public override void _UnhandledInput(InputEvent @event)
    {
		if (!this.Visible)
			return;
        // base._UnhandledInput(@event);
 		if (@event is InputEventKey eventKey) {

           // GD.Print("SCANCODE: " + eventKey.Scancode);
            if (eventKey.Pressed)
                if (eventKey.Keycode is Key.I) {
					// scene_script parentScene = this.GetParent() as scene_script;
					InventoryClose();
					
				//	this.QueueFree();
					// inventoryUI = null;


                }
            
        } else if (@event is InputEventMouseButton eventMouseButton) {
            
            if (eventMouseButton.ButtonIndex is MouseButton.Left ) {                

                if (eventMouseButton.Pressed) {

					Vector2 mousePos = GetGlobalMousePosition();
					Rect2 thisRect = this.GetGlobalRect();

					if (mousePos.X < thisRect.Position.X || 
					    mousePos.X > thisRect.End.X ||
						mousePos.Y < thisRect.Position.Y || 
					    mousePos.Y > thisRect.End.Y
						) {
						
						InventoryClose();
						
					}


				}
			} else if (eventMouseButton.ButtonIndex is MouseButton.Right) {
                if (eventMouseButton.Pressed) {
					GD.Print("inventory right click");
					parentScene.AdvanceInteractMode(); //_UnhandledInput(@event);
				}
			}
		}



    }
}
