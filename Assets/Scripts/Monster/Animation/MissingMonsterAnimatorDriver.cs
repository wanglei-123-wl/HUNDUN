using UnityEngine;

public sealed class MissingMonsterAnimatorDriver : MonoBehaviour, IMonsterAnimatorDriver
{
    public void RebindAnimator() { }
    public void BootstrapState(string state) { }
    public void ResetView() { }
    public void SetLocomotion(MonsterLocomotionMode mode) { }
    public void SetMoveSpeed(float normalizedSpeed) { }
    public void PlayAction(int actionSlot, float actionSpeed) { }
}
