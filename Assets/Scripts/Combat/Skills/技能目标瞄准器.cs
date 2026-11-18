using UnityEngine;

/// <summary>
/// 计算玩家技能从释放点指向目标位置的世界旋转。
/// 本组件只负责方向计算，不处理网络、伤害或目标选择。
/// </summary>
[DisallowMultipleComponent]
public sealed class 技能目标瞄准器 : MonoBehaviour
{
    [SerializeField, Min(0.0001f)] private float 最小方向长度 = 0.0001f;

    public bool TryGetRotation(
        Vector3 releasePosition,
        Vector3 targetPosition,
        out Quaternion rotation)
    {
        Vector3 direction = targetPosition - releasePosition;
        if (direction.sqrMagnitude < 最小方向长度 * 最小方向长度)
        {
            rotation = Quaternion.identity;
            return false;
        }

        rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        return true;
    }
}
