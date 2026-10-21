using UnityEngine;
using UnityEngine.Rendering;

namespace PoseKart.Vehicle
{
    /// <summary>
    /// Skidmarks controller. Generates dynamic procedural mesh quad-strips for tire tracks conforming to ground contours.
    /// Based on Nition's skidmarks algorithm with arcade enhancements for high speeds, singleton auto-discovery,
    /// and URP material auto-resolution.
    /// </summary>
    [DisallowMultipleComponent]
    public class Skidmarks : MonoBehaviour
    {
        public static Skidmarks Instance { get; private set; }

        [Header("Material & Settings")]
        [Tooltip("Material used for rendering skidmarks. If null, automatically falls back to project Tire/Skidmark material.")]
        [SerializeField] private Material skidmarksMaterial;

        [Header("Buffer & Dimensions")]
        [SerializeField, Min(256)] private int maxMarks = 2048;
        [SerializeField, Min(0.05f)] private float markWidth = 0.35f;
        [SerializeField, Min(0.005f)] private float groundOffset = 0.02f;
        [SerializeField, Min(0.05f)] private float minDistance = 0.22f;
        [SerializeField, Range(0.1f, 1f)] private float maxOpacity = 1.0f;
        [Tooltip("Maximum distance in meters between consecutive frames to keep connecting the strip at high racing speeds.")]
        [SerializeField, Min(1f)] private float maxConnectDistance = 6.0f;

        private class MarkSection
        {
            public Vector3 Pos = Vector3.zero;
            public Vector3 Normal = Vector3.zero;
            public Vector4 Tangent = Vector4.zero;
            public Vector3 Posl = Vector3.zero;
            public Vector3 Posr = Vector3.zero;
            public Color32 Colour;
            public int LastIndex;
        }

        private int markIndex;
        private MarkSection[] skidmarks;
        private Mesh marksMesh;
        private MeshRenderer mr;
        private MeshFilter mf;

        private Vector3[] vertices;
        private Vector3[] normals;
        private Vector4[] tangents;
        private Color32[] colors;
        private Vector2[] uvs;
        private int[] triangles;

        private bool meshUpdated;
        private bool haveSetBounds;
        private Color32 black = Color.black;

        private float minSqrDistance;
        private float maxSqrConnectDistance;

        public static Skidmarks GetOrCreateInstance()
        {
            if (Instance != null) return Instance;

            Skidmarks found = FindFirstObjectByType<Skidmarks>();
            if (found != null)
            {
                Instance = found;
                return Instance;
            }

            GameObject go = new GameObject("Skidmarks_Manager");
            go.transform.position = Vector3.zero;
            go.transform.rotation = Quaternion.identity;
            Instance = go.AddComponent<Skidmarks>();
            return Instance;
        }

        protected void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }

            if (transform.position != Vector3.zero)
            {
                transform.position = Vector3.zero;
                transform.rotation = Quaternion.identity;
            }

            minSqrDistance = minDistance * minDistance;
            maxSqrConnectDistance = maxConnectDistance * maxConnectDistance;
        }

        protected void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        protected void Start()
        {
            minSqrDistance = minDistance * minDistance;
            maxSqrConnectDistance = maxConnectDistance * maxConnectDistance;

            skidmarks = new MarkSection[maxMarks];
            for (int i = 0; i < maxMarks; i++)
            {
                skidmarks[i] = new MarkSection();
            }

            mf = GetComponent<MeshFilter>();
            if (mf == null) mf = gameObject.AddComponent<MeshFilter>();

            mr = GetComponent<MeshRenderer>();
            if (mr == null) mr = gameObject.AddComponent<MeshRenderer>();

            marksMesh = new Mesh();
            marksMesh.name = "Skidmarks_Mesh";
            marksMesh.MarkDynamic();
            mf.sharedMesh = marksMesh;

            vertices = new Vector3[maxMarks * 4];
            normals = new Vector3[maxMarks * 4];
            tangents = new Vector4[maxMarks * 4];
            colors = new Color32[maxMarks * 4];
            uvs = new Vector2[maxMarks * 4];
            triangles = new int[maxMarks * 6];

            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;

            // Auto-resolve material if unassigned
            if (skidmarksMaterial == null)
            {
                skidmarksMaterial = ResolveDefaultMaterial();
            }

            mr.material = skidmarksMaterial;
        }

        private Material ResolveDefaultMaterial()
        {
#if UNITY_EDITOR
            string[] guids = UnityEditor.AssetDatabase.FindAssets("SkidmarksMaterial t:Material");
            if (guids.Length > 0)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                Material mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null) return mat;
            }

            guids = UnityEditor.AssetDatabase.FindAssets("Tire t:Material");
            if (guids.Length > 0)
            {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                Material mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null) return mat;
            }
