
using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class PoseKartCurvedRamp : MonoBehaviour
{
    [Header("Ramp Dimensions")]
    [Min(1f)] public float length = 12f;
    [Min(1f)] public float width = 5f;
    [Min(2)] public int segments = 32;

    [Header("Ramp Shape")]
    public float endHeight = 3f;

    [Tooltip("Positive = right side rises. Negative = left side rises.")]
    public float twistHeight = 3f;

    [Range(0.2f, 4f)]
    public float curvature = 1.8f;

    [Header("Collision")]
    public bool generateCollider = true;

    private Mesh generatedMesh;

    private void OnEnable()
    {
        Generate();
    }

    private void OnValidate()
    {
        segments = Mathf.Max(2, segments);
        length = Mathf.Max(1f, length);
        width = Mathf.Max(1f, width);

        // Avoid rebuilding mesh directly inside OnValidate.
        // Use the component context menu instead.
    }

    [ContextMenu("Generate Curved Ramp")]
    public void Generate()
    {
        MeshFilter filter = GetComponent<MeshFilter>();

        if (generatedMesh != null)
        {
            if (Application.isPlaying)
                Destroy(generatedMesh);
            else
                DestroyImmediate(generatedMesh);
        }

        generatedMesh = new Mesh();
        generatedMesh.name = "PoseKart Curved Ramp";

        Vector3[] vertices = new Vector3[(segments + 1) * 2];
        Vector2[] uv = new Vector2[vertices.Length];
        int[] triangles = new int[segments * 6];

        for (int i = 0; i <= segments; i++)
        {
            float t = i / (float)segments;

            float z = t * length;

            float rise = Mathf.Pow(t, curvature) * endHeight;

            float twist = Mathf.SmoothStep(0f, 1f, t)
                          * twistHeight;

            int index = i * 2;

            // Left edge
            vertices[index] = new Vector3(
                -width * 0.5f,
                rise,
                z
            );

            // Right edge
            vertices[index + 1] = new Vector3(
                width * 0.5f,
                rise + twist,
                z
            );

            uv[index] = new Vector2(0f, t);
            uv[index + 1] = new Vector2(1f, t);
        }

        for (int i = 0; i < segments; i++)
        {
            int v = i * 2;
            int tri = i * 6;

            triangles[tri] = v;
            triangles[tri + 1] = v + 2;
            triangles[tri + 2] = v + 1;

            triangles[tri + 3] = v + 1;
            triangles[tri + 4] = v + 2;
            triangles[tri + 5] = v + 3;
        }

        generatedMesh.vertices = vertices;
        generatedMesh.uv = uv;
        generatedMesh.triangles = triangles;

        generatedMesh.RecalculateNormals();
        generatedMesh.RecalculateBounds();

        filter.sharedMesh = generatedMesh;

        if (generateCollider)
        {
            MeshCollider collider =
                GetComponent<MeshCollider>();

            if (collider == null)
                collider = gameObject.AddComponent<MeshCollider>();

            collider.sharedMesh = null;
            collider.sharedMesh = generatedMesh;
        }
        else
        {
            MeshCollider collider = GetComponent<MeshCollider>();

            if (collider != null)
                collider.sharedMesh = null;
        }
    }
}
