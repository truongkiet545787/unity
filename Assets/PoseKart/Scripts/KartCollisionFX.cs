using UnityEngine;

public class KartCollisionFX : MonoBehaviour
{
    [Header("References")]
    public GameObject sparkPrefab;

    [Header("Settings")]
    public float collisionForceThreshold = 5f; // Lực va chạm tối thiểu để xẹt lửa

    [Tooltip("Độ dốc tối đa. Giá trị từ 0 đến 1. Bằng 0 là nằm ngang tuyệt đối, càng gần 1 càng là va chạm đứng (đáy/bánh xe).")]
    [Range(0f, 1f)]
    public float verticalAngleThreshold = 0.4f;

    void OnCollisionEnter(Collision collision)
    {
        // 1. Kiểm tra lực va chạm xem đủ mạnh chưa
        if (collision.relativeVelocity.magnitude < collisionForceThreshold) return;

        // 2. Duyệt qua các điểm tiếp xúc
        foreach (ContactPoint contact in collision.contacts)
        {
            // Tính độ song song giữa Pháp tuyến va chạm và Trục Đứng (Up) của xe
            // Kết quả = 0: Va chạm bề ngang hoàn toàn (Hông/Trước/Sau)
            // Kết quả gần = 1: Va chạm theo chiều ngang/đứng (Sàn xe/Bánh xe)
            float verticalImpact = Mathf.Abs(Vector3.Dot(contact.normal, transform.up));

            // Chỉ tạo tia lửa nếu va chạm đến từ BỀ NGANG
            if (verticalImpact < verticalAngleThreshold)
            {
                GameObject sparks = Instantiate(sparkPrefab, contact.point, Quaternion.LookRotation(contact.normal));
                Destroy(sparks, 1.5f);

                // Tạo 1 vệt tia lửa cho đợt va chạm này là đủ, thoát vòng lặp
                break;
            }
        }
    }
}