#endif
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                Material fallbackMat = new Material(shader);
                fallbackMat.color = new Color(0.12f, 0.12f, 0.12f, 0.85f);
                return fallbackMat;
            }

            return null;
        }

        protected void LateUpdate()
        {
            if (!meshUpdated) return;
            meshUpdated = false;

            marksMesh.vertices = vertices;
            marksMesh.normals = normals;
            marksMesh.tangents = tangents;
            marksMesh.triangles = triangles;
            marksMesh.colors32 = colors;
            marksMesh.uv = uvs;

            if (!haveSetBounds)
            {
                marksMesh.bounds = new Bounds(Vector3.zero, new Vector3(10000f, 10000f, 10000f));
                haveSetBounds = true;
            }

            mf.sharedMesh = marksMesh;
        }

        /// <summary>
        /// Adds a skidmark section with opacity and default black color.
        /// </summary>
        public int AddSkidMark(Vector3 pos, Vector3 normal, float opacity, int lastIndex)
        {
            return AddSkidMark(pos, normal, opacity, lastIndex, markWidth);
        }

        /// <summary>
        /// Adds a skidmark section with opacity, custom width and default black color.
        /// </summary>
        public int AddSkidMark(Vector3 pos, Vector3 normal, float opacity, int lastIndex, float customWidth)
        {
            if (opacity > 1f) opacity = 1.0f;
            else if (opacity <= 0.001f) return -1;

            black.a = (byte)(opacity * 255f);
            return AddSkidMark(pos, normal, black, lastIndex, customWidth);
        }

        /// <summary>
        /// Adds a skidmark section with custom color.
        /// </summary>
        public int AddSkidMark(Vector3 pos, Vector3 normal, Color32 colour, int lastIndex)
        {
            return AddSkidMark(pos, normal, colour, lastIndex, markWidth);
        }

        /// <summary>
        /// Adds a skidmark section with custom color and custom width.
        /// </summary>
        public int AddSkidMark(Vector3 pos, Vector3 normal, Color32 colour, int lastIndex, float customWidth)
        {
            if (colour.a == 0) return -1;

            float width = customWidth > 0.01f ? customWidth : markWidth;
            MarkSection lastSection = null;
            Vector3 distAndDirection = Vector3.zero;
            Vector3 newPos = pos + normal * groundOffset;

            if (lastIndex != -1 && lastIndex < maxMarks)
            {
                lastSection = skidmarks[lastIndex];
                distAndDirection = newPos - lastSection.Pos;
                float sqrDist = distAndDirection.sqrMagnitude;

                if (sqrDist < minSqrDistance)
                {
                    return lastIndex;
                }

                // If distance is excessively far (teleport, respawn or circular buffer wrap-around), disconnect segment
                if (sqrDist > maxSqrConnectDistance)
                {
                    lastIndex = -1;
                    lastSection = null;
                }
            }

            colour.a = (byte)(colour.a * maxOpacity);

            MarkSection curSection = skidmarks[markIndex];
            curSection.Pos = newPos;
            curSection.Normal = normal;
            curSection.Colour = colour;
            curSection.LastIndex = lastIndex;

            if (lastSection != null)
            {
                Vector3 xDirection = Vector3.Cross(distAndDirection, normal).normalized;
                curSection.Posl = curSection.Pos + xDirection * width * 0.5f;
                curSection.Posr = curSection.Pos - xDirection * width * 0.5f;
                curSection.Tangent = new Vector4(xDirection.x, xDirection.y, xDirection.z, 1f);

                if (lastSection.LastIndex == -1)
                {
                    lastSection.Tangent = curSection.Tangent;
                    lastSection.Posl = curSection.Pos + xDirection * width * 0.5f;
                    lastSection.Posr = curSection.Pos - xDirection * width * 0.5f;
                }
            }

            UpdateSkidmarksMesh();

            int curIndex = markIndex;
            if (lastIndex == -1)
            {
                markIndex = (markIndex - 1 + maxMarks) % maxMarks;
            }
            else
            {
                markIndex = ++markIndex % maxMarks;
            }

            return curIndex;
        }

        private void UpdateSkidmarksMesh()
        {
            MarkSection curr = skidmarks[markIndex];
            if (curr.LastIndex == -1) return;

            MarkSection last = skidmarks[curr.LastIndex];
            vertices[markIndex * 4 + 0] = last.Posl;
            vertices[markIndex * 4 + 1] = last.Posr;
            vertices[markIndex * 4 + 2] = curr.Posl;
            vertices[markIndex * 4 + 3] = curr.Posr;

            normals[markIndex * 4 + 0] = last.Normal;
            normals[markIndex * 4 + 1] = last.Normal;
            normals[markIndex * 4 + 2] = curr.Normal;
            normals[markIndex * 4 + 3] = curr.Normal;

            tangents[markIndex * 4 + 0] = last.Tangent;
            tangents[markIndex * 4 + 1] = last.Tangent;
            tangents[markIndex * 4 + 2] = curr.Tangent;
            tangents[markIndex * 4 + 3] = curr.Tangent;

            colors[markIndex * 4 + 0] = last.Colour;
            colors[markIndex * 4 + 1] = last.Colour;
            colors[markIndex * 4 + 2] = curr.Colour;
            colors[markIndex * 4 + 3] = curr.Colour;

            uvs[markIndex * 4 + 0] = new Vector2(0f, 0f);
            uvs[markIndex * 4 + 1] = new Vector2(1f, 0f);
            uvs[markIndex * 4 + 2] = new Vector2(0f, 1f);
            uvs[markIndex * 4 + 3] = new Vector2(1f, 1f);

            triangles[markIndex * 6 + 0] = markIndex * 4 + 0;
            triangles[markIndex * 6 + 2] = markIndex * 4 + 1;
            triangles[markIndex * 6 + 1] = markIndex * 4 + 2;

            triangles[markIndex * 6 + 3] = markIndex * 4 + 2;
            triangles[markIndex * 6 + 5] = markIndex * 4 + 1;
            triangles[markIndex * 6 + 4] = markIndex * 4 + 3;

            meshUpdated = true;
        }

        /// <summary>
        /// Clears all existing skidmarks from the mesh.
        /// </summary>
        public void Clear()
        {
            if (skidmarks != null)
            {
                for (int i = 0; i < skidmarks.Length; i++)
                {
                    if (skidmarks[i] != null)
                    {
                        skidmarks[i].LastIndex = -1;
                    }
                }
            }

            markIndex = 0;
            if (marksMesh != null)
            {
                marksMesh.Clear();
            }
            meshUpdated = false;
            haveSetBounds = false;
        }
    }
}
