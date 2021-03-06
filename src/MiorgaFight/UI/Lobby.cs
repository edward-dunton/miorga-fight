using Godot;
using System;
using System.Collections.Generic;

namespace MiorgaFight {

//Manages moving players in and out of the game
//And the lobby UI scene which the game starts on
public class Lobby : Control {
	public enum MultiplayerRole {
		SPECTATOR, P1, P2, OFFLINE
	}

	public enum GameState {
		//On title screen
		TITLE,
		//Waiting for player data from server (upon joining)
		WAITING, 
		//Sat in character selection
		CHAR_SELECTION,
		//Sat in level selection
		LEVEL_SELECTION,
		//In game but not actually playing, currently only used once the game is over
		IN_GAME_NOT_PLAYING,
		//In game
		IN_GAME_PLAYING
	}

	public const int PORT = 6785;

	public static MultiplayerRole role = MultiplayerRole.OFFLINE;
	public static GameState state = GameState.TITLE;
	//Whether the 
	public static bool highLatency = false;

	//Returns whether this game is hosting or not
	public static bool IsHost() {
		return (Command.lobby.GetTree().GetNetworkUniqueId() == 1);
	}

	LineEdit nodeAddr;
	Button nodeHostButton, nodeJoinButton, nodeLocalButton, nodeQuitButton;
	RaiseButton nodeErrorButton;
	Level game;
	Panel nodeErrorPanel, nodeStartPanel;
	Label nodeErrorLabel;

	private PackedScene p1Scene, p2Scene;
	private PackedScene levelScene;

	private Player p1, p2;

	//MP ID's of players p1 and p2, or -1 if waiting for server to set, 0 if none
	public int p1Id, p2Id;

	private NetworkedMultiplayerENet peer;

	public Lobby() {
		Command.lobby = this;
	}

	public override void _Ready()
	{
		Lobby.state = GameState.TITLE;

		this.nodeAddr = GetNode<LineEdit>("pa_start/tx_address");    
		
		this.nodeHostButton = GetNode<Button>("pa_start/bt_host");
		this.nodeJoinButton = GetNode<Button>("pa_start/bt_join");
		this.nodeLocalButton = GetNode<Button>("pa_start/bt_local");
		this.nodeErrorButton = GetNode<RaiseButton>("pa_error/bt_error");
		this.nodeQuitButton = GetNode<Button>("bt_quit");

		this.nodeErrorPanel = GetNode<Panel>("pa_error");
		this.nodeStartPanel = GetNode<Panel>("pa_start");

		//Continue through pauses
		this.PauseMode = Node.PauseModeEnum.Process;

		this.nodeErrorLabel = GetNode<Label>("pa_error/la_error");

		this.nodeHostButton.Connect("pressed", this, nameof(_OnHostPressed));
		this.nodeJoinButton.Connect("pressed", this, nameof(_OnJoinPressed));
		this.nodeLocalButton.Connect("pressed", this, nameof(_OnLocalPressed));
		this.nodeErrorButton.Connect("pressed", this, nameof(_OnErrorPressed));
		this.nodeQuitButton.Connect("pressed", this, nameof(_OnQuitPressed));

		GetTree().Connect("network_peer_connected", this, nameof(_PlayerConnected));
		GetTree().Connect("network_peer_disconnected", this, nameof(_PlayerDisconnected));
		GetTree().Connect("connected_to_server", this, nameof(_ConnectedToServer));
		GetTree().Connect("connection_failed", this, nameof(_ConnectionFailed));
		GetTree().Connect("server_disconnected", this, nameof(_ServerDisconnected));

		this.RpcConfig(nameof(this.SetupClient), MultiplayerAPI.RPCMode.Puppet);
		this.RpcConfig(nameof(this.ResetToLobby), MultiplayerAPI.RPCMode.Remotesync);
		this.RpcConfig(nameof(this.ChangeRole), MultiplayerAPI.RPCMode.Remotesync);
	
		this.GrabFocus();
	}

	public override void _Input(InputEvent input) {
		if (!this.Visible) return; //Make sure this doesn't snap up input when it's not in the foreground

		if (input.IsActionPressed("ui_jump_to_back")) this.nodeQuitButton.GrabFocus();
	}

