using Godot;

namespace MarioStudy;

public partial class Game
{
    private Rect2 playerContactStart;
    private World? playerContactWorld;
    private bool playerContactCaptured;
    private void CapturePlayerContact()
    {
        playerContactStart = Player.Box;
        playerContactWorld = World;
        playerContactCaptured = true;
    }
    private static bool PlayerEnemyContact(Rect2 oldPlayer, Rect2 player, Rect2 oldEnemy, Rect2 enemy, out bool above)
    {
        // Crouching/standing changes the head position, not the feet trajectory.
        oldPlayer = new(oldPlayer.Position.X, oldPlayer.End.Y - player.Size.Y, player.Size.X, player.Size.Y);
        bool contact = BodyContact(oldPlayer, player, oldEnemy, enemy, out bool side, out _);
        float relativeFeet = (player.End.Y - oldPlayer.End.Y) - (enemy.Position.Y - oldEnemy.Position.Y);
        above = contact && !side && relativeFeet > .00001f && oldPlayer.End.Y <= oldEnemy.Position.Y + .01f;
        return contact;
    }
    private static bool WalkingEnemy(Enemy e) => e.Kind is "Goomba" or "GreenKoopaTroopa" or "RedKoopaTroopa" or "BuzzyBeetle" or "Spiny" && !(e.Winged && e.Kind == "RedKoopaTroopa");
    private static bool RestingShell(Enemy e) => e.Shell && !e.Kicked;
    private void ResolveEnemyCollisions()
    {
        var active = World.Enemies.Where(e => e.Simulated && !e.Dead && !e.Removed).ToArray();
        // Resolve projectile-like shells by impact time, independent of list order.
        var shellHits = new List<(Enemy a, Enemy b, float time)>();
        for (int i = 0; i < active.Length; i++) for (int j = i + 1; j < active.Length; j++)
        {
            var a = active[i]; var b = active[j];
            if (!(a.Shell && a.Kicked || b.Shell && b.Kicked) || a.Kind == "PiranhaPlant" || b.Kind == "PiranhaPlant") continue;
            if (EnemyContact(a, b, out _, out float time)) shellHits.Add((a, b, time));
        }
        foreach (var hit in shellHits.OrderBy(h => h.time).ThenBy(h => MathF.Min(h.a.PreviousBox.Position.X, h.b.PreviousBox.Position.X)).ThenBy(h => MathF.Min(h.a.PreviousBox.Position.Y, h.b.PreviousBox.Position.Y)))
        {
            Enemy a = hit.a, b = hit.b;
            if (a.Dead || b.Dead || a.Removed || b.Removed) continue;
            bool kickA = a.Shell && a.Kicked, kickB = b.Shell && b.Kicked;
            if (kickA && kickB) { DefeatEnemy(a, true, ShellScore(++b.Combo)); DefeatEnemy(b, true, ShellScore(++a.Combo)); }
            else { Enemy shell = kickA ? a : b, other = kickA ? b : a; DefeatEnemy(other, true, ShellScore(++shell.Combo)); }
        }
        var handled = new HashSet<(Enemy, Enemy)>();
        for (int pass = 0; pass < 8; pass++)
            for (int i = 0; i < active.Length; i++)
                for (int j = i + 1; j < active.Length; j++)
                {
                    Enemy a = active[i], b = active[j];
                    if (a.Dead || b.Dead || a.Removed || b.Removed) continue;
                    if (a.Shell && a.Kicked || b.Shell && b.Kicked || !WalkingEnemy(a) || !WalkingEnemy(b)) continue;
                    if (!EnemyContact(a, b, out bool side, out _) || !side) continue;
                    bool first = handled.Add((a, b));
                    Enemy left = a.PreviousBox.GetCenter().X <= b.PreviousBox.GetCenter().X ? a : b, right = ReferenceEquals(left, a) ? b : a;
                    float relative = (left.Body.Position.X - left.PreviousBox.GetCenter().X) - (right.Body.Position.X - right.PreviousBox.GetCenter().X);
                    if (first && relative >= -.001f)
                    {
                        int ld = RestingShell(left) ? 0 : left.Direction, rd = RestingShell(right) ? 0 : right.Direction;
                        if (ld > 0 && rd < 0) { TurnEnemy(left); TurnEnemy(right); }
                        else if (ld > 0 && rd >= 0) TurnEnemy(left);
                        else if (rd < 0 && ld <= 0) TurnEnemy(right);
                    }
                    float overlap = left.Body.Box.End.X - right.Body.Box.Position.X;
                    if (overlap < 0) continue;
                    float distance = overlap + .02f;
                    float share = RestingShell(left) ? 0 : RestingShell(right) ? 1 : .5f;
                    NudgeEnemy(left, -distance * share); NudgeEnemy(right, distance * (1 - share));
                    float remainder = left.Body.Box.End.X - right.Body.Box.Position.X + .01f;
                    if (remainder > 0)
                    {
                        if (!RestingShell(left)) NudgeEnemy(left, -remainder);
                        remainder = left.Body.Box.End.X - right.Body.Box.Position.X + .01f;
                        if (remainder > 0 && !RestingShell(right)) NudgeEnemy(right, remainder);
                    }
                }
    }
    private static void TurnEnemy(Enemy enemy)
    {
        enemy.Direction *= -1;
        enemy.Body.Velocity = new(enemy.Direction * (enemy.Shell ? 0 : 30), enemy.Body.Velocity.Y);
    }
    private void NudgeEnemy(Enemy enemy, float amount)
    {
        if (amount == 0) return;
        Rect2 before = enemy.Body.Box;
        Rect2 after = new(before.Position + new Vector2(amount, 0), before.Size);
        float x = enemy.Body.Position.X + amount;
        foreach (var (box, _) in World.Colliders(before.Merge(after), false))
        {
            if (box.Position.Y >= before.End.Y || box.End.Y <= before.Position.Y) continue;
            if (amount < 0 && box.End.X <= before.Position.X + .001f) x = MathF.Max(x, box.End.X + enemy.Body.Width / 2);
            else if (amount > 0 && box.Position.X >= before.End.X - .001f) x = MathF.Min(x, box.Position.X - enemy.Body.Width / 2);
        }
        enemy.Body.Position = new(x, enemy.Body.Position.Y);
    }
    private static bool EnemyContact(Enemy a, Enemy b, out bool side, out float impactTime) =>
        BodyContact(a.PreviousBox, a.Body.Box, b.PreviousBox, b.Body.Box, out side, out impactTime);
    private static bool BodyContact(Rect2 oldA, Rect2 currentA, Rect2 oldB, Rect2 currentB, out bool side, out float impactTime)
    {
        impactTime = 0;
        Vector2 delta = (currentA.Position - oldA.Position) - (currentB.Position - oldB.Position);
        if (oldA.Intersects(oldB))
        {
            float x = MathF.Min(oldA.End.X, oldB.End.X) - MathF.Max(oldA.Position.X, oldB.Position.X);
            float y = MathF.Min(oldA.End.Y, oldB.End.Y) - MathF.Max(oldA.Position.Y, oldB.Position.Y);
            side = x <= y;
            return currentA.Intersects(currentB);
        }
        float enterX = float.NegativeInfinity, exitX = float.PositiveInfinity, enterY = enterX, exitY = exitX;
        side = false;
        if (!ContactAxis(oldA.Position.X, oldA.End.X, oldB.Position.X, oldB.End.X, delta.X, ref enterX, ref exitX) ||
            !ContactAxis(oldA.Position.Y, oldA.End.Y, oldB.Position.Y, oldB.End.Y, delta.Y, ref enterY, ref exitY)) return false;
        float enter = MathF.Max(enterX, enterY), exit = MathF.Min(exitX, exitY);
        side = enterX >= enterY; impactTime = enter;
        return enter >= 0 && enter <= 1 && enter <= exit;
    }
    private static bool ContactAxis(float a0, float a1, float b0, float b1, float delta, ref float enter, ref float exit)
    {
        if (MathF.Abs(delta) < .00001f) return a1 > b0 && a0 < b1;
        float t0 = (b0 - a1) / delta, t1 = (b1 - a0) / delta;
        enter = MathF.Min(t0, t1); exit = MathF.Max(t0, t1); return true;
    }
}
