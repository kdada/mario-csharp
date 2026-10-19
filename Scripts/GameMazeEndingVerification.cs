using Godot;

namespace MarioStudy;

public partial class Game
{
    private void VerifyCastleMazeAndPrincess()
    {
        foreach (string id in new[] { "4-4", "7-4" })
            foreach (var gate in Maps.Rooms[id].Entities.Where(e => e.Kind == "PickAPathTeleport"))
            {
                StartGame(Array.IndexOf(Maps.MainLevels, id), true); State = Screen.Playing; StarTime = 100;
                string group = gate.Text("_maze_group", gate.Name);
                var points = World.Triggers.Where(t => t.Kind == "PickAPathPoint" && t.Text("_maze_group") == group).ToArray();
                string label = id + "/" + gate.Text("_path", gate.Name);
                Check(label + " has authored route checkpoints", points.Length > 0);
                Player.Position = gate.Position + new Vector2(0, 8); pipeCooldown = 0;
                TickTriggers(0, new());
                Vector2 reset = gate.Vector("reset_pos", new(-248, 0));
                Check(label + " wrong route returns to authored section start", Player.Position.DistanceTo(reset) < .01f);
                foreach (var point in points)
                {
                    Player.Position = point.Position + new Vector2(0, 8); pipeCooldown = 0; TickTriggers(0, new());
                }
                Check(label + " correct route records its checkpoints", points.All(p => World.MazePoints.Contains(p.Text("_path", p.Name))));
                Vector2 crossing = gate.Position + new Vector2(0, 8);
                Player.Position = crossing; pipeCooldown = 0; TickTriggers(0, new());
                Check(label + " correct route passes without a reset", Player.Position.DistanceTo(crossing) < .01f);
            }
        var first = Maps.Rooms["4-4"].Entities.First(e => e.Kind == "PickAPathPoint" && e.Text("_maze_group") == "PickAPathTeleport");
        var second = Maps.Rooms["4-4"].Entities.First(e => e.Kind == "PickAPathPoint" && e.Text("_maze_group") == "PickAPathTeleport2");
        Check("4-4 route uses upper passage then lower passage", first.Y == -136 && second.Y == -8);

        // Validate the sprite definition and chosen atlas frame, without loading or comparing image pixels.
        string metadata = "res://Assets/Sprites/" + PrincessSprite.Replace(".png", ".json");
        var npc = Art.Json(metadata);
        var frames = npc.GetProperty("animations").GetProperty("Idle").GetProperty("frames");
        var frame = frames[0]; var variant = npc.GetProperty("variations").GetProperty("SMB1");
        var region = variant.GetProperty("rect");
        Check("princess binding uses the single-character NPC definition", PrincessSprite == "Players/PeachNPC.png" && frames.GetArrayLength() == 1 && frame[0].GetInt32() == 0 && frame[1].GetInt32() == 0 && frame[2].GetInt32() == 16 && frame[3].GetInt32() == 32);
        Check("princess selects only SMB1 frame from the three-variant sheet", variant.GetProperty("source").GetString() == "Peach.png" && region[0].GetInt32() == 0 && region[2].GetInt32() == 16 && region[3].GetInt32() == 32);
        Check("final castle contains one princess entity", Maps.Rooms["8-4d"].Entities.Count(e => e.Kind == "CastlePeach") == 1);
    }
}
