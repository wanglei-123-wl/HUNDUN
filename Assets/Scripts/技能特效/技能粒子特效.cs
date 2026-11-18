using UnityEngine;

/// <summary>
/// 播放第三方粒子预制体的技能特效组件。
/// </summary>
[DisallowMultipleComponent]
public sealed class 技能粒子特效 : MonoBehaviour
{
    [SerializeField] private float 播放后销毁延迟 = 3f;
    private ParticleSystem[] 粒子系统;

    private void Awake()
    {
        缓存粒子系统();
        停止播放();
    }

    public void 释放技能()
    {
        缓存粒子系统();
        if (粒子系统 == null || 粒子系统.Length == 0)
        {
            Debug.LogWarning("技能粒子特效没有找到 ParticleSystem。", this);
            return;
        }

        停止播放();
        for (int index = 0; index < 粒子系统.Length; index++)
        {
            ParticleSystem particle = 粒子系统[index];
            if (particle != null)
            {
                particle.Play(true);
            }
        }
    }

    public void 释放到位置(Vector3 position, Quaternion rotation)
    {
        创建并释放到位置(position, rotation);
    }

    public GameObject 创建并释放到位置(Vector3 position, Quaternion rotation)
    {
        GameObject instance = Instantiate(gameObject, position, rotation);
        技能粒子特效 effect = instance.GetComponent<技能粒子特效>();
        if (effect == null)
        {
            Destroy(instance);
            Debug.LogWarning("技能粒子特效实例缺少自身组件。", this);
            return null;
        }

        effect.释放技能();
        if (effect.播放后销毁延迟 > 0f)
        {
            Destroy(instance, effect.播放后销毁延迟);
        }

        return instance;
    }

    private void 停止播放()
    {
        if (粒子系统 == null)
        {
            return;
        }

        for (int index = 0; index < 粒子系统.Length; index++)
        {
            ParticleSystem particle = 粒子系统[index];
            if (particle != null)
            {
                particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
    }

    private void 缓存粒子系统()
    {
        if (粒子系统 == null)
        {
            粒子系统 = GetComponentsInChildren<ParticleSystem>(true);
        }
    }
}
