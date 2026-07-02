using UnityEngine;

namespace Game.World
{
    /// <summary>
    /// Construit le Mesh d'une île (smooth ou flat-shading) et ses couleurs par vertex
    /// (sable / herbe / roche selon hauteur et pente). Classe pure, sans état.
    /// </summary>
    public static class IslandMeshBuilder
    {
        [System.Serializable]
        public struct ColorSettings
        {
            public Color Sand;
            public Color Grass;
            public Color Rock;
            [Tooltip("Hauteur (m) jusqu'où le sable domine.")]
            public float SandHeight;
            [Range(0f, 1f)] public float RockSlope;
        }

        public static Mesh BuildSmooth(Vector3[] vertices, Vector2[] uvs, int[] triangles, string name, in ColorSettings cs)
        {
            var mesh = new Mesh { name = name };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var normals = mesh.normals;
            var colors = new Color[vertices.Length];
            for (int i = 0; i < vertices.Length; i++)
                colors[i] = VertexColor(vertices[i].y, 1f - Mathf.Clamp01(normals[i].y), cs);
            mesh.colors = colors;
            return mesh;
        }

        public static Mesh BuildFlatShaded(Vector3[] vertices, int[] triangles, string name, in ColorSettings cs)
        {
            int n = triangles.Length;
            var fv = new Vector3[n];
            var fn = new Vector3[n];
            var fc = new Color[n];
            var ft = new int[n];

            for (int i = 0; i < n; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];

                Vector3 nrm = Vector3.Cross(b - a, c - a).normalized;
                if (nrm.y < 0f) nrm = -nrm;
                float slope = 1f - Mathf.Clamp01(nrm.y);

                fv[i] = a; fv[i + 1] = b; fv[i + 2] = c;
                fn[i] = fn[i + 1] = fn[i + 2] = nrm;
                fc[i] = VertexColor(a.y, slope, cs);
                fc[i + 1] = VertexColor(b.y, slope, cs);
                fc[i + 2] = VertexColor(c.y, slope, cs);
                ft[i] = i; ft[i + 1] = i + 1; ft[i + 2] = i + 2;
            }

            var mesh = new Mesh { name = name };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = fv;
            mesh.normals = fn;
            mesh.triangles = ft;
            mesh.colors = fc;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Color VertexColor(float height, float slope, in ColorSettings cs)
        {
            float sandWeight = 1f - SmoothStep(cs.SandHeight - 0.5f, cs.SandHeight + 0.5f, height);
            Color baseCol = Color.Lerp(cs.Grass, cs.Sand, sandWeight);
            float rockWeight = SmoothStep(cs.RockSlope, cs.RockSlope + 0.15f, slope);
            return Color.Lerp(baseCol, cs.Rock, rockWeight);
        }

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            if (Mathf.Approximately(edge0, edge1)) return x < edge0 ? 0f : 1f;
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
