using Godot;

namespace MarioStudy;

public partial class Game
{
    private static readonly string[] NormalPauseOptions = ["RESUME", "RESTART LEVEL", "LEVEL SELECT", "TITLE"];
    private static readonly string[] PracticePauseOptions = ["RESUME", "RESTART LEVEL", "LEVEL SELECT", "TITLE", "BIG MARIO", "FIRE MARIO"];
    private string[] PauseOptions => practice ? PracticePauseOptions : NormalPauseOptions;

    private void ApplyPracticePower(int target)
    {
        if (!practice || State != Screen.Pause || target is not (1 or 2)) return;
        Power = target;
        Crouching = Crouching || !World.Headroom(Player, 32);
        Player.Height = Crouching ? 16 : 32;
        TogglePause();
        ReleaseTouchControls();
    }
}
