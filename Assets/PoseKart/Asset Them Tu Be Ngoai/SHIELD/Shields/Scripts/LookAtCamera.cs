using System.Collections;
using System.Collections.Generic;
using UnityEngine;
public class LookAtCamera : MonoBehaviour
{
    Camera _cam;
    private void Start()
    {
        _cam = Camera.main;
    }
    void Update()
    {
        if (_cam == null)
            _cam = Camera.main;
        if (_cam == null)
            return;

        transform.forward = _cam.transform.position - transform.position;
    }
}