	//Called when a player connects (duh)	
	void _PlayerConnected(int id) {
		GD.Print(id.ToString() + " connected!");

		//Don't let the server get in here (I don't think it can anyway tbf)
		if (id == 1) return;

		if (Lobby.IsHost()) {
			//If p1 or p2 change, push changes to all clients
			if (this.p1Id == 0) {
				this.p1Id = id;
				Rpc(nameof(this.ResetToLobby), new object[] {this.p1Id, this.p2Id});
			} else if (this.p2Id == 0) {
				this.p2Id = id;
				Rpc(nameof(this.ResetToLobby), new object[] {this.p1Id, this.p2Id});
			} else {
				//Otherwise just current ids to new player
				RpcId(id, nameof(this.SetupClient), new object[] {this.p1Id, this.p2Id});
			
				//Also send through any character selection choices that have been made
				CharSelection cs = GetTree().Root.GetNode<CharSelection>("char_selection");
				if (cs.p1.mpConfirmed) 
					cs.RpcId(id, nameof(cs.Confirm), new object[] {Lobby.MultiplayerRole.P1, cs.p1.selection});
				if (cs.p2.mpConfirmed) 
					cs.RpcId(id, nameof(cs.Confirm), new object[] {Lobby.MultiplayerRole.P2, cs.p2.selection});
			}
		}

		this.ResetSpectators();
	}

	void _PlayerDisconnected(int id) {
		if (Lobby.IsHost()) {
			//Force everyone back to lobby (if you're the server)
			if (id == this.p1Id) {
				Rpc(nameof(this.ResetToLobby), new object[] {0, this.p2Id});
			} else if (id == this.p2Id) {
				Rpc(nameof(this.ResetToLobby), new object[] {this.p1Id, 0});
			} 
		}

		this.ResetSpectators();
	}

	void _ConnectedToServer() {
		this.p1Id = -1;
		this.p2Id = -1;

		this.nodeErrorPanel.Visible = false;
		this.nodeStartPanel.Visible = true;
		this.Visible = false;
	} 

	void _ConnectionFailed() {
		this.ShowError("Error: Unable to connect\nPerhaps there is no server running?");
	}

	void _ServerDisconnected() {
		this.Reset();
		this.ShowError("Connection to server lost!");
	}

	void _OnHostPressed() {
		//Create peer
		this.peer = new NetworkedMultiplayerENet();
		this.peer.CompressionMode = NetworkedMultiplayerENet.CompressionModeEnum.RangeCoder;    
		Error error = this.peer.CreateServer(PORT, 8);

		if (error != Error.Ok) {
			this.ShowError("Error: Unable to host server\nPerhaps there is alreay a server open?");
			return;
		}

		this.p1Id = 1;
		this.p2Id = 0;
		GetTree().NetworkPeer = this.peer;
		Lobby.state = GameState.CHAR_SELECTION;
		//Hosts should start out as a spectator
		Lobby.role = MultiplayerRole.P1;
	
		this.Visible = false;

		//Goto CS as spectator
		CharSelection cs = (ResourceLoader.Load("res://scenes/ui/char_selection/char_selection.tscn") as PackedScene)
				.Instance() as CharSelection;
		GetTree().Root.AddChild(cs);
		//Set the character selection up for multiplayer, with this as a spectator
		cs.SetMp();
		cs.SetCallback(this._MpCSCallback);
		cs.PlayersUpdated(true, false); //Get the player connected sprites set correctly
	}

	void _OnJoinPressed() {
		String ip = IP.ResolveHostname(nodeAddr.Text);
		
		if (ip == "") {
			this.ShowError("Error: Invalid host!");
			return;
		}

		//Create host
		this.peer = new NetworkedMultiplayerENet();
		this.peer.CompressionMode = NetworkedMultiplayerENet.CompressionModeEnum.RangeCoder;    
		this.peer.CreateClient(ip, PORT);
		GetTree().NetworkPeer = this.peer;
		 
		this.ShowError("Connecting...", "Cancel");
		Lobby.state = GameState.WAITING;
		Lobby.role = MultiplayerRole.SPECTATOR;
	}

	//Host a regular, local game
	void _OnLocalPressed() {
		Lobby.role = MultiplayerRole.OFFLINE;
		Lobby.state = GameState.CHAR_SELECTION;
		this.p1Id = 0;
		this.p2Id = 0;
		//Move to char selection
		this.Visible = false;
		CharSelection cs = (ResourceLoader.Load("res://scenes/ui/char_selection/char_selection.tscn") as PackedScene)
				.Instance() as CharSelection;
		cs.SetCallback(this._LocalCSCallback);
		GetTree().Root.AddChild(cs);
	}

