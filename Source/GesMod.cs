using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace GlowingEyesScaled
{
    // Above which game speed to stop computing the glow entirely.
    public enum GlowSpeedGate
    {
        Never = 0,
        AboveNormal = 1,
        AboveFast = 2
    }

    public class GesSettings : ModSettings
    {
        // Grow the glow sprite along with the head it sits on. Off leaves
        // the original fixed sprite size at every pawn size.
        public bool scaleGlowWithHead = true;

        // Skip the glow above a chosen game speed.
        //
        // On by default at above-normal, because that is exactly where Glowing
        // eyes genes stops drawing the movement trail - it gates flecks on
        // CurTimeSpeed == Normal - so past that point the per-tick cost keeps
        // multiplying while half the effect is already gone.
        public GlowSpeedGate hideAboveSpeed = GlowSpeedGate.AboveNormal;

        // Run the glow at frame rate rather than tick rate. Nothing is skipped at
        // normal speed; above it, one tick in three or six is let through and the
        // rest only keep the mote alive.
        public bool throttleToFrameRate = true;

        // Throttle at normal speed too. Nothing above is affected by this - at
        // normal speed the stride is chosen from whether the pawn is moving, since
        // that is the only time anything visual changes between ticks.
        public bool throttleAtNormalSpeed = true;

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref scaleGlowWithHead, "scaleGlowWithHead", true);
            Scribe_Values.Look(ref hideAboveSpeed, "hideAboveSpeed", GlowSpeedGate.AboveNormal);
            Scribe_Values.Look(ref throttleToFrameRate, "throttleToFrameRate", true);
            Scribe_Values.Look(ref throttleAtNormalSpeed, "throttleAtNormalSpeed", true);
        }
    }

    public class GesMod : Mod
    {
        private static GesSettings settings;

        public static GesSettings Settings => settings ?? (settings = new GesSettings());

        public GesMod(ModContentPack content) : base(content)
        {
            settings = GetSettings<GesSettings>();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(inRect);

            listing.CheckboxLabeled("Scale glow with head size", ref Settings.scaleGlowWithHead,
                "Grows the glow sprite on larger pawns. Turn off to keep the original fixed size at every scale.");
            listing.Gap();

            listing.CheckboxLabeled("Update at frame rate, not tick rate", ref Settings.throttleToFrameRate,
                "Glowing eyes genes recomputes the glow every tick, so at triple speed it does so three times per frame "
                + "and at sextuple, six. This lets one of those through and has the rest only keep the glow alive. "
                + "Normal speed is untouched. With this on, showing the glow at every speed costs about what normal "
                + "speed costs.");

            if (Settings.throttleToFrameRate)
            {
                listing.CheckboxLabeled("    Throttle at normal speed too", ref Settings.throttleAtNormalSpeed,
                    "At normal speed a moving pawn updates every other tick and a standing one every sixth, since a "
                    + "standing pawn's glow does not change between ticks. A change of facing always updates at once. "
                    + "The movement trail is drawn twice as sparsely, which is still dense enough to look continuous "
                    + "at any pawn speed.");
            }
            listing.Gap();

            listing.Label("Stop computing the glow above:");
            if (listing.RadioButton("Never - always show the glow", Settings.hideAboveSpeed == GlowSpeedGate.Never,
                    0f, "The glow is drawn at every game speed, at full per-tick cost."))
            {
                Settings.hideAboveSpeed = GlowSpeedGate.Never;
            }
            if (listing.RadioButton("Normal speed", Settings.hideAboveSpeed == GlowSpeedGate.AboveNormal,
                    0f, "Default. The glow disappears on fast and above, where the movement trail is already gone. Cheapest option."))
            {
                Settings.hideAboveSpeed = GlowSpeedGate.AboveNormal;
            }
            if (listing.RadioButton("Fast speed", Settings.hideAboveSpeed == GlowSpeedGate.AboveFast,
                    0f, "The glow disappears only on superfast and above."))
            {
                Settings.hideAboveSpeed = GlowSpeedGate.AboveFast;
            }

            listing.Gap();
            listing.Label("The glow is computed every tick, so its cost multiplies with game speed - three times over at fast, "
                + "six at superfast. The movement trail is already skipped above normal speed by Glowing eyes genes itself, "
                + "so above normal you are paying for a steady glow only.");

            listing.End();
            base.DoSettingsWindowContents(inRect);
        }

        // Called when the settings window closes.
        public override void WriteSettings()
        {
            base.WriteSettings();
            GesStartup.ApplySpeedGate();
        }

        public override string SettingsCategory()
        {
            return "Glowing Eyes Scaled";
        }
    }

    [StaticConstructorOnStartup]
    public static class GesStartup
    {
        private static Harmony harmony;
        private static bool speedGateApplied;

        static GesStartup()
        {
            // Touching GegBridge forces its static ctor, which needs defs loaded -
            // hence patching here rather than in the Mod constructor.
            harmony = new Harmony("DanZinagri.GlowingEyesScaled");

            if (GegBridge.PositionActive)
            {
                // The only unconditional position hook. Fires when GEG creates a
                // mote, i.e. on rotation change; the head-height half is XML.
                harmony.Patch(
                    AccessTools.Method(typeof(MoteMaker), nameof(MoteMaker.MakeAttachedOverlay)),
                    prefix: new HarmonyMethod(typeof(EyeGlowPositionPatch), nameof(EyeGlowPositionPatch.MoteOffsetPrefix)));
            }

            if (GegBridge.EyeCacheActive)
            {
                harmony.Patch(GegBridge.HasLeftEye,
                    prefix: new HarmonyMethod(typeof(EyeCachePatch), nameof(EyeCachePatch.HasLeftEyePrefix)));
                harmony.Patch(GegBridge.HasRightEye,
                    prefix: new HarmonyMethod(typeof(EyeCachePatch), nameof(EyeCachePatch.HasRightEyePrefix)));
                harmony.Patch(
                    AccessTools.Method(typeof(HediffSet), nameof(HediffSet.DirtyCache)),
                    postfix: new HarmonyMethod(typeof(EyeCachePatch), nameof(EyeCachePatch.DirtyCachePostfix)));
            }

            if (GegBridge.FleckActive)
            {
                harmony.Patch(GegBridge.ThrowFleck,
                    prefix: new HarmonyMethod(typeof(EyeGlowPositionPatch), nameof(EyeGlowPositionPatch.FleckLocPrefix)));
            }

            ApplySpeedGate();
        }

        // Applied and removed with the setting rather than left in place behind a
        // check, so that with the gate off GEG's tick carries no hook of ours at
        // all - nothing to attribute and nothing to pay for.
        public static void ApplySpeedGate()
        {
            bool wanted = GegBridge.GeneTick != null
                && (GesMod.Settings.hideAboveSpeed != GlowSpeedGate.Never
                    || (GesMod.Settings.throttleToFrameRate && TickThrottle.MoteAccessReady));

            if (wanted == speedGateApplied)
            {
                return;
            }

            MethodInfo patch = AccessTools.Method(typeof(TickThrottle), nameof(TickThrottle.Prefix));
            if (wanted)
            {
                harmony.Patch(GegBridge.GeneTick, prefix: new HarmonyMethod(patch));
            }
            else
            {
                harmony.Unpatch(GegBridge.GeneTick, patch);
            }
            speedGateApplied = wanted;
        }
    }

    // Swaps GEG's two per-tick body part scans for a cached lookup. Identical
    // results; see EyeCache for why.
    public static class EyeCachePatch
    {
        public static bool HasLeftEyePrefix(Gene __instance, ref bool __result)
        {
            // Doubles as the trail's pawn stash; see GegTickContext.
            GegTickContext.Note(__instance.pawn);
            __result = EyeCache.HasLeftEye(__instance.pawn);
            return false;
        }

        public static bool HasRightEyePrefix(Gene __instance, ref bool __result)
        {
            GegTickContext.Note(__instance.pawn);
            __result = EyeCache.HasRightEye(__instance.pawn);
            return false;
        }

        public static void DirtyCachePostfix(HediffSet __instance)
        {
            EyeCache.Invalidate(__instance.pawn);
        }
    }
}
