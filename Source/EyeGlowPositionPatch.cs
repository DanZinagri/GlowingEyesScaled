using RimWorld;
using UnityEngine;
using Verse;

namespace GlowingEyesScaled
{
    // Corrects the glow's offset, and nothing else. Glowing eyes genes' Tick is
    // not hooked at all: its motes, its trail, its conditions and its own settings
    // run exactly as written.
    //
    // For the steady glow, the head-height half of the correction lives in XML
    // now (see Patches/EyeGlowMote.xml), applied live by vanilla every tick. What
    // is left here is the whole-pawn ground lift, which nothing in XML can reach,
    // plus the eye's own travel as the head quad scales.
    //
    // The movement trail is corrected separately, because it is a fleck rather
    // than a mote and so no XML on the mote def touches it.
    public static class EyeGlowPositionPatch
    {
        // The vanilla head height baked into GEG's mote offset. Every vanilla
        // BodyTypeDef has headOffset.y == 0.34, and GEG's 0.27 is that minus the
        // eye's own 0.07 drop.
        private const float GegAssumedHeadHeight = 0.34f;

        // Prefix on MoteMaker.MakeAttachedOverlay. Filtered to GEG's glow mote by
        // reference, so every other attached mote in the game costs one comparison.
        public static void MoteOffsetPrefix(Thing thing, ThingDef moteDef, ref Vector3 offset, ref float scale)
        {
            if (moteDef != GegBridge.EyeGlowMote)
            {
                return;
            }

            Pawn pawn = thing as Pawn;
            if (pawn?.story?.bodyType == null)
            {
                return;
            }

            float lift, headScale;
            Vector3 bodyOffset;
            if (!TryGetCorrection(pawn, pawn.Rotation, out lift, out headScale, out bodyOffset))
            {
                return;
            }

            // Where the eye sits inside the head. The XML patch cancels GEG's baked
            // 0.34 and lets vanilla supply the real head offset in its place, so
            // what remains of GEG's constant is exactly this local drop - and it
            // travels with the head quad as that quad scales.
            Vector3 eyeLocal = new Vector3(offset.x, 0f, offset.z - GegAssumedHeadHeight);

            offset += eyeLocal * (headScale - 1f)
                + new Vector3(0f, 0f, lift)
                + bodyOffset;

            if (GesMod.Settings.scaleGlowWithHead)
            {
                scale *= headScale;
            }
        }

        // Prefix on GEG_FleckMaker.ThrowEyeGlowFleck - the movement trail.
        //
        // GEG builds this location from BaseHeadOffsetAt, so the head offset is
        // already right here; what it misses is the whole-pawn ground lift, which
        // is what drops the trail to chest height on a heavily resized pawn.
        public static void FleckLocPrefix(ref Vector3 loc, Map map)
        {
            Pawn pawn = GegTickContext.CurrentPawn;
            // Validated against both the tick and the fleck's own map, so a stale
            // stash can never be applied to the wrong pawn.
            if (pawn?.story?.bodyType == null
                || GegTickContext.CurrentTick != Find.TickManager.TicksGame
                || !pawn.Spawned
                || pawn.Map != map)
            {
                return;
            }

            Rot4 rot = pawn.Rotation;
            float lift, headScale;
            Vector3 bodyOffset;
            if (!TryGetCorrection(pawn, rot, out lift, out headScale, out bodyOffset))
            {
                return;
            }

            // Recovered rather than assumed, so it stays right whichever of GEG's
            // four throw sites we are in.
            Vector3 eyeLocal = loc - pawn.Drawer.DrawPos - pawn.Drawer.renderer.BaseHeadOffsetAt(rot);
            eyeLocal.y = 0f;

            loc += eyeLocal * (headScale - 1f)
                + new Vector3(0f, 0f, lift)
                + bodyOffset;
        }

        // One-entry memo. GEG throws a fleck per eye, so without this the identical
        // correction is computed twice per pawn per tick.
        private static Pawn memoPawn;
        private static int memoTick = -1;
        private static Rot4 memoRot;
        private static bool memoResult;
        private static float memoLift;
        private static float memoHeadScale;
        private static Vector3 memoBodyOffset;

        private static bool TryGetCorrection(Pawn pawn, Rot4 rot, out float lift, out float headScale, out Vector3 bodyOffset)
        {
            int tick = Find.TickManager.TicksGame;
            if (memoPawn == pawn && memoTick == tick && memoRot == rot)
            {
                lift = memoLift;
                headScale = memoHeadScale;
                bodyOffset = memoBodyOffset;
                return memoResult;
            }

            memoResult = Compute(pawn, rot, out memoLift, out memoHeadScale, out memoBodyOffset);
            memoPawn = pawn;
            memoTick = tick;
            memoRot = rot;

            lift = memoLift;
            headScale = memoHeadScale;
            bodyOffset = memoBodyOffset;
            return memoResult;
        }

        private static bool Compute(Pawn pawn, Rot4 rot, out float lift, out float headScale, out Vector3 bodyOffset)
        {
            if (!SizeBridge.TryGet(pawn, rot, out lift, out headScale, out bodyOffset))
            {
                return false;
            }

            // B&S only lifts a standing pawn, and only when its own body offset
            // setting is on. Matching that gate keeps us from over-correcting a
            // pawn it is currently drawing on the ground.
            if (lift != 0f && (!SizeBridge.BodyOffsetEnabled || pawn.GetPosture() != PawnPosture.Standing))
            {
                lift = 0f;
            }

            return lift != 0f || headScale != 1f || bodyOffset != Vector3.zero;
        }
    }
}
