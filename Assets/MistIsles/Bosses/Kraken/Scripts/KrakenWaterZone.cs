using System.Collections.Generic;
using UnityEngine;

namespace MistIsles.Bosses.Kraken
{
    /// <summary>
    /// Marks shallow water the player can stand in (Drowned Isle rule).
    /// While the player stands inside: grabs miss and ink dissolves. A slam that lands inside gets the tentacle stuck,
    /// which stuns the kraken. Use a Box/Sphere/Capsule (or convex Mesh) collider; it is forced to be a trigger.
    /// The zone is tested on its XZ footprint, so its height only needs to cover the water surface.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class KrakenWaterZone : MonoBehaviour
    {
        static readonly List<KrakenWaterZone> Zones = new List<KrakenWaterZone>();

        [Tooltip("Extra vertical tolerance (m) when testing if the player's feet are inside.")]
        public float verticalTolerance = 0.6f;

        Collider col;

        void OnEnable()
        {
            col = GetComponent<Collider>();
            col.isTrigger = true;
            if (!Zones.Contains(this)) Zones.Add(this);
        }

        void OnDisable()
        {
            Zones.Remove(this);
        }

        bool Inside(Vector3 q)
        {
            if (col == null || !col.enabled) return false;
            var b = col.bounds;
            if (!b.Contains(q)) return false;
            return (col.ClosestPoint(q) - q).sqrMagnitude < 0.0004f;
        }

        /// <summary>Point is inside the zone footprint (height ignored).</summary>
        public bool ContainsXZ(Vector3 p)
        {
            if (col == null) return false;
            var b = col.bounds;
            return Inside(new Vector3(p.x, Mathf.Clamp(p.y, b.min.y + 0.001f, b.max.y - 0.001f), p.z));
        }

        /// <summary>Feet position is inside the zone (with vertical tolerance).</summary>
        public bool ContainsFeet(Vector3 feet)
        {
            if (col == null) return false;
            var b = col.bounds;
            if (feet.y < b.min.y - verticalTolerance || feet.y > b.max.y + verticalTolerance) return false;
            return ContainsXZ(feet);
        }

        public static bool AnyContainsXZ(Vector3 p)
        {
            for (int i = 0; i < Zones.Count; i++)
                if (Zones[i].ContainsXZ(p)) return true;
            return false;
        }

        public static bool AnyContainsFeet(Vector3 feet)
        {
            for (int i = 0; i < Zones.Count; i++)
                if (Zones[i].ContainsFeet(feet)) return true;
            return false;
        }

        void OnDrawGizmos()
        {
            var c = GetComponent<Collider>();
            if (c == null) return;
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.25f);
            Gizmos.DrawCube(c.bounds.center, c.bounds.size);
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.9f);
            Gizmos.DrawWireCube(c.bounds.center, c.bounds.size);
        }
    }
}
