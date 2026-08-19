using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace GlowingEyesScaled
{
    // The glow gene, deliberately doing nothing.
    //
    // Glowing eyes genes drives its glow from Gene.Tick, which fires once per gene
    // per tick forever. A patch can stop that tick doing work but cannot stop it
    // being called, so patching left a floor of thousands of near-empty calls a
    // second that showed up whether or not anything was glowing.
    //
    // The glow does not need a tick at all: it is a render node, drawn by the pass
    // that draws the pawn, and its trail is laid from that same pass. So the gene
    // class is swapped for this one, which overrides nothing, and the floor goes to
    // zero.
    //
    // Their assembly still loads and their defs, textures, mod extension and
    // settings window all still work - only the class named by geneClass changes,
    // and that is an XML field. See Patches/EyeGlowGeneClass.xml.
    public class Gene_EyeGlow : Gene
    {
    }

    // Saves store a gene's concrete class name, not just its def - Pawn_GeneTracker
    // scribes its lists LookMode.Deep - so pawns from a save made before this mod
    // come back holding the original class and would still tick.
    //
    // Rather than leave them ticking forever, they are swapped over once on load.
    // A Gene carries only def, pawn, overriddenByGene, loadID and passionPreAdd, so
    // the swap is a straight copy; nothing about the pawn changes.
    [StaticConstructorOnStartup]
    public static class LegacyGeneMigration
    {
        private static readonly FieldInfo XenogenesField = AccessTools.Field(typeof(Pawn_GeneTracker), "xenogenes");
        private static readonly FieldInfo EndogenesField = AccessTools.Field(typeof(Pawn_GeneTracker), "endogenes");
        private static readonly FieldInfo PassionPreAddField = AccessTools.Field(typeof(Gene), "passionPreAdd");

        public static void Run()
        {
            if (GegBridge.GeneType == null || XenogenesField == null || EndogenesField == null)
            {
                return;
            }

            int swapped = 0;
            Dictionary<Gene, Gene> replaced = new Dictionary<Gene, Gene>();

            foreach (Pawn pawn in PawnsFinder.All_AliveOrDead)
            {
                Pawn_GeneTracker genes = pawn?.genes;
                if (genes == null)
                {
                    continue;
                }

                replaced.Clear();
                List<Gene> xeno = XenogenesField.GetValue(genes) as List<Gene>;
                List<Gene> endo = EndogenesField.GetValue(genes) as List<Gene>;
                swapped += SwapIn(xeno, replaced);
                swapped += SwapIn(endo, replaced);

                if (replaced.Count > 0)
                {
                    // A gene this one was overriding still points at the instance we
                    // just took out of the list. Left alone that is a reference to an
                    // object nothing owns any more, so repoint it at the replacement.
                    Repoint(xeno, replaced);
                    Repoint(endo, replaced);
                }
            }

            if (swapped > 0)
            {
                Log.Message($"[Glowing Eyes Scaled] Moved {swapped} glowing eye gene(s) off the original ticking class.");
            }
        }

        private static int SwapIn(List<Gene> list, Dictionary<Gene, Gene> replaced)
        {
            if (list == null)
            {
                return 0;
            }

            int swapped = 0;
            for (int i = 0; i < list.Count; i++)
            {
                Gene old = list[i];
                if (old == null || old.GetType() != GegBridge.GeneType)
                {
                    continue;
                }

                Gene_EyeGlow replacement = new Gene_EyeGlow
                {
                    def = old.def,
                    pawn = old.pawn,
                    loadID = old.loadID,
                    overriddenByGene = old.overriddenByGene
                };
                if (PassionPreAddField != null)
                {
                    PassionPreAddField.SetValue(replacement, PassionPreAddField.GetValue(old));
                }

                list[i] = replacement;
                replaced[old] = replacement;
                swapped++;
            }
            return swapped;
        }

        private static void Repoint(List<Gene> list, Dictionary<Gene, Gene> replaced)
        {
            if (list == null)
            {
                return;
            }
            for (int i = 0; i < list.Count; i++)
            {
                Gene gene = list[i];
                Gene target;
                if (gene?.overriddenByGene != null && replaced.TryGetValue(gene.overriddenByGene, out target))
                {
                    gene.overriddenByGene = target;
                }
            }
        }
    }
}
