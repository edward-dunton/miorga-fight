using Godot;
using System;
using System.Collections.Generic;

namespace MiorgaFight {

public class Player : KinematicBody2D, CameraTrack.Trackable {
	//Players direction, also deciedes whether they are p1 or p2
	//Right is always p1 and left is always p2
	public enum Direction {RIGHT, LEFT}

	public enum State {NONE, LOW, HIGH, LAX, WALK, ATTACK, PARRY, TRANS}

	public enum ControlMethod {PLAYER1, PLAYER2, REMOTE}

	//private static Vector2 FLIP = new Vector2(-1, 1);
	//private static Vector2 NONFLIP = new Vector2(1, 1);

	[Export] string LAX_SPRITE = "lax";
	[Export] string LOW_SPRITE = "low";
	[Export] string HIGH_SPRITE = "high";

	[Export] List<Action> actions;

	[Export] int SPEED;

	[Export] int HP_MAX;

	[Export] float FOOTSTEP_VOLUME;

	//All of this characters sfx, along with 
	[Export] Dictionary<string, SoundEffect> sfx;

	public ControlMethod controls;

	public Direction DIRECTION;

	public Stance stance;

	public State state;

	//Currently playing soundeffect
	public SoundEffect sound;

	//The players current attack, null if not attacking
	public Attack attack;
	//Players current parry, only valid if the player is parrying
	public Parry parry;
	//Set to false at the start of a parry, then true if the parry is successful
	public bool parrySuccessful;
	
	private int hp;
	//Seems like a getter should be able to do this, but I couldn't the autogenerated one to work properly
	public int GetHP() {return hp;}

	private int lives;
	public int GetLives() {return lives;}

	//State to move to once transition is over
	private State endTrans;
	
	private Stance STANCE_LAX, STANCE_LOW, STANCE_HIGH;

	//Nodes
	public Level level;
	public PlayerAnimation nodeAnimateSprite;
	public CollisionShape2D nodeCollision;
	public Player nodeEnemy;
	public Particles2D nodeSparks;
	public PlayerOverlay nodeOverlayTrack;
	public PlayerOverlay nodeOverlayNoTrack;
	public AudioStreamPlayer2D nodeSfx;

	//Current HUD, may be null, if there is no HUD
	public PlayerHUD hud;

	public String inputPrefix;

	private Vector2 velocity;
	private Vector2 lastVelocity;

	//These are declared based on the players direction
	public string ACTION_UP, ACTION_DOWN, ACTION_LEFT, ACTION_RIGHT; 

	//1 if the player is facing right and -1 if facing left
	//Seems redundant but I couldn't get the enums to play nicely with the editor
	public Vector2 SCALEFACTOR;

	public Player() {
		this.attack = null;
		this.parry = null;
		this.lastVelocity = new Vector2();
		this.lives = 5;
	}

