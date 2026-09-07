using UnityEngine;
using Verse;

namespace GlowingEyesScaled
{
    // A head-parented glow quad.
    //
    // Living in the render tree is the whole point: every transform that moves or
    // scales the head - Big and Small's body lift, its scaled head offset and head
    // quad, life stage, crawling, lying down, animations - applies to the glow for
    // free, and layered size sources compose without this mod knowing any of them
    // exist. None of the mote-offset arithmetic is needed on this path.
    //
    // It also costs nothing per tick. The quad is drawn in the pawn draw pass that
    // already happens, and CanDrawNow is only re-evaluated when the render tree
    // recaches, not per frame and not per tick.
    public class PawnRenderNode_EyeGlow : PawnRenderNode_AttachmentHead
    {
        // World position as of the last frame this node was actually drawn. The
        // trail is thrown from here. It cannot go stale: the trail is laid inside
        // the same PostDraw that records it, so a node that stops drawing also
        // stops emitting in the same breath.
        public Vector3 lastDrawnWorldPos;

        // World width of the glow as last drawn. The trail is sized off this so it
        // stays in proportion on a resized pawn without asking anyone how big the
        // pawn is.
        public float lastDrawnWidth = 0.3f;

        // Where this node last dropped a trail fleck. Emission is gated on distance
        // from here rather than on a tick count, which is what keeps the spacing
        // even: the drawn position only advances once per frame, so counting ticks
        // stacks several flecks on one stale position and then jumps, and how badly
        // depends on how frame rate and tick rate happen to line up.
        public Vector3 lastEmitPos;
        public bool hasEmitPos;

        // Set from the draw pass, read by ScaleFor on the next one. A frame of lag
        // on a size change nobody is measuring is not worth a tick hook.
        public bool movingNow;

        // Resolved once when visibility is refreshed, so laying a trail mark does
        // not walk the pawn's gene list every time.
        public Color cachedColor = Color.white;

        public bool IsLeft => props.side == PawnRenderNodeProperties.Side.Left;

        public PawnRenderNode_EyeGlow(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree)
            : base(pawn, props, tree)
        {
        }

        public override Color ColorFor(Pawn pawn)
        {
            Color color;
            if (GegBridge.TryGetGlowColor(pawn, out color))
            {
                // Kept for the trail, which wants the same colour and should not
                // walk the gene list to find it.
                cachedColor = color;
                return color;
            }
            return base.ColorFor(pawn);
        }

        // Graphic_Single, not vanilla's Graphic_Multi.
        //
        // GEG ships Things/Mote/EyeGlow.png (128px, one eye) alongside
        // Things/Mote/EyeGlow_south.png (256px, both eyes) - the latter is a
        // separate unused mote def, not a rotation variant. Graphic_Multi scans for
        // exactly that "_south" suffix, so it would load the double-width glow for
        // the south facing and then reuse it for every other rotation.
        public override Graphic GraphicFor(Pawn pawn)
        {
            string texPath = TexPathFor(pawn);
            if (texPath.NullOrEmpty())
            {
                return null;
            }
            Shader shader = ShaderFor(pawn);
            if (shader == null)
            {
                return null;
            }
            return GraphicDatabase.Get<Graphic_Single>(texPath, shader, Vector2.one, ColorFor(pawn));
        }
    }

    // Inherits the vanilla eye worker, which resolves anchorTag against the body
    // type's eye wound anchors - that is what gets the per-rotation and
    // narrow-crown eye positions right, which GEG's flat +/-0.08 never did.
    public class PawnRenderNodeWorker_EyeGlow : PawnRenderNodeWorker_Eye
    {
        public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
        {
            if (!base.CanDrawNow(node, parms))
            {
                return false;
            }

            PawnRenderNode_EyeGlow glow = node as PawnRenderNode_EyeGlow;
            if (glow == null)
            {
                return false;
            }

            // Portraits kept dark to match the mote this replaces, which never
            // appeared in them.
            if (parms.Portrait)
            {
                return false;
            }

            // Worked out here rather than polled from a tick.
            //
            // This only runs when the render tree recaches, which vanilla already
            // does on facing, posture and death - so almost every reason the glow
            // turns on or off brings us here anyway, and the two that do not
            // (drafting, changing game speed) are hooked as the events they are.
            // That is what lets the gene have no tick at all.
            Pawn pawn = parms.pawn;
            bool hasEye = glow.IsLeft ? EyeCache.HasLeftEye(pawn) : EyeCache.HasRightEye(pawn);
            if (!hasEye)
            {
                return false;
            }
            if (GlowSpeedGating.Gated())
            {
                return false;
            }
            return GlowVisibility.ShouldGlow(pawn);
        }

