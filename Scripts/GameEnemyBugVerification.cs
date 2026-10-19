using Godot;

namespace MarioStudy;

public partial class Game
{
    // Mechanism-only checks: no frame capture, redraw, rendering wait or file output.
    private void VerifyEnemyAndPlantFixes()
    {
        bool enabled = IsPhysicsProcessing(); SetPhysicsProcess(false);
        try
        {
            foreach (int hz in new[] { 30, 60, 120 })
            {
                float dt = 1f / hz;
                foreach (float playerYSpeed in new[] { 0f, -60f })
                {
                    MechanicsWorld(); Power = 1; Player.Height = 32; Player.Grounded = false;
                    Player.Position = new(40, -64); Player.Velocity = new(0, playerYSpeed);
                    var rising = MechanicsEnemy("Goomba", 40, -47.5f, -1);
                    rising.Body.Grounded = false; rising.Body.Velocity = new(0, -200);
                    CapturePlayerContact(); Player.Position += Player.Velocity * dt; TickEnemies(dt);
                    Check($"enemy rising into Mario's feet is stomped at {hz}Hz player vy {playerYSpeed}",
                        rising.Dead && rising.Squashed && Power == 1 && Invulnerable == 0 && State == Screen.Playing && Player.Velocity.Y == -185);
                }
                MechanicsWorld(); Power = 1; Player.Height = 32; Player.Grounded = false;
                Player.Position = new(40, -64); Player.Velocity = new(0, 180);
                var fallingStomp = MechanicsEnemy("Goomba", 40, -47.5f, -1);
                CapturePlayerContact(); Player.Position += Player.Velocity * dt; TickEnemies(dt);
                Check($"ordinary falling stomp still works at {hz}Hz", fallingStomp.Dead && Power == 1 && Invulnerable == 0 && Player.Velocity.Y == -185);

                foreach (float playerYSpeed in new[] { -60f, 60f })
                {
                    MechanicsWorld(); Power = 1; Player.Height = 32; Player.Grounded = false;
                    Player.Position = new(40, -64); Player.Velocity = new(120, playerYSpeed);
                    var side = MechanicsEnemy("Goomba", 53, -64, -1);
                    CapturePlayerContact(); Player.Position += Player.Velocity * dt; TickEnemies(dt);
                    Check($"airborne side contact still hurts at {hz}Hz player vy {playerYSpeed}", !side.Dead && Power == 0 && Invulnerable > 0);
                }
                MechanicsWorld(); Power = 1; Player.Height = 32; Player.Grounded = false;
                var overhead = MechanicsEnemy("Goomba", 40, -96, -1);
                Player.Position = new(40, -63.5f); Player.Velocity = new(0, -210);
                CapturePlayerContact(); Player.Position += Player.Velocity * dt; TickEnemies(dt);
                Check($"Mario's head hitting enemy underside still hurts at {hz}Hz", !overhead.Dead && Power == 0 && Invulnerable > 0);

                MechanicsWorld(); Power = 1; Player.Height = 32; Player.Grounded = false;
                Player.Position = new(40, -64); Player.Velocity = Vector2.Zero;
                var spiny = MechanicsEnemy("Spiny", 40, -47.5f, -1);
                spiny.Body.Grounded = false; spiny.Body.Velocity = new(0, -200);
                CapturePlayerContact(); TickEnemies(dt);
                Check($"rising Spiny remains dangerous at {hz}Hz", !spiny.Dead && Power == 0 && Invulnerable > 0);

                MechanicsWorld(); Power = 1; Player.Height = 32; Player.Grounded = false;
                var dangerousPlant = NewEnemy("PiranhaPlant", new(40, -48)); World.Enemies.Add(dangerousPlant);
                dangerousPlant.Active = true; dangerousPlant.Clock = 2.5f; dangerousPlant.Body.Position = dangerousPlant.Home;
                Player.Position = new(40, -72.5f); Player.Velocity = new(0, 180);
                CapturePlayerContact(); Player.Position += Player.Velocity * dt; TickEnemies(dt);
                Check($"exposed plant remains dangerous from above at {hz}Hz", !dangerousPlant.Dead && Power == 0 && Invulnerable > 0);

                foreach (bool reversed in new[] { false, true })
                {
                    MechanicsWorld(); Power = 1; Player.Height = 32; Player.Grounded = false;
                    Player.Position = new(30, -24.5f + 30f / hz); Player.Velocity = new(0, 120);
                    MechanicsEnemy("GreenKoopaTroopa", 24, 0, 1);
                    var winged = MechanicsEnemy("GreenKoopaTroopa", 36, -.5f, -1); winged.Winged = true; winged.Body.Grounded = false;
                    if (reversed) World.Enemies.Reverse(); TickEnemies(dt);
                    Check($"direct TickEnemies preserves adjacent stomp contacts at {hz}Hz reversed {reversed}",
                        Power == 1 && Invulnerable == 0 && State == Screen.Playing && Player.Velocity.Y < 0);
                }

                MechanicsWorld(); Player.Position = new(-64, 0);
                var plant = NewEnemy("PiranhaPlant", new(64, -48)); plant.Active = true; World.Enemies.Add(plant);
                Check($"dynamic plant starts fully hidden at {hz}Hz", plant.Body.Position == plant.Home + new Vector2(0, 24) && plant.Body.Height == 24 && plant.Clock == 0 && plant.Phase == 0);
                plant.Phase = 2.73f; // Legacy phase data must not alter the plant's own cycle.
                for (int f = 0; f < hz / 2; f++) TickEnemies(dt);
                Check($"plant begins with a hidden interval at {hz}Hz", plant.Body.Position == plant.Home + new Vector2(0, 24));
                for (int f = 0; f < hz; f++) TickEnemies(dt);
                Check($"plant rises continuously after hidden interval at {hz}Hz", MathF.Abs(plant.Body.Position.Y - plant.Home.Y - 12) < .01f);

                float localClock = plant.Clock, clock = Clock, animation = animationTime;
                Vector2 position = plant.Body.Position;
                State = Screen.Pause;
                for (int f = 0; f < hz * 2; f++) AdvanceState(dt, new());
                Check($"pause freezes plant position local and global clocks at {hz}Hz", plant.Clock == localClock && plant.Body.Position == position && Clock == clock && animationTime == animation);
                State = Screen.Playing; AdvanceState(dt, new());
                Check($"resuming pause does not jump plant position at {hz}Hz", MathF.Abs(plant.Clock - localClock - dt) < .0001f && MathF.Abs(plant.Body.Position.Y - position.Y) <= 24 * dt + .001f);

                BeginPowerTransition(1); localClock = plant.Clock; clock = Clock; animation = animationTime; position = plant.Body.Position;
                AdvanceState(.2f, new()); AdvanceState(1, new());
                Check($"power transition freezes plant and clocks at {hz}Hz", State == Screen.Playing && plant.Clock == localClock && plant.Body.Position == position && Clock == clock && animationTime == animation);
                AdvanceState(dt, new());
                Check($"resuming power transition does not jump plant at {hz}Hz", MathF.Abs(plant.Clock - localClock - dt) < .0001f && MathF.Abs(plant.Body.Position.Y - position.Y) <= 24 * dt + .001f);

                pipeClock = .75f; pipeVelocity = Vector2.Zero; afterPipe = null;
                localClock = plant.Clock; clock = Clock; animation = animationTime; position = plant.Body.Position;
                AdvanceState(.5f, new()); AdvanceState(.5f, new());
                Check($"pipe transition freezes plant and clocks at {hz}Hz", pipeClock <= 0 && plant.Clock == localClock && plant.Body.Position == position && Clock == clock && animationTime == animation);
                AdvanceState(dt, new());
                Check($"resuming pipe transition does not jump plant at {hz}Hz", MathF.Abs(plant.Clock - localClock - dt) < .0001f && MathF.Abs(plant.Body.Position.Y - position.Y) <= 24 * dt + .001f);

                plant.Clock = 2.5f; plant.Body.Position = plant.Home; plant.PipeSuppressed = true;
                ResetHiddenPlant(plant); Player.Position = plant.Home;
                for (int f = 0; f < hz * 2; f++) TickEnemies(dt);
                Check($"pipe plant reset holds hidden while Mario remains at mouth at {hz}Hz", plant.PipeSuppressed && plant.Clock == 0 && plant.Body.Position == plant.Home + new Vector2(0, 24) && plant.Body.Height == 24 && plant.Body.Velocity == Vector2.Zero);
                Player.Position = plant.Home + new Vector2(-64, 0); TickEnemies(dt);
                Check($"released pipe plant restarts hidden interval at {hz}Hz", !plant.PipeSuppressed && MathF.Abs(plant.Clock - dt) < .0001f && plant.Body.Position == plant.Home + new Vector2(0, 24));
            }

            foreach (bool water in new[] { false, true })
                foreach (string kind in new[] { "Spiny", "PiranhaPlant", "Bowser", "TrueBowser", "Podoboo", "BowserFlame" })
                {
                    MechanicsWorld(); World.IsWater = water; Power = 1; Player.Height = 32;
                    var hazard = NewEnemy(kind, new(40, -48));
                    if (kind == "PiranhaPlant") hazard.Body.Position = hazard.Home;
                    TouchEnemy(hazard, true);
                    Check($"top-contact classification cannot make {kind} stompable water {water}", !hazard.Dead && Power == 0 && Invulnerable > 0);
                }
            MechanicsWorld(); World.IsWater = true; Power = 1; Player.Height = 32;
            var submerged = NewEnemy("Goomba", new(40, -48)); TouchEnemy(submerged, true);
            Check("underwater top contact uses swimming bounce without damage", submerged.Dead && Power == 1 && Invulnerable == 0 && Player.Velocity.Y == -115);
            VerifyUnderwaterStompFixes();

            MechanicsWorld(); Power = 0; Player.Position = new(40, -84); Player.Grounded = false; Player.Velocity = new(0, 600);
            var crossed = MechanicsEnemy("BulletBill", 40, -48, -1);
            CapturePlayerContact(); Player.Position += Player.Velocity * .1f; TickEnemies(.1f);
            Check("swept top contact catches enemy crossed entirely in one step", crossed.Dead && State == Screen.Playing && Player.Velocity.Y == -185);

            int plantCount = 0; bool allHidden = true;
            foreach (var data in Maps.Rooms.Values)
                foreach (var plant in new World(data, Maps, MainId).Enemies.Where(e => e.Kind == "PiranhaPlant"))
                {
                    plantCount++;
                    allHidden &= plant.Body.Position == plant.Home + new Vector2(0, 24) && plant.Body.Height == 24 && plant.Clock == 0 && plant.Phase == 0;
                }
            Check("all new-room plants are hidden before first simulation", plantCount > 0 && allHidden, $"plants={plantCount}");

            MechanicsWorld(); Player.Position = new(-64, 0);
            var offscreen = NewEnemy("PiranhaPlant", new(400, -48)); World.Enemies.Add(offscreen); TickEnemies(2);
            Check("offscreen unactivated plant does not accumulate cycle time", !offscreen.Active && offscreen.Clock == 0 && offscreen.Body.Position == offscreen.Home + new Vector2(0, 24));
            CameraX = 300; TickEnemies(1f / 60);
            Check("first on-screen simulation begins with hidden plant", offscreen.Active && offscreen.Clock < .02f && offscreen.Body.Position == offscreen.Home + new Vector2(0, 24));

            StartGame(1, true); State = Screen.Playing;
            var cached = World; var cachedPlant = cached.Enemies.First(e => e.Kind == "PiranhaPlant");
            cached.Enemies.RemoveAll(e => !ReferenceEquals(e, cachedPlant)); cached.Triggers.Clear();
            CameraX = cachedPlant.Home.X - 128; Player.Position = cachedPlant.Home + new Vector2(-64, 0);
            cachedPlant.Active = true; TickEnemies(1.5f);
            float cachedClock = cachedPlant.Clock; Vector2 cachedPosition = cachedPlant.Body.Position;
            LoadRoom("1-1a", false); State = Screen.Playing;
            for (int f = 0; f < 120; f++) AdvanceState(1f / 60, new());
            LoadRoom("1-2", true, cachedPlant.Home + new Vector2(-64, 0)); State = Screen.Playing;
            Check("returning to cached room preserves plant time and position", ReferenceEquals(World, cached) && cachedPlant.Clock == cachedClock && cachedPlant.Body.Position == cachedPosition);
            TickEnemies(1f / 60);
            Check("cached plant resumes by one simulated step without catch-up", MathF.Abs(cachedPlant.Clock - cachedClock - 1f / 60) < .0001f && MathF.Abs(cachedPlant.Body.Position.Y - cachedPosition.Y) <= 24f / 60 + .001f);
            LoadRoom("1-2", false);
            Check("fresh reload restores hidden plant stage", World.Enemies.Where(e => e.Kind == "PiranhaPlant").All(e => e.Clock == 0 && e.Body.Position == e.Home + new Vector2(0, 24)));
            VerifyFireballFixes();
            VerifyLavaBubbleFixes();
        }
        catch (Exception ex) { Check("enemy and plant mechanism exception", false, ex.ToString()); }
        finally { SetPhysicsProcess(enabled); }
    }