	public override void _Ready() {
		this.level = GetParent() as Level;

		this.nodeAnimateSprite = GetNode<PlayerAnimation>("animate_sprite");
		this.nodeCollision = GetNode<CollisionShape2D>("collision");
		this.nodeSparks = GetNode<Particles2D>("animate_sprite/sparks");
		this.nodeOverlayTrack = GetNode<PlayerOverlay>("animate_sprite/overlay_track");
		this.nodeOverlayNoTrack = GetNode<PlayerOverlay>("overlay_notrack");
		
		this.nodeSfx = GetNode<AudioStreamPlayer2D>("sfx");
		this.nodeSfx.Connect("finished", this, nameof(_OnSfxFinished));

		//Load stances
		STANCE_LAX = new Stance("lax", LAX_SPRITE);
		//Creates attacks if they exist
		STANCE_LOW = new Stance("low", LOW_SPRITE);
		STANCE_HIGH = new Stance("high", HIGH_SPRITE);

		this.ChangeState(State.LAX);

		this.SCALEFACTOR = new Vector2((this.DIRECTION == Direction.RIGHT) ? 1 : -1, 1);

		//Set up actions
		if (Lobby.role == Lobby.MultiplayerRole.OFFLINE) {
			if (this.controls == ControlMethod.PLAYER1) {this.inputPrefix = "p1_";}
			else {this.inputPrefix = "p2_";}
		} else {//Is multiplayer
			if (this.controls == ControlMethod.PLAYER1) { //Master
				this.inputPrefix = "p1_";
				RsetConfig("position", MultiplayerAPI.RPCMode.Puppetsync);
				RpcConfig(nameof(this.ActionStart), MultiplayerAPI.RPCMode.Puppetsync);
			} else { // Puppet
				this.inputPrefix = "remote_";
				RsetConfig("position", MultiplayerAPI.RPCMode.Puppet);
				RpcConfig(nameof(this.ActionStart), MultiplayerAPI.RPCMode.Puppet);
			}
			
			RsetConfig(nameof(this.velocity), MultiplayerAPI.RPCMode.Puppet);
			RpcConfig(nameof(this.ChangeState), MultiplayerAPI.RPCMode.Puppet);
			RpcConfig(nameof(this.Hurt_), MultiplayerAPI.RPCMode.Remotesync);
			RpcConfig(nameof(this.ChangeHP_), MultiplayerAPI.RPCMode.Remotesync);
			RpcConfig(nameof(this.Parried_ByIndex_), MultiplayerAPI.RPCMode.Remotesync);
		}

		this.ACTION_UP = this.inputPrefix + "up";
		this.ACTION_DOWN = this.inputPrefix + "down";
		this.ACTION_LEFT = this.inputPrefix + "left";
		this.ACTION_RIGHT = this.inputPrefix + "right";

		//Flip if necessary
		if (this.DIRECTION == Direction.LEFT) this.Flip();
	}

	//Called once both players exist, points players towards their HP bar and the enemy
	//Must be called externally
	public void Start(Player enemy, PlayerHUD hud) {
 		this.hud = hud;
		this.hud.SetMaxHealth(this.HP_MAX);
		this.hud.Visible = true;
		this.parrySuccessful = false;

		this.ChangeLives(5);

		this.nodeEnemy = enemy;

		this.Restart();
	}

	//Called when the at the end of round, moves players back and resets HP
	public void Restart() {
		this.Position = this.level.GetPlayerPosition(this.DIRECTION);
		//Only reset locally
		//stops RPC calls going off to other clients which may not have finished setting up their game
		this.ChangeHP_(this.HP_MAX);
		this.ChangeState(State.LAX);
		
		//direction == left ensures that this is only called once
		if (this.DIRECTION == Direction.LEFT)
			this.hud.parent.ChangeRound(11 - this.lives - this.nodeEnemy.lives);
	}

	//Called when the game ends either if one player wins or one disconnects
	public void End() {
		this.hud.Visible = false;
		this.nodeEnemy = null;
	}

	public override void _Input(InputEvent inputEvent) {
		if (this.controls == ControlMethod.REMOTE) return; //Do not check for inputs for remote objects

		//No actions if the game hasn't started yet
		if (Lobby.state == Lobby.GameState.IN_GAME_NOT_PLAYING) return;

		//Check for actions
		foreach (Action action in this.actions) {
			if (action.IsPossible(this, inputEvent)) {
				if (Lobby.role == Lobby.MultiplayerRole.OFFLINE) {
					action.Start(this);
				} else {
					RpcUnreliable(nameof(this.ActionStart), new object[] {this.actions.IndexOf(action)});
				}
				//Mark input as dealt with
				GetTree().SetInputAsHandled();
				return;
			}
		}		

		//Check for up / down calls
		if (this.state == State.ATTACK || this.state == State.PARRY || this.state == State.TRANS) return;

		if (inputEvent.IsActionPressed(this.ACTION_UP)) {
			if (this.state == State.HIGH) {
				this.ChangeState(State.LAX);
			} else {
				this.ChangeState(State.HIGH);
			}
			GetTree().SetInputAsHandled();
		} else if (inputEvent.IsActionPressed(this.ACTION_DOWN)) {
			if (this.state == State.LOW) {
				this.ChangeState(State.LAX);
			} else {
				this.ChangeState(State.LOW);
			}
			GetTree().SetInputAsHandled();
		}
	}

