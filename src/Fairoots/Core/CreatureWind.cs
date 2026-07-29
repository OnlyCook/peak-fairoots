using System;

namespace Fairoots.Core
{
    /// <summary>
    /// How much the Roots wind pushes creatures around (ROADMAP.md Phase 7's last row).
    ///
    /// <b>The two creatures start from opposite places, so one number can't honestly serve
    /// both</b> - which is why this exposes two:
    /// <list type="bullet">
    /// <item><b>Zombies already take wind.</b> A zombie is a <c>Character</c> with
    /// <c>isBot</c> set, so it lives in <c>Character.AllBotCharacters</c>, and
    /// <c>WindChillZone.FixedUpdate</c> calls <c>AddWindForceToCharacter</c> on every bot
    /// in the zone at <b>0.6x</b> the multiplier a player gets. So there is a real vanilla
    /// value here, and <see cref="ResolveZombieMultiplier"/> scales it with 1.0 meaning
    /// vanilla, exactly like every other multiplier in the mod.</item>
    /// <item><b>Beetles take none at all, and can't.</b> A beetle is a <c>Mob</c>, not a
    /// <c>Character</c>, so the character path never sees it. It <em>is</em> also a
    /// <c>MobItem : Item</c>, so <c>AddWindForceToItem</c> does add force to its rigidbody -
    /// but <c>Mob.FixedUpdate</c> assigns <c>rig.linearVelocity = Vector3.zero</c> every
    /// single tick while walking, so that force is erased before it can move anything. A
    /// walking beetle is immovable by wind by construction, not by tuning.</item>
    /// </list>
    /// So the beetle dial is a <em>susceptibility</em> rather than a multiplier: 0 is
    /// vanilla (immune) and any positive value grants an effect the game never had. Naming
    /// it separately keeps "1.0 means vanilla" true of the zombie dial instead of quietly
    /// making 1.0 mean something different for each creature.
    /// </summary>
    public static class CreatureWind
    {
        /// <summary>
        /// The share of a player's wind force that vanilla already applies to bot
        /// characters - zombies included. Documentation for what the zombie multiplier is
        /// scaling; the game applies it, not this code.
        /// </summary>
        public const float VanillaBotWindShare = 0.6f;

        /// <summary>Ceiling for both dials. Beyond this, wind stops being weather.</summary>
        public const double MaxMultiplier = 5.0;

        /// <summary>
        /// Scales the wind force a zombie receives. 1.0 = vanilla (0.6x a player's).
        /// Clamped non-negative; 0 makes zombies wind-immune.
        /// </summary>
        public static double ResolveZombieMultiplier(double configured)
        {
            if (double.IsNaN(configured))
            {
                return 1.0;
            }

            return Math.Min(MaxMultiplier, Math.Max(0.0, configured));
        }

        /// <summary>
        /// How susceptible beetles are to wind. 0 = vanilla (completely immune).
        /// </summary>
        public static double ResolveBeetleSusceptibility(double configured)
        {
            if (double.IsNaN(configured))
            {
                return 0.0;
            }

            return Math.Min(MaxMultiplier, Math.Max(0.0, configured));
        }

        /// <summary>Whether the zombie dial is sitting at vanilla, so the patch can no-op exactly.</summary>
        public static bool IsVanillaZombieWind(double configured)
        {
            return Math.Abs(ResolveZombieMultiplier(configured) - 1.0) < 1e-6;
        }

        /// <summary>
        /// How fast wind slides a beetle, in world units per second, given the beetle's own
        /// walking speed.
        ///
        /// <b>Expressed relative to the beetle's own speed on purpose.</b> The obvious
        /// alternative - deriving it from the zone's <c>windForce</c> - looks more principled
        /// and is much worse: <c>windForce</c> is an acceleration fed to
        /// <c>Rigidbody.AddForce</c> (15-20 in Roots), and using that number as a velocity
        /// would fling beetles across the map at 20+ units/s. Tying it to <c>movementSpeed</c>
        /// makes susceptibility 1.0 mean "wind shoves a beetle about as fast as it walks",
        /// which is a quantity a player can actually predict, and it stays sane if a prefab
        /// ever ships a differently-scaled beetle.
        /// </summary>
        public static float BeetleDriftSpeed(float movementSpeed, double susceptibility)
        {
            return (float)(Math.Max(0f, movementSpeed) * ResolveBeetleSusceptibility(susceptibility));
        }
    }
}
