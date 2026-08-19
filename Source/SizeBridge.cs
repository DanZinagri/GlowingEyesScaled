using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace GlowingEyesScaled
{
    // Reads the size values Big and Small applies at render time straight out of
    // its own BSCache, rather than recomputing anything. Only the two the XML
    // patch cannot reach are read here - the head offset is handled by vanilla's
    // attachedToHead, which already picks up B&S's postfix. Because they all land in
    // that one cache, stacked sources - a large frame gene plus a gigantism trait
    // plus anything else - are already composed by the time we read them, and this
    // mod never needs to know what contributed.
    //
    // Every accessor is compiled to a delegate once at startup. Reflecting per
    // call cost about 11us here, almost all of it MethodInfo.Invoke plus a boxing
    // FieldInfo.GetValue per field; compiled getters are direct field loads with
    // no boxing.
    //
    // Entirely optional: with B&S absent, or on a pawn it is not resizing,
    // TryGet returns false and the patch leaves the original numbers alone.
    [StaticConstructorOnStartup]
    public static class SizeBridge
    {
        public const string PackageId = "RedMattis.BetterPrerequisites";

        private static readonly bool available;

        private static readonly Func<Pawn, object> getCache;
        private static readonly Func<object, bool> readApproxNoChange;    // B&S's own "unmodified pawn" flag
        private static readonly Func<object, float> readWorldspaceOffset; // whole-pawn lift
        private static readonly Func<object, float> readHeadRenderSize;   // head quad scale
        private static readonly Func<object, List<Vector3>> readBodyOffsets;

        private static readonly Func<bool> readBodyOffsetSetting;

        static SizeBridge()
        {
            if (!ModsConfig.IsActive(PackageId))
            {
                return;
            }

            Type scaler = AccessTools.TypeByName("BigAndSmall.HumanoidPawnScaler");
            Type cache = AccessTools.TypeByName("BigAndSmall.BSCache");
            if (scaler == null || cache == null)
            {
                return;
            }

            try
            {
                // GetCacheUltraSpeed(pawn, canRegenerate: false) is the cheap
                // read-only accessor; we never ask B&S to regenerate on our account.
                MethodInfo method = AccessTools.Method(scaler, "GetCacheUltraSpeed", new[] { typeof(Pawn), typeof(bool) });
                if (method != null)
                {
                    ParameterExpression pawnParam = Expression.Parameter(typeof(Pawn), "pawn");
                    getCache = Expression.Lambda<Func<Pawn, object>>(
                        Expression.Convert(Expression.Call(method, pawnParam, Expression.Constant(false)), typeof(object)),
                        pawnParam).Compile();
                }

                readApproxNoChange = FieldGetter<bool>(cache, "approximatelyNoChange");
                readWorldspaceOffset = FieldGetter<float>(cache, "worldspaceOffset");
                readHeadRenderSize = FieldGetter<float>(cache, "headRenderSize");
                readBodyOffsets = FieldGetter<List<Vector3>>(cache, "complexBodyOffsets");

                readBodyOffsetSetting = SettingGetter();
            }
            catch (Exception e)
            {
                Log.Warning("[Glowing Eyes Scaled] Could not bind Big and Small's size cache, glow positions will not "
                    + "follow its resizing. " + e.Message);
                return;
            }

            available = getCache != null && readWorldspaceOffset != null && readHeadRenderSize != null;
            if (!available)
            {
                Log.Warning("[Glowing Eyes Scaled] Big and Small is active but its size cache could not be read. "
                    + "Glow positions will not follow its resizing.");
            }
        }

        private static Func<object, T> FieldGetter<T>(Type owner, string fieldName)
        {
            FieldInfo field = AccessTools.Field(owner, fieldName);
            if (field == null)
            {
                return null;
            }
            ParameterExpression obj = Expression.Parameter(typeof(object), "o");
            Expression body = Expression.Field(Expression.Convert(obj, owner), field);
            if (field.FieldType != typeof(T))
            {
                body = Expression.Convert(body, typeof(T));
            }
            return Expression.Lambda<Func<object, T>>(body, obj).Compile();
        }

        // B&S's own "offset body position" setting, which gates the whole-pawn
        // lift. Compiled as a static field read so honouring it is free.
        private static Func<bool> SettingGetter()
        {
            Type mod = AccessTools.TypeByName("BigAndSmall.BigSmallMod");
            FieldInfo settingsField = mod == null ? null : AccessTools.Field(mod, "settings");
            FieldInfo offsetField = settingsField == null
                ? null
                : AccessTools.Field(settingsField.FieldType, "offsetBodyPos");
            if (settingsField == null || offsetField == null)
            {
                return null;
            }
            return Expression.Lambda<Func<bool>>(
                Expression.Field(Expression.Field(null, settingsField), offsetField)).Compile();
        }

        // Defaults to true when unreadable, matching B&S's own default.
        public static bool BodyOffsetEnabled => readBodyOffsetSetting == null || readBodyOffsetSetting();

        // Returns false when there is nothing to correct, so the caller can leave
        // the original numbers exactly as they were.
        public static bool TryGet(Pawn pawn, Rot4 rot, out float lift, out float headScale, out Vector3 bodyOffset)
        {
            lift = 0f;
            headScale = 1f;
            bodyOffset = Vector3.zero;

            if (!available || pawn == null)
            {
                return false;
            }

            object cache = getCache(pawn);
            if (cache == null)
            {
                return false;
            }

            // B&S sets this when the pawn is drawn at stock size; taking its word
            // for it keeps the common case to a single field read.
            if (readApproxNoChange != null && readApproxNoChange(cache))
            {
                return false;
            }

            lift = readWorldspaceOffset(cache);
            headScale = readHeadRenderSize(cache);
            bodyOffset = Rotational(readBodyOffsets, cache, rot);

            return lift != 0f || headScale != 1f || bodyOffset != Vector3.zero;
        }

        private static Vector3 Rotational(Func<object, List<Vector3>> getter, object cache, Rot4 rot)
        {
            if (getter == null)
            {
                return Vector3.zero;
            }
            List<Vector3> offsets = getter(cache);
            return offsets != null && offsets.Count > rot.AsInt ? offsets[rot.AsInt] : Vector3.zero;
        }
    }
}
