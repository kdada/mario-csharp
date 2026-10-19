using Godot;

namespace MarioStudy;

public partial class Game
{
    // Contact behavior and subsequent movement only; no rendering or frame capture.
    private void VerifyEnemyContactFacingFixes()
    {
        bool enabled = IsPhysicsProcessing(); SetPhysicsProcess(false);
        try
        {
            foreach (int hz in new[] { 30, 60, 120 }) foreach (int side in new[] { -1, 1 })
            {
                float dt = 1f / hz;
                foreach (string kind in new[] { "Goomba", "GreenKoopaTroopa", "RedKoopaTroopa", "BuzzyBeetle", "Spiny", "GreenCheepCheep", "RedCheepCheep" })
                    foreach (bool protectedContact in new[] { false, true })
                    {
                        MechanicsWorld(); World.IsWater = kind.Contains("Cheep"); Power = 2; Player.Height = 32;
                        Invulnerable = protectedContact ? .75f : 0;
                        Player.Position = new(40 + side * 10, -64); Player.Velocity = Vector2.Zero;
                        var enemy = MechanicsEnemy(kind, 40, -64, -side); enemy.Body.Grounded = false;
                        TickEnemies(dt);
                        float speed = kind.Contains("Cheep") ? 32 : 30;
                        Check($"{kind} contact immediately faces Mario side {side} protected {protectedContact} at {hz}Hz",
                            !enemy.Dead && enemy.Direction == side && enemy.Body.Velocity.X == side * speed && Power == (protectedContact ? 2 : 1) && (protectedContact ? Invulnerable == .75f : Invulnerable > 0));
                        float before = enemy.Body.Position.X; TickEnemies(dt);
                        Check($"{kind} next step follows contact direction side {side} at {hz}Hz", (enemy.Body.Position.X - before) * side > 0 && enemy.Direction == side);
                    }

                foreach (string kind in new[] { "Blooper", "HammerBro", "Lakitu", "Bowser", "TrueBowser" })
                {
                    MechanicsWorld(); Power = 2; Player.Height = 32; Invulnerable = .75f;
                    Player.Position = new(40 + side * 10, -64);
                    var enemy = MechanicsEnemy(kind, 40, -64, -side); enemy.Body.Velocity = new(17, -13); enemy.Timer = 10;
                    Vector2 velocity = enemy.Body.Velocity; TouchEnemy(enemy, false);
                    Check($"{kind} contact changes facing without replacing AI velocity side {side} at {hz}Hz", enemy.Direction == side && enemy.Body.Velocity == velocity && Power == 2 && Invulnerable == .75f);
                }
                MechanicsWorld(); Power = 2; Player.Height = 32; Invulnerable = .75f; Player.Position = new(40 + side * 10, -64);
                var flying = MechanicsEnemy("FlyingFish", 40, -64, -side); flying.Body.Velocity = new(-side * 70, -180);
                TouchEnemy(flying, false);
                Check($"leaping fish contact turns horizontal travel while preserving vertical arc side {side} at {hz}Hz", flying.Direction == side && flying.Body.Velocity == new Vector2(side * 70, -180));

                foreach (float offset in new[] { -.2f, 0f, .2f })
                {
                    MechanicsWorld(); Power = 2; Invulnerable = .75f; Player.Position = new(40 + offset, -64);
                    var centered = MechanicsEnemy("Goomba", 40, -64, side); centered.Body.Velocity = new(side * 30, 0);
                    TouchEnemy(centered, false);
                    Check($"near-equal contact X preserves facing {side} offset {offset} at {hz}Hz", centered.Direction == side && centered.Body.Velocity.X == side * 30);
                }

                MechanicsWorld(); Power = 2; Player.Height = 32; Invulnerable = .75f; Player.Position = new(40 + side * 10, -64);
                var stomped = MechanicsEnemy("Goomba", 40, -64, -side); TouchEnemy(stomped, true);
                Check($"stomp defeat takes priority over contact facing side {side} at {hz}Hz", stomped.Dead && stomped.Direction == -side && Player.Velocity.Y == -185);

                MechanicsWorld(); Power = 2; Invulnerable = .75f; Player.Position = new(40 + side * 10, -64);
                var winged = MechanicsEnemy("GreenKoopaTroopa", 40, -64, -side); winged.Winged = true;
                TouchEnemy(winged, true);
                Check($"removing wings preserves stomp result and direction side {side} at {hz}Hz", !winged.Winged && !winged.Shell && !winged.Dead && winged.Direction == -side);

                MechanicsWorld(); Power = 2; Invulnerable = .75f; Player.Position = new(40 + side * 10, -64);
                var shelled = MechanicsEnemy("GreenKoopaTroopa", 40, -64, -side); TouchEnemy(shelled, true);
                Check($"forming shell takes priority over contact facing side {side} at {hz}Hz", shelled.Shell && !shelled.Kicked && shelled.Direction == -side);

                MechanicsWorld(); Power = 2; Invulnerable = .75f; Player.Position = new(40 + side * 10, -64);
                var kicked = MechanicsEnemy("GreenKoopaTroopa", 40, -64, side); kicked.Shell = true; kicked.Body.Height = 14; kicked.Timer = 7;
                TouchEnemy(kicked, false); TickEnemies(dt);
                Check($"kicked resting shell still launches away from Mario side {side} at {hz}Hz", kicked.Kicked && kicked.Direction == -side && kicked.Body.Velocity.X == -side * 220);
                int kickDirection = kicked.Direction; Vector2 kickVelocity = kicked.Body.Velocity;
                TouchEnemy(kicked, false);
                Check($"side contact cannot turn a moving shell into pursuit side {side} at {hz}Hz", kicked.Kicked && kicked.Direction == kickDirection && kicked.Body.Velocity == kickVelocity);
                TouchEnemy(kicked, true); TickEnemies(dt);
                Check($"stomping moving shell still stops it without refacing side {side} at {hz}Hz", !kicked.Kicked && kicked.Direction == kickDirection && kicked.Body.Velocity.X == 0);

                foreach (string kind in new[] { "BulletBill", "BowserFlame", "PiranhaPlant", "Podoboo" })
                {
                    MechanicsWorld(); Power = 2; Invulnerable = .75f; Player.Position = new(40 + side * 10, -64);
                    var fixedEnemy = MechanicsEnemy(kind, 40, -64, -side);
                    if (kind == "PiranhaPlant") fixedEnemy.Body.Position = fixedEnemy.Home;
                    fixedEnemy.Body.Velocity = new(-side * 90, 0); Vector2 original = fixedEnemy.Body.Velocity;
                    TouchEnemy(fixedEnemy, false);
                    Check($"{kind} contact preserves fixed flight or plant direction side {side} at {hz}Hz", fixedEnemy.Direction == -side && fixedEnemy.Body.Velocity == original);
                    if (kind is "BulletBill" or "BowserFlame")
                    {
                        float x = fixedEnemy.Body.Position.X; TickEnemies(dt);
                        Check($"{kind} keeps original projectile trajectory side {side} at {hz}Hz", (fixedEnemy.Body.Position.X - x) * -side > 0 && fixedEnemy.Direction == -side);
                    }
                }

                MechanicsWorld(); Power = 2; Player.Height = 32; Invulnerable = .75f; Player.Position = new(40 + side * 10, -64);
                var boss = MechanicsEnemy("Bowser", 40, -64, -side); boss.Body.Height = 32; boss.Body.Grounded = false; boss.Timer = 0;
                var oldFlame = NewEnemy("BowserFlame", new(-40, -96)); oldFlame.Active = true; World.Enemies.Add(oldFlame);
                TouchEnemy(boss, false); TickEnemies(dt);
                var newFlame = World.Enemies.Single(e => e.Kind == "BowserFlame" && !ReferenceEquals(e, oldFlame));
                Check($"Bowser emits new flame on contact-facing side {side} at {hz}Hz", boss.Direction == side && newFlame.Direction == side && MathF.Abs(newFlame.Body.Position.X - boss.Body.Position.X - side * 16) < .001f);
                float oldX = oldFlame.Body.Position.X, newX = newFlame.Body.Position.X; TickEnemies(dt);
                Check($"Bowser new flame follows launch side without redirecting old flames side {side} at {hz}Hz", (newFlame.Body.Position.X - newX) * side > 0 && oldFlame.Body.Position.X < oldX && oldFlame.Direction == -1);
            }

            MechanicsWorld(); Player.Position = new(-100, 0);
            var defaultBoss = MechanicsEnemy("Bowser", 64, -64, -1); defaultBoss.Body.Grounded = false; defaultBoss.Timer = 0;
            TickEnemies(1f / 60);
            var defaultFlame = World.Enemies.Single(e => e.Kind == "BowserFlame");
            Check("Bowser without player contact still emits default leftward flame", defaultBoss.Direction == -1 && defaultFlame.Direction == -1 && MathF.Abs(defaultFlame.Body.Position.X - defaultBoss.Body.Position.X + 16) < .001f);
        }
        catch (Exception ex) { Check("enemy contact facing mechanism exception", false, ex.ToString()); }
        finally { SetPhysicsProcess(enabled); }
    }
}
