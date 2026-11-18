using Game.Items;
using Game.UI.ItemDetail;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 显示当前绑定的技能和悬浮详情。
/// </summary>
[DisallowMultipleComponent]
public sealed class 技能栏槽位视图 : MonoBehaviour
{
    [Header("槽位绑定")]
    [SerializeField] private int 槽位编号;
    [SerializeField] private 技能栏控制器 技能栏;
    [SerializeField] private Image 技能图标;
    [SerializeField] private TMP_Text 技能名称;
    [SerializeField] private Image 槽位背景;

    private Color 原始背景颜色 = Color.white;
    private bool 已缓存背景颜色;
    private bool 已绑定技能;
    private ItemDetailHoverTrigger 详情悬浮触发器;

    private void Awake()
    {
        if (槽位背景 == null)
        {
            槽位背景 = GetComponent<Image>();
        }

        if (槽位背景 != null)
        {
            原始背景颜色 = 槽位背景.color;
            已缓存背景颜色 = true;
        }

        详情悬浮触发器 = GetComponent<ItemDetailHoverTrigger>();
        if (详情悬浮触发器 == null
            && ItemDetailHoverService.IsConfiguredInScene(this))
            详情悬浮触发器 = gameObject.AddComponent<ItemDetailHoverTrigger>();

        刷新槽位绑定();
    }

    private void Start()
    {
        刷新槽位绑定();
    }

    private void OnEnable()
    {
        if (技能栏 != null)
            技能栏.技能槽位已变更 += 处理技能槽位变更;
        刷新槽位绑定();
    }

    private void OnDisable()
    {
        if (技能栏 != null)
            技能栏.技能槽位已变更 -= 处理技能槽位变更;
        if (详情悬浮触发器 != null)
            详情悬浮触发器.ClearBinding();
    }

    private void OnValidate()
    {
        if (!Application.isPlaying)
        {
            if (槽位背景 == null)
            {
                槽位背景 = GetComponent<Image>();
            }

            if (槽位背景 != null)
            {
                原始背景颜色 = 槽位背景.color;
                已缓存背景颜色 = true;
            }

            更新槽位显示();
        }
    }

    private void 处理技能槽位变更(int 变更槽位编号, string skillId)
    {
        if (变更槽位编号 != 槽位编号)
            return;

        刷新槽位绑定();
    }

    private void 刷新槽位绑定()
    {
        已绑定技能 = 技能栏 != null
            && 技能栏.TryGetSkillId(槽位编号, out _);
        ItemDetailData detail = null;
        if (已绑定技能)
            技能栏.TryGetSkillDetail(槽位编号, out detail);

        if (技能图标 != null)
            技能图标.sprite = detail?.Icon;
        if (技能名称 != null)
            技能名称.text = detail?.DisplayName ?? string.Empty;
        刷新详情绑定(detail);
        更新槽位显示();
    }

    private void 刷新详情绑定(ItemDetailData detail)
    {
        if (详情悬浮触发器 == null)
            return;

        if (detail != null && detail.IsValid)
        {
            详情悬浮触发器.Bind(detail);
        }
        else
        {
            详情悬浮触发器.ClearBinding();
        }
    }

    private void 更新槽位显示()
    {
        bool 有技能图标 = 已绑定技能;

        if (技能图标 != null)
        {
            bool 显示图标 = 有技能图标 && 技能图标.sprite != null;
            Color 图标颜色 = 技能图标.color;
            图标颜色.a = 显示图标 ? 1f : 0f;
            技能图标.color = 图标颜色;
            技能图标.gameObject.SetActive(显示图标);
        }

        if (技能名称 != null)
        {
            bool 有技能名称 = !string.IsNullOrWhiteSpace(技能名称.text);
            技能名称.gameObject.SetActive(有技能名称);
        }

        if (槽位背景 != null)
        {
            if (!已缓存背景颜色)
            {
                原始背景颜色 = 槽位背景.color;
                已缓存背景颜色 = true;
            }

            Color 背景颜色 = 原始背景颜色;
            背景颜色.a = 有技能图标 ? 原始背景颜色.a : 0f;
            槽位背景.color = 背景颜色;
        }
    }
}
