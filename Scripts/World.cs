using Godot;

namespace MarioStudy;

public sealed class Block
{
    public Vector2 Center;
    public string Kind = "BrickBlock", Item = "";
    public bool Used, Removed, Hidden;
    public int CoinsLeft = 1;
    public float Bump;
    public const float CoinWindowDuration = 3.85f;
    public bool CoinWindowStarted;
    public float CoinWindowRemaining;
    public Rect2 Box => new(Center - Vector2.One * 8, Vector2.One * 16);
    public float Lift => Bump > 0 ? MathF.Sin(Bump / .18f * MathF.PI) * 6 : 0;
    public Vector2 DrawPosition => Box.Position - new Vector2(0, Lift);
}
public sealed class Body
{
    public Vector2 Position, Velocity;
    public float Width = 12, Height = 16;
    public bool Grounded, HitWall, HitCeiling;
    public Platform? Support;
    public Rect2 Box => new(Position.X - Width / 2, Position.Y - Height, Width, Height);
}
public sealed class Enemy
{
    public string Kind = "Goomba";
    public readonly Body Body = new();
    public Vector2 Home;
    public float Clock, Timer, Phase, JumpHeight;
    public bool Active, Dead, Removed, Winged, Shell, Kicked, Squashed, Flipped, Simulated, PipeSuppressed;
    public Rect2 PreviousBox;
    public int Direction = -1, Health = 5, Combo;
    public EntityData Data = new();
}
public sealed class Item
{
    public string Kind = "SuperMushroom";
    public readonly Body Body = new();
    public const float EmergenceDuration = .7f;
    public Vector2 Home;
    public Block? SourceBlock;
    public float Age;
    public bool Emerging => Age < EmergenceDuration;
    public bool Removed;
}
public sealed class Platform
{
    public Vector2 Position, Previous, Home;
    public float Width = 48, Clock;
    public string Kind = "SidewaysPlatform";
    public bool Falling, Touched;
    public float FallSpeed;
    public EntityData Data = new();
    public Rect2 Box => new(Position.X - Width / 2, Position.Y, Width, 8);
}
public sealed class FireShot
{
    public readonly Body Body = new() { Width = 8, Height = 8 };
    public bool Enemy, Removed, Hammer;
    public float Age;
}
public sealed class Popup
{
    public Vector2 Position, Velocity;
    public string Text = "", Sprite = "";
    public float Age, Duration = 0.7f;
    public bool Debris, CoinArc;
    public Block? SourceBlock;
}

