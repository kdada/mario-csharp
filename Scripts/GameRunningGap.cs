using Godot;

namespace MarioStudy;

public partial class Game
{
    private const float RunningGapTolerance = .001f;
    private void MovePlayerWithRunningGap(float dt, bool wasGrounded, bool jumpPressed)
    {
        // A 12px body briefly loses tile contact over a 16px gap. Keep the feet
        // level before collision stepping, so gravity cannot turn the far bank
        // into a side wall. Horizontal motion is still performed exactly once.
        float feet = Player.Position.Y;
        bool supportedGap = CanRunAcrossGap(dt, wasGrounded, jumpPressed);
        if (supportedGap) Player.Velocity = new(Player.Velocity.X, 0);
        World.Move(Player, dt, HitBlock, cornerCorrection: true);
        if (supportedGap && Player.Support == null && MathF.Abs(Player.Position.Y - feet) <= RunningGapTolerance)
        {
            Player.Grounded = true;
            Player.Velocity = new(Player.Velocity.X, 0);
        }
    }
    private bool CanRunAcrossGap(float dt, bool wasGrounded, bool jumpPressed)
    {
        if (!wasGrounded || jumpPressed || World.IsWater || Player.Support != null || Player.Velocity.Y < 0 || MathF.Abs(Player.Velocity.X) <= 100 || dt <= 0)
            return false;
        float feet = Player.Position.Y, halfWidth = Player.Width / 2;
        float nextX = Player.Position.X + Player.Velocity.X * dt;
        float centerLeft = MathF.Min(Player.Position.X, nextX), centerRight = MathF.Max(Player.Position.X, nextX);
        float sweepLeft = centerLeft - halfWidth, sweepRight = centerRight + halfWidth;
        Rect2 probe = new(sweepLeft - 16, feet - RunningGapTolerance, sweepRight - sweepLeft + 32, RunningGapTolerance * 2);
        // Colliders exposes solid terrain and solid blocks, never moving platforms.
        // Reject buried tops (a higher bank is a wall), then merge neighboring
        // exposed surfaces before measuring the actual empty interval.
        var solids = World.Colliders(probe, false).Select(c => c.box).Where(box => box.Intersects(probe)).ToArray();
        var surfaces = solids.Where(box => MathF.Abs(box.Position.Y - feet) <= RunningGapTolerance &&
                !solids.Any(above => above.Position.Y < feet - RunningGapTolerance && above.End.Y > feet - RunningGapTolerance &&
                    above.End.X > box.Position.X + RunningGapTolerance && above.Position.X < box.End.X - RunningGapTolerance))
            .OrderBy(box => box.Position.X);
        var banks = new List<(float Left, float Right)>();
        foreach (var box in surfaces)
        {
            if (banks.Count > 0 && box.Position.X <= banks[^1].Right + RunningGapTolerance)
            {
                var bank = banks[^1]; banks[^1] = (bank.Left, MathF.Max(bank.Right, box.End.X));
            }
            else banks.Add((box.Position.X, box.End.X));
        }
        for (int i = 1; i < banks.Count; i++)
        {
            var left = banks[i - 1]; var right = banks[i];
            float gap = right.Left - left.Right;
            if (gap > 16 + RunningGapTolerance) continue;
            if (sweepRight <= left.Right || sweepLeft >= right.Left) continue;
            // Both ends of the entire step must remain over these banks or their
            // bounded gap. A nearby small hole cannot mask a cliff beyond it.
            if (centerLeft <= left.Left - halfWidth + RunningGapTolerance || centerRight >= right.Right + halfWidth - RunningGapTolerance) continue;
            return true;
        }
        return false;
    }
}
