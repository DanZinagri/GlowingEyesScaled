using Verse;

namespace GlowingEyesScaled
{
    // A safety net, not the mechanism.
    //
    // The glow genes are pointed at Gene_EyeGlow, which overrides nothing, so their
    // tick is normally never reached at all - this is never called for a pawn whose
    // gene has been swapped over. It stays patched for the window between loading a
    // pre-existing save and LegacyGeneMigration running, and for anything that
    // manages to construct the original class afterwards.
    public static class GlowSuppressor
    {
        public static bool Prefix()
        {
            return false;
        }
    }
}
