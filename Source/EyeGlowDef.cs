using System.Collections.Generic;
using RimWorld;
using Verse;

namespace GlowingEyesScaled
{
    // Holds the glow's render node properties in XML so they can be retuned
    // without a rebuild. One shared instance serves every pawn - the per-pawn
    // colour comes from PawnRenderNode_EyeGlow.ColorFor, so nothing is cloned.
    public class EyeGlowDef : Def
    {
        public List<PawnRenderNodeProperties> nodes;

        public override void ResolveReferences()
        {
            base.ResolveReferences();
            if (nodes == null)
            {
                return;
            }
            foreach (PawnRenderNodeProperties props in nodes)
            {
                props.ResolveReferencesRecursive();
            }
        }

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }
            if (nodes.NullOrEmpty())
            {
                yield return "no nodes defined";
                yield break;
            }
            foreach (PawnRenderNodeProperties props in nodes)
            {
                if (props.tagDef == null)
                {
                    yield return "a node has no tagDef; the driver will not be able to find it";
                }
            }
        }
    }

    [DefOf]
    public static class GesDefOf
    {
        public static EyeGlowDef GES_EyeGlow;
        public static PawnRenderNodeTagDef GES_EyeGlowLeft;
        public static PawnRenderNodeTagDef GES_EyeGlowRight;

        static GesDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(GesDefOf));
        }
    }
}
