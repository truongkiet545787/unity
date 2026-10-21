using UnityEngine;

namespace PoseKart.Environment
{
    [RequireComponent(typeof(SkinnedMeshRenderer))]
    [DisallowMultipleComponent]
    public class ProceduralSailWind : MonoBehaviour
    {
        [Header("Wind Settings")]
        [Tooltip("Tốc độ phấp phới")]
        public float windSpeed = 1.8f;

        [Tooltip("Biên độ biến dạng (Khuyên dùng từ 5 - 15 để tránh vỡ mesh)")]
        [Range(0f, 30f)]
        public float maxWeight = 12f;

        [Header("BlendShape Target")]
        [Tooltip("Chỉ định danh sách ID BlendShape dùng để uốn buồm (Để trống sẽ tự lấy 3 cái đầu)")]
        public int[] activeBlendShapeIndices;

        private SkinnedMeshRenderer skinnedMesh;

        void Awake()
        {
            skinnedMesh = GetComponent<SkinnedMeshRenderer>();
            if (skinnedMesh == null || skinnedMesh.sharedMesh == null) return;

            int totalShapes = skinnedMesh.sharedMesh.blendShapeCount;

            // Reset tất cả BlendShape về 0 để dọn dẹp các biến dạng cũ
            for (int i = 0; i < totalShapes; i++)
            {
                skinnedMesh.SetBlendShapeWeight(i, 0f);
            }

            // Nếu người chơi không chọn cụ thể, mặc định chỉ lấy 3 BlendShape đầu tiên
            if (activeBlendShapeIndices == null || activeBlendShapeIndices.Length == 0)
            {
                int count = Mathf.Min(3, totalShapes);
                activeBlendShapeIndices = new int[count];
                for (int i = 0; i < count; i++)
                {
                    activeBlendShapeIndices[i] = i;
                }
            }
        }

        void Update()
        {
            if (skinnedMesh == null || activeBlendShapeIndices == null) return;

            float time = Time.time * windSpeed;

            for (int i = 0; i < activeBlendShapeIndices.Length; i++)
            {
                int shapeIndex = activeBlendShapeIndices[i];
                if (shapeIndex < 0 || shapeIndex >= skinnedMesh.sharedMesh.blendShapeCount) continue;

                // Tạo nhịp sóng mượt mà dạng hình sin (giá trị từ 0 đến maxWeight)
                float wave = (Mathf.Sin(time + (i * 1.5f)) + 1f) * 0.5f;
                skinnedMesh.SetBlendShapeWeight(shapeIndex, wave * maxWeight);
            }
        }
    }
}