	public override void _PhysicsProcess(float delta) {
		if (this.controls != ControlMethod.REMOTE) {
			if ((this.state == State.LAX || this.state == State.WALK)) {
				this.CalcMovement();
				if (this.velocity.x != 0) {
					//If player is actually moving
					MoveAndCollide(this.velocity * delta);

					if (Lobby.role != Lobby.MultiplayerRole.OFFLINE) 
						RsetUnreliable(nameof(this.velocity), this.velocity);

					//Player has just started moving
					if (this.state == State.LAX) this.ChangeState(State.WALK);
					//Player has changed direction        
					else if (Math.Sign(this.lastVelocity.x) != Math.Sign(this.velocity.x)){
						//restarts the walk
						this.WalkStart();
					}

				} else {
					//Player is not moving
					//Player has stopped moving
					if (this.state == State.WALK) this.ChangeState(this.GetStateFromStance(this.stance));
				}

				//Only send the position if walking
				if (Lobby.role != Lobby.MultiplayerRole.OFFLINE)
					RsetUnreliable("position", this.Position);
			} else {
				//If neither player should be still
				this.velocity = new Vector2();
			}

		} else {
			//Check for direction swapping when remote
			if (this.velocity.x != 0 && this.lastVelocity.x != 0 && 
					Math.Sign(this.lastVelocity.x) != Math.Sign(this.velocity.x)) {
				this.WalkStart();
			}
		}

		if (Lobby.state == Lobby.GameState.IN_GAME_PLAYING) {
			//Catches strange edge cases, most likely caused by multi-threaded physics
			if (this.DIRECTION == Direction.LEFT && this.Position.x < this.nodeEnemy.Position.x) {
				this.Restart();
				this.nodeEnemy.Restart();
			}
		}

		this.lastVelocity = this.velocity;
	}

	void _OnSfxFinished() {
		if (this.sound == null) {
			GD.Print("ERROR: Player._OnSfxFinished: Called but player.sound  is null");
			return;
		}

		if (this.sound.repeat) { //sfx sound repeat, play another random sound
			this.nodeSfx.Stream = Command.Random(this.sound.streams);
			this.nodeSfx.Play();
		} else {
			this.sound = null;
			this.nodeSfx.Stop();
		}
	}

	//Plays a sound from this players sound effects
	public void PlaySfx(SoundEffect sound) {
		this.sound = sound;
		this.nodeSfx.Stream = Command.Random(sound.streams);
		this.nodeSfx.VolumeDb = sound.volume;
		this.nodeSfx.Play();
	}
	
	//Plays a sound
	public void PlaySfx(string name) {
		SoundEffect sound;
		if (! this.sfx.TryGetValue(name, out sound)) return; //Check that the sound actually exists

		this.PlaySfx(sound);
	}

	//Stops the current sound from being played
	public void StopSfx() {
		this.sound = null;
		this.nodeSfx.Stop();
	}

	//Necessary to make RPC happy
	public void ChangeState(State newState) {
		this.ChangeState(newState, true);
	}

	//Moves the player from one state to another, all state changes should be routed through here
	//A little unnecessary I know
	public void ChangeState(State newState, bool transition) {
		//Perform actions for leaving state
		switch (this.state) {
			case State.WALK:
				this.WalkEnd();
				break;
		}
		
		//Check if a transition should be done
		//Not particularly proud of this 
		if (transition)
			transition = ((this.state == State.LAX || this.state == State.LOW || this.state == State.HIGH || 
				this.state == State.WALK) && (newState == State.LAX || newState == State.LOW || newState == State.HIGH));

		this.state = newState;

		//Perform actions for entering 
		switch (this.state) {
			case State.WALK:
				this.WalkStart();
				break;
			case State.LAX:
				this.ChangeStance(STANCE_LAX, transition);
				break;
			case State.LOW:
				this.ChangeStance(STANCE_LOW, transition);
				break;
			case State.HIGH:
				this.ChangeStance(STANCE_HIGH, transition);
				break;
		}

		if (Lobby.role != Lobby.MultiplayerRole.OFFLINE && this.controls != ControlMethod.REMOTE && this.state != State.ATTACK && this.state != State.PARRY) 
			RpcUnreliable(nameof(this.ChangeState), new object[] {newState});
	}

	
	public void Knockback(int amount, bool animate = true) {
		this.MoveAndCollide(new Vector2(-amount * 2, 0) * this.SCALEFACTOR);	
		if (animate)
			this.Transition("flinch");
	}

