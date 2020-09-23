using Godot;
using System;

public class SoundEffect : Resource
{
    //A random one of these will be played
    [Export] public AudioStream[] streams;

    [Export] public bool repeat;
}
