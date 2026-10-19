using Godot;

namespace MarioStudy;

public partial class Game
{
    private void VerifyPracticeAndLayerFixes()
    {
        foreach (int before in new[] { 0, 1, 2 }) foreach (int target in new[] { 1, 2 })
        {
            MechanicsWorld(); Power = before; Player.Height = before == 0 ? 16 : 32;
            Player.Position = new(40, -64); Player.Velocity = new(75, -80); Player.Grounded = false;
            World savedWorld = World; Vector2 feet = Player.Position, speed = Player.Velocity;
            float time = TimeLeft, clock = Clock; int score = Score, lives = Lives;
            TogglePause(); pauseChoice = target + 3; HandleMenuKey(Key.Enter);
            Check($"practice power {before} to {target} applies and resumes", Power == target && State == Screen.Playing && Player.Height == 32 && !Crouching);
            Check($"practice power {before} to {target} preserves gameplay", World == savedWorld && Player.Position == feet && Player.Velocity == speed && !Player.Grounded && TimeLeft == time && Clock == clock && Score == score && Lives == lives);
        }
        foreach (int target in new[] { 1, 2 })
        {
            MechanicsWorld((0, -2), (1, -2)); Player.Position = new(16, 0); TogglePause();
            pauseChoice = target + 3; HandleMenuKey(Key.J);
            Check($"practice power {target} respects low ceiling", State == Screen.Playing && Power == target && Crouching && Player.Height == 16 && !World.Colliders(Player.Box, false).Any(c => c.box.Intersects(Player.Box)));
        }
        MechanicsWorld(); practice = false; TogglePause(); ApplyPracticePower(2);
        Check("normal mode cannot use practice power grants", PauseOptions.Length == 4 && Power == 0 && State == Screen.Pause);
        TogglePause(); practice = true;
        Check("practice mode exposes both power grants", PauseOptions.Length == 6 && PauseOptions[4] == "BIG MARIO" && PauseOptions[5] == "FIRE MARIO");

        foreach (string id in new[] { "8-4c", "2-2", "7-2" })
        {
            StartGame(id == "8-4c" ? 31 : Array.IndexOf(Maps.MainLevels, id), true); LoadRoom(id, false); State = Screen.Playing;
            var commands = TileDrawOrder().ToArray();
            int lastWater = Array.FindLastIndex(commands, c => !c.decoration && (int)c.tile[2] == 2);
            int firstForeground = Array.FindIndex(commands, c => c.decoration || (int)c.tile[2] != 2);
            Check($"{id} liquid layer draws before scenery and rocks", lastWater >= 0 && firstForeground > lastWater);
            Check($"{id} layer ordering preserves every map cell", commands.Length == World.Data.Tiles.Count + World.Data.Deco.Count);
            var water = World.Data.Tiles.Where(t => (int)t[2] == 2).Select(t => (t[0], t[1])).ToHashSet();
            var rock = World.Data.Tiles.Where(t => (int)t[2] == 0 && Maps.IsSolid(0, (int)t[3], (int)t[4]) && water.Contains((t[0], t[1]))).ToArray();
            var top = new Dictionary<(float, float), float[]>();
            foreach (var command in commands) top[(command.tile[0], command.tile[1])] = command.tile;
            int visible = rock.Count(t => (int)top[(t[0], t[1])][2] == 0 && Maps.IsSolid(0, (int)top[(t[0], t[1])][3], (int)top[(t[0], t[1])][4]));
            Check($"{id} rocks overlapping water remain the foreground", rock.Length > 0 && visible == rock.Length, $"rocks={rock.Length}, foreground={visible}");
            if (id == "8-4c") Check("8-4c retains castle terrain in its water room", World.IsWater && World.Theme == "CastleWater" && TerrainPath() == "Tilesets/Terrain/Castle.png");
        }
    }
}