    private void VerifyUnderwaterStompFixes()
    {
        foreach (int hz in new[] { 30, 60, 120 })
            foreach (string kind in new[] { "Blooper", "GreenCheepCheep", "RedCheepCheep" })
            {
                float dt = 1f / hz;
                foreach (int power in new[] { 0, 1, 2 }) foreach (float protection in new[] { 0f, .75f })
                    foreach (bool risingEnemy in new[] { false, true })
                    {
                        MechanicsWorld(); World.IsWater = true; Power = power; Player.Height = power == 0 ? 16 : 32;
                        Invulnerable = protection; Player.Grounded = false; Player.Position = new(40, -64);
                        Player.Velocity = new(0, risingEnemy ? 0 : 84);
                        var enemy = MechanicsEnemy(kind, 40, risingEnemy ? -47.98f : -47.75f, -1);
                        if (risingEnemy && kind != "Blooper") enemy.Phase = -MathF.PI / 2 - 2 * dt;
                        CapturePlayerContact(); Player.Position += Player.Velocity * dt; TickEnemies(dt);
                        string contactName = risingEnemy ? "swims into feet" : "is stomped while falling";
                        Check($"water {kind} {contactName} at {hz}Hz power {power} protection {protection}",
                            enemy.Dead && Power == power && Player.Height == (power == 0 ? 16 : 32) && Invulnerable == protection && State == Screen.Playing && Player.Velocity.Y == -115);
                    }

                foreach (int power in new[] { 0, 1, 2 }) foreach (bool protectedContact in new[] { false, true })
                    foreach (bool fromBelow in new[] { false, true })
                    {
                        MechanicsWorld(); World.IsWater = true; Power = power; Player.Height = power == 0 ? 16 : 32;
                        Invulnerable = protectedContact ? .75f : 0; Player.Grounded = false;
                        var enemy = MechanicsEnemy(kind, fromBelow ? 40 : 53, fromBelow ? -96 : -64, -1);
                        Player.Position = fromBelow ? new(40, -96 + Player.Height + .02f) : new(40, -64);
                        Player.Velocity = fromBelow ? new(0, -115) : new(120, 0);
                        CapturePlayerContact(); Player.Position += Player.Velocity * dt; TickEnemies(dt);
                        bool correctDamage = protectedContact
                            ? Power == power && Invulnerable == .75f && State == Screen.Playing
                            : power == 0 ? State == Screen.Dying : Power == power - 1 && Invulnerable > 0 && State == Screen.Playing;
                        string contactName = fromBelow ? "hit by Mario's head" : "side contact";
                        Check($"water {kind} {contactName} preserves damage rules at {hz}Hz power {power} protected {protectedContact}",
                            !enemy.Dead && correctDamage && Player.Height == (Power == 0 ? 16 : 32));
                    }
            }
    }

