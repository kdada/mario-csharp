using Godot;

namespace MarioStudy;

public partial class Game
{
    private Vector2 View(Vector2 position) => (position + new Vector2(-CameraX, 208)).Round();
    private void SetGameDrawTransform(Vector2 offset)
    {
        float scale = GameScreenScale;
        DrawSetTransform(GameScreenOrigin + offset * scale, scale: Vector2.One * scale);
    }
    public override void _Draw()
    {
        if (World == null || Art == null) return;
        try
        {
            if (studyFramePath.Length > 0) studyRaster = new StudyRaster();
            Art.Theme = World.Theme;
            bool dark = World.Theme is "Castle" or "Underground" or "CastleWater" || Maps.IsNight(World.Data.Id, MainId);
            Color sky = dark || State == Screen.Intro ? Colors.Black : new Color("5c94fc");
            SetGameDrawTransform(Vector2.Zero);
            DrawRect(new Rect2(0, 0, 256, 240), sky);
            bool menu = State is Screen.Select or Screen.Help or Screen.GameOver or Screen.Ending;
            if (!menu && State != Screen.Intro)
            {
                SetGameDrawTransform(new Vector2(-MathF.Round(CameraX), 208));
                DrawScenery();
                foreach (var plant in World.Enemies.Where(e => e.Kind == "PiranhaPlant" && !e.Removed && e.Active)) DrawEnemy(plant);
                if (pipeClock > 0) DrawPlayer();
                DrawFlagpoles(); DrawTiles(); DrawMechanisms(); DrawBlocks(); DrawCoins();
                foreach (var item in World.Items.Where(i => !i.Removed))
                    Art.Sprite(this, "Items/" + (item.Kind switch { "OneUp" => "OneUp", "SuperStar" => "StarMan", _ => item.Kind }) + ".png", item.Body.Position, "default", Clock,
                        clipBottom: item.Emerging ? item.SourceBlock?.DrawPosition.Y ?? item.Home.Y : null);
                foreach (var e in World.Enemies.Where(e => e.Kind != "PiranhaPlant" && !e.Removed && e.Active && e.Body.Position.X > CameraX - 48 && e.Body.Position.X < CameraX + 300)) DrawEnemy(e);
                foreach (var shot in World.Shots.Where(s => !s.Removed)) Art.Sprite(this, shot.Hammer ? "Items/Hammer.png" : "Items/PlayerFireball.png", shot.Body.Position, "default", Clock);
                if (pipeClock <= 0 && !(State == Screen.Clear && message == "FLAG" && clearInsideCastle) && !(Invulnerable > 0 && State != Screen.Transform && (int)(Clock * 18) % 2 == 0)) DrawPlayer();
                foreach (var popup in World.Popups)
                {
                    if (popup.Sprite.Length > 0) Art.Sprite(this, popup.Sprite, popup.Position, "default", popup.Age, clipBottom: popup.CoinArc ? popup.SourceBlock?.DrawPosition.Y : null);
                    else if (popup.Debris) Art.Tile(this, TerrainPath(), popup.Position, 0, 8, 8);
                    else Art.Text(this, popup.Text, popup.Position);
                }
                SetGameDrawTransform(Vector2.Zero);
            }
            DrawHud();
            switch (State)
            {
                case Screen.Title: DrawTitle(); break;
                case Screen.Select: DrawLevelSelect(); break;
                case Screen.Intro:
                    DrawRect(new Rect2(0, 40, 256, 200), Colors.Black);
                    Art.Text(this, "WORLD " + MainId, new(88, 80));
                    Art.Sprite(this, "Players/Mario/Small.png", new(104, 127)); Art.Text(this, "* " + Lives, new(128, 112));
                    if (practice) Art.Text(this, "PRACTICE", new(96, 152), new Color("ffcc88"));
                    break;
                case Screen.Pause: DrawPause(); break;
                case Screen.GameOver:
                    Art.Text(this, "GAME OVER", new(88, 104)); Art.Text(this, showTouch ? "OK TO TITLE" : "ENTER TO TITLE", new(showTouch ? 84 : 72, 144)); break;
                case Screen.Ending:
                    Art.Sprite(this, "Players/Mario/Big.png", new(104, 112)); Art.Sprite(this, "Players/Peach.png", new(152, 112));
                    Art.Text(this, "THANK YOU MARIO!", new(64, 136)); Art.Text(this, "YOUR QUEST IS OVER.", new(48, 160)); Art.Text(this, "ALL 8 WORLDS CLEARED!", new(40, 184)); Art.Text(this, showTouch ? "OK TO TITLE" : "ENTER TO TITLE", new(showTouch ? 84 : 72, 216)); break;
                case Screen.Help: DrawHelp(); break;
            }
            if (State == Screen.Clear && message == "CASTLE" && StateClock > 3.5f)
            {
                DrawRect(new Rect2(16, 64, 224, 48), Colors.Black);
                Art.Text(this, LevelIndex == 31 ? "THANK YOU MARIO!" : "THANK YOU MARIO!\nBUT OUR PRINCESS IS IN\nANOTHER CASTLE!", new(24, 68));
            }

            DrawSetTransform(Vector2.Zero);
            DrawPhoneBorders();
            DrawTouchControls();
            FinishStudyFrame();
        }
        catch (Exception ex) { lastRenderError = ex.ToString(); GD.PushError("Render error: " + ex); if (!verifying) { SetProcess(false); SetPhysicsProcess(false); } }
    }
    private string TerrainPath() => "Tilesets/Terrain/" + (World.Theme is "CastleWater" ? "Castle" : World.Theme is "Bonus" ? "Overworld" : World.Theme) + ".png";
    private void DrawScenery()
    {
        if (World.Theme != "Overworld" || World.Data.Id.EndsWith('a') || World.Data.Id.StartsWith("cloud-") || World.Data.Id == "4-2b") return;
        bool night = Maps.IsNight(World.Data.Id, MainId);
        var clouds = Art.Texture(night ? "Backgrounds/CloudOverlays/NightCloudOverlay.png" : "Backgrounds/CloudOverlays/CloudOverlay.png");
        float width = clouds.GetWidth();
        for (float x = MathF.Floor((CameraX - width) / width) * width; x < CameraX + 256; x += width)
            DrawTexture(clouds, new(x, -321));
        if (MainId.EndsWith("-3")) return;
        var hills = Art.Texture(night ? "Backgrounds/Hills/OverworldNight.png" : "Backgrounds/Hills/Overworld.png");
        float hillWidth = hills.GetWidth();
        for (float x = MathF.Floor((CameraX - hillWidth) / hillWidth) * hillWidth; x < CameraX + 256; x += hillWidth)
            DrawTexture(hills, new(x, -hills.GetHeight() + 32));
    }
    private void DrawTiles()
    {
        string terrain = TerrainPath();
        foreach (var tile in World.Data.Deco)
            if (tile[0] * 16 > CameraX - 16 && tile[0] * 16 < CameraX + 256)
                Art.Tile(this, tile[2] < 0 ? TerrainPath() : "Tilesets/Deco/OverworldDeco.png", new(tile[0] * 16, tile[1] * 16), (int)tile[3], (int)tile[4]);
        foreach (var tile in World.Data.Tiles)
        {
            float x = tile[0] * 16;
            if (x < CameraX - 16 || x > CameraX + 256) continue;
            int source = (int)tile[2]; string path;
            if (source == 0) path = terrain;
            else if (source == 2) path = "Tilesets/Liquids.png";
            else if (source == 3) path = "Tilesets/Deco/OverworldDeco.png";
            else if (World.Catalog.Atlases.TryGetValue(source, out var atlas) && atlas.Texture.Length > 0) path = atlas.Texture;
            else continue;
            int ax = (int)tile[3], ay = (int)tile[4];
            if (source == 0 && ax is 8 or 9 && ay >= 2 && ay <= 7) ay = 6 + ay % 2;
            Art.Tile(this, path, new(x, tile[1] * 16), ax, ay);
        }
    }
    private void DrawBlocks()
    {
        foreach (var block in World.Blocks)
        {
            if (block.Removed || block.Hidden || block.Center.X < CameraX - 16 || block.Center.X > CameraX + 272) continue;
            Vector2 at = block.DrawPosition;
            if (block.Used)
            {
                // The spent block is terrain tile (4, 0), not a darker question frame.
                Art.Tile(this, TerrainPath(), at, 4, 0);
            }
            else if (block.Kind.Contains("Question")) Art.Sprite(this, "Blocks/QuestionBlock.png", at + new Vector2(8, 16), "default", Clock);
            else Art.Tile(this, TerrainPath(), at, 0, 4);
        }
    }
    private void DrawCoins()
    {
        for (int i = 0; i < World.Coins.Count; i++)
            if (!World.CollectedCoins.Contains(i) && World.Coins[i].X > CameraX - 16 && World.Coins[i].X < CameraX + 272)
                Art.Sprite(this, "Items/Coin.png", World.Coins[i] + new Vector2(0, 8), "default", Clock);
    }
    private void DrawFlagpoles()
    {
        foreach (var t in World.Triggers.Where(t => t.Kind == "EndFlagpole" && t.X >= CameraX - 16 && t.X <= CameraX + 272))
        {
            DrawLine(new(t.X, -160), new(t.X, -8), new Color("a4e85c"), 2);
            DrawCircle(new(t.X, -164), 4, new Color("a4e85c"));
            Art.Sprite(this, "Tilesets/Flag.png", new(t.X - 8, State == Screen.Clear ? MathF.Min(-16, -144 + StateClock * 100) : -144), "default", Clock);
        }
    }
    private void DrawMechanisms()
    {
        foreach (var p in World.Platforms)
        {
            if (p.Position.X < CameraX - 64 || p.Position.X > CameraX + 320) continue;
            var tex = Art.Texture("Tilesets/Platform.png");
            for (int i = 0; i < p.Width; i += 16) DrawTextureRectRegion(tex, new Rect2((p.Position + new Vector2(i - p.Width / 2, 0)).Round(), new Vector2(16, 8)), new Rect2(0, 0, 16, 8));
            if (p.Kind.Contains("Rope")) DrawLine(new(p.Home.X, -176), p.Position, new Color("b8b8b8"), 1);
        }
        foreach (var t in World.Triggers)
        {
            if (t.X < CameraX - 256 || t.X > CameraX + 320) continue;
            switch (t.Kind)
            {
                // Castle silhouettes are original terrain tiles imported as decoration.
                case "EndSmallCastle": case "StartCastle": case "EndFinalCastle": case "LargeStartCastle": break;
                case "CastleBridge":
                    if (!World.BridgeGone)
                    {
                        for (int i = 0; i < 13; i++) Art.Tile(this, TerrainPath(), t.Position + new Vector2(i * 16 - 8, 0), 4, 3);
                        Art.Sprite(this, "Items/BridgeAxe.png", CastleAxeFeet(t), "default", Clock);
                    }
                    break;
                case "Firebar":
                    for (int n = 0; n <= 6; n++) Art.Sprite(this, "Items/PlayerFireball.png", t.Position + Vector2.FromAngle(Clock * 1.85f * (t.Number("direction") == 1 ? -1 : 1)) * (n * 8) + new Vector2(0, 4), "default", Clock);
                    break;
                case "Trampoline": Art.Sprite(this, "Blocks/Trampoline.png", t.Position, "default", Clock); break;
                case "CastleToad": Art.Sprite(this, "Players/ToadNPC.png", t.Position); break;
                case "CastlePeach": Art.Sprite(this, "Players/Peach.png", t.Position); break;
                case "Vine":
                    for (float y = t.Y; y > -200; y -= 16) Art.Sprite(this, "Items/Vine.png", new(t.X, y), "default", Clock);
                    break;
                case "WarpZone": DrawWarpZone(t); break;
            }
        }
    }
    private void DrawWarpZone(EntityData zone)
    {
        Art.Text(this, "WELCOME TO WARP ZONE!", zone.Position + new Vector2(-88, -96));
        if (!zone.P.TryGetValue("pipe_destinations", out var destinations)) return;
        for (int i = 0; i < destinations.GetArrayLength(); i++)
        {
            int world = destinations[i].GetInt32(); if (world < 1) continue;
            float x = zone.X + (i - 1) * 64;
            Art.Text(this, world.ToString(), new(x - 4, zone.Y - 72));
        }
    }
    private void DrawEnemy(Enemy e)
    {
        if (e.PipeSuppressed) return;
        string sprite = e.Kind switch
        {
            "FlyingFish" => "Enemies/RedCheepCheep.png", "TrueBowser" => "Enemies/Bowser.png", "BowserFlame" => "Items/BowserFlame.png",
            _ => "Enemies/" + e.Kind + ".png"
        };
        string animation = e.Kind == "Goomba" && e.Squashed ? "Stomped" : e.Kind.Contains("Bowser") ? "Idle" : "Walk";
        if (e.Shell) sprite = e.Kind == "BuzzyBeetle" ? "Enemies/BuzzyBeetleShell.png" : e.Kind.StartsWith("Red") ? "Enemies/RedKoopaShell.png" : "Enemies/GreenKoopaShell.png";
        if (e.Winged) Art.Sprite(this, "Items/Wings.png", e.Body.Position + new Vector2(0, -8), "default", Clock);
        if (e.Kind == "Lakitu") Art.Sprite(this, "Enemies/LakituCloud.png", e.Body.Position + new Vector2(0, 8), "default", Clock);
        Art.Sprite(this, sprite, e.Body.Position, animation, e.Clock, e.Direction > 0, flipVertical: e.Flipped);
    }
    private void DrawPlayer()
    {
        int visible = VisiblePower;
        string size = visible == 0 ? "Small" : visible == 2 ? "Fire" : "Big";
        string anim = State == Screen.Transform ? Crouching ? "Crouch" : "Idle" : pipeClock > 0 ? pipeVelocity.X != 0 ? "Move" : "Idle" : State == Screen.Dying ? "Die" : Crouching ? "Crouch" : World.IsWater ? Player.Velocity.Y < 0 ? "SwimUp" : "SwimIdle" : !Player.Grounded && State != Screen.Title ? "Jump" : MathF.Abs(Player.Velocity.X) > 4 ? "Move" : "Idle";
        if (State == Screen.Clear && StateClock < 1.3f && message == "FLAG") anim = "Climb";
        Color tint = StarTime > 0 ? Color.FromHsv(Clock * 2 % 1, .35f, 1) : Colors.White;
        Art.Sprite(this, "Players/Mario/" + size + ".png", Player.Position, anim, animationTime * (MathF.Abs(Player.Velocity.X) > 100 ? 2 : 1), FacingLeft, tint);
    }
    private void DrawHud()
    {
        Art.Text(this, "MARIO", new(24, 8)); Art.Text(this, Score.ToString("D6"), new(24, 16));
        Art.Sprite(this, "UI/CoinIcon.png", new(102, 24), "default", Clock); Art.Text(this, "*" + Coins.ToString("D2"), new(112, 16));
        Art.Text(this, "WORLD", new(160, 8)); Art.Text(this, MainId, new(168, 16));
        Art.Text(this, "TIME", new(216, 8)); if (State is not (Screen.Title or Screen.Select or Screen.Help)) Art.Text(this, Math.Max(0, (int)TimeLeft).ToString("D3"), new(224, 16));
    }
    private void Panel(Rect2 box)
    {
        DrawRect(box, new Color(0, 0, 0, .92f)); DrawRect(box, new Color("fcfcfc"), false, 1);
    }
    private void DrawTitle()
    {
        var logo = Art.Texture("UI/TitleSMB1.png");
        DrawTextureRectRegion(logo, new Rect2(40, 32, 176, 88), new Rect2(0, 0, 176, 88));
        Art.Text(this, "C# STUDY EDITION", new(64, 126), new Color("ffcc88"));
        string[] options = ["NEW GAME", "CONTINUE", "LEVEL SELECT", "CONTROLS"];
        for (int i = 0; i < options.Length; i++) Art.Text(this, (titleChoice == i ? "* " : "  ") + options[i], new(64, 146 + i * 16));
        Art.Text(this, "TOP-" + profile.HighScore.ToString("D6"), new(80, 218));
    }
    private void DrawLevelSelect()
    {
        Art.Text(this, "LEVEL SELECT", new(80, 48)); Art.Text(this, "8 WORLDS  32 COURSES", new(48, 64), new Color("ffcc88"));
        for (int w = 0; w < 8; w++)
            for (int l = 0; l < 4; l++)
            {
                int i = w * 4 + l; Vector2 at = new(40 + l * 52, 86 + w * 14);
                if (selectChoice == i) DrawRect(new Rect2(at - new Vector2(4, 2), new Vector2(36, 12)), new Color("b53120"));
                Art.Text(this, Maps.MainLevels[i], at, profile.Completed[i] ? new Color("80d010") : Colors.White);
            }
        if (showTouch) Art.Text(this, "OK PLAY  BACK", new(80, 218));
        else { Art.Text(this, "ARROWS SELECT  J PLAY", new(40, 210)); Art.Text(this, "ESC BACK", new(96, 226)); }
    }
    private void DrawPause()
    {
        Panel(new Rect2(40, 64, 176, 112)); Art.Text(this, "PAUSED", new(104, 76));
        string[] options = ["RESUME", "RESTART LEVEL", "LEVEL SELECT", "TITLE"];
        for (int i = 0; i < options.Length; i++) Art.Text(this, (pauseChoice == i ? "* " : "  ") + options[i], new(56, 96 + i * 16));
    }
    private void DrawHelp()
    {
        Art.Text(this, "CONTROLS", new(96, 48));
        Art.Text(this, showTouch
            ? "ARROWS - MOVE / CLIMB\nDOWN - DUCK / PIPE\nJUMP - JUMP / SWIM\nRUN - RUN / FIREBALL\nII - PAUSE\nHOLD RUN AND JUMP\nFOR A LONGER JUMP"
            : "A D - MOVE\nJ - JUMP / SWIM\nK - RUN / FIREBALL\nS DOWN - DUCK / PIPE\nW UP - CLIMB VINE\nENTER ESC - PAUSE\nM - MUTE\nF11 - FULLSCREEN\nF12 - SCREENSHOT", new(32, 72));
        Art.Text(this, showTouch ? "BACK TO TITLE" : "ENTER TO TITLE", new(72, 220));
    }
}