	//Multiplayer character selection callback, stores the selected characters and moves on to map selection
	LevelSelection _MpCSCallback(CharSelection cs, PackedScene p1, PackedScene p2) {
		//Disable all new connections
		this.peer.RefuseNewConnections = true;

		LevelSelection ls = this._LocalCSCallback(cs, p1, p2);
		ls.SetMp(Lobby.role);

		return ls;
	}

	//Local character selection callback, stores the selected characters and moves on to level selection
	//Returns the new level selection
	LevelSelection _LocalCSCallback(CharSelection cs, PackedScene p1, PackedScene p2) {
		//Removes the charselection
		GetTree().Root.RemoveChild(cs);

		//Stores player choices
		this.p1Scene = p1;
		this.p2Scene = p2;

		//Moves to level selection
		LevelSelection ls = (ResourceLoader.Load("res://scenes/ui/level_selection.tscn") as PackedScene).Instance() 
				as LevelSelection;
		ls.SetCallback(this._LSCallback);
		GetTree().Root.AddChild(ls);

		Lobby.state = GameState.LEVEL_SELECTION;

		return ls;
	}

	int _LSCallback(LevelSelection ls, PackedScene level) {
		//Remove level selection
		GetTree().Root.RemoveChild(ls);
		
		this.levelScene = level;

		//Create and goto level
		this.GameCreate(level);

		//Add the players
		this.AddPlayer("p1", this.p1Id, this.p1Scene);
		this.AddPlayer("p2", this.p2Id, this.p2Scene);

		//Start the game
		this.GameStart();
		return 0;
	}

	//Called when the error panel's button is called
	//Either to cancel a connection or to accept an error
	void _OnErrorPressed() {
		if (this.peer != null) {
			//Cancel the connection
			this.peer.CloseConnection();
			this.peer = null;
			GetTree().NetworkPeer = null;
		}
		
		this.nodeStartPanel.Visible = true;
		this.nodeErrorPanel.Visible = false;
		this.GrabFocus();
	}

	//Called when the quit button is pressed
	//Closes the game
	void _OnQuitPressed() {
		//I wanted the button directly, but it wouldn't let me
		GetTree().Quit();
	}

	/*
		Which of these is called when?
		If a player has changed -> Call SetPlayerId(...) on all clients
		If a player hasn't changed -> Call SetupClient on just the new spectator
	*/
	
	//Called whenever the game has to be setup
	//This is called internally when the players have changed
	//Or by the server if you join late as a spectator
	private void SetupClient(int p1Id, int p2Id) {
		//Set the role correctly
		if (p1Id == GetTree().GetNetworkUniqueId()) Lobby.role = MultiplayerRole.P1;
		else if (p2Id == GetTree().GetNetworkUniqueId()) Lobby.role = MultiplayerRole.P2;
		else Lobby.role = MultiplayerRole.SPECTATOR;

		//Set the player ids
		this.p1Id = p1Id;
		this.p2Id = p2Id;
		//It doesn't matter what state you were in, once this is called you're in char selections
		Lobby.state = GameState.CHAR_SELECTION;

		//Case reseting lobby
		CharSelection cs = (ResourceLoader.Load("res://scenes/ui/char_selection/char_selection.tscn") as PackedScene)
				.Instance() as CharSelection;
		GetTree().Root.AddChild(cs);
		//Set the character selection up for multiplayer, with this as a spectator
		cs.SetMp();
		cs.SetCallback(this._MpCSCallback);
		cs.PlayersUpdated(p1Id != 0, p2Id != 0);

		this.ResetSpectators();
	}

	public void ChangeRole(int id) {
		if (! Lobby.IsHost()) return; //None hosts can fuck off

		//P1 -> Spectator
		if (this.p1Id == id) {
			//Issue player change to all clients
			Rpc(nameof(this.ResetToLobby), new object[] {0, this.p2Id});
		} else if (this.p2Id == id) {
			Rpc(nameof(this.ResetToLobby), new object[] {this.p1Id, 0});
		} else {// Is spectator
			if (this.p1Id == 0) {//P1 empty, take over
				Rpc(nameof(this.ResetToLobby), new object[] {id, this.p2Id});
			} else if (this.p2Id == 0) {//P1 empty, take over
				Rpc(nameof(this.ResetToLobby), new object[] {this.p1Id, id});
			}
		}
	}

