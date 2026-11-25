using System.Collections.Generic;
using UnityEngine;

namespace RagnarockEditor.VR
{
    // Coordinates match the stock cylinder: radius 0.5, top/bottom +1/-1.
    internal static class DrumShell
    {
        private static Mesh _mesh;

        public static Mesh SharedMesh
        {
            get
            {
                if (_mesh == null) _mesh = Build();
                return _mesh;
            }
        }

        private static Mesh Build()
        {
            const int segments = 16;
            var vertices = new List<Vector3>();
            var wood = new List<int>();
            var hoops = new List<int>();
            var lacing = new List<int>();

            Vector3 Point(int i, float radius, float height)
            {
                float angle = i * Mathf.PI * 2f / segments;
                return new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius);
            }

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, List<int> faces)
            {
                int n = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                faces.Add(n); faces.Add(n+1); faces.Add(n+2);
                faces.Add(n); faces.Add(n+2); faces.Add(n+3);
            }

            void Band(float y0, float r0, float y1, float r1, List<int> faces)
            {
                for (int i = 0; i < segments; i++)
                    Quad(Point(i,r0,y0), Point(i,r1,y1), Point(i+1,r1,y1), Point(i+1,r0,y0), faces);
            }

            Band(-1,.39f,-.78f,.42f,wood);
            Band(-.78f,.42f,.72f,.49f,wood);
            Band(.72f,.49f,1,.49f,wood);
            for (int i = 0; i < segments; i++)
            {
                int n = vertices.Count;
                vertices.Add(Point(i,.39f,-1)); vertices.Add(Point(i+1,.39f,-1)); vertices.Add(Vector3.down);
                wood.Add(n); wood.Add(n+1); wood.Add(n+2);
            }
            Band(.72f,.49f,.76f,.50f,hoops);
            Band(.76f,.50f,.99f,.50f,hoops);
            Band(.99f,.50f,1,.48f,hoops);
            Band(-.90f,.41f,-.86f,.43f,hoops);
            Band(-.86f,.43f,-.72f,.433f,hoops);
            Band(-.72f,.433f,-.70f,.424f,hoops);

            for (int i = 0; i < segments; i++)
            {
                bool high = i % 2 == 0;
                var a = Point(i, high ? .498f : .433f, high ? .76f : -.74f);
                var b = Point(i+1, high ? .433f : .498f, high ? -.74f : .76f);
                // Bow the thong out at midspan so the wood facets remain behind it.
                var mid = (a+b)*.5f;
                var outward = new Vector3(mid.x,0,mid.z).normalized;
                mid += outward*.014f;
                var width = Vector3.Cross(b-a,outward).normalized*.009f;
                Quad(a-width,a+width,mid+width,mid-width,lacing);
                Quad(mid-width,mid+width,b+width,b-width,lacing);
            }
            var mesh = new Mesh {name="Laced drum shell", subMeshCount=3};
            mesh.SetVertices(vertices);
            mesh.SetTriangles(wood,0); mesh.SetTriangles(hoops,1); mesh.SetTriangles(lacing,2);
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
