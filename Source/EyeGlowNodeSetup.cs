using System.Collections.Generic;
using Verse;

namespace GlowingEyesScaled
{
    // Injects the glow nodes through the vanilla dynamic node system.
    //
    // This is why the glow is not declared as renderNodeProperties on the gene
    // defs. Facial Animation's PawnRenderTree.AddChild prefix drops any node whose
    // child.gene is non-null when its DrawGeneParts setting is off, or whose props
    // are PawnRenderNodeProperties_Eye when DrawEyesParts is off - which is exactly
    // how people run FA. Nodes created here have a null gene, so that prefix
    // returns true on its first check and leaves them alone.
    public class EyeGlowNodeSetup : DynamicPawnRenderNodeSetup
    {
        public override bool HumanlikeOnly => true;

        public override IEnumerable<(PawnRenderNode node, PawnRenderNode parent)> GetDynamicNodes(Pawn pawn, PawnRenderTree tree)
        {
            if (!GegBridge.Active)
            {
                yield break;
            }
            if (GesDefOf.GES_EyeGlow?.nodes == null)
            {
                yield break;
            }
            if (pawn?.story?.bodyType == null || !GegBridge.HasGlowGene(pawn))
            {
                yield break;
            }

            foreach (PawnRenderNodeProperties props in GesDefOf.GES_EyeGlow.nodes)
            {
                // parent null lets AddChild resolve it from props.parentTagDef
                // (Head), which is already registered - the static tree is built
                // before SetupDynamicNodes runs.
                yield return (new PawnRenderNode_EyeGlow(pawn, props, tree), null);
            }
        }
    }
}