    private void VerifyLavaBubbleFixes()
    {
        foreach (int hz in new[] { 30, 60, 120 })
            foreach (var data in Maps.Rooms.Values.Where(r => r.Entities.Any(e => e.Kind == "Podoboo")))
            {
                float dt = 1f / hz;
                LevelIndex = Array.IndexOf(Maps.MainLevels, data.Id[..3]);
                World = new World(data, Maps, MainId); State = Screen.Playing; Player.Position = new(-10000, 0); Player.Velocity = Vector2.Zero;
                World.Triggers.Clear();
                var bubbles = World.Enemies.Where(e => e.Kind == "Podoboo").ToArray();
                foreach (var bubble in bubbles)
                {
                    World.Enemies.Clear(); World.Enemies.Add(bubble); CameraX = bubble.Home.X - 128;
                    float lavaY = data.Tiles.Where(t => (int)t[2] == 2 && t[0] * 16 <= bubble.Home.X && (t[0] + 1) * 16 > bubble.Home.X).Min(t => t[1] * 16);
                    Check($"{data.Id}/{bubble.Data.Name} starts entirely inside lava at {hz}Hz", !bubble.Active && bubble.Body.Box.Position.Y >= lavaY && bubble.Body.Position == bubble.Home && bubble.Clock == 0 && bubble.Phase == 0);
                    TickEnemies(dt);
                    Check($"{data.Id}/{bubble.Data.Name} first active frame stays submerged at {hz}Hz", bubble.Active && bubble.Simulated && bubble.Body.Position == bubble.Home && bubble.Body.Velocity == Vector2.Zero);
                    bool rose = false, fell = false, returned = false;
                    float highest = bubble.Body.Position.Y;
                    for (int f = 0; f < (int)MathF.Ceiling(3.3f * hz) + 1; f++)
                    {
                        TickEnemies(dt); highest = MathF.Min(highest, bubble.Body.Position.Y);
                        rose |= bubble.Body.Velocity.Y < 0;
                        fell |= rose && bubble.Body.Velocity.Y > 0;
                        if (fell && bubble.Body.Position.DistanceTo(bubble.Home) < .01f && bubble.Body.Velocity == Vector2.Zero) { returned = true; break; }
                    }
                    Check($"{data.Id}/{bubble.Data.Name} rises then falls completely back into lava at {hz}Hz", rose && fell && returned && bubble.Body.Box.Position.Y >= lavaY);
                    Check($"{data.Id}/{bubble.Data.Name} preserves authored apex at {hz}Hz", MathF.Abs(highest - bubble.Data.Y) < .3f, $"apex={highest:F3}, marker={bubble.Data.Y:F3}");
                }
            }

        MechanicsWorld(); Player.Position = new(-64, 0);
        var fallback = NewEnemy("Podoboo", new(64, -32)); fallback.Active = true; World.Enemies.Add(fallback);
        Check("generated Podoboo without lava uses spawn as mouth and old jump height", fallback.Home == new Vector2(64, -16) && fallback.Body.Position == fallback.Home && MathF.Abs(fallback.JumpHeight - 122.5f) < .001f);
        TickEnemies(2.3f);
        float localClock = fallback.Clock, clock = Clock, animation = animationTime; Vector2 position = fallback.Body.Position;
        State = Screen.Pause; AdvanceState(.5f, new());
        State = Screen.Playing; pipeClock = .75f; pipeVelocity = Vector2.Zero; afterPipe = null; AdvanceState(.75f, new());
        Check("pause and pipe transition freeze flying Podoboo and clocks", fallback.Clock == localClock && fallback.Body.Position == position && Clock == clock && animationTime == animation);
        AdvanceState(1f / 60, new());
        Check("flying Podoboo resumes without a catch-up jump", MathF.Abs(fallback.Clock - localClock - 1f / 60) < .0001f && MathF.Abs(fallback.Body.Position.Y - position.Y) <= 4 * fallback.JumpHeight / 1.4f / 60 + .001f);

        MechanicsWorld(); Player.Position = new(-100, 0);
        var flame = NewEnemy("BowserFlame", new(64, -48)); flame.Active = true; World.Enemies.Add(flame); TickEnemies(.5f);
        Check("Bowser flame retains horizontal travel without gravity", flame.Body.Position == new Vector2(19, -48) && !flame.Dead);
    }

