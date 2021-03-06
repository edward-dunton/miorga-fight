using Godot;
using System;

namespace MiorgaFight {

public abstract class Action : Resource {
    public enum Type {
        ATTACK,
        PARRY,
        OVERLAY,
        VOID
    }

    //Trigger data 
    //Input action that causes action
    [Export] public String triggerInput;

    //Animation which must be playing for action to be triggered
    [Export] public String triggerAnimation;

    //Frames which must be playing in order for action to be triggered
    //Set min to -1 to allow for any trigger
    [Export] public int triggerMinFrame;
    [Export] public int triggerMaxFrame;

    [Export] public String animation;

    //Movement performed once the parry has concluded
    [Export] public Vector2 movement;

    //Transition animation
    [Export] public String transition;

    //State to move to when transtition is finished
    [Export] public Player.State transitionTo;

    //Whether the transition should be animated (if this is true transition will be ignored)
    [Export] public bool animateTransition;
    
    //Alternate option, enters another action when this action has finished (having this set will ignore transition, 
    //transitionTo and AnimateTransition)
    [Export] public Action transitionAction;

    public Type type;

    public abstract void Start(Player player);

    //Cuts an attack short, stopping it where it is
    //Does not play the transition animation
    public void Cut(Player player) {
        player.ChangeState(player.GetStateFromStance(player.stance));
    }

    public void End(Player player) {
        player.MoveAndCollide(this.movement * player.SCALEFACTOR);
    
        //Check for ending action
        if (this.transitionAction != null) {
            this.transitionAction.Start(player);
        } else if (this.transitionTo != Player.State.NONE) {
            //player.ChangeStance(player.GetStanceFromState(this.transitionTo), this.animateTransition);
            if (this.animateTransition) {
                player.stance = player.GetStanceFromState(this.transitionTo);
                player.TransitionTo(transition, this.transitionTo);
            } else {
                //Transition without animation
                player.ChangeState(this.transitionTo);
            }
        } else {
            player.ChangeState(player.GetStateFromStance(player.stance));
        }   
    }

    //Returns whether all trigger conditions have been met or not
    public bool IsPossible(Player player, InputEvent input) {
        //I don't like the way this works

        //For those actions which are not possible
        if (this.triggerInput == "" || this.triggerInput == null) return false;

        //Colossal return statement
        //Sorry
        return (input.IsActionPressed(player.inputPrefix + this.triggerInput) && //Correct action has been pressed 
            player.nodeAnimateSprite.Animation == this.triggerAnimation && //Correct animation is playing
            ((this.triggerMinFrame == -1) || //Checks if min frame is set to -1
                (player.nodeAnimateSprite.Frame >= this.triggerMinFrame && //OR players current frame >= min frame 
                player.nodeAnimateSprite.Frame <= this.triggerMaxFrame))); //AND players current frame ,= max frame
    }
}}
