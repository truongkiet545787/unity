using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Spin : MonoBehaviour
{
    [SerializeField] private Vector3 angularVelocity = new Vector3();
	
	// Update is called once per frame
	void Update ()
    {
        transform.Rotate(angularVelocity.x * Time.deltaTime, angularVelocity.y * Time.deltaTime, angularVelocity.z * Time.deltaTime);
	}
}
