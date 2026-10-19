using Godot;

namespace MarioStudy;

public partial class Game
{
    public void TickGame(float dt, Controls input)
    {
        if (pipeClock > 0)
        {
            float moveTime = MathF.Min(dt, pipeClock);
            Player.Position += pipeVelocity * moveTime; pipeClock -= moveTime;
            if (pipeClock <= 0) { var action = afterPipe; ResetPipeAnimation(); action?.Invoke(); }
            return;
        }
        pipeCooldown = MathF.Max(0, pipeCooldown - dt);
        fireCooldown = MathF.Max(0, fireCooldown - dt);
        Invulnerable = MathF.Max(0, Invulnerable - dt);
        if (StarTime > 0) { StarTime -= dt; if (StarTime <= 0) PlayThemeMusic(); }
        TimeLeft -= dt * 1.5f;
        if (TimeLeft < 100 && !hurry) { hurry = true; Sound.Effect("TimerRunningLow"); PlayThemeMusic(); }
        if (TimeLeft <= 0) { Die(); return; }
        World.UpdatePlatforms(dt);
        TickBlockTimers(dt);
        CapturePlayerContact();
        TickPlayer(dt, input);
        if (State != Screen.Playing) return;
        TickItems(dt);
        if (State != Screen.Playing) return;
        PreparePipeEntry(input);
        TickEnemies(dt);
        TickShots(dt);
        TickTriggers(dt, input);
        for (int i = 0; i < World.Coins.Count; i++)
            if (!World.CollectedCoins.Contains(i) && Player.Box.Intersects(new Rect2(World.Coins[i] - new Vector2(6, 14), new Vector2(12, 16))))
            { World.CollectedCoins.Add(i); AddCoin(World.Coins[i]); }
        TickPopups(dt);
        UpdateCamera();
        if (Player.Position.Y > 64) Die();
    }
    private void UpdateCamera()
    {
        float target = CameraX;
        if (Player.Position.X < CameraX + 80) target = Player.Position.X - 80;
        else if (Player.Position.X > CameraX + 160) target = Player.Position.X - 160;
        CameraX = Math.Clamp(target, World.MinX, MathF.Max(World.MinX, World.MaxX - 256));
        float x = Math.Clamp(Player.Position.X, World.MinX + Player.Width / 2, World.MaxX - Player.Width / 2);
        if (x != Player.Position.X)
        {
            Player.Position = new(x, Player.Position.Y);
            Player.Velocity = new(0, Player.Velocity.Y);
        }
    }
    private static float JumpLaunchSpeed(bool running, float speed, float dt) => running || MathF.Abs(speed) > 100 ? -(MathF.Sqrt(2 * 570 * 65) + 570 * dt / 2) : -255;
    private void TickPlayer(float dt, Controls input)
    {
        bool wasGrounded = Player.Grounded;
        bool blockedStanding = Power > 0 && Player.Height < 32 && !World.Headroom(Player, 32);
        Crouching = Power > 0 && !World.IsWater && (input.Down && wasGrounded || blockedStanding);
        if (Crouching) Player.Height = 16;
        else if (Power > 0 && World.Headroom(Player, 32)) Player.Height = 32;
        else if (Power == 0) Player.Height = 16;
        float axis = Crouching && !input.Jump ? 0 : input.Axis;
        float maxSpeed = World.IsWater ? 72 : input.Run ? 150 : 90;
        float acceleration = wasGrounded ? 560 : 420;
        if (axis != 0)
        {
            float a = MathF.Sign(Player.Velocity.X) != MathF.Sign(axis) && MathF.Abs(Player.Velocity.X) > 12 ? acceleration * 1.5f : acceleration;
            Player.Velocity = new(Mathf.MoveToward(Player.Velocity.X, axis * maxSpeed, a * dt), Player.Velocity.Y);
            if (wasGrounded || MathF.Abs(Player.Velocity.X) < 8) FacingLeft = axis < 0;
        }
        else if (wasGrounded) Player.Velocity = new(Mathf.MoveToward(Player.Velocity.X, 0, 500 * dt), Player.Velocity.Y);
        if (World.IsWater)
        {
            Player.Velocity += new Vector2(0, 240 * dt);
            Player.Velocity = new(Player.Velocity.X, MathF.Min(Player.Velocity.Y, 84));
            if (input.JumpPressed && Player.Position.Y > -176) { Player.Velocity = new(Player.Velocity.X, -115); Sound.Effect("Swim"); }
            if (Player.Position.Y - Player.Height < -184) { Player.Position = new(Player.Position.X, -184 + Player.Height); Player.Velocity = new(Player.Velocity.X, MathF.Max(0, Player.Velocity.Y)); }
        }
        else
        {
            if (input.JumpPressed && wasGrounded)
            {
                Player.Velocity = new(Player.Velocity.X, JumpLaunchSpeed(input.Run || input.HighJump, Player.Velocity.X, dt));
                Sound.Effect(Power > 0 ? "BigJump" : "SmallJump");
            }
            float gravity = Player.Velocity.Y < 0 && input.Jump ? 570 : 1350;
            Player.Velocity += new Vector2(0, gravity * dt);
            Player.Velocity = new(Player.Velocity.X, MathF.Min(Player.Velocity.Y, 270));
        }
        // Vines use their original map position and lead to the data-defined cloud room.
        var vine = World.Triggers.FirstOrDefault(t => t.Kind == "Vine" && MathF.Abs(Player.Position.X - t.X) < 10);
        if (vine != null && (input.Up || input.Down))
        {
            Player.Position = new(vine.X, Player.Position.Y + (input.Up ? -64 : 64) * dt); Player.Velocity = Vector2.Zero;
            if (Player.Position.Y < -192 && vine.Bool("can_tele", true)) EnterCloudRoom();
        }
        else MovePlayerWithRunningGap(dt, wasGrounded, input.JumpPressed);
        if (Player.Grounded) stompCombo = 0;
        if (Power == 2 && input.FirePressed && fireCooldown == 0 && World.Shots.Count(s => !s.Enemy && !s.Removed) < 2)
        {
            fireCooldown = .2f;
            var shot = new FireShot(); shot.Body.Position = Player.Position + new Vector2(FacingLeft ? -10 : 10, Crouching ? -6 : -18);
            shot.Body.Velocity = new(FacingLeft ? -240 : 240, 130); World.Shots.Add(shot); Sound.Effect("Fireball");
        }
    }
    public void HitBlock(Block block)
    {
        if (block.Removed || block.Bump > 0) return;
        if (block.Used) { Sound.Effect("Bump"); return; }
        block.Hidden = false; block.Bump = .18f;
        BumpContentsAbove(block);
        foreach (var e in World.Enemies)
            if (!e.Dead && MathF.Abs(e.Body.Position.X - block.Center.X) < 17 && MathF.Abs(e.Body.Position.Y - (block.Center.Y - 8)) < 6) DefeatEnemy(e, true);
        if (block.Item == "" && Power > 0)
        {
            block.Removed = true; Sound.Effect("BreakBlock"); Score += 50;
            for (int i = 0; i < 4; i++) World.Popups.Add(new Popup { Position = block.Center + new Vector2(i % 2 * 8 - 4, i / 2 * 8 - 4), Velocity = new(i % 2 == 0 ? -60 : 60, i / 2 == 0 ? -220 : -150), Duration = 1.0f, Debris = true });
            return;
        }
        if (block.Item == "") { Sound.Effect("Bump"); return; }
        if (block.Item.Contains("Coin") && block.CoinsLeft > 1 && !block.CoinWindowStarted)
        { block.CoinWindowStarted = true; block.CoinWindowRemaining = Block.CoinWindowDuration; }
        if (--block.CoinsLeft <= 0) block.Used = true;
        if (block.Item.Contains("Coin")) { AddCoin(block.Center, block); return; }
        if (block.Item == "Vine")
        {
            World.Triggers.Add(new EntityData { Kind = "Vine", X = block.Center.X, Y = block.Center.Y });
            Sound.Effect("VineAppear"); return;
        }
        string kind = block.Item == "PowerUp" || block.Item is "SuperMushroom" or "FireFlower" ? (Power == 0 ? "SuperMushroom" : "FireFlower") : block.Item;
        if (kind == "StarMan") kind = "SuperStar";
        var item = new Item { Kind = kind, Home = new(block.Center.X, block.Box.Position.Y), SourceBlock = block };
        item.Body.Position = item.Home + new Vector2(0, item.Body.Height - block.Lift);
        item.Body.Velocity = Vector2.Zero;
        World.Items.Add(item); Sound.Effect("ItemAppear");
    }
    private void BumpContentsAbove(Block block)
    {
        Rect2 above = new(block.Box.Position - new Vector2(0, 16), new Vector2(block.Box.Size.X, 16));
        for (int i = 0; i < World.Coins.Count; i++)
            if (!World.CollectedCoins.Contains(i) && above.Intersects(new Rect2(World.Coins[i] - new Vector2(6, 7), new Vector2(12, 14))))
            { World.CollectedCoins.Add(i); AddCoin(World.Coins[i]); SpawnCoinArc(World.Coins[i] + new Vector2(0, 8), 8); }
        foreach (var item in World.Items)
        {
            if (item.Removed || item.Emerging || item.Kind == "FireFlower") continue;
            if (MathF.Abs(item.Body.Position.Y - block.Box.Position.Y) > 3 || item.Body.Box.End.X <= block.Box.Position.X || item.Body.Box.Position.X >= block.Box.End.X) continue;
            float direction = item.Body.Velocity.X != 0 ? -MathF.Sign(item.Body.Velocity.X) : item.Body.Position.X >= Player.Position.X ? 1 : -1;
            item.Body.Velocity = new(direction * MathF.Max(40, MathF.Abs(item.Body.Velocity.X)), -150);
            item.Body.Grounded = false; item.Body.Support = null;
        }
    }
    private void AddCoin(Vector2 position, Block? source = null)
    {
        Coins++; Score += 200; Sound.Effect("Coin");
        if (source != null) SpawnCoinArc(new(source.Center.X, source.Box.End.Y), 24, source);
        if (Coins >= 100) { Coins -= 100; Lives++; Sound.Effect("1up"); }
    }
    private void SpawnCoinArc(Vector2 feet, float rise, Block? source = null)
    {
        const float gravity = 480;
        World.Popups.Add(new Popup { Position = feet, Velocity = new(0, -MathF.Sqrt(2 * gravity * rise)), Sprite = "Items/SpinningCoin.png", Duration = 2 * MathF.Sqrt(2 * rise / gravity), CoinArc = true, SourceBlock = source });
    }
    private void TickBlockTimers(float dt)
    {
        foreach (var block in World.Blocks)
        {
            block.Bump = MathF.Max(0, block.Bump - dt);
            if (!block.CoinWindowStarted || block.Used) continue;
            block.CoinWindowRemaining = MathF.Max(0, block.CoinWindowRemaining - dt);
            if (block.CoinWindowRemaining == 0) block.CoinsLeft = Math.Min(block.CoinsLeft, 1);
        }
    }
    private void TickPopups(float dt)
    {
        foreach (var popup in World.Popups)
        {
            popup.Age += dt; popup.Position += popup.Velocity * dt;
            if (popup.CoinArc) popup.Position += new Vector2(0, 480 * dt * dt / 2);
            if (popup.Debris || popup.CoinArc) popup.Velocity += new Vector2(0, (popup.CoinArc ? 480 : 600) * dt);
        }
        World.Popups.RemoveAll(p => p.Age >= p.Duration);
    }
    private void Popup(string text, Vector2 position, float duration = .7f) => World.Popups.Add(new Popup { Position = position, Text = text, Velocity = new(0, -28), Duration = duration });
    private void TickItems(float dt)
    {
        foreach (var item in World.Items)
        {
            if (item.Removed) continue;
            bool wasEmerging = item.Emerging;
            item.Age += dt;
            float moveTime = dt;
            if (wasEmerging)
            {
                float progress = Math.Clamp(item.Age / Item.EmergenceDuration, 0, 1);
                item.Body.Position = item.Home + new Vector2(0, item.Body.Height * (1 - progress) - (item.Emerging ? item.SourceBlock?.Lift ?? 0 : 0));
                if (item.Emerging) continue;
                // Finish exactly on the block top before enabling collisions. The last
                // rising sample may still overlap the block and falsely reverse direction.
                item.Body.Position = item.Home;
                item.Body.Velocity = item.Kind == "FireFlower" ? Vector2.Zero : new Vector2(40, 0);
                moveTime = MathF.Min(dt, MathF.Max(0, item.Age - Item.EmergenceDuration));
            }
            if (item.Kind != "FireFlower")
            {
                item.Body.Velocity += new Vector2(0, 650 * moveTime);
                if (item.Kind == "SuperStar" && item.Body.Grounded) item.Body.Velocity = new(item.Body.Velocity.X, -210);
                float vx = item.Body.Velocity.X;
                if (moveTime > 0) World.Move(item.Body, moveTime);
                if (item.Body.HitWall) item.Body.Velocity = new(-vx, item.Body.Velocity.Y);
            }
            if (Player.Box.Intersects(item.Body.Box))
            {
                item.Removed = true;
                if (item.Kind.Contains("OneUp") || item.Kind == "1UP") { Lives++; Sound.Effect("1up"); Popup("1UP", Player.Position - new Vector2(12, 32)); }
                else if (item.Kind == "SuperStar") { StarTime = 12; PlayThemeMusic(); Score += 1000; Sound.Effect("Powerup"); }
                else { BeginPowerTransition(item.Kind == "FireFlower" ? 2 : Math.Max(Power, 1)); Invulnerable = .6f; Sound.Effect("Powerup"); Score += 1000; Popup("1000", Player.Position - new Vector2(10, 40)); }
                if (State == Screen.Transform) return;
            }
            if (item.Body.Position.Y > 64) item.Removed = true;
        }
    }
    public void Hurt()
    {
        if (Invulnerable > 0 || StarTime > 0 || State != Screen.Playing) return;
        if (Power > 0)
        {
            Power--; if (Power == 0) Crouching = false;
            Player.Height = Power == 0 || Crouching ? 16 : 32;
            Invulnerable = 2.2f; Sound.Effect("Damage");
        }
        else Die();
    }
    public void Die()
    {
        if (State != Screen.Playing) return;
        ResetPipeAnimation();
        State = Screen.Dying; StateClock = Sound.DeathDurationSeconds; deathY = Player.Position.Y;
        Player.Velocity = new(0, -270); Sound.BeginDeath(); StarTime = 0;
    }
    private void FinishDeath()
    {
        Lives--; Power = 0; Invulnerable = 0; SaveProfile();
        if (Lives <= 0 && !practice) { State = Screen.GameOver; StateClock = 3; Sound.Music("GameOver", false); return; }
        if (Lives <= 0) Lives = 3;
        roomCache.Clear(); firedTriggers.Clear();
        LoadRoom(MainId, false); TimeLeft = Maps.Rooms[MainId].Time; hurry = false;
        Sound.StopMusic(); State = Screen.Intro; StateClock = 1.65f;
    }
    private int fireworkGoal, fireworkMade;
    private float clearBonusDoneAt = -1, clearCameraStart, clearCastleX, clearCastleY, clearFlagTop;
    private bool clearInsideCastle;
    private void StartClear(EntityData end, bool castle)
    {
        if (State != Screen.Playing) return;
        ResetPipeAnimation();
        State = Screen.Clear; StateClock = 0; flagX = end.X; clearStartY = Player.Position.Y;
        TimeLeft = MathF.Floor(MathF.Max(0, TimeLeft));
        int digit = (int)TimeLeft % 10; fireworkGoal = !castle && digit is 1 or 3 or 6 ? digit : 0;
        fireworkMade = 0; clearBonusDoneAt = -1;
        clearCameraStart = CameraX; clearInsideCastle = false;
        var building = World.Triggers.Where(t => t.Kind is "EndSmallCastle" or "EndFinalCastle").OrderBy(t => MathF.Abs(t.X - flagX)).FirstOrDefault();
        clearCastleX = building?.X ?? flagX + 96; clearCastleY = building?.Y ?? 0;
        Player.Velocity = Vector2.Zero; Player.Grounded = true; Sound.StopMusic(); Sound.Effect(castle ? "CastleClear" : "FlagSlide");
        if (castle)
        {
            var npc = World.Triggers.FirstOrDefault(t => t.Kind is "CastleToad" or "CastlePeach");
            clearCastleX = npc?.X - 24 ?? end.X + 352;
            clearCastleY = npc?.Y ?? 0;
            World.BridgeGone = true;
            foreach (var e in World.Enemies.Where(e => e.Kind.Contains("Bowser"))) { e.Dead = true; e.Body.Velocity = new(0, 30); }
            Score += 5000; Sound.Effect("BowserFall");
        }
        else
        {
            int height = (int)MathF.Max(0, -Player.Position.Y); int points = height > 144 ? 5000 : height > 112 ? 2000 : height > 80 ? 800 : height > 48 ? 400 : 100;
            Score += points; Popup(points.ToString(), new(flagX + 12, -140), 2);
            clearFlagTop = World.FlagBase(end)?.Position.Y ?? end.Y - 8;
            clearStartY = MathF.Min(clearStartY, clearFlagTop);
            Player.Position = new(flagX - 8, clearStartY);
            Player.Grounded = MathF.Abs(clearStartY - clearFlagTop) < .001f;
        }
        message = castle ? "CASTLE" : "FLAG";
    }
    private void TickClear(float dt)
    {
        StateClock += dt;
        TickPopups(dt);
        if (message == "FLAG")
        {
            if (StateClock < 1.3f)
            {
                Player.Position = new(flagX - 8, Mathf.Lerp(clearStartY, clearFlagTop, StateClock / 1.3f));
                Player.Velocity = Vector2.Zero;
                Player.Grounded = MathF.Abs(Player.Position.Y - clearFlagTop) < .001f;
            }
            else
            {
                if (StateClock - dt < 1.3f)
                {
                    Player.Position = new(flagX - 8, clearFlagTop);
                    Player.Velocity = Vector2.Zero; Player.Grounded = true;
                    Sound.Effect("CourseClear");
                }
                float pan = Math.Clamp((StateClock - 1.3f) / 2, 0, 1);
                pan = pan * pan * (3 - 2 * pan);
                float destination = Math.Clamp(clearCastleX - 128, World.MinX, MathF.Max(World.MinX, World.MaxX - 256));
                CameraX = Mathf.Lerp(clearCameraStart, destination, pan);
                float walkDt = MathF.Min(dt, MathF.Max(0, StateClock - 1.3f));
                if (!clearInsideCastle && walkDt > 0)
                {
                    float remaining = MathF.Max(0, clearCastleX - Player.Position.X);
                    Player.Velocity = new(MathF.Min(52, remaining / walkDt), MathF.Min(270, Player.Velocity.Y + 570 * walkDt));
                    World.Move(Player, walkDt); FacingLeft = false;
                    clearInsideCastle = MathF.Abs(Player.Position.X - clearCastleX) < .01f && Player.Grounded && MathF.Abs(Player.Position.Y - clearCastleY) < .05f;
                    if (clearInsideCastle) Player.Velocity = Vector2.Zero;
                }
            }
        }
        else
        {
            foreach (var e in World.Enemies.Where(e => e.Dead)) { e.Body.Velocity += new Vector2(0, 700 * dt); e.Body.Position += e.Body.Velocity * dt; }
            float speed = StateClock > 1.5f && Player.Position.X < clearCastleX - .5f ? 48 : 0;
            Player.Velocity = new(speed, MathF.Min(270, Player.Velocity.Y + 900 * dt));
            World.Move(Player, dt);
            UpdateCamera();
        }
        if (StateClock <= 3.5f) return;
        if (TimeLeft > 0)
        {
            int before = (int)MathF.Ceiling(TimeLeft);
            TimeLeft = MathF.Max(0, TimeLeft - 200 * dt); Score += (before - (int)MathF.Ceiling(TimeLeft)) * 50; return;
        }
        if (clearBonusDoneAt < 0) clearBonusDoneAt = StateClock;
        if (fireworkMade < fireworkGoal && StateClock > clearBonusDoneAt + .5f + fireworkMade * .45f)
        {
            Vector2[] offsets = [new(-32, -96), new(16, -144), new(48, -112)];
            World.Popups.Add(new Popup { Position = new Vector2(clearCastleX, clearCastleY) + offsets[fireworkMade % 3], Sprite = "Particles/Firework.png", Duration = .15f });
            fireworkMade++; Score += 500; Sound.Effect("Firework");
        }
        if (StateClock > MathF.Max(7.0f, clearBonusDoneAt + 1 + fireworkGoal * .45f))
        {
            profile.Completed[LevelIndex] = true; if (!practice) profile.Unlocked = Math.Max(profile.Unlocked, Math.Min(31, LevelIndex + 1)); SaveProfile();
            if (LevelIndex == 31) { State = Screen.Ending; Sound.Music("Ending"); }
            else BeginLevel(LevelIndex + 1);
        }
    }
}
