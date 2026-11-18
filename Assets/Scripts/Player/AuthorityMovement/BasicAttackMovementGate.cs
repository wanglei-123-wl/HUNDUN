public sealed class BasicAttackMovementGate
{
    private const float StopDelaySeconds = 0.2f;
    private float stoppedAt = float.NegativeInfinity;
    private bool moving;

    public void Observe(bool isMoving, float now)
    {
        if (isMoving)
        {
            moving = true;
            stoppedAt = float.PositiveInfinity;
        }
        else if (moving)
        {
            moving = false;
            stoppedAt = now;
        }
    }

    public bool CanAttack(float now)
    {
        return !moving && now - stoppedAt >= StopDelaySeconds;
    }

    public void Reset()
    {
        moving = false;
        stoppedAt = float.NegativeInfinity;
    }
}
