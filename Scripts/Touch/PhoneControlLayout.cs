using Godot;

namespace MarioStudy;

/// <summary>Viewport-space geometry shared by phone drawing and touch hit testing.</summary>
internal sealed class PhoneControlLayout
{
    public const int BaseWidth = 512, BaseHeight = 240;
    public readonly Vector2 Viewport;
    public readonly float Scale;
    public readonly Rect2 Game;
    public readonly Rect2 LeftPanel, RightPanel, MovementPad, JumpRow;
    public readonly Rect2[] Buttons;

    private PhoneControlLayout(Vector2 viewport, float scale, Rect2 game, Rect2[] buttons)
    {
        Viewport = viewport; Scale = scale; Game = game; Buttons = buttons;
        LeftPanel = new(Vector2.Zero, new Vector2(game.Position.X, viewport.Y));
        RightPanel = new(new Vector2(game.End.X, 0), new Vector2(viewport.X - game.End.X, viewport.Y));
        MovementPad = buttons[0].Merge(buttons[3]);
        JumpRow = buttons[4].Merge(buttons[5]);
    }

    public static PhoneControlLayout Create(Vector2 viewport)
    {
        viewport = new(MathF.Max(1, viewport.X), MathF.Max(1, viewport.Y));
        float scale = MathF.Min(viewport.X / BaseWidth, viewport.Y / BaseHeight);
        Vector2 gameSize = new Vector2(256, 240) * scale;
        Rect2 game = new(((viewport - gameSize) / 2).Floor(), gameSize);
        float left = game.Position.X / 2, right = (game.End.X + viewport.X) / 2;
        float middle = Math.Clamp(viewport.Y * .68f, 88 * scale, viewport.Y - 56 * scale);
        float x = left - 52 * scale, y = middle - 48 * scale;
        Vector2 movementSize = new Vector2(52, 48) * scale;
        Rect2 Action(int column, int row) => new(new Vector2(right - 52 * scale + column * 56 * scale,
            middle - 52 * scale + row * 56 * scale), Vector2.One * 48 * scale);
        // Left grid shares exact edges: walking above, running below; no gaps to lose a held direction.
        // Indices match TouchLeft/Right/RunLeft/RunRight/Jump/BigJump/Down/Fire/Pause.
        Rect2[] buttons =
        [
            new(new(x, y), movementSize), new(new(x + movementSize.X, y), movementSize),
            new(new(x, y + movementSize.Y), movementSize), new(new(x + movementSize.X, y + movementSize.Y), movementSize),
            Action(0, 0), Action(1, 0), Action(0, 1), Action(1, 1),
            new(new Vector2(right - 15 * scale, 9 * scale), Vector2.One * 30 * scale)
        ];
        return new(viewport, scale, game, buttons);
    }
}
