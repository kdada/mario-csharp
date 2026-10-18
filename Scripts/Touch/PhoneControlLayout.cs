using Godot;

namespace MarioStudy;

/// <summary>One viewport-space geometry shared by phone rendering and touch hit testing.</summary>
internal sealed class PhoneControlLayout
{
    public const int BaseWidth = 512, BaseHeight = 240;
    public readonly Vector2 Viewport;
    public readonly float Scale;
    public readonly Rect2 Game;
    public readonly Rect2 LeftPanel, RightPanel;
    public readonly Rect2[] Buttons;
    public readonly Rect2 RunJumpBridge;

    private PhoneControlLayout(Vector2 viewport, float scale, Rect2 game, Rect2[] buttons)
    {
        Viewport = viewport; Scale = scale; Game = game; Buttons = buttons;
        LeftPanel = new(Vector2.Zero, new Vector2(game.Position.X, viewport.Y));
        RightPanel = new(new Vector2(game.End.X, 0), new Vector2(viewport.X - game.End.X, viewport.Y));
        RunJumpBridge = buttons[3].Merge(buttons[4]);
    }

    public static PhoneControlLayout Create(Vector2 viewport)
    {
        viewport = new(MathF.Max(1, viewport.X), MathF.Max(1, viewport.Y));
        float scale = MathF.Min(viewport.X / BaseWidth, viewport.Y / BaseHeight);
        Vector2 gameSize = new Vector2(256, 240) * scale;
        Rect2 game = new(((viewport - gameSize) / 2).Floor(), gameSize);
        float left = game.Position.X / 2, right = (game.End.X + viewport.X) / 2;
        float middle = Math.Clamp(viewport.Y * .68f, 88 * scale, viewport.Y - 40 * scale);
        Rect2 Box(float x, float y, float size) => new(new Vector2(x, y) - Vector2.One * size * scale / 2, Vector2.One * size * scale);
        // Indices match Game's TouchLeft/Right/Down/Run/Jump/Pause/Up constants.
        Rect2[] buttons =
        [
            Box(left - 36 * scale, middle, 32), Box(left + 36 * scale, middle, 32),
            Box(left, middle + 36 * scale, 32), Box(right - 24 * scale, middle + 22 * scale, 40),
            Box(right + 24 * scale, middle - 20 * scale, 44), Box(right, 40 * scale, 30),
            Box(left, middle - 36 * scale, 32)
        ];
        return new(viewport, scale, game, buttons);
    }
}
