using System;
using System.Collections.Generic;
using UnityEngine;

namespace PoseKart.Vehicle
{
    /// <summary>
    /// Replaces the overlapping road-piece colliders with one continuous, flat strip.
    /// The generated road meshes remain visible; only their collision representation changes.
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class SeamlessRoadSurface : MonoBehaviour
    {
        private const string RoadRootPath = "Environment/Road";
        private const string RuntimeObjectName = "RoadCollisionSurface_Runtime";

        private Mesh generatedMesh;

        public static void EnsureBuilt()
        {
            GameObject roadRootObject = GameObject.Find(RoadRootPath);
            if (roadRootObject == null || roadRootObject.transform.Find(RuntimeObjectName) != null)
            {
                return;
            }

            List<Transform> joints = new List<Transform>();
            foreach (Transform child in roadRootObject.transform)
            {
                if (child.name.StartsWith("RoadJoint_", StringComparison.Ordinal))
                {
                    joints.Add(child);
                }
            }

            if (joints.Count < 3)
            {
                return;
            }

            joints.Sort((left, right) => string.CompareOrdinal(left.name, right.name));

            GameObject surfaceObject = new GameObject(RuntimeObjectName)
            {
                layer = roadRootObject.layer,
                hideFlags = HideFlags.DontSave
            };
            surfaceObject.transform.SetParent(roadRootObject.transform, false);
            SeamlessRoadSurface surface = surfaceObject.AddComponent<SeamlessRoadSurface>();
            surface.Build(joints);

            // The old BoxColliders have vertical end/side faces inside the driving line.
            // Keep their renderers, but let only the continuous mesh handle road contact.
            foreach (Transform child in roadRootObject.transform)
            {
                if (!child.name.StartsWith("Road_", StringComparison.Ordinal) &&
                    !child.name.StartsWith("RoadJoint_", StringComparison.Ordinal))
                {
                    continue;
                }

                Collider pieceCollider = child.GetComponent<Collider>();
                if (pieceCollider != null)
                {
                    pieceCollider.enabled = false;
                }
            }
        }

        private void Build(IReadOnlyList<Transform> joints)
        {
            int nodeCount = joints.Count;
            Vector3[] vertices = new Vector3[nodeCount * 2];
            int[] triangles = new int[nodeCount * 6];

            for (int index = 0; index < nodeCount; index++)
            {
                Transform previous = joints[(index - 1 + nodeCount) % nodeCount];
                Transform current = joints[index];
                Transform next = joints[(index + 1) % nodeCount];

                Vector3 previousDirection = (current.localPosition - previous.localPosition).normalized;
                Vector3 nextDirection = (next.localPosition - current.localPosition).normalized;
                Vector3 previousRight = Vector3.Cross(Vector3.up, previousDirection).normalized;
                Vector3 nextRight = Vector3.Cross(Vector3.up, nextDirection).normalized;
                Vector3 miter = (previousRight + nextRight).normalized;
                if (miter.sqrMagnitude < 0.5f)
                {
                    miter = nextRight;
                }

                float halfWidth = current.localScale.x * 0.5f;
                float denominator = Mathf.Max(0.5f, Mathf.Abs(Vector3.Dot(miter, nextRight)));
                float miterExtent = Mathf.Min(halfWidth / denominator, halfWidth * 2f);
                float surfaceY = current.localPosition.y + current.localScale.y * 0.5f;
                Vector3 center = new Vector3(current.localPosition.x, surfaceY, current.localPosition.z);

                vertices[index * 2] = center - miter * miterExtent;
                vertices[index * 2 + 1] = center + miter * miterExtent;
            }

            for (int index = 0; index < nodeCount; index++)
            {
                int nextIndex = (index + 1) % nodeCount;
                int left = index * 2;
                int right = left + 1;
                int nextLeft = nextIndex * 2;
                int nextRight = nextLeft + 1;
                int triangle = index * 6;

                // Clockwise from below, so the collision face normal points upward.
                triangles[triangle] = left;
                triangles[triangle + 1] = nextLeft;
                triangles[triangle + 2] = right;
                triangles[triangle + 3] = right;
                triangles[triangle + 4] = nextLeft;
                triangles[triangle + 5] = nextRight;
            }

            generatedMesh = new Mesh { name = "Seamless Road Collision Mesh" };
            generatedMesh.vertices = vertices;
            generatedMesh.triangles = triangles;
            generatedMesh.RecalculateBounds();

            MeshCollider meshCollider = gameObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = generatedMesh;
        }

        private void OnDestroy()
        {
            if (generatedMesh != null)
            {
                Destroy(generatedMesh);
            }
        }
    }
}
