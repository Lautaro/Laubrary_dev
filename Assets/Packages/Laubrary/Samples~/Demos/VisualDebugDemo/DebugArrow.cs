using Sirenix.OdinInspector;
using UnityEngine;


namespace Laubrary.VisualDebug.Samples
    {
    public class DebugArrow : MonoBehaviour
    {
        public Transform startPoint;
        public Transform endPoint;
        public float cylinderRadius = 0.1f;
        public float coneHeight = 0.5f;
        public float coneRadius = 0.2f;
        public float padding = 0.1f;
        public int segments = 16;

        [Button]
        void Arrow()
        {
            if (startPoint != null && endPoint != null)
            {
                CreateArrow(startPoint.position, endPoint.position, padding);
            }
            else
            {
                Debug.LogError("StartPoint and EndPoint transforms must be assigned.");
            }
        }

        void CreateArrow(Vector3 start, Vector3 end, float padding)
        {
            GameObject arrow = new GameObject("DebugArrow");

            // Calculate direction and length
            Vector3 direction = (end - start).normalized;
            float totalLength = Vector3.Distance(start, end) - padding;

            // Calculate positions
            Vector3 cylinderEnd = start + direction * (totalLength - coneHeight);
            Vector3 conePosition = start + direction * (totalLength - coneHeight / 2);

            // Create Cylinder (Tube)
            GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.transform.SetParent(arrow.transform);
            cylinder.transform.position = (start + cylinderEnd) / 2;
            cylinder.transform.up = direction;
            cylinder.transform.localScale = new Vector3(cylinderRadius, (totalLength - coneHeight) / 2, cylinderRadius);

            // Create Cone
            GameObject cone = new GameObject("Cone");
            cone.transform.SetParent(arrow.transform);
            MeshFilter coneMeshFilter = cone.AddComponent<MeshFilter>();
            coneMeshFilter.mesh = CreateConeMesh(coneRadius, coneHeight, segments);
            MeshRenderer coneRenderer = cone.AddComponent<MeshRenderer>();
            coneRenderer.material = new Material(Shader.Find("Standard"));
            cone.transform.position = conePosition;
            cone.transform.up = direction;
        }

        Mesh CreateConeMesh(float radius, float height, int segments)
        {
            Mesh mesh = new Mesh();

            Vector3[] vertices = new Vector3[segments + 2];
            int[] triangles = new int[segments * 6];

            vertices[0] = new Vector3(0, height, 0); // Tip of the cone
            for (int i = 0; i < segments; i++)
            {
                float angle = (2 * Mathf.PI / segments) * i;
                float x = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;
                vertices[i + 1] = new Vector3(x, 0, z);
            }
            vertices[segments + 1] = new Vector3(0, 0, 0); // Base center

            for (int i = 0; i < segments; i++)
            {
                // Side triangles
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = i + 1;
                triangles[i * 3 + 2] = (i + 1) % segments + 1;

                // Base triangles
                triangles[segments * 3 + i * 3] = segments + 1;
                triangles[segments * 3 + i * 3 + 1] = (i + 1) % segments + 1;
                triangles[segments * 3 + i * 3 + 2] = i + 1;
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
          
            return mesh;
        }
    }

}