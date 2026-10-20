using UnityEngine;

public class FloatingCheckpoint : MonoBehaviour
{
    public float rotationSpeed = 50f;
    public float floatingSpeed = 2f;
    public float floatingHeight = 0.25f;

    private Vector3 startPosition;

    void Start()
    {
        startPosition = transform.localPosition;
    }

    void Update()
    {
        transform.Rotate(
            0f,
            rotationSpeed * Time.deltaTime,
            0f
        );

        float newY = Mathf.Sin(Time.time * floatingSpeed) * floatingHeight;

        transform.localPosition =
            startPosition + new Vector3(0f, newY, 0f);
    }
}