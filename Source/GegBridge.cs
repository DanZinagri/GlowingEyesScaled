using System;
using System.Reflection;
using HarmonyLib;
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

        // The mote def GEG attaches to the pawn for the steady glow.
        public static readonly ThingDef EyeGlowMote;

        // True when the glow position can be corrected.
        public static readonly bool PositionActive;

        // GEG_Gene_GlowingEyes.Tick, and the class it lives on. Only patched while
        // the speed gate or the throttle is switched on, and unpatched again when
        // both are off, so with both off there is no hook on it at all.
        public static readonly MethodInfo GeneTick;
        public static readonly Type GeneType;

        // GEG's two per-tick eye scans, replaced with a cached lookup.
        public static readonly MethodInfo HasLeftEye;
        public static readonly MethodInfo HasRightEye;

        // True when both eye scans were found and can be memoised.
        public static readonly bool EyeCacheActive;

        // GEG_FleckMaker.ThrowEyeGlowFleck - the movement trail.
        public static readonly MethodInfo ThrowFleck;

        // True when the trail can be corrected. Depends on EyeCacheActive: the
        // trail helper takes no pawn, and the ticking pawn is picked up from the
        // eye-check prefixes, which GEG calls immediately before every throw.
        public static readonly bool FleckActive;

        static GegBridge()
        {
            if (!ModsConfig.IsActive(PackageId))
            {
                return;
            }

            EyeGlowMote = DefDatabase<ThingDef>.GetNamedSilentFail("GEG_EyeGlowMote");
            PositionActive = EyeGlowMote != null;
            if (!PositionActive)
            {
                Log.Warning("[Glowing Eyes Scaled] Could not find the GEG_EyeGlowMote def. Glow position will not be corrected.");
            }

            Type geneType = AccessTools.TypeByName("GEG_GlowingEyes.GEG_Gene_GlowingEyes");
            GeneType = geneType;
            if (geneType != null)
            {
                GeneTick = AccessTools.DeclaredMethod(geneType, "Tick");
                TickThrottle.Bind(geneType);
                HasLeftEye = AccessTools.DeclaredMethod(geneType, "HasLeftEye");
                HasRightEye = AccessTools.DeclaredMethod(geneType, "HasRightEye");
            }

            EyeCacheActive = HasLeftEye != null && HasRightEye != null;
            if (!EyeCacheActive)
            {
                Log.Warning("[Glowing Eyes Scaled] Could not find GEG's eye checks. Their per-tick body part scans will not be cached, "
                    + "and the movement trail will not be corrected.");
            }

            Type fleckMaker = AccessTools.TypeByName("GEG_GlowingEyes.GEG_FleckMaker");
            ThrowFleck = fleckMaker == null ? null : AccessTools.DeclaredMethod(fleckMaker, "ThrowEyeGlowFleck");
            FleckActive = ThrowFleck != null && EyeCacheActive;
        }
    }
}

namespace GlowingEyesScaled
{
    // GEG throws its trail through a static helper that takes no pawn. Rather
    // than hooking its Tick just to learn who is ticking, the pawn is picked up
    // from the eye-check prefixes - GEG guards every single throw with
    // HasRightEye() or HasLeftEye() on that same pawn immediately beforehand, so
    // the stash is always current and costs one field write inside a prefix that
    // is running anyway.
    //
    // Every read is validated against both the tick and the fleck's own map, so a
    // stale value can never be applied to the wrong pawn.
    public static class GegTickContext
    {
        public static Pawn CurrentPawn;
        public static int CurrentTick = -1;

        public static void Note(Pawn pawn)
        {
            CurrentPawn = pawn;
            CurrentTick = Find.TickManager.TicksGame;
        }
    }
}
