using Godot;
using System;

namespace MiorgaFight {

public class LevelSelection : Control
{
    /*
        Data for these levels is stored in 2 different structures, the LevelSelectionLevelData resources stores all
        static data about each level, including names, descriptions and packed scenes etc.

        This structure contains all instance specific data for a level (this is actually unnecessary as there will only
        ever be a single instance of any of these resources and just a resource could be used, but that would be bad
        practice)
    */
    private struct LevelData {
        //Button for this level
        public TextureButton button;
        //This levels map
        public Level level;
        //Raw data for this level
        public LevelSelectionLevelData resource;
    }

    [Export] LevelSelectionLevelData[] levelData;

    private LevelData[] levels;

    private Control nodeButtonPanel;
    private TextureButton nodeButtonPlay;
    private Sprite nodeLevelSprite;
    private Viewport nodeLevelViewport;
    private Camera2D nodeLevelCamera;
    private HSlider nodeLevelSlider;

    //Function to call once map selection is finished
    private Func<LevelSelection, PackedScene, int> callback;

    //The currently selected level
    private LevelData? level;

    public override void _Ready() {
        this.level = null;

        //Gets nodes and sets up connections
        this.nodeButtonPanel = this.GetNode<Control>("pa_buttons");
        this.nodeButtonPlay = this.GetNode<TextureButton>("bt_play");

        this.nodeLevelSprite = this.GetNode<Sprite>("sp_map");
        this.nodeLevelViewport = this.GetNode<Viewport>("vp_map");
        this.nodeLevelCamera = this.GetNode<Camera2D>("vp_map/camera");
        this.nodeLevelSlider = this.GetNode<HSlider>("sl_map");

        this.nodeButtonPlay.Connect("pressed", this, nameof(_OnPlayPressed));

        this.nodeLevelSlider.Connect("value_changed", this, nameof(_OnSliderChanged));
    
        //Checks that number of buttons and levels are the same
        Godot.Collections.Array buttonPanelChildren = this.nodeButtonPanel.GetChildren();

        if (this.levelData.Length != buttonPanelChildren.Count) {
            GD.Print("ERROR: MAP SELECTION: Unequal number of buttons and maps, aborting");
            return;
        }

        //Init levels array
        this.levels = new LevelData[this.levelData.Length];

        //Temp looping values
        int i = 0;
        LevelData level;
        //Iterate through level data provided
        foreach (LevelSelectionLevelData data in this.levelData) {
            //Create new resource for level
            level = new LevelData();
            level.resource = data;

            //Instance level
            level.level = level.resource.packed.Instance() as Level;
            if (level.level == null) {
                GD.Print("Error: Processing level from pack");
                continue;
            }

            //Remove hud and camera track
            level.level.RemoveChild(level.level.GetNode("hud"));
            level.level.RemoveChild(level.level.GetNode("camera_track"));
        
            //Gets all children of the button panel
            level.button = buttonPanelChildren[i] as TextureButton;

            //Call _OnButtonPress with this buttons index on press
            level.button.Connect("pressed", this, nameof(this._OnButtonPressed), 
                    new Godot.Collections.Array(new int[] {i}));

            this.levels[i] = level;

            i ++;
            //Something has gone very wrong if there's more than 6 buttons
            if (i == 6) {
                GD.Print("ERROR: MAP SELECTION: More than 6 maps");
                break;
            }
        }
    }

    void _OnSliderChanged(float value) {
        this.nodeLevelCamera.Position = new Vector2(value, this.nodeLevelCamera.Position.y);
    }

    void _OnButtonPressed(int i) {
        LevelData newlevel = this.levels[i];
        //Reable last pressed button, if one was pressed
        if (this.level.HasValue) {
            this.level.Value.button.Disabled = false;
            this.level.Value.button.Pressed = false;
        }

        //Diable this button (so it can't be unselected)
        newlevel.button.Disabled = true;

        this.nodeButtonPlay.Disabled = false;
        this.ShowMap(newlevel, this.level);
        this.level = newlevel;
    }

    //When the play button is pressed
    void _OnPlayPressed() {
        if (this.callback == null) {
            GD.Print("Fatal Error: No callback set for Character Selection, unable to proceed");
            return;
        }
    
        if (this.level.HasValue) {
            this.callback(this, this.level.Value.resource.packed);
        }
    }

    private void ShowMap(LevelData newlevel, LevelData? oldlevel) {
        if (oldlevel.HasValue) {
            this.nodeLevelViewport.RemoveChild(oldlevel.Value.level);
        }
        
        this.nodeLevelSlider.Value = 0f;
        this.nodeLevelSlider.MinValue = -newlevel.resource.GetMovement();
        this.nodeLevelSlider.MaxValue = newlevel.resource.GetMovement();
        this.nodeLevelCamera.Position = newlevel.resource.GetCameraPos();
        
        this.nodeLevelViewport.AddChild(newlevel.level);
    }

    public void SetCallback(Func<LevelSelection, PackedScene, int> c) {
        this.callback = c;
    }
}}
