using System.Collections.Generic;
using UnityEngine;

namespace Fairoots.Creatures
{
    /// <summary>
    /// The shared "which GameObject <em>is</em> this creature" logic every mechanic
    /// in this folder calls, for the same reason <c>SporeAreas/SporeAreaScan</c>
    /// exists as one copy: two slightly different answers would mean the disable
    /// pass and (later) the tuning passes disagreeing about how many creatures the
    /// level has, or restoring something they never hid.
    ///
    /// The three creatures PEAK puts in Roots are three unrelated implementations,
    /// not one hierarchy - see <c>CreatureDisablePatch</c>'s remarks:
    /// <list type="bullet">
    /// <item><c>Beetle : Mob</c> - a rigidbody prop that is also a <c>MobItem : Item</c>.</item>
    /// <item><c>MushroomZombie</c> - a full <c>Character</c> with its own state machine,
    /// spawned at runtime by <c>ZombieManager</c> rather than placed in the scene.</item>
    /// <item><c>Spider</c> - a plain <c>MonoBehaviour</c> hanging from the ceiling that
    /// culls itself by toggling its own root GameObject active.</item>
    /// </list>
    /// </summary>
    internal static class CreatureScan
    {
        /// <summary>
        /// The GameObject to deactivate for a beetle: the <see cref="Mob"/>'s own
        /// object. Deliberately <em>not</em> a parent walk (the spore-area
        /// treatment): a beetle carries its own rigidbody, collider, visuals and
        /// <c>MobItem</c> on one object, and its parent is a shared prop-grouping
        /// node holding every other beetle in the group.
        /// </summary>
        internal static GameObject ResolveBeetleRoot(Beetle beetle) => beetle.gameObject;

        /// <summary>
        /// The GameObject to hide for a spider: the <c>spider</c> child holding the
        /// mesh, <em>not</em> the <see cref="Spider"/> root. The root is off limits
        /// because the game drives it itself - <c>Spider.UpdateCulled</c> calls
        /// <c>gameObject.SetActive(!culled)</c> every scan based on player distance,
        /// so anything this mod did to the root would be silently undone (or
        /// re-done) the next time a player walked past.
        /// </summary>
        internal static GameObject ResolveSpiderVisual(Spider spider) =>
            spider.spider != null ? spider.spider : null;

        /// <summary>
        /// Human-readable scene path, for the verbose log lines that make a
        /// "disabled N creatures" claim checkable against a real level. Same
        /// format/limit as <c>SporeAreaScan.DescribePath</c>.
        /// </summary>
        internal static string DescribePath(Transform t)
        {
            var parts = new List<string>();
            for (Transform cur = t; cur != null && parts.Count < 6; cur = cur.parent)
            {
                parts.Add(cur.name);
            }

            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
