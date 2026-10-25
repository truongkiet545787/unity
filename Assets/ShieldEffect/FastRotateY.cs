using UnityEngine;

public class FastRotateY : MonoBehaviour
{
    [SerializeField] private float rotateSpeed = 1000f;

    private void Update()
    {
        transform.Rotate(
            0f,
            rotateSpeed * Time.deltaTime,
            0f,
            Space.Self
        );
    }
}