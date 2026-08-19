using System;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace GlowingEyesScaled
{
    // Runs Glowing eyes genes' tick at roughly frame rate instead of tick rate.
    //
    // None of its logic is reimplemented here. On the ticks we let through, its own
    // Tick runs untouched and does all the work; we only decide how often that is.
    // The glow is a visual, so at triple or sextuple speed it was being recomputed
    // three or six times for every frame that could possibly show the result.
    //
    // The stride is the tick rate multiplier, so at normal speed it is 1 and the
    // prefix never skips anything - behaviour there is byte-identical to unpatched.
    // Pawns are staggered by thingIDNumber so the ticks that do pass through are
    // spread out rather than landing together.
    //
    // Skipping a tick would normally kill the glow, because the mote is
    // needsMaintenance. So on a skipped tick we call Maintain on GEG's own two
    // motes and nothing else - a couple of field reads against the whole of its
    // Tick.
    public static class TickThrottle
    {
        // Trail flecks are one per eye per tick while moving, each living 0.4s, so
        // roughly 48 overlap per eye at any moment. Halving that still leaves them
        // 0.27 cells apart at the fastest pawn speeds against a 0.28 sprite, so the
        // streak stays continuous. Thirding it opens visible gaps above ~5.6 c/s.
        private const int MovingStride = 2;

        // A standing pawn emits no trail, so nothing visual changes between ticks
        // at all. Only state transitions - falling asleep, being drafted - are
        // delayed, by at most a tenth of a second. Rotation is handled separately.
        private const int IdleStride = 6;

        private static Func<object, Mote> getMote1;
        private static Func<object, Mote> getMote2;
        private static Func<object, Rot4> getPreviousDirection;

        public static bool MoteAccessReady => getMote1 != null && getMote2 != null;

        public static void Bind(Type geneType)
        {
            try
            {
                getMote1 = MoteGetter(geneType, "EyeGlowMote");
                getMote2 = MoteGetter(geneType, "EyeGlowMote2");
                getPreviousDirection = FieldGetter<Rot4>(geneType, "previousDirection");
            }
            catch (Exception e)
            {
                Log.Warning("[Glowing Eyes Scaled] Could not bind GEG's mote fields, so its tick cannot be throttled "
                    + "without the glow fading. " + e.Message);
                getMote1 = null;
                getMote2 = null;
            }
        }

        private static Func<object, Mote> MoteGetter(Type owner, string fieldName)
        {
            return FieldGetter<Mote>(owner, fieldName);
        }

        private static Func<object, T> FieldGetter<T>(Type owner, string fieldName)
        {
            FieldInfo field = AccessTools.Field(owner, fieldName);
            if (field == null)
            {
                return null;
            }
            ParameterExpression obj = Expression.Parameter(typeof(object), "o");
            return Expression.Lambda<Func<object, T>>(
                Expression.Field(Expression.Convert(obj, owner), field), obj).Compile();
        }

        // GEG rebuilds its mote when the pawn's facing changes, so a skipped tick
        // there would leave the glow at the old rotation's offset. Rather than keep
        // our own copy of the facing, compare against GEG's - it only advances on
        // the ticks we let through, which is exactly the value we want.
        private static bool FacingChanged(Gene gene, Pawn pawn)
        {
            return getPreviousDirection != null && getPreviousDirection(gene) != pawn.Rotation;
        }

        // Prefix on GEG_Gene_GlowingEyes.Tick. Returning false skips that tick.
        public static bool Prefix(Gene __instance)
        {
            Pawn pawn = __instance?.pawn;
            if (pawn == null)
            {
                return true;
            }

            TickManager ticks = Find.TickManager;

            // Hard gate first: above the chosen speed the glow is switched off
            // outright, so the motes are deliberately left unmaintained and fade.
            switch (GesMod.Settings.hideAboveSpeed)
            {
                case GlowSpeedGate.AboveNormal:
                    if (ticks.CurTimeSpeed > TimeSpeed.Normal)
                    {
                        return false;
                    }
                    break;
                case GlowSpeedGate.AboveFast:
                    if (ticks.CurTimeSpeed > TimeSpeed.Fast)
                    {
                        return false;
                    }
                    break;
            }

            if (!GesMod.Settings.throttleToFrameRate || !MoteAccessReady)
            {
                return true;
            }

            int stride = Mathf.Max(1, Mathf.RoundToInt(ticks.TickRateMultiplier));

            if (stride <= 1)
            {
                // Normal speed. Above it the trail is off anyway, but here it is
                // live, so the stride depends on whether the pawn is laying one.
                if (!GesMod.Settings.throttleAtNormalSpeed)
                {
                    return true;
                }
                stride = (pawn.pather != null && pawn.pather.MovingNow) ? MovingStride : IdleStride;
            }

            // Never sit on a facing change: the mote has to be rebuilt at the new
            // offset before it is drawn wrong.
            if (FacingChanged(__instance, pawn))
            {
                return true;
            }

            // Staggered per pawn so the passed-through ticks do not bunch up.
            if ((ticks.TicksGame + pawn.thingIDNumber) % stride == 0)
            {
                return true;
            }

            KeepAlive(__instance);
            return false;
        }

        // The entire cost of a skipped tick.
        private static void KeepAlive(Gene gene)
        {
            Mote mote = getMote1(gene);
            if (mote != null && !mote.Destroyed)
            {
                mote.Maintain();
            }
            mote = getMote2(gene);
            if (mote != null && !mote.Destroyed)
            {
                mote.Maintain();
            }
        }
    }
}