    private void VerifyFireballFixes()
    {
        bool enabled = IsPhysicsProcessing(); SetPhysicsProcess(false);
        try
        {
            foreach (int hz in new[] { 30, 60, 120 }) foreach (bool water in new[] { false, true })
            {
                float dt = 1f / hz;
                MechanicsWorld(); World.IsWater = water; Power = 2; Player.Height = 32;
                Player.Position = new(16, -96); Player.Grounded = false;
                TickPlayer(dt, new Controls(0, false, false, false, true, false, false));
                var shot = World.Shots.Single(); Vector2 start = shot.Body.Position;
                Check($"fireball spawns at Mario's hand with downward velocity {hz}Hz water {water}", shot.Body.Position.Y == Player.Position.Y - 18 && shot.Body.Velocity.Y == 130);
                TickShots(dt);
                Check($"unsupported fireball immediately falls under gravity {hz}Hz water {water}", !shot.Removed && !shot.Body.Grounded && shot.Body.Position.Y > start.Y && MathF.Abs(shot.Body.Velocity.Y - (130 + 700 * dt)) < .001f);
                for (int f = 0; f < hz && !shot.Removed && !shot.Body.Grounded; f++) TickShots(dt);
                Check($"fireball bounces only after reaching terrain {hz}Hz water {water}", !shot.Removed && shot.Body.Grounded && MathF.Abs(shot.Body.Position.Y) < .001f && shot.Body.Velocity.Y == -170);
                float bounceY = shot.Body.Position.Y; TickShots(dt);
                Check($"fireball leaves ground after bounce {hz}Hz water {water}", !shot.Removed && !shot.Body.Grounded && shot.Body.Position.Y < bounceY);
            }

            // An invisible question block is not a downward support surface.
            MechanicsWorld();
            World.Data.Entities.Add(new EntityData { Kind = "InvisibleQuestionBlock", X = 40, Y = -32 });
            World = new World(World.Data, Maps, MainId);
            var throughHidden = new FireShot(); throughHidden.Body.Position = new(40, -42); throughHidden.Body.Velocity = new(0, 130); World.Shots.Add(throughHidden);
            TickShots(1f / 30);
            Check("hidden question block cannot suspend a descending fireball", !throughHidden.Removed && !throughHidden.Body.Grounded && throughHidden.Body.Position.Y > -40 && throughHidden.Body.Velocity.Y > 130);

            // In 8-4c the water layer overlays real masonry. A bounce in mid-water
            // must correspond to that terrain, not to water or an invented floor.
            LevelIndex = 31; LoadRoom("8-4c", false); State = Screen.Playing; World.Enemies.Clear(); World.Triggers.Clear();
            var surface = World.Data.Tiles.Where(t => t[1] < 0 && Maps.IsSolid((int)t[2], (int)t[3], (int)t[4]))
                .Select(t => new Vector2(t[0] * 16 + 8, t[1] * 16))
                .First(p => !World.Colliders(new Rect2(p.X - 4, p.Y - 9, 8, 8), false).Any(c => c.box.Intersects(new Rect2(p.X - 4, p.Y - 9, 8, 8))));
            CameraX = surface.X - 128;
            var supported = new FireShot(); supported.Body.Position = surface - new Vector2(0, 1); supported.Body.Velocity = new(0, 130); World.Shots.Add(supported);
            TickShots(1f / 30);
            bool support = World.Colliders(new Rect2(surface.X - 4, surface.Y, 8, 1), false).Any(c => MathF.Abs(c.box.Position.Y - surface.Y) < .001f);
            Check("8-4c mid-water fireball bounce has an actual masonry support", World.IsWater && support && supported.Body.Grounded && supported.Body.Position.Y == surface.Y && supported.Body.Velocity.Y == -170, surface.ToString());
        }
        catch (Exception ex) { Check("fireball mechanism exception", false, ex.ToString()); }
        finally { SetPhysicsProcess(enabled); }
    }
}