	//public wrapper function for ChangeHP_(..)
	//Used to ensure that ChangeHP_(..) is only called by the server in multiplayer
	//Passes straight through to ChangeHP_(..) in singleplayer
	public void ChangeHP(int newhp) {
		if (Lobby.role != Lobby.MultiplayerRole.OFFLINE) {
			if (Lobby.IsHost()) { //Only issue commands if host
				RpcUnreliable(nameof(this.ChangeHP_), new object[] {newhp});
			}
		} else {
			this.ChangeHP_(newhp);
		}
	}

	private void ChangeHP_(int newhp) {
		//Ensures hp is not above max
		this.hp = Math.Min(this.HP_MAX, newhp);

		this.hud.SetHealth(this.hp);

		if (this.hp <= 0) 
			this.LoseLife();
	}

	private void LoseLife() {
		//Lost
		if (this.lives == 0) {
			this.nodeEnemy.Wins();
			return;
		}

		this.ChangeLives(this.lives - 1);
		this.Restart();
		this.nodeEnemy.Restart();
	}

	//Called when this player wins
	public void Wins() {
		GetTree().Paused = true;
		this.hud.parent.Win(this.DIRECTION);
		Lobby.state = Lobby.GameState.IN_GAME_NOT_PLAYING;
	}

	//Changes this player score to the 
	public void ChangeLives(int newlives) {
		this.lives = newlives;

		this.hud.SetLives(this.lives);
	}

	//public wrapper function for Hurt_(..)
	//Used to ensure that Hurt_(..) is only called by the server in multiplayer
	//Passes straight through to Hurt_(..) in singleplayer
	public void Hurt(int damage, int knockback, bool halting = false) {
		if (Lobby.role != Lobby.MultiplayerRole.OFFLINE) {
			if (Lobby.IsHost()) { //Only issue commands if host
				RpcUnreliable(nameof(this.Hurt_), new object[] {damage, knockback, halting});
			}
		} else {
			this.Hurt_(damage, knockback, halting);
		}
	}

	//Called when a player is damaged
	//Halting is true if the player received halting damage
	private void Hurt_(int damage, int knockback, bool halting = false) {
		if (halting || (this.state != State.ATTACK && this.state != State.PARRY)) {
			//Minus here as he's moving away from the damage
			if (halting) {
				if (this.state == State.ATTACK) this.attack.Cut(this);
				else if (this.state == State.PARRY) this.parry.Cut(this);
			}

			this.Knockback(knockback);
		}

		this.nodeOverlayTrack.Play("blood_mid");
		this.ChangeHP(this.hp - damage);
	}

	//public wrapper function for Parried_(..)
	//Used to ensure that Parried_(..) is only called by the server in multiplayer
	//Passes straight through to Parried_(..) in singleplayer
	public void Parried(Attack attack, Parry by) {
		if (Lobby.role != Lobby.MultiplayerRole.OFFLINE) {
			if (Lobby.IsHost()) { //Only issue calls if host
				RpcUnreliable(nameof(this.Parried_ByIndex_), new object[] 
						{this.actions.IndexOf(attack), this.nodeEnemy.actions.IndexOf(by)});
			}
		} else {
			this.Parried_(attack, by);
		}
	}

	//Called when this players attack is parried
	//attack is this player's attack that was parried
	//by is the enemy's parry that stoppped this attack 
	private void Parried_(Attack attack, Parry by) {
		attack.Cut(this);
		//Call the enemies parry success
		by.Success(this.nodeEnemy);
		this.Knockback(by.knockback);
	}

	//Same as Parried_(Attack, Parry)
	//However instead uses the indexes, to allow for less data to be sent by over RPC
	//attackindex is the index of of the attack that was parried in this.actions
	//byindex is the index of of the parry in this.nodeEnemy.actions 
	private void Parried_ByIndex_(int attackindex, int byindex) {
		Attack attack = this.actions[attackindex] as Attack;
		Parry by = this.nodeEnemy.actions[byindex] as Parry;
		this.Parried_(attack, by);
	}

	//Returns the state of the given stance
	public State GetStateFromStance(Stance stance) {
		if (stance == STANCE_LOW) return State.LOW;
		else if (stance == STANCE_HIGH) return State.HIGH;
		else return State.LAX;
	}

