using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace GlowingEyesScaled
{
    // The handful of Glowing eyes genes members this mod touches, resolved
    // reflectively once at startup. No assembly reference: GEG ships no public
    // API, and reflecting lets this mod sit inert rather than throwing a
    // TypeLoadException if GEG is absent.
    [StaticConstructorOnStartup]
    public static class GegBridge
    {
        public const string PackageId = "lts.GEG";

        // True when GEG is present and its glow genes were resolved.
        public static readonly bool Active;

        // GEG's trail fleck.
        public static readonly FleckDef GlowFleck;

        // GEG_Gene_GlowingEyes.Tick, and the class it lives on. Their gene class is
        // no longer instantiated - geneClass is repointed at Gene_EyeGlow - so this
        // is only wanted to suppress the tick if that ever fails to apply, and to
        // recognise their class on a save made before this mod.
        public static readonly MethodInfo GeneTick;
        public static readonly Type GeneType;

        static GegBridge()
        {
            if (!ModsConfig.IsActive(PackageId))
            {
                return;
            }

            GlowFleck = DefDatabase<FleckDef>.GetNamedSilentFail("GEG_EyeGlowFleck");

            Type geneType = AccessTools.TypeByName("GEG_GlowingEyes.GEG_Gene_GlowingEyes");
            GeneType = geneType;
            if (geneType != null)
            {
                GeneTick = AccessTools.DeclaredMethod(geneType, "Tick");
            }

            Type settingsHandler = AccessTools.TypeByName("GEG_GlowingEyes.SettingsHandler");
            SettingsField = settingsHandler == null ? null : AccessTools.Field(settingsHandler, "Settings");
            if (SettingsField != null)
            {
                OnlyWhileDraftedField = AccessTools.Field(SettingsField.FieldType, "LTS_GEG_OnlyWhileDrafted");
                if (OnlyWhileDraftedField != null)
                {
                    try
                    {
                        // Compiled rather than reflected: FieldInfo.GetValue on this
                        // pair measured over 6us a call, which is a lot for reading
                        // one bool on a path that runs per pawn.
                        readOnlyWhileDrafted = Expression.Lambda<Func<bool>>(
                            Expression.Field(Expression.Field(null, SettingsField), OnlyWhileDraftedField)).Compile();
                    }
                    catch (Exception)
                    {
                        readOnlyWhileDrafted = null;
                    }
                }
            }

            Type extType = AccessTools.TypeByName("GEG_GlowingEyes.GEGModExtension");
            FieldInfo colorField = extType == null ? null : AccessTools.Field(extType, "GEG_EyeColor");
            {
                foreach (GeneDef def in DefDatabase<GeneDef>.AllDefsListForReading)
                {
                    // geneClass is repointed at ours by Patches/EyeGlowGeneClass.xml,
                    // so match either - theirs still turns up on an older save.
                    if (def.geneClass != null
                        && (typeof(Gene_EyeGlow).IsAssignableFrom(def.geneClass)
                            || geneType.IsAssignableFrom(def.geneClass)))
                    {
                        GlowGenes[def] = ReadColor(def, extType, colorField);
                    }
                }
            }

            Active = GeneTick != null && GlowGenes.Count > 0;
        }

        private static readonly Dictionary<GeneDef, Color> GlowGenes = new Dictionary<GeneDef, Color>();
        private static readonly FieldInfo SettingsField;
        private static readonly FieldInfo OnlyWhileDraftedField;
        private static readonly Func<bool> readOnlyWhileDrafted;

        private static Color ReadColor(GeneDef def, Type extType, FieldInfo colorField)
        {
            if (extType == null || colorField == null || def.modExtensions == null)
            {
                return Color.white;
            }
            foreach (DefModExtension ext in def.modExtensions)
            {
                if (extType.IsInstanceOfType(ext))
                {
                    return (Color)colorField.GetValue(ext);
                }
            }
            return Color.white;
        }

        // GEG's own "only glow while drafted" setting, read live so its settings
        // window keeps working.
        public static bool OnlyWhileDrafted => readOnlyWhileDrafted != null && readOnlyWhileDrafted();

        public static bool HasGlowGene(Pawn pawn)
        {
            Color ignored;
            return TryGetGlowColor(pawn, out ignored);
        }

        public static bool TryGetGlowColor(Pawn pawn, out Color color)
        {
            color = Color.white;
            if (pawn?.genes == null || GlowGenes.Count == 0)
            {
                return false;
            }
            List<Gene> genes = pawn.genes.GenesListForReading;
            for (int i = 0; i < genes.Count; i++)
            {
                Gene gene = genes[i];
                if (gene != null && gene.Active && GlowGenes.TryGetValue(gene.def, out color))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
