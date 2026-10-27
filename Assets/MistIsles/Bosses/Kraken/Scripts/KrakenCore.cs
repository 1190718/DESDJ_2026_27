using UnityEngine;

namespace MistIsles.Bosses.Kraken
{
    /// <summary>Data passed to the player when a kraken attack connects.</summary>
    public struct KrakenHitInfo
    {
        public int damage;
        public Vector3 point;
        public Vector3 direction;
        public string source;
    }

    /// <summary>
    /// Optional: implement on the player's health script to receive kraken hits with full info.
    /// If no component implements it, the boss calls SendMessage("TakeDamage", int) on the player instead.
    /// </summary>
    public interface IKrakenDamageable
    {
        void TakeKrakenDamage(KrakenHitInfo hit);
    }

    /// <summary>World-space capsule describing the player's body.</summary>
    public struct PlayerBody
    {
        public Vector3 bottom;
        public Vector3 top;
        public float radius;
        public Vector3 Center => (bottom + top) * 0.5f;
    }

    public static class KrakenGeometry
    {
        public static PlayerBody GetPlayerBody(Transform player)
        {
            var body = new PlayerBody();
            var cc = player.GetComponent<CharacterController>();
            if (cc != null)
            {
                Vector3 s = player.lossyScale;
                float r = cc.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
                float half = Mathf.Max(0f, cc.height * 0.5f * Mathf.Abs(s.y) - r);
                Vector3 c = player.TransformPoint(cc.center);
                body.bottom = c - Vector3.up * half;
                body.top = c + Vector3.up * half;
                body.radius = r;
                return body;
            }
            var cap = player.GetComponent<CapsuleCollider>();
            if (cap != null)
            {
                Vector3 s = player.lossyScale;
                float r = cap.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
                float half = Mathf.Max(0f, cap.height * 0.5f * Mathf.Abs(s.y) - r);
                Vector3 c = player.TransformPoint(cap.center);
                body.bottom = c - Vector3.up * half;
                body.top = c + Vector3.up * half;
                body.radius = r;
                return body;
            }
            body.bottom = player.position + Vector3.up * 0.4f;
            body.top = player.position + Vector3.up * 1.4f;
            body.radius = 0.4f;
            return body;
        }

        /// <summary>Squared distance between segments p1-q1 and p2-q2.</summary>
        public static float SegmentSegmentSqr(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            float s, t;
            const float eps = 1e-6f;
            if (a <= eps && e <= eps) return r.sqrMagnitude;
            if (a <= eps)
            {
                s = 0f;
                t = Mathf.Clamp01(f / e);
            }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= eps)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-c / a);
                }
                else
                {
                    float b = Vector3.Dot(d1, d2);
                    float denom = a * e - b * b;
                    s = denom != 0f ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            Vector3 c1 = p1 + d1 * s, c2 = p2 + d2 * t;
            return (c1 - c2).sqrMagnitude;
        }

        /// <summary>
        /// True if any segment of the bone chain (from 'fromIndex') overlaps the player capsule.
        /// The last segment is extrapolated from the previous one.
        /// </summary>
        public static bool ChainHitsPlayer(Transform[] bones, float[] radii, float scale, PlayerBody p, int fromIndex = 0)
        {
            if (bones == null || bones.Length < 2) return false;
            for (int i = Mathf.Max(0, fromIndex); i < bones.Length; i++)
            {
                if (bones[i] == null) continue;
                Vector3 a = bones[i].position;
                Vector3 b = i + 1 < bones.Length ? bones[i + 1].position : a + (a - bones[i - 1].position);
                float r = (i < radii.Length ? radii[i] : radii[radii.Length - 1]) * scale + p.radius;
                if (SegmentSegmentSqr(a, b, p.bottom, p.top) < r * r) return true;
            }
            return false;
        }

        public static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var t = FindDeep(root.GetChild(i), name);
                if (t != null) return t;
            }
            return null;
        }

        public static Transform[] FindChain(Transform root, string prefix, int count)
        {
            var arr = new Transform[count];
            for (int i = 0; i < count; i++) arr[i] = FindDeep(root, prefix + i.ToString("00"));
            return arr;
        }

        public static float Yaw(Vector3 dir)
        {
            return Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
        }
    }
}
