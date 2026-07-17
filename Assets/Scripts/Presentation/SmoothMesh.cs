using UnityEngine;

namespace Poker.Presentation
{
    /// <summary>
    /// Higher-segment cylinder/disc meshes so table and chip curves look smoother under MSAA.
    /// </summary>
    public static class SmoothMesh
    {
        static Mesh _cylinder;
        static Mesh _disc;

        public static Mesh Cylinder(int segments = 64)
        {
            if (_cylinder != null) return _cylinder;
            _cylinder = BuildCylinder(segments);
            _cylinder.name = "SmoothCylinder64";
            return _cylinder;
        }

        public static Mesh Disc(int segments = 64)
        {
            if (_disc != null) return _disc;
            _disc = BuildDisc(segments);
            _disc.name = "SmoothDisc64";
            return _disc;
        }

        public static void ReplacePrimitiveMesh(GameObject go, Mesh mesh)
        {
            var filter = go.GetComponent<MeshFilter>();
            if (filter != null)
                filter.sharedMesh = mesh;
        }

        static Mesh BuildCylinder(int segments)
        {
            // Unity default cylinder: height 2 along Y, radius 0.5
            int sideVerts = (segments + 1) * 2;
            int capVerts = segments + 1;
            var verts = new Vector3[sideVerts + capVerts * 2];
            var norms = new Vector3[verts.Length];
            var uvs = new Vector2[verts.Length];

            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                float a = t * Mathf.PI * 2f;
                float x = Mathf.Cos(a) * 0.5f;
                float z = Mathf.Sin(a) * 0.5f;
                var n = new Vector3(x, 0f, z).normalized;

                verts[i] = new Vector3(x, 1f, z);
                verts[i + segments + 1] = new Vector3(x, -1f, z);
                norms[i] = n;
                norms[i + segments + 1] = n;
                uvs[i] = new Vector2(t, 1f);
                uvs[i + segments + 1] = new Vector2(t, 0f);
            }

            int topStart = sideVerts;
            int botStart = sideVerts + capVerts;
            verts[topStart] = new Vector3(0f, 1f, 0f);
            norms[topStart] = Vector3.up;
            uvs[topStart] = new Vector2(0.5f, 0.5f);
            verts[botStart] = new Vector3(0f, -1f, 0f);
            norms[botStart] = Vector3.down;
            uvs[botStart] = new Vector2(0.5f, 0.5f);

            for (int i = 0; i < segments; i++)
            {
                float t = (float)i / segments;
                float a = t * Mathf.PI * 2f;
                float x = Mathf.Cos(a) * 0.5f;
                float z = Mathf.Sin(a) * 0.5f;
                verts[topStart + 1 + i] = new Vector3(x, 1f, z);
                norms[topStart + 1 + i] = Vector3.up;
                uvs[topStart + 1 + i] = new Vector2(x + 0.5f, z + 0.5f);
                verts[botStart + 1 + i] = new Vector3(x, -1f, z);
                norms[botStart + 1 + i] = Vector3.down;
                uvs[botStart + 1 + i] = new Vector2(x + 0.5f, z + 0.5f);
            }

            int sideTris = segments * 6;
            int capTris = segments * 3 * 2;
            var tris = new int[sideTris + capTris];
            int ti = 0;
            for (int i = 0; i < segments; i++)
            {
                int t0 = i;
                int t1 = i + 1;
                int b0 = i + segments + 1;
                int b1 = i + 1 + segments + 1;
                tris[ti++] = t0;
                tris[ti++] = b0;
                tris[ti++] = t1;
                tris[ti++] = t1;
                tris[ti++] = b0;
                tris[ti++] = b1;
            }

            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                tris[ti++] = topStart;
                tris[ti++] = topStart + 1 + i;
                tris[ti++] = topStart + 1 + next;
                tris[ti++] = botStart;
                tris[ti++] = botStart + 1 + next;
                tris[ti++] = botStart + 1 + i;
            }

            var mesh = new Mesh { name = "SmoothCylinder" };
            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh BuildDisc(int segments)
        {
            var verts = new Vector3[segments + 1];
            var norms = new Vector3[segments + 1];
            var uvs = new Vector2[segments + 1];
            verts[0] = Vector3.zero;
            norms[0] = Vector3.up;
            uvs[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < segments; i++)
            {
                float a = (float)i / segments * Mathf.PI * 2f;
                float x = Mathf.Cos(a) * 0.5f;
                float z = Mathf.Sin(a) * 0.5f;
                verts[i + 1] = new Vector3(x, 0f, z);
                norms[i + 1] = Vector3.up;
                uvs[i + 1] = new Vector2(x + 0.5f, z + 0.5f);
            }

            var tris = new int[segments * 3];
            for (int i = 0; i < segments; i++)
            {
                tris[i * 3] = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = (i + 1) % segments + 1;
            }

            var mesh = new Mesh { name = "SmoothDisc" };
            mesh.vertices = verts;
            mesh.normals = norms;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
