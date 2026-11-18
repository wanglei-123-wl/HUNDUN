using System;
using System.Collections.Generic;
using UnityEngine;

public enum 技能释放方式
{
    玩家释放节点 = 0,
    怪物目标位置 = 1
}

[Serializable]
public sealed class 技能详情条目
{
    public string 名称 = string.Empty;
    public string 数值 = string.Empty;

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(名称)
        || !string.IsNullOrWhiteSpace(数值);
}

[Serializable]
public sealed class 技能释放配置
{
    [Tooltip("发给服务端的唯一技能 ID。")]
    public string 技能ID = string.Empty;

    [Tooltip("仅用于 Inspector 识别。")]
    public string 技能名称 = string.Empty;

    [Tooltip("决定特效从玩家释放节点还是怪物位置生成。")]
    public 技能释放方式 释放方式 = 技能释放方式.玩家释放节点;

    [Tooltip("需要目标时填写 monster。")]
    public string 目标类型 = string.Empty;

    [Tooltip("该技能是否必须选中目标。")]
    public bool 需要目标;

    [Tooltip("该技能成功后播放的特效预制体。")]
    public GameObject 特效预制体;
    [Tooltip("本地玩家收到施法开始确认时触发的 Animator Trigger。")]
    public string 本地动画触发器 = string.Empty;

    [Tooltip("旁观者看到远端施法时触发的 Animator Trigger；留空则只播放特效。")]
    public string 远端动画触发器 = string.Empty;

    [Min(0.1f)]
    [Tooltip("旁观者特效的视觉寿命，按服务端发布的技能表现配置调整。")]
    public float 远端特效时长秒数 = 5f;

    [Header("详情展示（可选）")]
    [Tooltip("技能详情窗口使用的图标。")]
    public Sprite 图标;

    [TextArea(3, 8)]
    [Tooltip("技能详情窗口使用的介绍文本。")]
    public string 介绍 = string.Empty;

    [TextArea(3, 8)]
    [Tooltip("技能详情窗口使用的详细说明。")]
    public string 详情 = string.Empty;

    [Tooltip("技能详情窗口显示的属性条目。")]
    public List<技能详情条目> 详情条目 =
        new List<技能详情条目>();

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(技能ID)
        && (!需要目标 || !string.IsNullOrWhiteSpace(目标类型));
}