	//Starts the game
	//Once both players are already in the game
	public void GameStart() {
		Lobby.state = GameState.IN_GAME_PLAYING;

		this.p1.Start(this.p2, this.game.GetNode("hud/gr_p1") as PlayerHUD);
		this.p2.Start(this.p1, this.game.GetNode("hud/gr_p2") as PlayerHUD);
	}

	//Creates the game then goes to it
	private void GameCreate(PackedScene map) {
		this.game = (map.Instance()) as Level;

		GetTree().Root.AddChild(this.game);
		this.Visible = false;
		Input.SetMouseMode(Input.MouseMode.Hidden);
	}

	public void AddPlayer(String name, int id, PackedScene player) {
		Player _new = player.Instance() as Player;
		_new.Name = name;
		
		_new.SetNetworkMaster(id);
		
		if (Lobby.role == MultiplayerRole.OFFLINE) {
			_new.controls = (name == "p1") ? Player.ControlMethod.PLAYER1 : Player.ControlMethod.PLAYER2; 
		} else {
			_new.controls = (id == GetTree().GetNetworkUniqueId()) ? 
					Player.ControlMethod.PLAYER1 : Player.ControlMethod.REMOTE;
		}

		//Position specific stuff
		if (name == "p1") { 
			this.p1 = _new;
			_new.DIRECTION = Player.Direction.RIGHT;
		} else if (name == "p2") {
			this.p2 = _new;
			_new.DIRECTION = Player.Direction.LEFT;
		}
		
		_new.Position = this.game.GetPlayerPosition(_new.DIRECTION);
		
		this.game.AddChild(_new);
		(this.game.GetNode("camera_track") as CameraTrack).Track(_new);	
	}

	//Called when a player changes, causes the game to reset to character selection or once a game has ended 
	//Including if this client is one of those players
	public void ResetToLobby(int p1Id, int p2Id) {
		GetTree().Paused = false;

		this.RemoveAll();
		
		//Make cursor visible
		Input.SetMouseMode(Input.MouseMode.Visible);

		if (Lobby.role != MultiplayerRole.OFFLINE) { //Multiplayer	
			//Allow new connections
			if (GetTree().NetworkPeer != null) GetTree().NetworkPeer.RefuseNewConnections = false;
			
			//Reset state and action as if this client has just connected
			this.SetupClient(p1Id, p2Id);
		} else { //Singleplayer
			this._OnLocalPressed();
		}
	}

	//Reset all the way back to title screen
	public void Reset() {
		//Unpause game (if it is paused)
		GetTree().Paused = false;

		this.RemoveAll();

		//Make cursor visible
		Input.SetMouseMode(Input.MouseMode.Visible);

		//If online close connection
		if (Lobby.role != MultiplayerRole.OFFLINE) this.peer.CloseConnection();
		Lobby.state = GameState.TITLE;
		Lobby.role = MultiplayerRole.OFFLINE;

		//Reset everything
		GetTree().ChangeScene("res://scenes/ui/lobby.tscn");
	}

	//Resets the char selection panes number of spectators if it exists
	//The overhead for this function is pretty small and thus it can be called sparingly
	//I chose to call this function more than necessary
	//rather than introduce more complex logic around when this function should be called
	//Lobby logic is bad enough as is
	private void ResetSpectators() {
		if (GetTree().Root.HasNode("char_selection"))
			GetTree().Root.GetNode<CharSelection>("char_selection").SetSpectators(this.CalcSpectators());
	}

	//Calculates the number of spectators current connected
	//should only be called by the host really
	public int CalcSpectators() {
		//Peers() + 1 - All other clients + this one
		int c = GetTree().GetNetworkConnectedPeers().Length + 1;
		if (this.p1Id != 0) c--; //Decrement for each client which is actually playing
		if (this.p2Id != 0) c--;

		return c;
	}

	//Removes everything other than this and Command from the root node
	private void RemoveAll() {
		foreach (Node n in GetTree().Root.GetChildren()) {
			if (n is Command || n == this) continue;
			GetTree().Root.RemoveChild(n);
			n.QueueFree();
		}
	}

	//Enable the error panel and have it display the given message
	private void ShowError(string msg, string buttonmsg = "Ok") {
		this.nodeStartPanel.Visible = false;
		this.nodeErrorPanel.Visible = true;
		this.nodeErrorLabel.Text = msg;
		this.nodeErrorButton.Text = buttonmsg;
		this.nodeErrorButton.GrabFocus();
	}
}}