	public Stance GetStanceFromState(State state) {
		if (state == State.LOW) return STANCE_LOW;
		else if (state == State.HIGH) return STANCE_HIGH;
		else return STANCE_LAX;
	}

	//Called when a transition has ended
	public void TransitionEnd() {
		this.state = this.endTrans;
		//Resets sprite
		//Might have to be changed later
		this.nodeAnimateSprite.Reset();
	}

	//Called when the player starts a transition
	public void Transition(string animation,  bool backwards = false) {
		//Ensures that player does not get stuck in transition if this called whilst the player is already transitioning
		if (this.state != State.TRANS) this.endTrans = this.state;
		this.state = State.TRANS;
		this.nodeAnimateSprite.Play(animation, backwards);
	}

	//Called when the player starts a transition
	public void TransitionTo(string animation, State state, bool backwards = false) {
		//Ensures that player does not get stuck in transition if this called whilst the player is already transitioning
		if (state != State.TRANS) this.endTrans = state;
		this.state = State.TRANS;
		this.nodeAnimateSprite.Play(animation, backwards);
	}

	//Only use this publiclly if you know what you're doing,
	//Normally stick to change state
	public void ChangeStance(Stance newStance, bool fromStance = false) {
		//ChangeStance is only required to provide transitions if moving from stance to stance
		//If moving from Action to Stance then the action is responsible for providing the transition
		if (fromStance) {
			//String concatenation and comparisons aren't phenomally fast I know, but it beats a mess of nested ifs
			//And this code will not be called very often (at max about twice a second I imagine)

			string transition = "trans_" + this.stance.Name() + "->" + newStance.Name();

			//Check for transition
			if (this.nodeAnimateSprite.Frames.HasAnimation(transition)) {
				this.Transition(transition);
			} else {
				//Check for oppsite transition
				transition = "trans_" + newStance.Name() + "->" + this.stance.Name();
				if (this.nodeAnimateSprite.Frames.HasAnimation(transition)) {
					this.Transition(transition, true); //Plays opposite backwards
				} else {
					this.nodeAnimateSprite.Play(newStance.sprite);
				}
			}
			
			this.stance = newStance;
		} else {
			//Not coming from stance, therefore transition will be handled by the current action
			this.stance = newStance;
			this.nodeAnimateSprite.Reset();
		}
	}

	//Used for RPC calls
	private void ActionStart(int actionIndex) {
		this.actions[actionIndex].Start(this);
	}

	//Starts to walk 
	private void WalkStart() {
		this.nodeAnimateSprite.Play("walk", this.IsWalkingBackwards());
		this.PlaySfx(this.level.footsteps);
		//Set volume separately to normal way
		this.nodeSfx.VolumeDb = this.FOOTSTEP_VOLUME; 
	}

	//Walk ends
	private void WalkEnd() {
		this.nodeAnimateSprite.Reset();
		if (this.sound == this.level.footsteps) {
			this.StopSfx();
		}
	}

	//Returns this players hitbox as a shape and a transform
	//Will return a 0 long line with a transform of 0 the player does not have one
	//Returns value of this.nodeAnimateSprite.GetHitbox(..)
	public (Shape2D, Transform2D) GetHitbox() {
		return this.nodeAnimateSprite.GetHitbox();
	}

	//True if the player is current walking in the opposite direction to that that they're facing
	public bool IsWalkingBackwards() {
		return Math.Sign(this.velocity.x) != this.SCALEFACTOR.x;
	}

	//Flips the player to face the opposite direction
	private void Flip() {
		this.Scale = this.SCALEFACTOR;
	}

	private void CalcMovement() {
		this.velocity = new Vector2();
		
		if (Input.IsActionPressed(this.ACTION_RIGHT))
			this.velocity.x += SPEED;
			
		if (Input.IsActionPressed(this.ACTION_LEFT))
			this.velocity.x -= SPEED;
	}
	
	Node2D CameraTrack.Trackable.GetTrackingNode() {
		//Gets camera to play nice
		return this.nodeCollision;
	}

	public class Stance {
		public string sprite;
		private string name {get;}

		public Stance(string name, string sprite) {
			this.name = name;
			this.sprite = sprite;
		}

		public string Name() {return name;}
	}
}}
