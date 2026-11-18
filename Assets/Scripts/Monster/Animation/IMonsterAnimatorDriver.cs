public interface IMonsterAnimatorDriver
{
    void RebindAnimator();
    void BootstrapState(string state);
    void ResetView();
    void SetLocomotion(MonsterLocomotionMode mode);
    void SetMoveSpeed(float normalizedSpeed);
    void PlayAction(int actionSlot, float actionSpeed);
}
