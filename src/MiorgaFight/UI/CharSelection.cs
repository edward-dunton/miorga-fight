using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MiorgaFight {

public class CharSelection : Control
{
    //Stores all the data for a specific player
    protected class PlayerData {
        //This data holders current selection
        public int selection;
        //The button to select this data holder
        public TextureButton button;
        //The other players data holder
        public PlayerData other;
        //This players button icon
        public Sprite icon;

        public string name;

        public PlayerData(string name, TextureButton button, Sprite icon) {
            this.selection = -1;
            this.name = name;
            this.button = button;
            this.icon = icon;
        }
    }

    private PlayerData p1, p2;

    //Character scenes in packed format
    //These are then instanced into the charScenes array 
    [Export] private List<PackedScene> charScenes; 
    
    private CharSelectionDataPanel[] chars;

    //Lobby which will be called back to once the players have decided which classes they will play as
    private Lobby callback;

    private TextureButton nodeP1Button, nodeP2Button, nodePlayButton;
    private Sprite nodeP1Icon, nodeP2Icon;
    private ItemList nodeCharList;
    private Sprite nodeAbilityTreeSprite, nodePlayerSprite;
    private CharSelectionDataPanel nodeDataPanel;

    public override void _Ready() {
        //Get nodes
        this.nodeP1Button = GetNode<TextureButton>("pa_player_buttons/bt_p1");
        this.nodeP1Icon = GetNode<Sprite>("pa_player_buttons/sp_p1");
        this.nodeP2Button = GetNode<TextureButton>("pa_player_buttons/bt_p2");
        this.nodeP2Icon = GetNode<Sprite>("pa_player_buttons/sp_p2");
        
        this.nodePlayButton = GetNode<TextureButton>("bt_play");

        this.nodeDataPanel = GetNode<CharSelectionDataPanel>("pa_char_data");

        this.p1 = new PlayerData("Player 1", this.nodeP1Button, this.nodeP1Icon);
        this.p2 = new PlayerData("Player 2", this.nodeP2Button, this.nodeP2Icon);
        this.p1.other = this.p2;
        this.p2.other = this.p1;
        
        this.nodeCharList = GetNode<ItemList>("il_selection");

        this.nodeAbilityTreeSprite = GetNode<Sprite>("pa_char_data/sp_action_tree");
        this.nodePlayerSprite = GetNode<Sprite>("pa_char_data/sp_player");

        //Connect player buttons
        this.nodeP1Button.Connect("pressed", this, nameof(this._OnPlayerPressed), 
                new Godot.Collections.Array(new byte[] {1}));
        this.nodeP2Button.Connect("pressed", this, nameof(this._OnPlayerPressed), 
                new Godot.Collections.Array(new byte[] {2}));
        this.nodePlayButton.Connect("pressed", this, nameof(this._OnPlayPressed));

        this.nodeCharList.Connect("item_selected", this, nameof(this._OnCharSelected));

        //Map PackedScenes in charScenes into Control nodes in chars
        this.chars = charScenes.Select(character => character.Instance() as CharSelectionDataPanel).ToArray();

        this.ShowChar(-1);
    }

    //When one of the player buttons is pressed
    void _OnPlayerPressed(byte pressed) {
        PlayerData d = (pressed == 1) ? this.p1 : this.p2;

        d.button.Pressed = true;
        d.other.button.Pressed = false;
        
        //Change the itemlist to the current selected item if there is one
        if (d.selection != -1) {this.nodeCharList.Select(d.selection);}
        //Otherwise remove selection
        else if (this.nodeCharList.GetSelectedItems().Length > 0) {
            this.nodeCharList.Unselect(this.nodeCharList.GetSelectedItems()[0]);
        }
        this.ShowChar(d.selection);
    }

    //When the play button is pressed
    void _OnPlayPressed() {
        if (this.callback == null) {
            GD.Print("Fatal Error: No callback set for Character Selection, unable to proceed");
            return;
        }

        this.callback.GameGoto();

        this.callback.AddPlayer("p1", 0, this.chars[this.p1.selection].character);
        this.callback.AddPlayer("p2", 0, this.chars[this.p2.selection].character);
        GetTree().Root.RemoveChild(this);
        this.callback.GameStart();
    }

    void _OnCharSelected(int index) {
        PlayerData d = this.nodeP1Button.Pressed ? this.p1 : this.p2;

        d.selection = index;
        d.icon.Texture = this.nodeCharList.GetItemIcon(index);

        if (this.p1.selection != -1 && this.p2.selection != -1) this.nodePlayButton.Disabled = false;
        this.ShowChar(index);
    }

    //Shows a character up on the panel on the right hand side
    //Index must be an index found in the itemlist, CharSprites and CharTrees or -1 for non
    private void ShowChar(int index) {
        if (index == -1) {
            //Remove the data panel
            this.RemoveChild(this.nodeDataPanel);
        } else {
            this.RemoveChild(this.nodeDataPanel);
            //Set the pointer to the new data panel
            this.nodeDataPanel = this.chars[index];
            //Add the new data panel
            this.AddChild(this.nodeDataPanel);
        }
    }

    //Sets the call back lobby to the one provided
    public void SetCallback(Lobby l) {
        this.callback = l;
    }
}}
