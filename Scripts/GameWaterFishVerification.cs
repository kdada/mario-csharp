using Godot;

namespace MarioStudy;

public partial class Game
{
    private void WaterFishVerificationRoom(string id, float camera)
    {
        StartGame(Array.IndexOf(Maps.MainLevels, id[..3]), true); State = Screen.Playing;
        CameraX = camera; Player.Position = new(camera + 128, -96); Player.Velocity = Vector2.Zero;
        Invulnerable = 100; StarTime = 0;
    }

    // Pure state/map checks; deliberately does not request frames or images.
    private void VerifyWaterFishPopulationFixes()
    {
        bool enabled = IsPhysicsProcessing(); SetPhysicsProcess(false);
        try
        {
            foreach (string id in new[] { "2-2", "7-2" }) foreach (int hz in new[] { 30, 60, 120 })
            {
                float dt = 1f / hz;
                foreach (float camera in new[] { 0f, 1000f, 2100f, 2624f })
                {
                    WaterFishVerificationRoom(id, camera);
                    var population = WaterFishPopulationForRoom(); var observed = new HashSet<Enemy>(); var bands = new HashSet<int>();
                    bool clearBirths = true, outsideView = true, spacedFromPlayer = true, cap = true, clearSwimming = true;
                    for (int f = 0; f < hz * 12; f++)
                    {
                        TickEnemies(dt);
                        foreach (var fish in population.Spawned.Where(e => observed.Add(e)))
                        {
                            clearBirths &= WaterFishOpen(fish.Body.Box, population);
                            outsideView &= fish.Body.Box.End.X < CameraX || fish.Body.Box.Position.X > CameraX + 256;
                            spacedFromPlayer &= !Player.Box.Grow(48).Intersects(fish.Body.Box);
                            bands.Add(Math.Clamp((int)((fish.Body.Position.Y - population.Top) * 3 / (population.Bottom - population.Top + 1)), 0, 2));
                        }
                        clearSwimming &= population.Spawned.Where(e => !e.Dead && !e.Removed).All(e => WaterFishOpen(e.Body.Box, population));
                        cap &= population.Spawned.Count(e => !e.Dead && !e.Removed) <= WaterFishLimit;
                    }
                    string phase = camera == 2624 ? "last screen" : camera == 0 ? "front" : camera == 1000 ? "middle" : "back";
                    Check($"{id} {phase} keeps generating swimming fish at {hz}Hz", observed.Count >= 3, $"spawned={observed.Count}");
                    string depths = string.Join(",", bands);
                    Check($"{id} {phase} distributes fish over swimming depths at {hz}Hz", bands.Count >= 2, $"bands={depths}");
                    Check($"{id} {phase} births and swimming avoid water edges and solids at {hz}Hz", clearBirths && clearSwimming);
                    Check($"{id} {phase} births occur outside view away from Mario at {hz}Hz", outsideView && spacedFromPlayer);
                    Check($"{id} {phase} generated population stays capped at {hz}Hz", cap);
                }

                WaterFishVerificationRoom(id, 0); World.Enemies.Clear();
                var source = World.Triggers.Single(t => t.Kind == "CheepCheepSideGenerator");
                var state = WaterFishPopulationForRoom();
                var authored = Enumerable.Range(0, WaterFishLimit).Select(i => MechanicsEnemy("GreenCheepCheep", 32 + i * 32, -96, -1)).ToArray();
                TickGenerators(5);
                Check($"{id} nearby authored fish also count toward density limit at {hz}Hz", state.Spawned.Count == 0);
                CameraX = 1000; Player.Position = new(1128, -96); TickGenerators(2);
                Check($"{id} frozen fish left behind cannot block later population at {hz}Hz", state.Spawned.Count > 0 && authored.All(e => !e.Removed && World.Enemies.Contains(e)));
                var generated = state.Spawned.ToArray();
                CameraX = 2100; Player.Position = new(2228, -96); TickGenerators(2);
                Check($"{id} generated fish leaving local interval are retired at {hz}Hz", generated.All(e => e.Removed && !World.Enemies.Contains(e)) && state.Spawned.Count > 0 && authored.All(e => !e.Removed && World.Enemies.Contains(e)));

                WaterFishVerificationRoom(id, 1000);
                source = World.Triggers.Single(t => t.Kind == "CheepCheepSideGenerator");
                state = WaterFishPopulationForRoom(); TickGenerators(1);
                var local = state.Sources[source]; float remaining = local.Remaining; int band = local.Band;
                float clock = Clock, animation = animationTime; int count = World.Enemies.Count;
                State = Screen.Pause; for (int f = 0; f < hz; f++) AdvanceState(dt, new());
                Check($"{id} pause freezes fish generator and world clock at {hz}Hz", local.Remaining == remaining && local.Band == band && World.Enemies.Count == count && Clock == clock && animationTime == animation);
                State = Screen.Playing; BeginPowerTransition(1); AdvanceState(.8f, new());
                pipeClock = .75f; pipeVelocity = Vector2.Zero; afterPipe = null; AdvanceState(.75f, new());
                Check($"{id} transformation and pipe travel freeze fish generator at {hz}Hz", local.Remaining == remaining && World.Enemies.Count == count);
                AdvanceState(dt, new());
                Check($"{id} resumed fish generator advances only one frame at {hz}Hz", MathF.Abs(local.Remaining - remaining + dt) < .0001f);
                var cached = World; float cachedTime = local.Remaining;
                LoadRoom("1-1", false); LoadRoom(id, true, new(1128, -96)); State = Screen.Playing;
                Check($"{id} cached return preserves generator time without catch-up at {hz}Hz", ReferenceEquals(World, cached) && ReferenceEquals(WaterFishPopulationForRoom(), state) && local.Remaining == cachedTime);

                WaterFishVerificationRoom(id, 1000);
                source = World.Triggers.Single(t => t.Kind == "CheepCheepSideGenerator");
                var stopper = new EntityData { Kind = "EntityGeneratorStopper", Name = "verification-stop", X = 1200 };
                World.Triggers.Add(stopper); state = WaterFishPopulationForRoom();
                Player.Position = new(source.X - 16, -96); TickGenerators(5);
                Check($"{id} generator does not start before its map trigger at {hz}Hz", state.Spawned.Count == 0 && !state.Sources[source].Started);
                Player.Position = new(1136, -96); TickGenerators(2.1f);
                Check($"{id} generator remains active beyond old proximity range until stopper at {hz}Hz", state.Sources[source].Started && state.Spawned.Count > 0);
                Player.Position = new(stopper.X, -96); TickGenerators(5); int stoppedCount = state.Spawned.Count;
                Player.Position = new(stopper.X - 100, -96); TickGenerators(5);
                Check($"{id} passing stopper latches generation off even after backtracking at {hz}Hz", state.Sources[source].Stopped && state.Spawned.Count == stoppedCount);
                cached = World; LoadRoom("1-1", false); LoadRoom(id, true);
                Check($"{id} cached room retains stopper state at {hz}Hz", ReferenceEquals(World, cached) && WaterFishPopulationForRoom().Sources[source].Stopped);
                LoadRoom(id, false);
                Check($"{id} fresh room has fresh generator lifecycle at {hz}Hz", !ReferenceEquals(WaterFishPopulationForRoom(), state) && WaterFishPopulationForRoom().Sources.Count == 0);
            }

            StartGame(31, true); LoadRoom("8-4b", false); State = Screen.Playing; CameraX = 0; Player.Position = new(-100, 0);
            var flyingSource = World.Triggers.First(t => t.Kind == "CheepCheepGenerator");
            generatorClocks.Remove(World.Data.Id + ":" + flyingSource.Name); TickGenerators(4);
            var flying = World.Enemies.Last(e => e.Kind == "FlyingFish");
            Check("non-water Cheep generator retains bottom launch and ballistic velocity", !World.IsWater && flying.Body.Position.Y == 24 && flying.Body.Velocity.X >= 40 && flying.Body.Velocity.X < 120 && flying.Body.Velocity.Y <= -270 && flying.Body.Velocity.Y > -330 && flying.Active);

            StartGame(3, true); State = Screen.Playing;
            var flameSource = World.Triggers.First(t => t.Kind == "BowserFlameGenerator"); CameraX = flameSource.X - 128;
            generatorClocks.Remove(World.Data.Id + ":" + flameSource.Name); TickGenerators(4);
            var flame = World.Enemies.Last(e => e.Kind == "BowserFlame");
            Check("Bowser flame generator keeps original right-edge spawn", flame.Body.Position.X == CameraX + 280 && new[] { -40f, -64f, -88f }.Contains(flame.Body.Position.Y) && flame.Active);

            StartGame(16, true); State = Screen.Playing;
            var cannon = World.Triggers.First(t => t.Kind == "BulletBillCannon"); CameraX = cannon.X - 128; Player.Position = cannon.Position - new Vector2(64, 0);
            generatorClocks.Remove(World.Data.Id + ":" + cannon.Name); TickGenerators(4);
            var bullet = World.Enemies.Last(e => e.Kind == "BulletBill");
            Check("cannon retains muzzle position and launch direction", bullet.Body.Position == cannon.Position + new Vector2(0, 8) && bullet.Direction == -1 && bullet.Active);
        }
        catch (Exception ex) { Check("water fish population mechanism exception", false, ex.ToString()); }
        finally { SetPhysicsProcess(enabled); }
    }
}
