
using UnityEngine;

[DisallowMultipleComponent]
public class ParticleSpin : MonoBehaviour
{
    [Header("Rotation Speed (degrees/second)")]
    [SerializeField] private Vector3 speed = new Vector3(0f, 0f, 140f);

    [Header("Optional")]
    [Tooltip("Leave empty to rotate the object containing this script.")]
    [SerializeField] private Transform spinTarget;

    [SerializeField] private Space rotationSpace = Space.Self;

    private void Update()
    {
        Transform target = spinTarget != null ? spinTarget : transform;
        target.Rotate(speed * Time.deltaTime, rotationSpace);
    }
}
