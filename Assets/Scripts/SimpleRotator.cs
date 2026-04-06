using UnityEngine;

public class Rotator : MonoBehaviour
{
    public float speed = 100f;
    public Vector3 axis = Vector3.up;
    public bool isRandom = false;

    private float currentAngle = 0f;
    private Vector3 currentRandomAxis = Vector3.up;

    void Start()
    {
        if (isRandom)
        {
            GenerateNewRandomAxis();
        }
    }

    void Update()
    {
        if (isRandom)
        {
            transform.Rotate(currentRandomAxis * speed * Time.deltaTime, Space.Self);

            currentAngle += speed * Time.deltaTime;

            if (currentAngle >= 360f)
            {
                currentAngle = 0f;
                GenerateNewRandomAxis();
            }
        }
        else
        {
            transform.Rotate(axis * speed * Time.deltaTime, Space.Self);
        }
    }

    void GenerateNewRandomAxis()
    {
        currentRandomAxis = new Vector3(
            Random.Range(-1f, 1f),
            Random.Range(-1f, 1f),
            Random.Range(-1f, 1f)
        ).normalized;
    }
}