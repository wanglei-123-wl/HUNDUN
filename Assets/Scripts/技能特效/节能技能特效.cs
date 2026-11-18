using System.Collections;
using UnityEngine;

/// <summary>
/// 独立的灵气技能特效脚本。
/// 本脚本只负责该技能特效自身，不处理角色控制、伤害结算或网络逻辑。
/// </summary>
public class 节能技能特效 : MonoBehaviour
{
    [Header("凝聚设置")]
    [SerializeField] private float 凝聚时间 = 0.8f;

    [Header("飞行设置")]
    [SerializeField] private float 飞行速度 = 10f;
    [SerializeField] private float 最大飞行时间 = 3f;
    [SerializeField] private float 碰撞半径 = 0.35f;

    [Header("外观设置")]
    [SerializeField] private Color 灵气颜色 = new Color(0.03f, 0.12f, 0.45f, 1f);
    [SerializeField] private float 初始半径 = 0.05f;
    [SerializeField] private float 最大半径 = 0.35f;
    [SerializeField] private float 播放后销毁延迟 = 5f;

    private bool 正在释放;
    private Transform 灵气根节点;
    private GameObject 灵气核心;
    private GameObject 灵气光环;
    private Material 灵气材质;
    private Coroutine 凝聚协程;

    /// <summary>
    /// 由外部调用，开始一次技能特效播放。
    /// 具体的凝聚、飞行和碰撞表现将在后续阶段实现。
    /// </summary>
    public void 释放技能()
    {
        if (正在释放)
        {
            return;
        }

        正在释放 = true;
        凝聚协程 = StartCoroutine(播放凝聚效果());
    }

    public void 释放到节点(Transform releaseNode)
    {
        if (releaseNode == null)
        {
            Debug.LogError(
                "节能技能特效缺少指定的玩家释放节点，已取消播放。", this);
            return;
        }

        释放到位置(releaseNode.position, releaseNode.rotation);
    }

    public void 释放到位置(Vector3 position, Quaternion rotation)
    {
        创建并释放到位置(position, rotation);
    }

    public GameObject 创建并释放到位置(Vector3 position, Quaternion rotation)
    {
        GameObject instance = Instantiate(
            gameObject, position, rotation);
        节能技能特效 effect = instance.GetComponent<节能技能特效>();
        if (effect == null)
        {
            Destroy(instance);
            Debug.LogWarning("节能技能特效实例缺少自身组件。", this);
            return null;
        }

        effect.释放技能();
        if (effect.播放后销毁延迟 > 0f)
        {
            Destroy(instance, effect.播放后销毁延迟);
        }

        return instance;
    }

    private IEnumerator 播放凝聚效果()
    {
        清理运行时特效();
        创建运行时特效();

        float elapsed = 0f;
        while (elapsed < 凝聚时间)
        {
            elapsed += Time.deltaTime;
            float progress = Mathf.Clamp01(elapsed / Mathf.Max(凝聚时间, 0.01f));
            float easedProgress = 1f - Mathf.Pow(1f - progress, 3f);

            if (灵气核心 != null)
            {
                float radius = Mathf.Lerp(初始半径, 最大半径, easedProgress);
                灵气核心.transform.localScale = Vector3.one * radius;
            }

            if (灵气光环 != null)
            {
                float ringScale = Mathf.Lerp(0.15f, 1f, easedProgress);
                灵气光环.transform.localScale = Vector3.one * ringScale;
            }

            if (灵气材质 != null)
            {
                Color color = 灵气颜色;
                color.a = Mathf.Lerp(0.05f, 0.85f, easedProgress);
                设置材质颜色(color);
            }

            yield return null;
        }

        if (灵气核心 != null)
        {
            灵气核心.transform.localScale = Vector3.one * 最大半径;
        }

        if (灵气光环 != null)
        {
            灵气光环.transform.localScale = Vector3.one;
        }

        凝聚协程 = null;
        StartCoroutine(播放飞行效果());
    }

