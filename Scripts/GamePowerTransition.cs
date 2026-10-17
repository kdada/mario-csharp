using Godot;

namespace MarioStudy;

public partial class Game
{
    private const float PowerTransitionDuration = .72f;
    private float powerTransitionClock;
    private int powerBeforeTransition;
    private int VisiblePower => State == Screen.Transform && (int)((PowerTransitionDuration - powerTransitionClock) * 12) % 2 == 0 ? powerBeforeTransition : Power;
    private void BeginPowerTransition(int target)
    {
        if (target == Power) return;
        powerBeforeTransition = Power; Power = target;
        Crouching = Crouching || !World.Headroom(Player, 32);
        Player.Height = Crouching ? 16 : 32;
        powerTransitionClock = PowerTransitionDuration; State = Screen.Transform;
        Sound.Pause(true);
    }
    private void TickPowerTransition(float dt)
    {
        powerTransitionClock = MathF.Max(0, powerTransitionClock - dt);
        if (powerTransitionClock == 0) { State = Screen.Playing; Sound.Pause(false); }
    }
}
