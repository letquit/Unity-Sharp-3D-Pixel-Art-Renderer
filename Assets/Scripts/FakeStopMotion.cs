using System;
using UnityEngine;

public class FakeStopMotion : MonoBehaviour
{
    public Animator animator;
    public int FPS = 8;

    private float _time;

    public Vector3 velocity;
    private bool _grabVelocity;
    
    private void OnValidate()
    {
        if (!animator) animator = GetComponent<Animator>();
    }

    private void Update()
    {
        if (_grabVelocity)
        {
            velocity = animator.velocity / animator.speed;
            _grabVelocity = false;
        }
        
        _time += Time.deltaTime;
        var updateTime = 1f / FPS;
        animator.speed = 0;

        if (_time > updateTime)
        {
            _time -= updateTime;
            animator.speed = updateTime / Time.deltaTime;
            _grabVelocity = true;
        }
    }
}