    private IEnumerator 播放飞行效果()
    {
        if (灵气根节点 == null)
        {
            正在释放 = false;
            yield break;
        }

        Vector3 飞行方向 = transform.forward.normalized;
        灵气根节点.SetParent(null, true);

        float elapsed = 0f;
        while (elapsed < 最大飞行时间 && 灵气根节点 != null)
        {
            elapsed += Time.deltaTime;
            灵气根节点.position += 飞行方向 * 飞行速度 * Time.deltaTime;

            if (检测到外部碰撞体())
            {
                清理运行时特效();
                正在释放 = false;
                yield break;
            }

            yield return null;
        }

        清理运行时特效();
        正在释放 = false;
    }

    private bool 检测到外部碰撞体()
    {
        if (灵气根节点 == null)
        {
            return false;
        }

        Collider[] colliders = Physics.OverlapSphere(
            灵气根节点.position,
            Mathf.Max(碰撞半径, 0.01f),
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Collide);

        foreach (Collider collider in colliders)
        {
            if (collider == null)
            {
                continue;
            }

            Transform hitTransform = collider.transform;
            if (hitTransform == transform || hitTransform.IsChildOf(transform))
            {
                continue;
            }

            if (hitTransform.root == transform.root)
            {
                continue;
            }

            if (灵气根节点 != null &&
                (hitTransform == 灵气根节点 || hitTransform.IsChildOf(灵气根节点)))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private void 创建运行时特效()
    {
        灵气根节点 = new GameObject("灵气特效_运行时").transform;
        灵气根节点.SetParent(transform, false);

        灵气材质 = 创建灵气材质();

        灵气核心 = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        灵气核心.name = "灵气核心";
        灵气核心.transform.SetParent(灵气根节点, false);
        灵气核心.transform.localScale = Vector3.one * 初始半径;
        删除碰撞体(灵气核心);
        设置渲染材质(灵气核心, 灵气材质);

        灵气光环 = new GameObject("灵气光环");
        灵气光环.transform.SetParent(灵气根节点, false);
        LineRenderer ring = 灵气光环.AddComponent<LineRenderer>();
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.positionCount = 32;
        ring.widthMultiplier = 0.025f;
        ring.numCapVertices = 2;
        ring.material = 灵气材质;

        for (int i = 0; i < ring.positionCount; i++)
        {
            float angle = i / (float)ring.positionCount * Mathf.PI * 2f;
            ring.SetPosition(i, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * 最大半径 * 1.5f);
        }
    }

    private Material 创建灵气材质()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Color");
        }

        Material material = new Material(shader);
        material.name = "灵气材质_运行时";
        return material;
    }

    private void 设置材质颜色(Color color)
    {
        if (灵气材质.HasProperty("_BaseColor"))
        {
            灵气材质.SetColor("_BaseColor", color);
        }
        else if (灵气材质.HasProperty("_Color"))
        {
            灵气材质.SetColor("_Color", color);
        }
    }

    private void 设置渲染材质(GameObject target, Material material)
    {
        Renderer renderer = target.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material = material;
        }
    }

    private void 删除碰撞体(GameObject target)
    {
        Collider collider = target.GetComponent<Collider>();
        if (collider != null)
        {
            Destroy(collider);
        }
    }

    private void 清理运行时特效()
    {
        if (凝聚协程 != null)
        {
            StopCoroutine(凝聚协程);
            凝聚协程 = null;
        }

        if (灵气根节点 != null)
        {
            Destroy(灵气根节点.gameObject);
        }

        if (灵气材质 != null)
        {
            Destroy(灵气材质);
        }

        灵气根节点 = null;
        灵气核心 = null;
        灵气光环 = null;
        灵气材质 = null;
    }

    private void OnDisable()
    {
        清理运行时特效();
        正在释放 = false;
    }
}
