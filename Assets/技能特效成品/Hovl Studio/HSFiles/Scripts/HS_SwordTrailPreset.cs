using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace Hovl
{
    [CreateAssetMenu(
        fileName = "HS_SwordTrailPreset",
        menuName = "Hovl/刀光拖尾预设")]
    public class HS_SwordTrailPreset : ScriptableObject
    {
        public enum AutomaticAxis
        {
            [InspectorName("最长轴")]
            Longest = 0,
            [InspectorName("本地 X 轴")]
            LocalX = 1,
            [InspectorName("本地 Y 轴")]
            LocalY = 2,
            [InspectorName("本地 Z 轴")]
            LocalZ = 3
        }

        [Serializable]
        public class MaterialLayer
        {
            public Material material;
            public int sortingOrder;

            [Tooltip("停止生成拖尾后，用来做溶解的着色器数值属性，例如 _Dissolve。")]
            public string dissolvePropertyName = "_Dissolve";

            [Tooltip("停止生成拖尾后，在持续时间内渐变到的溶解值。最小变化范围由材质的初始值决定，填 0 时沿用材质的溶解值。")]
            [Range(0f, 1f)] public float maximumDissolve;
        }

        [Header("拖尾")]
        [Min(0.01f)] public float trailLifetime = 0.35f;
        [Min(0f)] public float minimumSectionDistance = 0.015f;
        [Min(0f)] public float sampleInterval;
        [Tooltip("增加拖尾横向的网格细分。组件启用自定义中间点后，此数值也决定中间点数量。")]
        [Range(0, 10)] public int linesAlongTrail = 2;

        [Tooltip("开启后，各段拖尾会按淡出曲线改变透明度。")]
        public bool fadeAlphaOverLifetime = true;

        public AnimationCurve alphaOverLifetime =
            new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));

        public bool startActive;
        public bool clearPreviousTrailOnStart = true;

        [Header("移动端优化")]
        [Tooltip("降低移动端开销。限制采样、网格刷新和溶解更新频率，减少细分和平滑段，关闭阴影。")]
        public bool optimizeForMobile;

        [Header("低帧率平滑")]
        [Tooltip("帧率较低时，在采样点之间补出曲线，让拖尾过渡更平滑。")]
        public bool smoothLowFps = true;

        [Tooltip("平滑曲线上相邻网格段的大致最大距离。值越小，补出的轨迹越平滑。")]
        [Min(0.001f)] public float maximumSmoothedSectionDistance = 0.08f;

        [Tooltip("相邻两次采样之间最多补出的网格段数。")]
        [Range(0, 32)] public int maxIntermediateSectionsPerFrame = 8;

        [Header("拖尾端点")]
        public AutomaticAxis automaticAxis = AutomaticAxis.Longest;

        public bool recalculatePointsOnAwake = true;
        [Min(0f)] public float pointAInset;
        [Min(0f)] public float pointBInset;
        public float endpointPadding;

        [Header("材质层")]
        [Tooltip("每个材质用单独的网格渲染层绘制。")]
        public List<MaterialLayer> materialLayers = new List<MaterialLayer>
        {
            new MaterialLayer()
        };

        [Header("渲染")]
        public bool receiveShadows;
        public ShadowCastingMode shadowCastingMode = ShadowCastingMode.Off;

        [Header("刀尖附加特效")]
        [Tooltip("应用预设时，会把列表里的特效生成在 A 点下方。")]
        [FormerlySerializedAs("pointAEffectPrefabs")]
        public List<GameObject> trailTopEffectPrefabs = new List<GameObject>();

        private void OnValidate()
        {
            trailLifetime = Mathf.Max(0.01f, trailLifetime);
            minimumSectionDistance = Mathf.Max(0f, minimumSectionDistance);
            sampleInterval = Mathf.Max(0f, sampleInterval);
            linesAlongTrail = Mathf.Clamp(linesAlongTrail, 0, 10);
            maximumSmoothedSectionDistance =
                Mathf.Max(0.001f, maximumSmoothedSectionDistance);
            maxIntermediateSectionsPerFrame =
                Mathf.Clamp(maxIntermediateSectionsPerFrame, 0, 32);
            pointAInset = Mathf.Max(0f, pointAInset);
            pointBInset = Mathf.Max(0f, pointBInset);

            if (materialLayers == null)
            {
                materialLayers = new List<MaterialLayer>();
            }

            for (int i = 0; i < materialLayers.Count; i++)
            {
                MaterialLayer layer = materialLayers[i];
                if (layer != null)
                {
                    layer.maximumDissolve =
                        Mathf.Clamp01(layer.maximumDissolve);
                }
            }

            if (trailTopEffectPrefabs == null)
            {
                trailTopEffectPrefabs = new List<GameObject>();
            }
        }
    }
}