using Godot;

namespace MarioStudy;

public partial class Game
{
    private void TickEnemies(float dt)
    {
        // Direct mechanics checks can tick enemies without a preceding player tick.
        Rect2 oldPlayer = playerContactCaptured && ReferenceEquals(playerContactWorld, World)
            ? playerContactStart : new(Player.Box.Position - Player.Velocity * dt, Player.Box.Size);
        Rect2 player = Player.Box;
        playerContactCaptured = false;
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
                // Each plant starts with a full hidden interval and advances only
                // while this room/enemy is simulated; global time and Phase cannot jump it.
                bool near = MathF.Abs(Player.Position.X - e.Home.X) < 24;
                if (near && e.Body.Position.Y >= e.Home.Y + 23) ResetHiddenPlant(e);
                else
                {
                    float cycle = e.Clock %= 4;
                    float rise = cycle < 1 ? 0 : cycle < 2 ? cycle - 1 : cycle < 3 ? 1 : 4 - cycle;
                    e.Body.Position = e.Home + new Vector2(0, 24 * (1 - rise)); e.Body.Height = 24;
                }
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
                if (World.IsWater)
                {
                    Vector2 before = e.Body.Position;
                    e.Body.Velocity = new(e.Direction * 32, MathF.Sin(e.Clock * 2 + e.Phase) * 10);
                    World.Move(e.Body, dt, platforms: false);
                    bool turn = e.Body.HitWall;
                    if (!WaterFishOpen(e.Body.Box, WaterFishPopulationForRoom())) { e.Body.Position = before; turn = true; }
                    if (turn) e.Direction *= -1;
                }
                else
                {
                    e.Body.Velocity = new(e.Direction * 32, MathF.Sin(e.Clock * 2 + e.Phase) * 10);
                    e.Body.Position += e.Body.Velocity * dt;
                }
            }
            else if (e.Kind == "FlyingFish")
            {
                e.Body.Velocity += new Vector2(0, 350 * dt); e.Body.Position += e.Body.Velocity * dt;
                if (e.Body.Position.Y > 40 && e.Body.Velocity.Y > 0) e.Removed = true;
            }
            else if (e.Kind == "Podoboo")
            {
                // Rest below the lava before each complete up/down arc. The
                // map marker remains the apex; Home is the submerged launch point.
                float cycle = e.Clock %= 3.3f;
                float flight = cycle - 1.9f;
                if (flight <= 0) { e.Body.Position = e.Home; e.Body.Velocity = Vector2.Zero; }
                else
                {
                    float progress = flight / 1.4f;
                    e.Body.Position = e.Home - new Vector2(0, 4 * e.JumpHeight * progress * (1 - progress));
                    e.Body.Velocity = new(0, 4 * e.JumpHeight / 1.4f * (2 * progress - 1));
                }
            }
            else if (e.Kind == "BulletBill") e.Body.Position += new Vector2(e.Direction * 120 * dt, 0);
            else if (e.Kind == "BowserFlame") e.Body.Position += new Vector2(e.Direction * 90 * dt, 0);
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
                    var flame = NewEnemy("BowserFlame", e.Body.Position + new Vector2(e.Direction * 16, -14)); flame.Direction = e.Direction; flame.Active = true; spawned.Add(flame); Sound.Effect("BowserFire");
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
            if (PlayerEnemyContact(oldPlayer, player, e.PreviousBox, e.Body.Box, out bool above)) TouchEnemy(e, above);
        }
        World.Enemies.AddRange(spawned);
        TickGenerators(dt);
    }
    private static void ResetHiddenPlant(Enemy plant)
    {
        plant.Body.Position = plant.Home + new Vector2(0, 24); plant.Body.Height = 24; plant.Body.Velocity = Vector2.Zero;
        plant.Clock = 0; plant.Phase = 0;
    }
    private Enemy NewEnemy(string kind, Vector2 position)
    {
        var e = new Enemy { Kind = kind, Home = position }; e.Body.Position = position;
        if (kind == "PiranhaPlant") ResetHiddenPlant(e);
        if (kind == "Podoboo") World.InitializePodoboo(e);
        return e;
    }
    private int ShellScore(int count) => count switch { 1 => 500, 2 => 800, 3 => 1000, 4 => 2000, 5 => 4000, 6 => 5000, 7 => 8000, _ => 10000 };
    public void TouchEnemy(Enemy e)
    {
        Rect2 player = Player.Box, enemy = e.Body.Box;
        PlayerEnemyContact(new(player.Position - Player.Velocity / 60, player.Size), player,
            new(enemy.Position - e.Body.Velocity / 60, enemy.Size), enemy, out bool above);
        TouchEnemy(e, above);
    }
    private void TouchEnemy(Enemy e, bool above)
    {
        if (e.Dead || e.Removed || e.PipeSuppressed || e.Kind == "PiranhaPlant" && e.Body.Position.Y >= e.Home.Y + 22) return;
        if (StarTime > 0 && e.Kind is not ("Podoboo" or "BowserFlame")) { DefeatEnemy(e, true, 100); return; }
        bool stompable = e.Kind is "Goomba" or "GreenKoopaTroopa" or "RedKoopaTroopa" or "BuzzyBeetle" or "HammerBro" or "Lakitu" or "BulletBill" or "FlyingFish" or "Blooper" or "GreenCheepCheep" or "RedCheepCheep";
        if (stompable && above)
        {
            Player.Position = new(Player.Position.X, MathF.Min(Player.Position.Y, e.Body.Box.Position.Y)); Player.Velocity = new(Player.Velocity.X, World.IsWater ? -115 : -185);
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
        FacePlayerOnContact(e);
        Hurt();
    }
    private void FacePlayerOnContact(Enemy enemy)
    {
        if (enemy.Shell || enemy.Kind is not ("Goomba" or "GreenKoopaTroopa" or "RedKoopaTroopa" or "BuzzyBeetle" or "Spiny" or "GreenCheepCheep" or "RedCheepCheep" or "Blooper" or "HammerBro" or "Lakitu" or "Bowser" or "TrueBowser" or "FlyingFish")) return;
        float distance = Player.Position.X - enemy.Body.Position.X;
        if (MathF.Abs(distance) <= .5f) return;
        enemy.Direction = distance < 0 ? -1 : 1;
        if (enemy.Kind is "Goomba" or "GreenKoopaTroopa" or "RedKoopaTroopa" or "BuzzyBeetle" or "Spiny")
        {
            if (!(enemy.Winged && enemy.Kind == "RedKoopaTroopa")) enemy.Body.Velocity = new(enemy.Direction * 30, enemy.Body.Velocity.Y);
        }
        else if (enemy.Kind is "GreenCheepCheep" or "RedCheepCheep") enemy.Body.Velocity = new(enemy.Direction * 32, enemy.Body.Velocity.Y);
        else if (enemy.Kind == "FlyingFish") enemy.Body.Velocity = new(enemy.Direction * MathF.Abs(enemy.Body.Velocity.X), enemy.Body.Velocity.Y);
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
    private const int WaterFishLimit = 6;
    private sealed class WaterFishSource
    {
        public float Remaining = 2;
        public int Band;
        public bool Started, Stopped;
    }
    private sealed class WaterFishPopulation
    {
        public readonly Dictionary<EntityData, WaterFishSource> Sources = [];
        public readonly HashSet<Enemy> Spawned = [];
        public readonly HashSet<(int x, int y)> Cells;
        public readonly float Top, Bottom;
        public WaterFishPopulation(World world)
        {
            Cells = world.Data.Tiles.Where(t => (int)t[2] == 2).Select(t => ((int)t[0], (int)t[1])).ToHashSet();
            Top = Cells.Count == 0 ? -152 : Cells.Min(c => c.y) * 16 + 24;
            Bottom = Cells.Count == 0 ? -8 : MathF.Min(0, (Cells.Max(c => c.y) + 1) * 16) - 8;
        }
    }
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<World, WaterFishPopulation> waterFishPopulations = new();
    private WaterFishPopulation WaterFishPopulationForRoom() => waterFishPopulations.GetValue(World, static world => new WaterFishPopulation(world));
    private bool WaterFishOpen(Rect2 body, WaterFishPopulation population)
    {
        Rect2 clearance = body.Grow(4);
        if (World.Colliders(clearance, false).Any(c => clearance.Intersects(c.box))) return false;
        // Mechanics fixtures can mark an empty room as water without liquid tiles.
        if (population.Cells.Count == 0) return true;
        for (int y = Mathf.FloorToInt(clearance.Position.Y / 16); y <= Mathf.FloorToInt((clearance.End.Y - .001f) / 16); y++)
            for (int x = Mathf.FloorToInt(clearance.Position.X / 16); x <= Mathf.FloorToInt((clearance.End.X - .001f) / 16); x++)
                if (!population.Cells.Contains((x, y))) return false;
        return true;
    }
    private void CullGeneratedWaterFish(WaterFishPopulation population)
    {
        foreach (var fish in population.Spawned.ToArray())
        {
            if (!fish.Removed && (fish.Dead || fish.Body.Position.X >= CameraX - 80 && fish.Body.Position.X <= CameraX + 336)) continue;
            fish.Removed = true; World.Enemies.Remove(fish); population.Spawned.Remove(fish);
        }
    }
    private bool SpawnWaterFish(WaterFishSource source, WaterFishPopulation population)
    {
        if (population.Cells.Count == 0) return false;
        var candidates = new List<(Vector2 position, int band)>();
        foreach (float x in new[] { CameraX + 280, CameraX + 296, CameraX + 312, CameraX - 24, CameraX - 40, CameraX - 56 })
        {
            if (x - 10 < World.MinX || x + 10 > World.MaxX) continue;
            for (float y = population.Top; y <= population.Bottom; y += 16)
            {
                Rect2 box = new(x - 6, y - 16, 12, 16);
                if (!WaterFishOpen(box, population) || Player.Box.Grow(48).Intersects(box) ||
                    World.Enemies.Any(e => !e.Dead && !e.Removed && e.Body.Box.Grow(16).Intersects(box))) continue;
                int band = Math.Clamp((int)((y - population.Top) * 3 / (population.Bottom - population.Top + 1)), 0, 2);
                candidates.Add((new(x, y), band));
            }
        }
        if (candidates.Count == 0) return false;
        var ahead = candidates.Where(c => c.position.X > CameraX + 256).ToList();
        if (ahead.Count > 0) candidates = ahead;
        var preferred = candidates.Where(c => c.band == source.Band).ToArray();
        var selected = preferred.Length > 0 ? preferred[random.Next(preferred.Length)] : candidates[random.Next(candidates.Count)];
        var fish = NewEnemy("RedCheepCheep", selected.position);
        fish.Active = true; fish.Direction = selected.position.X > CameraX + 128 ? -1 : 1;
        fish.Body.Velocity = new(fish.Direction * 32, 0); fish.Phase = (float)random.NextDouble() * MathF.Tau;
        World.Enemies.Add(fish); population.Spawned.Add(fish);
        source.Band = (selected.band + 1) % 3;
        return true;
    }
    private void TickWaterFishGenerator(EntityData trigger, float dt, WaterFishPopulation population)
    {
        if (!population.Sources.TryGetValue(trigger, out var source)) population.Sources[trigger] = source = new WaterFishSource();
        float end = World.Triggers.Where(t => t.Kind == "EntityGeneratorStopper" && t.X > trigger.X).Select(t => t.X).DefaultIfEmpty(World.MaxX).Min();
        if (Player.Position.X >= end) source.Stopped = true;
        if (source.Stopped || Player.Position.X < trigger.X) return;
        source.Started = true;
        // Count only the nearby density. Generated fish leaving this interval are
        // transient; authored enemies keep their normal backtracking semantics.
        int nearby = World.Enemies.Count(e => !e.Dead && !e.Removed && (e.Kind is "GreenCheepCheep" or "RedCheepCheep") && e.Body.Position.X >= CameraX - 80 && e.Body.Position.X <= CameraX + 336);
        if (nearby >= WaterFishLimit) { source.Remaining = MathF.Max(source.Remaining, .5f); return; }
        source.Remaining -= dt;
        if (source.Remaining > 0) return;
        source.Remaining = SpawnWaterFish(source, population) ? 1.6f : .5f;
    }
    private void TickGenerators(float dt)
    {
        WaterFishPopulation? population = World.IsWater ? WaterFishPopulationForRoom() : null;
        if (population != null) CullGeneratedWaterFish(population);
        foreach (var trigger in World.Triggers)
        {
            string k = trigger.Kind;
            if (population != null && k.Contains("Cheep") && k.Contains("Generator"))
            { TickWaterFishGenerator(trigger, dt, population); continue; }
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