        // Cached because these run once per node per frame.
        public static float glowScale = 1f;
        public static float movingBoost = 1.6f;

        public override Vector3 ScaleFor(PawnRenderNode node, PawnDrawParms parms)
        {
            // The glow swells a little while the pawn walks.
            //
            // The newest trail mark has to fade in from nothing at its leading edge
            // - that is what makes the marks sum to an even line - so there is
            // always a dim stretch one spacing long sitting right at the eye, and
            // it is where new marks appear. A slightly larger glow covers that
            // stretch, so the trail reads as coming out of the glow instead of
            // flickering into being just in front of it.
            //
            // Free: this runs in the draw pass either way, and lets the trail be
            // laid more sparsely than it would otherwise need to be.
            PawnRenderNode_EyeGlow glow = node as PawnRenderNode_EyeGlow;
            float scale = (glow != null && glow.movingNow) ? glowScale * movingBoost : glowScale;
            return base.ScaleFor(node, parms) * scale;
        }

        public override void PostDraw(PawnRenderNode node, PawnDrawParms parms, Mesh mesh, Matrix4x4 matrix)
        {
            base.PostDraw(node, parms, mesh, matrix);

            if (parms.Portrait)
            {
                return;
            }
            PawnRenderNode_EyeGlow glow = node as PawnRenderNode_EyeGlow;
            if (glow == null)
            {
                return;
            }

            // This is the only thing this mod does per frame, and the position is
            // only ever wanted for laying a trail. A pawn that is not walking needs
            // none of it - which also means nothing here runs while the game is
            // paused, since nothing is walking then.
            Pawn pawn = parms.pawn;
            bool moving = pawn?.pather != null && pawn.pather.MovingNow;
            glow.movingNow = moving;
            if (!moving)
            {
                // Forget where the last run ended.
                //
                // Otherwise the first mark of the next walk is drawn spanning from
                // wherever the pawn stopped to wherever it started again - a stray
                // mark, followed by a pause while the real spacing builds up, which
                // is the stutter at the start of a walk. Only walks that restart
                // within the maximum step got this far; longer ones were already
                // caught.
                glow.hasEmitPos = false;
                return;
            }

            // matrix is the node's final world matrix, handed to us by
            // PawnRenderTree.Draw, with every ancestor transform already folded in.
            glow.lastDrawnWorldPos = matrix.MultiplyPoint3x4(Vector3.zero);

            // The matrix scale is not the drawn width on its own - the node's mesh
            // is the head's hair mesh, 1.5 wide by default, and the matrix scales
            // that. Sizing the trail off the matrix alone understated the glow by
            // that factor, and since spacing follows width, it also spread the
            // marks further apart than they should have been on a large pawn.
            float meshWidth = pawn.story?.headType != null ? pawn.story.headType.hairMeshSize.x : 1.5f;
            glow.lastDrawnWidth = matrix.lossyScale.x * meshWidth;

            // Emitted from here rather than from the tick.
            //
            // The position a mark is laid at only advances when the pawn is drawn,
            // so driving emission from the draw pass removes the mismatch between
            // the two rates entirely - no stale positions, no phase drift between
            // frame rate and tick rate - and it costs nothing extra, because this
            // is already the only per-frame work being done.
            TrailEmitter.Emit(glow, pawn);
        }
    }
}
