using UnityEngine;
using UnityEngine.Rendering;

namespace Game.World
{
    /// <summary>
    /// Crée un plan d'eau (mesh subdivisé + matériau DealIsland/WaterURP) au runtime ou en éditeur.
    /// Réutilisé par le bouton "Add Water" et par la génération de monde en multijoueur.
    /// </summary>
    public static class WaterPlane
    {
        // Matériau partagé entre toutes les créations (évite d'en fuiter un par régénération).
        private static Material _waterMaterial;

        public static GameObject Create(Vector3 center, float size, int subdivisions = 120)
        {
            var go = new GameObject("Water");
            go.transform.position = new Vector3(center.x, 0f, center.z);

            go.AddComponent<MeshFilter>().sharedMesh = BuildPlane(size, subdivisions);
            go.AddComponent<GeneratedMeshCleanup>(); // libère le mesh à la destruction

            var mr = go.AddComponent<MeshRenderer>();
            if (_waterMaterial == null)
            {
                var shader = Shader.Find("DeadIsland/WaterURP");
                if (shader != null) _waterMaterial = new Material(shader);
            }
            mr.sharedMaterial = _waterMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        /// <summary>Plan plat XZ centré, normales +Y, subdivisé pour des vagues lisses.</summary>
        public static Mesh BuildPlane(float size, int sub)
        {
            sub = Mathf.Max(1, sub);
            int side = sub + 1;
            var verts = new Vector3[side * side];
            var normals = new Vector3[side * side];
            var uvs = new Vector2[side * side];
            float half = size * 0.5f;

            for (int z = 0; z < side; z++)
            {
                for (int x = 0; x < side; x++)
                {
                    int i = z * side + x;
                    float fx = (float)x / sub;
                    float fz = (float)z / sub;
                    verts[i] = new Vector3(fx * size - half, 0f, fz * size - half);
                    normals[i] = Vector3.up;
                    uvs[i] = new Vector2(fx, fz);
                }
            }

            var tris = new int[sub * sub * 6];
            int t = 0;
            for (int z = 0; z < sub; z++)
            {
                for (int x = 0; x < sub; x++)
                {
                    int i0 = z * side + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + side;
                    int i3 = i2 + 1;
                    tris[t++] = i0; tris[t++] = i2; tris[t++] = i1;
                    tris[t++] = i1; tris[t++] = i2; tris[t++] = i3;
                }
            }

            var mesh = new Mesh { name = "WaterPlane" };
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();

            // Le shader déplace les sommets (vagues) mais les bounds restent plats à y=0 → Unity
            // culle le plan dès qu'on lève la caméra alors que les vagues sont encore visibles.
            // On gonfle les bounds verticalement pour couvrir l'amplitude des vagues.
            var b = mesh.bounds;
            b.Expand(new Vector3(0f, 8f, 0f));
            mesh.bounds = b;
            return mesh;
        }
    }
}
