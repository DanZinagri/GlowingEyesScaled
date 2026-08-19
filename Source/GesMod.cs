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
        // Size multiplier for the render node glow.
        public float glowScale = 1f;

        // Cells of travel between trail marks. Only affects how finely the path is
        // chopped up - the marks are sized to overlap whatever this is set to.
        public float trailSpacing = 0.15f;

        // Overall trail opacity. The sprite stacks additively, so this is the knob
        // for "too intense" rather than the size.
        public float trailBrightness = 0.6f;

        // How much the eye glow swells while the pawn is walking, covering the
        // stretch where new trail marks appear.
        public float movingGlowBoost = 1.6f;

        // Skip the glow above a chosen game speed.
        //
        // On by default at above-normal, because that is exactly where Glowing
        // eyes genes stops drawing the movement trail - it gates flecks on
        // CurTimeSpeed == Normal - so past that point the per-tick cost keeps
        // multiplying while half the effect is already gone.
        public GlowSpeedGate hideAboveSpeed = GlowSpeedGate.AboveNormal;



        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref glowScale, "glowScale", 1f);
            Scribe_Values.Look(ref trailSpacing, "trailSpacing", 0.15f);
            Scribe_Values.Look(ref trailBrightness, "trailBrightness", 0.6f);
            Scribe_Values.Look(ref movingGlowBoost, "movingGlowBoost", 1.6f);
            Scribe_Values.Look(ref hideAboveSpeed, "hideAboveSpeed", GlowSpeedGate.AboveNormal);
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

            listing.Label("Glow size: " + Settings.glowScale.ToStringPercent());
            Settings.glowScale = listing.Slider(Settings.glowScale, 0.25f, 3f);
            listing.Gap(6f);

            listing.Label("Glow swell while walking: " + Settings.movingGlowBoost.ToStringPercent());
            Settings.movingGlowBoost = listing.Slider(Settings.movingGlowBoost, 1f, 2.5f);
            listing.Label("Covers the stretch at the eye where new trail marks appear. Costs nothing, and lets the "
                + "trail be drawn more sparsely than it otherwise could be.");
            listing.Gap();

            listing.Label("Trail density: one mark every " + Settings.trailSpacing.ToString("0.00") + " cells");
            Settings.trailSpacing = listing.Slider(Settings.trailSpacing, 0.05f, 0.25f);
            listing.Label("Each mark is sized to overlap its neighbours by half, so this only decides how finely the "
                + "path is chopped up, not whether the trail has holes in it. Lower follows a turn more closely and "
                + "costs more.");
            listing.Gap(6f);

            listing.Label("Trail brightness: " + Settings.trailBrightness.ToStringPercent());
            Settings.trailBrightness = listing.Slider(Settings.trailBrightness, 0.1f, 1.5f);
            listing.Gap();

            listing.Label("Stop drawing the glow above:");
            if (listing.RadioButton("Never - always show the glow", Settings.hideAboveSpeed == GlowSpeedGate.Never,
                    0f, "The glow is drawn at every game speed."))
            {
                Settings.hideAboveSpeed = GlowSpeedGate.Never;
            }
            if (listing.RadioButton("Normal speed", Settings.hideAboveSpeed == GlowSpeedGate.AboveNormal,
                    0f, "Default. The glow disappears on fast and above, where the movement trail is already gone."))
            {
                Settings.hideAboveSpeed = GlowSpeedGate.AboveNormal;
            }
            if (listing.RadioButton("Fast speed", Settings.hideAboveSpeed == GlowSpeedGate.AboveFast,
                    0f, "The glow disappears only on superfast and above."))
            {
                Settings.hideAboveSpeed = GlowSpeedGate.AboveFast;
            }
            listing.Label("The glow itself costs nothing per tick, but its trail is still drawn while pawns walk. "
                + "Glowing eyes genes already stops the trail above normal speed, so past that there is little left "
                + "to see.");

            listing.End();
            base.DoSettingsWindowContents(inRect);
        }

        // Called when the settings window closes.
        public override void WriteSettings()
        {
            base.WriteSettings();
            GesStartup.Apply();
            GlowRecache.MarkAll();
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

        private static bool tickSuppressed;

        static GesStartup()
        {
            // Touching GegBridge forces its static ctor, which needs defs loaded -
            // hence patching here rather than in the Mod constructor.
            harmony = new Harmony("DanZinagri.GlowingEyesScaled");

            if (!GegBridge.Active)
            {
                return;
            }

            harmony.Patch(
                AccessTools.Method(typeof(HediffSet), nameof(HediffSet.DirtyCache)),
                postfix: new HarmonyMethod(typeof(EyeCachePatch), nameof(EyeCachePatch.DirtyCachePostfix)));
            harmony.Patch(
                AccessTools.PropertySetter(typeof(Pawn_DraftController), nameof(Pawn_DraftController.Drafted)),
                postfix: new HarmonyMethod(typeof(GlowRecache), nameof(GlowRecache.DraftedPostfix)));
            harmony.Patch(
                AccessTools.PropertySetter(typeof(TickManager), nameof(TickManager.CurTimeSpeed)),
                postfix: new HarmonyMethod(typeof(GlowRecache), nameof(GlowRecache.TimeSpeedPostfix)));
            harmony.Patch(
                AccessTools.Method(typeof(Game), nameof(Game.FinalizeInit)),
                postfix: new HarmonyMethod(typeof(GesStartup), nameof(GesStartup.FinalizeInitPostfix)));

            Apply();
        }

        public static void FinalizeInitPostfix()
        {
            LegacyGeneMigration.Run();
        }

        // Nothing here is conditional any more - the tick suppressor is applied
        // once and stays. Kept as a method so settings changes still push their
        // cached values through.
        public static void Apply()
        {
            if (!GegBridge.Active)
            {
                return;
            }

            TrailEmitter.CacheSettings();
            PawnRenderNodeWorker_EyeGlow.glowScale = GesMod.Settings.glowScale;
            PawnRenderNodeWorker_EyeGlow.movingBoost = GesMod.Settings.movingGlowBoost;

            if (!tickSuppressed && GegBridge.GeneTick != null)
            {
                // Their gene class is not instantiated at all once geneClass is
                // repointed, so this normally never runs. It is here for the case
                // where that patch fails to apply - their class would still be used
                // and would draw its own motes on top of our nodes - and for genes
                // on a save made before this mod, until they are migrated.
                harmony.Patch(GegBridge.GeneTick,
                    prefix: new HarmonyMethod(typeof(GlowSuppressor), nameof(GlowSuppressor.Prefix)));
                tickSuppressed = true;
            }
        }

    }

    // Keeps the eye-presence cache honest. The cache itself is read straight from
    // CanDrawNow; this is the only thing that has to invalidate it.
    public static class EyeCachePatch
    {
        public static void DirtyCachePostfix(HediffSet __instance)
        {
            EyeCache.Invalidate(__instance.pawn);
        }
    }
}
