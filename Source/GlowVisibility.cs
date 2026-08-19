using RimWorld;
using Verse;

namespace GlowingEyesScaled
{
    // The conditions under which Glowing eyes genes lit its motes, kept identical
    // so switching to render nodes is a rendering change and not a behaviour one.
    public static class GlowVisibility
    {
        public static bool ShouldGlow(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.health == null)
            {
                return false;
            }
            if (pawn.Downed || !pawn.Awake() || pawn.InBed())
            {
                return false;
            }
            if (pawn.IsPlayerControlled && GegBridge.OnlyWhileDrafted && !pawn.Drafted)
            {
                return false;
            }
            return true;
        }
    }
}
