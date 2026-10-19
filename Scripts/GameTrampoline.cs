using Godot;
using System.Runtime.CompilerServices;

namespace MarioStudy;

public partial class Game
{
    private const float TrampolineAnimationDuration = .16f;
    // Worlds own their timelines. Cached rooms resume their own frozen state;
    // newly loaded/restarted worlds always begin idle, even with shared map entities.
    private readonly ConditionalWeakTable<World, Dictionary<EntityData, float>> trampolineAnimations = new();
    private void StartTrampolineAnimation(EntityData trampoline)
    {
        trampolineAnimations.GetOrCreateValue(World)[trampoline] = 0;
    }
    private void TickTrampolineAnimations(float dt)
    {
        if (State != Screen.Playing || pipeClock > 0 || dt <= 0 || !trampolineAnimations.TryGetValue(World, out var animations)) return;
        foreach (var trampoline in animations.Keys.ToArray())
        {
            float elapsed = animations[trampoline] + dt;
            if (elapsed >= TrampolineAnimationDuration) animations.Remove(trampoline);
            else animations[trampoline] = elapsed;
        }
    }
    private int TrampolineFrame(EntityData trampoline)
    {
        if (!trampolineAnimations.TryGetValue(World, out var animations) || !animations.TryGetValue(trampoline, out float elapsed)) return 0;
        return elapsed < TrampolineAnimationDuration / 3 ? 1 : elapsed < TrampolineAnimationDuration * 2 / 3 ? 2 : 1;
    }
    private static Rect2 TrampolineSource(int frame) => new(Math.Clamp(frame, 0, 2) * 16, 0, 16, 32);
    private static Rect2 TrampolineDestination(EntityData trampoline) => new((trampoline.Position + new Vector2(-8, -24)).Round(), new Vector2(16, 32));
    private void DrawTrampoline(EntityData trampoline)
    {
        // Recipe content ends at row 24 within each 32px-high frame. The extra
        // 8 transparent rows put the visible base exactly on the trigger's Y.
        DrawTextureRectRegion(Art.Texture("Blocks/Trampoline.png"), TrampolineDestination(trampoline), TrampolineSource(TrampolineFrame(trampoline)));
    }
}
