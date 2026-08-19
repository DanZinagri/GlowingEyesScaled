using RimWorld;
using Verse;

namespace GlowingEyesScaled
{
    public static class GlowSpeedGating
    {
        public static bool Gated()
        {
            TickManager ticks = Find.TickManager;
            if (ticks == null)
            {
                return false;
            }
            switch (GesMod.Settings.hideAboveSpeed)
            {
                case GlowSpeedGate.AboveNormal:
                    return ticks.CurTimeSpeed > TimeSpeed.Normal;
                case GlowSpeedGate.AboveFast:
                    return ticks.CurTimeSpeed > TimeSpeed.Fast;
                default:
                    return false;
            }
        }
    }

    // Whether the glow is lit is decided in CanDrawNow, which the game only calls
    // when a pawn's render tree recaches. It already recaches on facing, posture and
    // death, which covers nearly every reason the glow changes - sleeping, being
    // downed, getting into bed, dying.
    //
    // Two reasons are not covered, and both are events rather than states, so they
    // are hooked as events and ask for a recache directly. That is what removes the
    // need to poll anything on a tick.
    public static class GlowRecache
    {
        // Drafting, when the "only glow while drafted" setting is on. The setter
        // returns early when the value has not changed, so this only fires on a real
        // change.
        public static void DraftedPostfix(Pawn_DraftController __instance)
        {
            MarkPawn(__instance?.pawn);
        }

        // Changing game speed, when the speed gate is set. Rare, and a player action.
        public static void TimeSpeedPostfix()
        {
            if (GesMod.Settings.hideAboveSpeed == GlowSpeedGate.Never
                || Current.ProgramState != ProgramState.Playing)
            {
                return;
            }
            MarkAll();
        }

        public static void MarkPawn(Pawn pawn)
        {
            PawnRenderTree tree = pawn?.Drawer?.renderer?.renderTree;
            if (tree == null || !tree.Resolved)
            {
                return;
            }
            Mark(tree, GesDefOf.GES_EyeGlowLeft);
            Mark(tree, GesDefOf.GES_EyeGlowRight);
        }

        public static void MarkAll()
        {
            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    MarkPawn(pawn);
                }
            }
        }

        private static void Mark(PawnRenderTree tree, PawnRenderNodeTagDef tag)
        {
            PawnRenderNode node;
            if (tree.TryGetNodeByTag(tag, out node) && node != null)
            {
                node.requestRecache = true;
            }
        }
    }
}