/// <summary>Tile collision and independent, deterministic axis stepping.</summary>
public sealed class World
{
    public readonly RoomData Data;
    public readonly MapCatalog Catalog;
    public readonly List<Block> Blocks = [];
    public readonly List<Enemy> Enemies = [];
    public readonly List<Item> Items = [];
    public readonly List<Platform> Platforms = [];
    public readonly List<EntityData> Triggers = [];
    public readonly List<Vector2> Coins = [];
    public readonly HashSet<int> CollectedCoins = [];
    public readonly List<FireShot> Shots = [];
    public readonly List<Popup> Popups = [];
    private readonly Dictionary<(int, int), Rect2> terrain = [];
    private readonly Dictionary<(int, int), Block> blockGrid = [];
    public string Theme;
    public bool IsWater, BridgeGone;
    public int MazeStage;
    public readonly HashSet<string> MazePoints = [];
    public float MinX, MaxX;
    public World(RoomData data, MapCatalog catalog, string mainId)
    {
        Data = data; Catalog = catalog;
        Theme = catalog.ClassicTheme(data.Id, mainId);
        IsWater = Theme is "Underwater" or "CastleWater";
        MinX = data.Tiles.Min(t => t[0] * 16);
        MaxX = data.Width;
        foreach (var t in data.Tiles)
        {
            int x = (int)t[0], y = (int)t[1], source = (int)t[2];
            if (catalog.IsSolid(source, (int)t[3], (int)t[4])) terrain[(x, y)] = new(x * 16, y * 16, 16, 16);
        }
        foreach (var e in data.Entities)
        {
            string k = e.Kind;
            if (k.Contains("BrickBlock") || k.Contains("QuestionBlock"))
            {
                var b = new Block { Center = e.Position, Kind = k, Hidden = k.StartsWith("Invisible"), CoinsLeft = k == "CoinBrickBlock" ? 10 : 1 };
                b.Item = k.Contains("PowerUp") ? "PowerUp" : k.Contains("OneUp") ? "OneUp" : k.Contains("Star") ? "SuperStar" : k.Contains("Coin") || k.Contains("Question") ? "Coin" : "";
                string item = e.Text("item");
                if (item.Length > 0) b.Item = System.IO.Path.GetFileNameWithoutExtension(item);
                Blocks.Add(b); blockGrid[(Mathf.FloorToInt((e.X - 8) / 16), Mathf.FloorToInt((e.Y - 8) / 16))] = b;
            }
            else if (k == "Coin") Coins.Add(e.Position);
            else if (IsEnemy(k))
            {
                var enemy = new Enemy { Kind = k, Home = e.Position, Data = e, Winged = e.Bool("is_paratroopa") || e.Bool("winged") || e.Bool("is_paratroopa", e.Bool("flying")), Phase = k is "PiranhaPlant" or "Podoboo" ? 0 : Enemies.Count * 1.37f };
                enemy.Body.Position = k == "PiranhaPlant" ? e.Position + new Vector2(0, 24) : e.Position;
                enemy.Body.Height = k.Contains("Koopa") || k is "HammerBro" or "PiranhaPlant" ? 24 : k.Contains("Bowser") ? 32 : 16;
                enemy.Body.Width = k.Contains("Bowser") ? 28 : 12;
                if (k == "Podoboo") InitializePodoboo(enemy);
                if (k is "Bowser" or "TrueBowser") enemy.Timer = 1.4f;
                Enemies.Add(enemy);
            }
            else if (k.Contains("Platform"))
            {
                float width = k.StartsWith("Small") ? 32 : k.StartsWith("Large") ? 64 : 48;
                Platforms.Add(new Platform { Position = e.Position, Home = e.Position, Previous = e.Position, Width = width, Kind = k, Data = e });
            }
            else Triggers.Add(e);
        }
    }
    /// <summary>The authored solid directly below a pole, queried from the same terrain used by Move.</summary>
    public Rect2? FlagBase(EntityData flag)
    {
        if (flag.Kind != "EndFlagpole") return null;
        var key = (Mathf.FloorToInt(flag.X / 16), Mathf.FloorToInt((flag.Y - 8) / 16));
        return terrain.TryGetValue(key, out var tile) ? tile : null;
    }
    public void InitializePodoboo(Enemy enemy)
    {
        Vector2 marker = enemy.Home;
        float? surface = null;
        foreach (var tile in Data.Tiles)
            if (!IsWater && (int)tile[2] == 2 && tile[0] * 16 <= marker.X && (tile[0] + 1) * 16 > marker.X)
            {
                float y = tile[1] * 16;
                surface = surface.HasValue ? MathF.Min(surface.Value, y) : y;
            }
        // Authored markers specify the apex, not the lava launch surface. For
        // generated/test bubbles without lava, position means the launch mouth;
        // retain the old 350 px/s, 500 px/s² trajectory's 122.5 px jump height.
        enemy.Home = new(marker.X, (surface ?? marker.Y) + enemy.Body.Height);
        enemy.JumpHeight = surface.HasValue ? MathF.Max(enemy.Body.Height, enemy.Home.Y - marker.Y) : 350f * 350 / (2 * 500);
        enemy.Body.Position = enemy.Home; enemy.Body.Velocity = Vector2.Zero;
        enemy.Clock = 0; enemy.Phase = 0;
    }
    public static bool IsEnemy(string k) => k is "Goomba" or "GreenKoopaTroopa" or "RedKoopaTroopa" or "PiranhaPlant" or "Blooper" or "GreenCheepCheep" or "RedCheepCheep" or "BuzzyBeetle" or "Bowser" or "TrueBowser" or "HammerBro" or "Lakitu" or "Podoboo" or "BowserFlame" or "BulletBill" or "Spiny";
    public bool SolidAt(Vector2 point)
    {
        var key = (Mathf.FloorToInt(point.X / 16), Mathf.FloorToInt(point.Y / 16));
        if (terrain.TryGetValue(key, out var tile) && tile.HasPoint(point)) return true;
        return blockGrid.TryGetValue(key, out var b) && !b.Removed && !b.Hidden && b.Box.HasPoint(point);
    }
    public IEnumerable<(Rect2 box, Block? block)> Colliders(Rect2 area, bool upward)
    {
        int x0 = Mathf.FloorToInt(area.Position.X / 16), x1 = Mathf.FloorToInt(area.End.X / 16);
        int y0 = Mathf.FloorToInt(area.Position.Y / 16), y1 = Mathf.FloorToInt(area.End.Y / 16);
        for (int y = y0 - 1; y <= y1 + 1; y++)
            for (int x = x0 - 1; x <= x1 + 1; x++)
            {
                if (terrain.TryGetValue((x, y), out var tile)) yield return (tile, null);
                if (blockGrid.TryGetValue((x, y), out var b) && !b.Removed && (!b.Hidden || upward)) yield return (b.Box, b);
            }
        if (!BridgeGone)
            foreach (var bridge in Triggers.Where(t => t.Kind == "CastleBridge")) yield return (new Rect2(bridge.X - 8, bridge.Y, 208, 8), null);
    }
    public void Move(Body body, float dt, Action<Block>? headHit = null, bool platforms = true, bool touchPlatforms = true, bool cornerCorrection = false)
    {
        body.HitWall = false; body.HitCeiling = false;
        if (platforms && body.Support != null && body.Velocity.Y >= 0) body.Position += body.Support.Position - body.Support.Previous;
        Vector2 delta = body.Velocity * dt;
        int steps = Math.Max(1, (int)MathF.Ceiling(MathF.Max(MathF.Abs(delta.X), MathF.Abs(delta.Y)) / 3));
        body.Grounded = false; body.Support = null;
        for (int s = 0; s < steps; s++)
        {
            float dx = delta.X / steps;
            body.Position += new Vector2(dx, 0);
            foreach (var (box, _) in Colliders(body.Box, false))
                if (body.Box.Intersects(box))
                {
                    body.Position = new(dx > 0 ? box.Position.X - body.Width / 2 : box.End.X + body.Width / 2, body.Position.Y);
                    body.HitWall = true; body.Velocity = new(0, body.Velocity.Y); break;
                }
            float oldFeet = body.Position.Y, dy = delta.Y / steps;
            body.Position += new Vector2(0, dy);
            foreach (var (box, block) in Colliders(body.Box, dy < 0))
                if (body.Box.Intersects(box))
                {
                    if (dy < 0)
                    {
                        if (cornerCorrection && CorrectCeilingCorner(body, box)) continue;
                        body.Position = new(body.Position.X, box.End.Y + body.Height);
                        body.HitCeiling = true;
                        if (block != null) headHit?.Invoke(block);
                    }
                    else { body.Position = new(body.Position.X, box.Position.Y); body.Grounded = true; }
                    body.Velocity = new(body.Velocity.X, 0); break;
                }
            if (platforms && dy >= 0)
                foreach (var p in Platforms)
                    if (oldFeet <= p.Position.Y + 2 && body.Position.Y >= p.Position.Y && body.Box.End.X > p.Box.Position.X && body.Box.Position.X < p.Box.End.X)
                    {
                        body.Position = new(body.Position.X, p.Position.Y);
                        body.Velocity = new(body.Velocity.X, 0); body.Grounded = true; body.Support = p; if (touchPlatforms) p.Touched = true;
                    }
        }
    }
    private bool CorrectCeilingCorner(Body body, Rect2 ceiling)
    {
        float x;
        if (body.Velocity.X > 1 && body.Position.X > ceiling.GetCenter().X && ceiling.End.X - body.Box.Position.X <= 3)
            x = ceiling.End.X + body.Width / 2 + .001f;
        else if (body.Velocity.X < -1 && body.Position.X < ceiling.GetCenter().X && body.Box.End.X - ceiling.Position.X <= 3)
            x = ceiling.Position.X - body.Width / 2 - .001f;
        else return false;
        var candidate = new Rect2(x - body.Width / 2, body.Box.Position.Y, body.Width, body.Height);
        if (Colliders(candidate, false).Any(c => candidate.Intersects(c.box))) return false;
        body.Position = new(x, body.Position.Y); return true;
    }
    public bool Headroom(Body body, float height)
    {
        Rect2 taller = new(body.Position.X - body.Width / 2, body.Position.Y - height, body.Width, height);
        return !Colliders(taller, false).Any(c => c.box.Intersects(taller));
    }
    public void UpdatePlatforms(float dt)
    {
        foreach (var p in Platforms) { p.Previous = p.Position; p.Clock += dt; }
        var updatedRopes = new HashSet<Platform>();
        foreach (var p in Platforms)
        {
            switch (p.Kind)
            {
                case "CloudPlatform":
                    if (p.Touched) p.Position += new Vector2(48 * dt, 0);
                    break;
                case "ElevatorPlatform": case "SmallElevatorPlatform":
                    float direction = p.Data.Number("vertical_direction", 1);
                    p.Position += new Vector2(0, (direction == 1 ? 1 : -1) * 30 * dt);
                    if (p.Position.Y < -200) p.Position += new Vector2(0, 240);
                    if (p.Position.Y > 32) p.Position -= new Vector2(0, 240);
                    break;
                case "VerticalPlatform": case "SmallVerticalPlatform":
                    p.Position = p.Home + new Vector2(0, (1 - MathF.Cos(p.Clock * 1.2f)) * 40); break;
                case "RopeElevatorPlatform": case "SmallRopeElevatorPlatform":
                    if (!updatedRopes.Add(p)) break;
                    string link = p.Data.Text("linked_platform").Replace("NodePath(\"../", "").Replace("\")", "");
                    var partner = Platforms.FirstOrDefault(q => q.Data.Name == link);
                    if (partner != null && !p.Falling && !partner.Falling)
                    {
                        updatedRopes.Add(partner);
                        float load = (p.Touched ? 1 : 0) - (partner.Touched ? 1 : 0);
                        p.FallSpeed = load == 0 ? Mathf.MoveToward(p.FallSpeed, 0, 48 * dt) : Math.Clamp(p.FallSpeed + load * 96 * dt, -60, 60);
                        float amount = p.FallSpeed * dt;
                        p.Position += new Vector2(0, amount); partner.Position -= new Vector2(0, amount);
                        partner.FallSpeed = -p.FallSpeed;
                        if (p.Position.Y < -176 || partner.Position.Y < -176) { p.Falling = partner.Falling = true; p.FallSpeed = partner.FallSpeed = 20; }
                    }
                    else if (p.Falling) { p.FallSpeed += 120 * dt; p.Position += new Vector2(0, p.FallSpeed * dt); }
                    break;
                case "FallingPlatform":
                    if (p.Touched) p.Falling = true;
                    if (p.Falling) { p.FallSpeed += 120 * dt; p.Position += new Vector2(0, p.FallSpeed * dt); }
                    break;
                default: p.Position = p.Home + new Vector2((1 - MathF.Cos(p.Clock * 1.2f)) * 40, 0); break;
            }
        }
        foreach (var p in Platforms.Where(p => p.Kind.Contains("Rope"))) p.Touched = false;
    }
}
