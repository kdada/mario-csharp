using Godot;
using System.Text.Json;
using FileAccess = Godot.FileAccess;

namespace MarioStudy;

public partial class Game
{
    /// <summary>State, collision, map-layer and input regressions; never runs a graphical test.</summary>
    public async void RunLogicVerification()
    {
        verifying = true; SetPhysicsProcess(false);
        if (DisplayServer.GetName() != "headless")
        {
            GD.PushError("Logic verification requires --headless; no graphical checks were started.");
            GetTree().Quit(1); return;
        }
        DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath("res://Reports/Regression"));
        try
        {
            VerifyEnemyAndPlantFixes();
            VerifyEnemyContactFacingFixes();
            VerifyWaterFishPopulationFixes();
            VerifyBowserHealthFixes();
            VerifyPipeArrivalFixes();
            VerifyRunningGapFixes();
            VerifyTrampolineSpriteFixes();
            VerifyPracticeAndLayerFixes();
            VerifyFlagBaseFixes();
            VerifyCastleMazeAndPrincess();
            VerifyAudioBugFixes();
            await VerifyMechanicsRegressions();
            await VerifyFollowupRegressions();
            await VerifyCoinPipeFlagRegressions();
            await VerifyTouchControls(false);
        }
        catch (Exception ex) { Check("logic verification exception", false, ex.ToString()); }
        using (var report = FileAccess.Open("res://Reports/Regression/gameplay-logic-fixes.json", FileAccess.ModeFlags.Write))
            report.StoreString(JsonSerializer.Serialize(new { display = DisplayServer.GetName(), graphicsIncluded = false, failures = verificationFailures, checks = verificationChecks }, new JsonSerializerOptions { WriteIndented = true }));
        GD.Print($"Logic verification: {verificationChecks.Count - verificationFailures}/{verificationChecks.Count} checks passed; graphics disabled.");
        GetTree().Quit(verificationFailures == 0 ? 0 : 1);
    }
}
