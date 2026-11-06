using UnityEngine;

namespace MistIsles.Player
{
    /// <summary>
    /// The "air mark" left by a sword swing: a crescent ribbon built in code that sweeps
    /// across the front of the player and fades out in about a quarter of a second.
    ///
    /// The mesh is a ring segment (inner/outer radius) split into columns along the arc.
    /// Each frame the vertex colours are recomputed: a bright head travels along the arc
    /// (the sweep) and leaves a fading tail, and the inner edge is always more transparent
    /// than the outer edge, so the slash looks sharp on the outside and airy inside.
    /// Uses vertex colours with an unlit transparent material, so it needs no textures.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class SwordSlash : MonoBehaviour
    {
        private const int Columns = 28;

        [SerializeField] private float radius = 2.3f;
        [SerializeField] private float thickness = 0.55f;
        [SerializeField] private float arcDegrees = 150f;
        [SerializeField] private float sweepSeconds = 0.12f;
        [SerializeField] private float lifeSeconds = 0.32f;
        [SerializeField] private Color color = new Color(0.85f, 0.97f, 1f, 1f);

        private Mesh mesh;
        private Color[] colors;
        private float age;
        private bool reverse;

        /// <summary>Spawns a slash in front of 'owner'. Tilt rolls the arc (diagonal cuts); reverse sweeps right-to-left.</summary>
        public static SwordSlash Spawn(Transform owner, Material material, float height, float radius, float tilt, bool reverse)
        {
            var go = new GameObject("SwordSlash");
            go.transform.SetPositionAndRotation(
                owner.position + Vector3.up * height,
                owner.rotation * Quaternion.Euler(0f, 0f, tilt));
            go.AddComponent<MeshFilter>();
            MeshRenderer mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material != null ? material : DefaultMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            SwordSlash s = go.AddComponent<SwordSlash>();
            s.radius = radius;
            s.reverse = reverse;
            s.Build();
            Destroy(go, s.lifeSeconds + 0.05f);
            return s;
        }

        private static Material fallback;

        /// <summary>Runtime fallback if no material asset was assigned: unlit, alpha-blended, vertex-coloured.</summary>
        public static Material DefaultMaterial()
        {
            if (fallback == null)
                fallback = new Material(Shader.Find("Sprites/Default")) { name = "SwordSlash (runtime)" };
            return fallback;
        }

        private void Build()
        {
            mesh = new Mesh { name = "SlashArc" };
            var verts = new Vector3[(Columns + 1) * 2];
            var uvs = new Vector2[verts.Length];
            colors = new Color[verts.Length];
            var tris = new int[Columns * 6];
            float half = arcDegrees * 0.5f * Mathf.Deg2Rad;
            for (int i = 0; i <= Columns; i++)
            {
                float u = (float)i / Columns;
                float a = Mathf.Lerp(-half, half, u);
                // thinner at both tips, thickest in the middle: a crescent
                float w = thickness * Mathf.Sin(u * Mathf.PI);
                Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                verts[i * 2] = dir * (radius - w);     // inner edge
                verts[i * 2 + 1] = dir * radius;       // outer edge
                uvs[i * 2] = new Vector2(u, 0f);
                uvs[i * 2 + 1] = new Vector2(u, 1f);
            }
            for (int i = 0; i < Columns; i++)
            {
                int v = i * 2, t = i * 6;
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2;
                tris[t + 3] = v + 1; tris[t + 4] = v + 3; tris[t + 5] = v + 2;
            }
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.colors = colors;
            mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = mesh;
            Paint();
        }

        private void Update()
        {
            age += Time.deltaTime;
            transform.localScale = Vector3.one * (1f + age * 0.6f); // the mark drifts outward
            Paint();
        }

        private void Paint()
        {
            float head = Mathf.Clamp01(age / sweepSeconds);                      // 0..1 along the arc
            float fade = 1f - Mathf.Clamp01((age - sweepSeconds) / (lifeSeconds - sweepSeconds));
            for (int i = 0; i <= Columns; i++)
            {
                float u = (float)i / Columns;
                if (reverse)
                    u = 1f - u;
                // revealed behind the head, brightest right at the head, fading tail
                float behind = head - u;
                float a = behind < 0f ? 0f : Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(behind * 1.4f));
                a *= fade * Mathf.Sin(((float)i / Columns) * Mathf.PI); // soft tips
                colors[i * 2] = new Color(color.r, color.g, color.b, a * 0.15f);   // inner edge: airy
                colors[i * 2 + 1] = new Color(color.r, color.g, color.b, a);       // outer edge: sharp
            }
            mesh.colors = colors;
        }

        private void OnDestroy()
        {
            if (mesh != null)
                Destroy(mesh);
        }
    }
}
