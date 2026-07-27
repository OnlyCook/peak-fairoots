using UnityEngine;

namespace Fairoots.SporeBombs
{
    /// <summary>
    /// A tag component Fairoots attaches to the object a spore bomb spawns when it
    /// detonates, so that object stays identifiable as "this came from a spore bomb"
    /// for as long as it exists.
    ///
    /// It exists because the obvious alternatives both fail. The spawned explosion has
    /// no distinguishing name or component of its own (<c>SpawnGameObject</c> is a
    /// generic, game-wide spawner - see <see cref="SporeBombExplosionPatch"/>), and
    /// <see cref="DetonationScreenshakeRegistry"/>, which knows where detonations
    /// recently happened, deliberately expires after a few seconds because it exists to
    /// attribute screen shakes.
    ///
    /// That expiry caused a real bug (live-reported 2026-07-27): a spore bomb's
    /// lingering cloud is a single <c>AOE</c> that <em>re-explodes on a timer</em> (a
    /// <c>TimeEvent</c> invoking <c>Explode</c> over and over), so its later repeats
    /// fell outside the registry's window and stopped being recognised as spore-bomb
    /// spores - which is why covering your mouth blocked the first few seconds of a
    /// cloud and then quietly stopped working. A tag on the object has no window to
    /// fall outside of.
    /// </summary>
    internal sealed class SporeBombDetonationMarker : MonoBehaviour
    {
    }
}
