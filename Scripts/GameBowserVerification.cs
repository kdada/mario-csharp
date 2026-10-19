using Godot;

namespace MarioStudy;

public partial class Game
{
    private static bool BowserBoss(Enemy enemy) => enemy.Kind is "Bowser" or "TrueBowser";

    private Enemy BowserVerificationRoom(string id)
    {
        StartGame(Array.IndexOf(Maps.MainLevels, id[..3]), true);
        if (World.Data.Id != id) LoadRoom(id, false);
        State = Screen.Playing; Power = 2; Player.Height = 32;
        StarTime = Invulnerable = pipeClock = 0; afterPipe = null;
        var boss = World.Enemies.Single(BowserBoss);
        CameraX = boss.Body.Position.X - 128;
        Player.Position = boss.Home + new Vector2(-64, 0); Player.Velocity = Vector2.Zero;
        World.Shots.Clear();
        return boss;
    }

    private FireShot BowserVerificationShot(Enemy boss)
    {
        var shot = new FireShot();
        shot.Body.Position = boss.Body.Position + new Vector2(0, -8);
        shot.Body.Velocity = Vector2.Zero;
        World.Shots.Add(shot);
        return shot;
    }

    // Logic only: exercise the real shot/trigger/cache paths without rendering.
    private void VerifyBowserHealthFixes()
    {
        bool enabled = IsPhysicsProcessing(); SetPhysicsProcess(false);
        try
        {
            var rooms = Maps.Rooms.Values.Where(r => r.Entities.Any(e => e.Kind is "Bowser" or "TrueBowser")).OrderBy(r => r.Id).ToArray();
            Check("all eight castle boss rooms are included in health verification", rooms.Length == 8);
            foreach (var data in rooms)
            {
                string id = data.Id;
                var boss = BowserVerificationRoom(id);
                Check($"{id} actual map initializes one living five-health boss", World.Enemies.Count(BowserBoss) == 1 && boss.Health == 5 && !boss.Dead && !boss.Removed && !World.BridgeGone);
                int score = Score;
                for (int hit = 1; hit <= 5; hit++)
                {
                    var shot = BowserVerificationShot(boss);
                    Check($"{id} fireball {hit} overlaps boss without terrain overlap", shot.Body.Box.Intersects(boss.Body.Box) && !World.Colliders(shot.Body.Box, false).Any(c => c.box.Intersects(shot.Body.Box)));
                    TickShots(0);
                    Check($"{id} fireball {hit} reduces health {6 - hit} to {5 - hit} exactly once", boss.Health == 5 - hit && boss.Dead == (hit == 5) && !boss.Removed && shot.Removed && !World.Shots.Contains(shot));
                    Check($"{id} fireball {hit} preserves bridge and clear-state distinction", !World.BridgeGone && State == Screen.Playing && Score == score + (hit == 5 ? 5000 : 0));
                    int health = boss.Health;
                    World.Shots.Add(shot); // A consumed object is inert even if retained/reinserted.
                    TickShots(0); TickShots(0);
                    Check($"{id} consumed fireball {hit} cannot apply damage again", boss.Health == health && World.Shots.Count == 0 && Score == score + (hit == 5 ? 5000 : 0));
                }
                Check($"{id} fifth fireball defeats and launches boss without clearing castle", boss.Health == 0 && boss.Dead && boss.Flipped && boss.Body.Velocity.Y < 0 && !World.BridgeGone && State == Screen.Playing);
                var afterDeath = BowserVerificationShot(boss); TickShots(0);
                Check($"{id} fireball overlapping defeated boss cannot subtract health or rescore", boss.Health == 0 && Score == score + 5000 && !afterDeath.Removed);

                boss = BowserVerificationRoom(id);
                var first = BowserVerificationShot(boss); var second = BowserVerificationShot(boss);
                TickShots(0);
                Check($"{id} two simultaneous fireballs each deal one damage", boss.Health == 3 && !boss.Dead && first.Removed && second.Removed && World.Shots.Count == 0 && Score == 0);

                var cached = World;
                LoadRoom("1-1", false); LoadRoom(id, true, boss.Home + new Vector2(-64, 0)); State = Screen.Playing;
                CameraX = boss.Home.X - 128;
                Check($"{id} cached return preserves prior two hits and boss identity", ReferenceEquals(World, cached) && World.Enemies.Contains(boss) && boss.Health == 3 && !boss.Dead);
                BowserVerificationShot(boss); TickShots(0);
                Check($"{id} one fireball after cached return only reduces three health to two", boss.Health == 2 && !boss.Dead);
                BowserVerificationShot(boss); TickShots(0);
                LoadRoom("1-1", false); LoadRoom(id, true, boss.Home + new Vector2(-64, 0)); State = Screen.Playing;
                CameraX = boss.Home.X - 128;
                Check($"{id} cached one-health boss keeps damage from four earlier fireballs", boss.Health == 1 && !boss.Dead && ReferenceEquals(World, cached));
                BowserVerificationShot(boss); TickShots(0);
                Check($"{id} one final fireball legitimately defeats previously wounded boss", boss.Health == 0 && boss.Dead && Score == 5000);
                LoadRoom("1-1", false); LoadRoom(id, true);
                Check($"{id} cached defeated boss remains defeated", ReferenceEquals(World, cached) && World.Enemies.Contains(boss) && boss.Dead && boss.Health == 0);

                LoadRoom(id, false);
                var fresh = World.Enemies.Single(BowserBoss);
                Check($"{id} fresh room reload resets boss to five health", !ReferenceEquals(World, cached) && !ReferenceEquals(fresh, boss) && fresh.Health == 5 && !fresh.Dead && !fresh.Removed);
                BeginLevel(LevelIndex);
                if (World.Data.Id != id) LoadRoom(id, true);
                var restarted = World.Enemies.Single(BowserBoss);
                Check($"{id} restarting level discards damaged room cache", !ReferenceEquals(restarted, fresh) && restarted.Health == 5 && !restarted.Dead && !World.BridgeGone);

                boss = BowserVerificationRoom(id); boss.Active = true; boss.Timer = 0;
                TickEnemies(1f / 60);
                Check($"{id} firing own flame cannot damage or defeat boss", boss.Health == 5 && !boss.Dead && World.Enemies.Any(e => e.Kind == "BowserFlame" && !e.Dead));
                TickShots(1f / 60);
                Check($"{id} boss-owned hammer or flame cannot subtract its health", boss.Health == 5 && !boss.Dead);

                boss = BowserVerificationRoom(id);
                BowserVerificationShot(boss); TickShots(0);
                var bridge = World.Triggers.Single(t => t.Kind == "CastleBridge");
                Player.Position = CastleAxeFeet(bridge); Player.Velocity = Vector2.Zero;
                TickTriggers(0, new());
                Check($"{id} touching axe bypasses remaining health and clears bridge", State == Screen.Clear && World.BridgeGone && boss.Dead && boss.Health == 4 && !boss.Flipped && boss.Body.Velocity == new Vector2(0, 30) && Score == 5000);
            }
        }
        catch (Exception ex) { Check("Bowser health mechanism exception", false, ex.ToString()); }
        finally { SetPhysicsProcess(enabled); }
    }
}
