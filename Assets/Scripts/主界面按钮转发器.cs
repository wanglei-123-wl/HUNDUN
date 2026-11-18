using Game.CharacterPanel.Presentation;
using Game.Inventory;
using Game.PlayerTrade.Presentation;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class 主界面按钮转发器 : MonoBehaviour
{
    [Header("按钮")]
    [SerializeField] private Button 人物属性面板按钮;
    [SerializeField] private Button 背包按钮;
    [SerializeField] private Button 市场按钮;

    [Header("现有控制器")]
    [SerializeField] private CharacterPanelToggle 人物属性面板控制器;
    [SerializeField] private InventoryModuleRuntime 背包控制器;
    [SerializeField] private PlayerTradeMarketToggleController 市场控制器;

#if UNITY_EDITOR
    private bool 已安排编辑器绑定;

    private void OnValidate()
    {
        if (Application.isPlaying || 已安排编辑器绑定)
            return;

        已安排编辑器绑定 = true;
        UnityEditor.EditorApplication.delayCall += 自动绑定编辑器引用;
    }

    private void 自动绑定编辑器引用()
    {
        已安排编辑器绑定 = false;
        if (this == null || Application.isPlaying || !gameObject.scene.IsValid())
            return;

        if (!补齐引用())
            return;

        UnityEditor.EditorUtility.SetDirty(this);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
    }
#endif

    private void OnEnable()
    {
        if (!Application.isPlaying)
            return;

        补齐引用();
        if (人物属性面板按钮 != null && 人物属性面板控制器 != null)
            人物属性面板按钮.onClick.AddListener(切换人物属性面板);
        else
            Debug.LogError("[主界面按钮] 人物属性面板按钮或控制器未绑定。", this);

        if (背包按钮 != null && 背包控制器 != null)
            背包按钮.onClick.AddListener(切换背包);
        else
            Debug.LogError("[主界面按钮] 背包按钮或控制器未绑定。", this);

        if (市场按钮 != null && 市场控制器 != null)
            市场按钮.onClick.AddListener(切换市场);
        else
            Debug.LogError("[主界面按钮] 市场按钮或控制器未绑定。", this);
    }

    private void OnDisable()
    {
        if (!Application.isPlaying)
            return;

        if (人物属性面板按钮 != null)
            人物属性面板按钮.onClick.RemoveListener(切换人物属性面板);
        if (背包按钮 != null)
            背包按钮.onClick.RemoveListener(切换背包);
        if (市场按钮 != null)
            市场按钮.onClick.RemoveListener(切换市场);
    }

    private void 切换人物属性面板()
    {
        人物属性面板控制器.Toggle();
    }

    private void 切换背包()
    {
        背包控制器.Toggle();
    }

    private void 切换市场()
    {
        市场控制器.Toggle();
    }

    private bool 补齐引用()
    {
        bool 有变更 = false;

        if (人物属性面板按钮 == null)
        {
            人物属性面板按钮 = 查找唯一组件<Button>("人物属性面板");
            有变更 |= 人物属性面板按钮 != null;
        }

        if (背包按钮 == null)
        {
            背包按钮 = 查找唯一组件<Button>("背包");
            有变更 |= 背包按钮 != null;
        }

        if (市场按钮 == null)
        {
            市场按钮 = 查找唯一组件<Button>("市场");
            有变更 |= 市场按钮 != null;
        }

        if (人物属性面板控制器 == null)
        {
            人物属性面板控制器 = 查找唯一组件<CharacterPanelToggle>();
            有变更 |= 人物属性面板控制器 != null;
        }

        if (背包控制器 == null)
        {
            背包控制器 = 查找唯一组件<InventoryModuleRuntime>();
            有变更 |= 背包控制器 != null;
        }

        if (市场控制器 == null)
        {
            市场控制器 = 查找唯一组件<PlayerTradeMarketToggleController>();
            有变更 |= 市场控制器 != null;
        }

        return 有变更;
    }

    private T 查找唯一组件<T>(string 对象名称 = null) where T : Component
    {
        T 结果 = null;
        T[] 组件列表 = FindObjectsOfType<T>(true);
        foreach (T 组件 in 组件列表)
        {
            if (组件.gameObject.scene != gameObject.scene
                || (对象名称 != null && 组件.gameObject.name != 对象名称))
                continue;

            if (结果 != null)
                return null;

            结果 = 组件;
        }

        return 结果;
    }
}
