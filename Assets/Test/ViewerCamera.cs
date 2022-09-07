using System.Collections;
using System.Collections.Generic;
using Rhinox.Lightspeed;
using UnityEngine;

public class ViewerCamera : MonoBehaviour
{
    public Transform Target;

    public float Distance = 20.0f;

    public float ZoomStep = 1.0f;

    public Vector2 MinMaxZoom = new Vector2(.4f, 5);

    [SerializeField] private float _sensitivity = 0.5f;

    private Vector3 prevMousePos;
    private Camera _camera;
    private float _currentDistance;

    private void Awake()
    {
        _camera = Camera.main;
        UpdateDistance(Distance);
    }

    private void Update()
    {
        if (Input.GetMouseButtonDown(0))
            prevMousePos = Input.mousePosition;
        else if (Input.GetMouseButton(0))
        {
            Vector3 mousePos = Input.mousePosition;
            Vector2 deltaPos = (mousePos - prevMousePos) * _sensitivity;

            Vector3 rot = transform.localEulerAngles;
            while (rot.x > 180f)
                rot.x -= 360f;
            while (rot.x < -180f)
                rot.x += 360f;

            rot.x = Mathf.Clamp(rot.x - deltaPos.y, -89.8f, 89.8f);
            rot.y += deltaPos.x;
            rot.z = 0f;

            transform.localEulerAngles = rot;
            prevMousePos = mousePos;
        }

        float axis = Input.mouseScrollDelta.y;
        if (axis < 0.0f)
            ZoomOut();
        else if (axis > 0.0f)
            ZoomIn();
    }

    // Reduce the distance from the camera to the target and
    // position of the camera (with the Rotate function).
    private void ZoomIn()
    {
        UpdateDistance(Mathf.Max(MinMaxZoom.x, _currentDistance - ZoomStep));
    }

    // Increase the distance from the camera to the target and
    // update the position of the camera (with the Rotate function).
    private void ZoomOut()
    {
        UpdateDistance(Mathf.Min(MinMaxZoom.y, _currentDistance + ZoomStep));
    }
    
    private void UpdateDistance(float distance)
    {
        var offset = (_camera.transform.position - Target.position).normalized;
        if (offset.LossyEquals(Vector3.zero))
            offset = Vector3.back;
        _camera.transform.position = Target.position + (distance * offset);
        _currentDistance = distance;

    }
}