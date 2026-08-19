using System.Collections.Generic;
using Verse;

namespace GlowingEyesScaled
{
    // Memoises Glowing eyes genes' two eye-presence checks.
    //
    // The originals each run
    // GetNotMissingParts().Where(p => p.customLabel == "left eye").ToList().Count == 1
    // per eye, per pawn, every tick - a full walk of roughly 67 body parts plus a
    // List allocation. Profiling showed 268,268 predicate invocations against
    // 4,004 eye checks, over half of the mod's total cost, growing with the number
    // of glowing pawns.
    //
    // This changes no semantics: the same counts, the same "exactly one" rule.
    // Both eyes are resolved in a single walk instead of two, and the answer is
    // held until the pawn's health actually changes.
    public static class EyeCache
    {
        private struct EyeState
        {
            public bool left;
            public bool right;
        }

        private const string LeftEyeLabel = "left eye";
        private const string RightEyeLabel = "right eye";

        // Keyed on thingIDNumber rather than Pawn so dead pawns are never
        // held alive by this cache.
        private static readonly Dictionary<int, EyeState> cache = new Dictionary<int, EyeState>();

        // Cheap self-limiting sweep. A full clear costs one rescan per
        // active pawn, which is far cheaper than tracking despawns.
        private static readonly int MaxEntries = 512;

        public static bool HasLeftEye(Pawn pawn)
        {
            return Get(pawn).left;
        }

        public static bool HasRightEye(Pawn pawn)
        {
            return Get(pawn).right;
        }

        // Postfixed onto HediffSet.DirtyCache, which vanilla already
        // calls whenever a pawn's hediffs change - including a body part being
        // lost, which adds a missing-part hediff.
        public static void Invalidate(Pawn pawn)
        {
            if (pawn != null)
            {
                cache.Remove(pawn.thingIDNumber);
            }
        }

        private static EyeState Get(Pawn pawn)
        {
            EyeState state;
            if (cache.TryGetValue(pawn.thingIDNumber, out state))
            {
                return state;
            }

            state = Scan(pawn);

            if (cache.Count >= MaxEntries)
            {
                cache.Clear();
            }
            cache[pawn.thingIDNumber] = state;
            return state;
        }

        private static EyeState Scan(Pawn pawn)
        {
            EyeState state = default(EyeState);
            if (pawn?.health?.hediffSet == null)
            {
                return state;
            }

            int left = 0;
            int right = 0;
            foreach (BodyPartRecord part in pawn.health.hediffSet.GetNotMissingParts())
            {
                string label = part.customLabel;
                if (label == LeftEyeLabel)
                {
                    left++;
                }
                else if (label == RightEyeLabel)
                {
                    right++;
                }
            }

            // "Exactly one" matches the original's .Count == 1.
            state.left = left == 1;
            state.right = right == 1;
            return state;
        }
    }
}
