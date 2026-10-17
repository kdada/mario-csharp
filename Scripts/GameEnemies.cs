using Godot;

namespace MarioStudy;

public partial class Game
{
    private void TickEnemies(float dt)
    {
        // New enemies spawned by generators are added after enumeration.
        var spawned = new List<Enemy>();
        foreach (var e in World.Enemies)
        {
            e.Simulated = false;
            if (e.Removed) continue;
            if (e.Dead)
            {
                e.Clock += dt;
                e.Timer -= dt;
                if (e.Squashed) { if (e.Timer <= 0) e.Removed = true; }
                else { e.Body.Velocity += new Vector2(0, 800 * dt); e.Body.Position += e.Body.Velocity * dt; if (e.Body.Position.Y > 80) e.Removed = true; }
                continue;
            }
            if (!e.Active && e.Body.Position.X >= CameraX - 80 && e.Body.Position.X < CameraX + 280) e.Active = true;
            if (!e.Active) continue;
            if (e.Body.Position.Y > 96) { e.Removed = true; continue; }
            // Preserve living enemies when backtracking; suspend distant simulation.
            if (e.Body.Position.X < CameraX - 80 || e.Body.Position.X > CameraX + 336) continue;
            if (e.Kind == "PiranhaPlant" && e.PipeSuppressed)
            {
                ResetHiddenPlant(e);
                if (pipeClock > 0 || MathF.Abs(Player.Position.X - e.Home.X) < 32) continue;
                e.PipeSuppressed = false;
            }
            e.PreviousBox = e.Body.Box; e.Simulated = true;
            e.Clock += dt;
            float speed = e.Shell ? e.Kicked ? 220 : 0 : 30;
            if (e.Kind is "GreenKoopaTroopa" or "RedKoopaTroopa" or "BuzzyBeetle" or "Goomba" or "Spiny")
            {
                if (e.Kind == "RedKoopaTroopa" && !e.Shell && !e.Winged && e.Body.Grounded && !World.SolidAt(e.Body.Position + new Vector2(e.Direction * 9, 2))) e.Direction *= -1;
                if (e.Winged && e.Kind == "RedKoopaTroopa")
                {
                    e.Body.Position = e.Home + new Vector2(0, MathF.Sin(e.Clock * 1.5f + e.Phase) * 48);
                }
                else
                {
                    if (e.Winged && e.Body.Grounded) e.Body.Velocity = new(e.Body.Velocity.X, -210);
                    e.Body.Velocity = new(e.Direction * speed, MathF.Min(270, e.Body.Velocity.Y + 900 * dt));
                    World.Move(e.Body, dt);
                    if (e.Body.HitWall) { e.Direction *= -1; if (e.Kicked) Sound.Effect("Bump"); }
                }
                if (e.Shell && !e.Kicked)
                {
                    e.Timer -= dt;
                    if (e.Timer <= 0) { e.Shell = false; e.Body.Height = 24; e.Direction = Player.Position.X < e.Body.Position.X ? 1 : -1; }
                }
            }
            else if (e.Kind == "PiranhaPlant")
            {
                float cycle = (e.Clock + e.Phase) % 4.0f;
                bool near = MathF.Abs(Player.Position.X - e.Home.X) < 24;
                float rise = cycle < 1 ? cycle : cycle < 2 ? 1 : cycle < 3 ? 3 - cycle : 0;
                if (near && e.Body.Position.Y >= e.Home.Y + 23) rise = 0;
                e.Body.Position = e.Home + new Vector2(0, 24 * (1 - rise)); e.Body.Height = 24;
            }
            else if (e.Kind == "Blooper")
            {
                float cycle = (e.Clock + e.Phase) % 2.4f;
                e.Body.Velocity = cycle < .9f ? new(MathF.Sign(Player.Position.X - e.Body.Position.X) * 38, -42) : new(0, 27);
                e.Body.Position += e.Body.Velocity * dt;
                e.Body.Position = new(e.Body.Position.X, Math.Clamp(e.Body.Position.Y, -158, -16));
            }
            else if (e.Kind is "GreenCheepCheep" or "RedCheepCheep")
            {
                e.Body.Position += new Vector2(-32 * dt, MathF.Sin(e.Clock * 2 + e.Phase) * 10 * dt);
            }
            else if (e.Kind == "FlyingFish")
            {
                e.Body.Velocity += new Vector2(0, 350 * dt); e.Body.Position += e.Body.Velocity * dt;
                if (e.Body.Position.Y > 40 && e.Body.Velocity.Y > 0) e.Removed = true;
            }
            else if (e.Kind == "Podoboo")
            {
                float cycle = (e.Clock + e.Phase) % 3.3f;
                if (cycle < 1.4f) e.Body.Position = new(e.Home.X, e.Home.Y + 32 - 350 * cycle + 250 * cycle * cycle);
                else e.Body.Position = e.Home + new Vector2(0, 48);
            }
            else if (e.Kind == "BulletBill") e.Body.Position += new Vector2(e.Direction * 120 * dt, 0);
            else if (e.Kind == "BowserFlame") e.Body.Position += new Vector2(-90 * dt, 0);
            else if (e.Kind == "HammerBro")
            {
                e.Timer -= dt;
                e.Body.Velocity = new(MathF.Sin(e.Clock * 2) * 25, MathF.Min(270, e.Body.Velocity.Y + 850 * dt));
                if (e.Body.Grounded && (int)(e.Clock * 60) % 145 == 0) e.Body.Velocity = new(e.Body.Velocity.X, -300);
                World.Move(e.Body, dt);
                if (e.Timer <= 0) { e.Timer = .8f + (float)random.NextDouble() * .5f; ThrowHammer(e); }
            }
            else if (e.Kind.Contains("Bowser"))
            {
                e.Timer -= dt;
                e.Body.Velocity = new(MathF.Sin(e.Clock * 1.7f) * 24, MathF.Min(250, e.Body.Velocity.Y + 800 * dt));
                if (e.Body.Grounded && MathF.Sin(e.Clock * 1.7f) > .98f) e.Body.Velocity = new(e.Body.Velocity.X, -210);
                World.Move(e.Body, dt);
                e.Body.Position = new(Math.Clamp(e.Body.Position.X, e.Home.X - 48, e.Home.X + 24), e.Body.Position.Y);
                if (e.Timer <= 0)
                {
                    e.Timer = 1.3f + (float)random.NextDouble() * 1.4f;
                    var flame = NewEnemy("BowserFlame", e.Body.Position + new Vector2(-16, -14)); flame.Active = true; spawned.Add(flame); Sound.Effect("BowserFire");
                    if (LevelIndex >= 20) ThrowHammer(e);
                }
            }
            else if (e.Kind == "Lakitu")
            {
                e.Body.Position = new(Mathf.Lerp(e.Body.Position.X, Player.Position.X + 72, 1 - MathF.Exp(-dt * 2)), -152 + MathF.Sin(e.Clock * 2) * 8);
                e.Timer -= dt;
                if (e.Timer <= 0) { e.Timer = 3.2f; var spiny = NewEnemy("Spiny", e.Body.Position + new Vector2(0, 16)); spiny.Body.Velocity = new(-30, -70); spiny.Active = true; spawned.Add(spiny); Sound.Effect("LakituThrow"); }
            }
        }
        ResolveEnemyCollisions();
        foreach (var e in World.Enemies.OrderBy(e => e.Body.Box.Position.Y).ThenBy(e => e.Body.Position.X))
        {
            if (State != Screen.Playing) break;
            if (!e.Simulated || e.Dead || e.Removed || e.PipeSuppressed || e.Kind == "PiranhaPlant" && e.Body.Position.Y >= e.Home.Y + 22) continue;
            if (Player.Box.Intersects(e.Body.Box)) TouchEnemy(e);
        }
        World.Enemies.AddRange(spawned);
        TickGenerators(dt);
    }
    private Enemy NewEnemy(string kind, Vector2 position)
    {
        var e = new Enemy { Kind = kind, Home = position }; e.Body.Position = position; return e;
    }
    private int ShellScore(int count) => count switch { 1 => 500, 2 => 800, 3 => 1000, 4 => 2000, 5 => 4000, 6 => 5000, 7 => 8000, _ => 10000 };
    public void TouchEnemy(Enemy e)
    {
        if (e.Dead || e.Removed || e.PipeSuppressed) return;
        if (StarTime > 0 && e.Kind is not ("Podoboo" or "BowserFlame")) { DefeatEnemy(e, true, 100); return; }
        bool stompable = e.Kind is "Goomba" or "GreenKoopaTroopa" or "RedKoopaTroopa" or "BuzzyBeetle" or "HammerBro" or "Lakitu" or "BulletBill" or "FlyingFish";
        bool above = Player.Velocity.Y > 0 && Player.Position.Y <= e.Body.Box.Position.Y + MathF.Max(7, Player.Velocity.Y / 60 + 3);
        if (stompable && above && !World.IsWater)
        {
            Player.Position = new(Player.Position.X, e.Body.Box.Position.Y); Player.Velocity = new(Player.Velocity.X, -185);
            Sound.Effect("Stomp"); stompCombo++;
            if (stompCombo >= 8) { Lives++; Sound.Effect("1up"); Popup("1UP", e.Body.Position - new Vector2(12, 24)); }
            if (e.Winged) { e.Winged = false; Score += 100; return; }
            if (e.Kind.Contains("Koopa") || e.Kind == "BuzzyBeetle")
            {
                if (!e.Shell) { e.Shell = true; e.Kicked = false; e.Timer = 7; e.Body.Height = 14; Score += 100; }
                else if (e.Kicked) { e.Kicked = false; e.Timer = 7; }
                else KickShell(e);
            }
            else DefeatEnemy(e, false, Math.Min(8000, 100 << Math.Min(6, stompCombo - 1)));
            return;
        }
        if (e.Shell && !e.Kicked)
        {
            KickShell(e); Player.Position += new Vector2(Player.Position.X < e.Body.Position.X ? -3 : 3, 0); return;
        }
        Hurt();
    }
    private void KickShell(Enemy e)
    {
        e.Kicked = true; e.Direction = Player.Position.X < e.Body.Position.X ? 1 : -1;
        e.Body.Position += new Vector2(e.Direction * 5, 0); Score += 400; Sound.Effect("Kick");
    }
    public void DefeatEnemy(Enemy e, bool thrown, int points = 100)
    {
        if (e.Dead || e.Removed) return;
        e.Dead = true; e.Flipped = thrown; e.Winged = false; e.Squashed = !thrown && e.Kind == "Goomba"; e.Timer = .45f;
        e.Body.Velocity = new(50 * e.Direction, -150); Score += points;
        Popup(points.ToString(), e.Body.Position - new Vector2(12, 20)); if (thrown) Sound.Effect("Kick");
    }
    private void ThrowHammer(Enemy e)
    {
        var hammer = new FireShot { Enemy = true, Hammer = true };
        hammer.Body.Position = e.Body.Position + new Vector2(0, -24); hammer.Body.Velocity = new(Player.Position.X < e.Body.Position.X ? -80 : 80, -230);
        World.Shots.Add(hammer);
    }
    private void TickShots(float dt)
    {
        foreach (var shot in World.Shots)
        {
            if (shot.Removed) continue;
            shot.Age += dt;
            if (shot.Enemy)
            {
                if (shot.Hammer) shot.Body.Velocity += new Vector2(0, 560 * dt);
                shot.Body.Position += shot.Body.Velocity * dt;
                if (shot.Body.Box.Intersects(Player.Box)) Hurt();
            }
            else
            {
                shot.Body.Velocity += new Vector2(0, 700 * dt);
                float vx = shot.Body.Velocity.X;
                World.Move(shot.Body, dt, platforms: false);
                if (shot.Body.Grounded) shot.Body.Velocity = new(vx, -170);
                if (shot.Body.HitWall || shot.Body.HitCeiling) { shot.Removed = true; Sound.Effect("Bump"); }
                foreach (var e in World.Enemies)
                    if (!e.Dead && !e.Removed && !e.PipeSuppressed && shot.Body.Box.Intersects(e.Body.Box))
                    {
                        shot.Removed = true;
                        if (e.Kind is "BuzzyBeetle" or "Podoboo" or "BowserFlame") break;
                        if (e.Kind.Contains("Bowser")) { if (--e.Health <= 0) DefeatEnemy(e, true, 5000); }
                        else DefeatEnemy(e, true, 200);
                        break;
                    }
            }
            if (shot.Age > 5 || shot.Body.Position.Y > 64 || shot.Body.Position.X < CameraX - 32 || shot.Body.Position.X > CameraX + 300) shot.Removed = true;
        }
        World.Shots.RemoveAll(s => s.Removed);
    }
    private readonly Dictionary<string, float> generatorClocks = [];
    private void TickGenerators(float dt)
    {
        foreach (var trigger in World.Triggers)
        {
            string k = trigger.Kind;
            if (k == "EntityGeneratorStopper" || !(k.Contains("Generator") || k == "BulletBillCannon") || trigger.X > CameraX + 256 || trigger.X < CameraX - 256) continue;
            string id = World.Data.Id + ":" + trigger.Name;
            float timer = generatorClocks.GetValueOrDefault(id, 2) - dt;
            if (timer > 0) { generatorClocks[id] = timer; continue; }
            generatorClocks[id] = k.Contains("Cheep") ? 1.6f : 3.5f;
            if (k.Contains("Cheep"))
            {
                var fish = NewEnemy(World.IsWater ? "RedCheepCheep" : "FlyingFish", new(CameraX + 32 + (float)random.NextDouble() * 224, 24));
                fish.Body.Velocity = new(40 + (float)random.NextDouble() * 80, -270 - (float)random.NextDouble() * 60); fish.Active = true; World.Enemies.Add(fish);
            }
            else if (k == "BowserFlameGenerator")
            {
                var flame = NewEnemy("BowserFlame", new(CameraX + 280, -40 - random.Next(3) * 24)); flame.Active = true; World.Enemies.Add(flame);
            }
            else
            {
                if (MathF.Abs(Player.Position.X - trigger.X) < 24) continue;
                var bullet = NewEnemy("BulletBill", k == "BulletBillCannon" ? trigger.Position + new Vector2(0, 8) : new(CameraX + 280, -48 - random.Next(3) * 24));
                bullet.Active = true; bullet.Direction = bullet.Body.Position.X > Player.Position.X ? -1 : 1; World.Enemies.Add(bullet); Sound.Effect("Cannon");
            }
        }
    }
}
