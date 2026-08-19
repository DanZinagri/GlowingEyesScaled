using RimWorld;
using UnityEngine;
using Verse;

namespace GlowingEyesScaled
{
    // Lays the movement trail, driven from the draw pass rather than from a tick.
    public static class TrailEmitter
    {
        // A mark is drawn twice as long as the step just taken. The trail sprite's
        // alpha is a tent along its length, and two tents overlapped by exactly
        // half sum to a flat line - 0% brightness ripple at 2x against 18% at 3x.
        // Sizing from the real step rather than a nominal spacing is what holds
        // that ratio whatever the step turns out to be.
        private const float SegmentOverlap = 2f;

        // Trail width as a fraction of the glow it comes out of.
        private const float TrailWidthFactor = 0.42f;

        // A floor on the spacing relative to a mark's own width.
        private const float MinSpacingPerWidth = 0.35f;

        // A step longer than this is not a step - the pawn stopped, went elsewhere
        // and set off again, so the last mark's position belongs to another walk.
        // Drawing across it puts a long streak at the midpoint of the two, often
        // nowhere near any pawn.
        private const float MaxStepSq = 0.75f * 0.75f;

        // How far back a mark is shifted from the eye, as a fraction of its half
        // length.
        private const float LeadFraction = 0.4f;

        private const float MaxStretch = 6f;

        private static float trailSpacing = 0.15f;
        private static float trailBrightness = 0.6f;
        private static float trailBaseSize = 0.32f;

        public static void CacheSettings()
        {
            GesSettings settings = GesMod.Settings;
            trailSpacing = settings.trailSpacing;
            trailBrightness = settings.trailBrightness;

            if (GegBridge.GlowFleck?.graphicData != null)
            {
                trailBaseSize = Mathf.Max(GegBridge.GlowFleck.graphicData.drawSize.y, 0.01f);
            }
        }

        public static void Emit(PawnRenderNode_EyeGlow node, Pawn pawn)
        {
            // No visibility test here: this only runs for a node that was drawn,
            // and a node is only drawn once CanDrawNow has said the glow is on.
            if (GegBridge.GlowFleck == null)
            {
                return;
            }

            Map map = pawn.Map;
            if (map == null)
            {
                return;
            }

            // The original only trailed at normal speed, so nothing is lost above
            // it, and the speed gate switches the whole thing off higher up.
            if (Find.TickManager.CurTimeSpeed != TimeSpeed.Normal)
            {
                return;
            }

            Vector3 loc = node.lastDrawnWorldPos;

            // The first mark of a run only records where the run started - there is
            // no direction to point one along yet.
            if (!node.hasEmitPos)
            {
                node.lastEmitPos = loc;
                node.hasEmitPos = true;
                return;
            }

            Vector3 delta = loc - node.lastEmitPos;
            float distSq = delta.sqrMagnitude;

            // Width follows the glow, so how far apart marks go has to follow it too.
            float width = node.lastDrawnWidth * TrailWidthFactor;
            float spacing = Mathf.Max(trailSpacing, width * MinSpacingPerWidth);

            if (distSq < spacing * spacing)
            {
                return;
            }
            if (distSq > MaxStepSq)
            {
                node.lastEmitPos = loc;
                return;
            }

            float dist = Mathf.Sqrt(distSq);
            node.lastEmitPos = loc;

            // Laid over ground already walked, but pulled forward far enough that
            // its bright middle sits at the eye rather than a spacing behind it.
            float length = dist * SegmentOverlap;
            Vector3 centre = loc - delta * (length * 0.5f * LeadFraction / dist);
            centre.y = AltitudeLayer.MoteOverhead.AltitudeFor();

            if (!centre.ToIntVec3().ShouldSpawnMotesAt(map, true))
            {
                return;
            }

            Color color = node.cachedColor;
            color.a *= trailBrightness;

            FleckCreationData data = FleckMaker.GetDataStatic(centre, map, GegBridge.GlowFleck, 1f);
            data.instanceColor = color;
            data.velocity = Vector3.zero;
            data.rotation = delta.AngleFlat();
            data.exactScale = new Vector3(
                Mathf.Clamp(width / trailBaseSize, 0.3f, 4f),
                1f,
                Mathf.Clamp(length / trailBaseSize, 0.5f, MaxStretch));
            map.flecks.CreateFleck(data);
        }
    }
}
