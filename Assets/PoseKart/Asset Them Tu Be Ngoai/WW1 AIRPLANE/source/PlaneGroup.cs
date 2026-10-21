using UnityEngine;

public class MoveOnZ : MonoBehaviour
{
    public float speed = 5f; // Vitesse de déplacement

    void Update()
    {
        // Déplace l'objet vers l'avant (axe Z) par seconde
        transform.Translate(Vector3.forward * speed * Time.deltaTime);
    }
}