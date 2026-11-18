using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "玩家公共表现配置", menuName = "游戏/玩家/公共表现配置")]
public sealed class PlayerPresentationSettings : ScriptableObject
{
    public static readonly Vector3 DefaultSkillReleaseOffset = new Vector3(0.055f, 1.009f, 0.256f);

    [SerializeField, InspectorName("施法挂点偏移")]
    private Vector3 skillReleaseOffset = DefaultSkillReleaseOffset;

    [SerializeField]
    private List<PlayerEffectDefinition> 玩家特效列表 = new List<PlayerEffectDefinition>();

    public Vector3 SkillReleaseOffset => skillReleaseOffset;
    public IReadOnlyList<PlayerEffectDefinition> PlayerEffects => 玩家特效列表;
}

[Serializable]
public sealed class PlayerEffectDefinition
{
    [SerializeField, Tooltip("同一份配置中的名称需要唯一。")]
    private string 特效名称;

    [SerializeField]
    private GameObject 粒子预制体;

    [SerializeField]
    private PlayerEffectAnchor 挂载位置;

    [SerializeField]
    private Vector3 位置偏移;

    [SerializeField]
    private PlayerEffectTrigger 触发方式;

    [SerializeField]
    private PlayerEffectPlayback 播放方式;

    public string EffectName => 特效名称;
    public GameObject Prefab => 粒子预制体;
    public PlayerEffectAnchor Anchor => 挂载位置;
    public Vector3 LocalOffset => 位置偏移;
    public PlayerEffectTrigger Trigger => 触发方式;
    public PlayerEffectPlayback Playback => 播放方式;
}

public enum PlayerEffectAnchor
{
    [InspectorName("跟随玩家")] Player,
    [InspectorName("施法挂点")] SkillRelease
}

public enum PlayerEffectTrigger
{
    [InspectorName("出场播放")] OnSpawn,
    [InspectorName("按名称调用")] OnDemand
}

public enum PlayerEffectPlayback
{
    [InspectorName("播放一次")] Once,
    [InspectorName("循环播放")] Loop
}
