using System;
using System.Collections.Generic;
using Game.CharacterPanel.Presentation;
using Game.Cultivation.Presentation;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class WorldShellUiSmokeTest
{
    [MenuItem("Tools/Tests/WorldShell UI Smoke")]
    public static void Run()
    {
        try
        {
            Scene scene = EditorSceneManager.OpenScene(
                "Assets/Scenes/WorldShell.unity", OpenSceneMode.Single);
            ProgressionHudPresenter hud = One<ProgressionHudPresenter>(scene);
            CharacterPanelPresenter character = One<CharacterPanelPresenter>(scene);
            CultivationProgressPresenter cultivation = One<CultivationProgressPresenter>(scene);
            CultivationRealmPresenter realm = One<CultivationRealmPresenter>(scene);
            快捷栏图标显示控制器 quickbar = One<快捷栏图标显示控制器>(scene);

            Check(hud.gameObject.name == "底部框", "HUD is not on the bottom frame.");
            GameSession session = Reference<GameSession>(hud, "session");
            Check(session == Reference<GameSession>(character, "session")
                && session == Reference<GameSession>(cultivation, "session")
                && session == Reference<GameSession>(realm, "session"),
                "HUD and panels do not share the same GameSession.");
            Check(Reference<TMP_Text>(hud, "nicknameText") != null
                && Reference<TMP_Text>(hud, "realmText") != null
                && Reference<TMP_Text>(hud, "experienceText") != null
                && Reference<Image>(hud, "experienceFill").type == Image.Type.Filled,
                "HUD text or cultivation fill is not assigned.");
            Check(character.transform.Find("人物面板等级标签") == null
                && character.transform.Find("人物面板等级") == null,
                "The retired character panel level row is still in the scene.");

            Transform left = null;
            foreach (Transform child in hud.GetComponentsInChildren<Transform>(true))
            {
                Check(child.name != "等级标签" && child.name != "等级显示框",
                    "The retired HUD level row is still in the scene.");
                if (child.name == "左" && child.childCount == 4
                    && child.GetChild(0).GetComponent<技能栏槽位视图>() != null)
                    left = child;
            }
            Check(left != null, "Left skill slots are missing.");
            技能栏控制器 skillBar = null;
            for (int index = 0; index < left.childCount; index++)
            {
                技能栏槽位视图 slot =
                    left.GetChild(index).GetComponent<技能栏槽位视图>();
                Check(slot != null && Number(slot, "槽位编号") == index + 1,
                    "Left skill slot order is incorrect.");
                技能栏控制器 boundBar = Reference<技能栏控制器>(slot, "技能栏");
                Check(skillBar == null || skillBar == boundBar,
                    "Left skill slots use different controllers.");
                skillBar = boundBar;
                Check(Reference<Image>(slot, "技能图标") != null
                    && left.GetChild(index).GetComponent<Image>() != null,
                    "Left skill slot image is missing.");
                Check(!typeof(IDropHandler).IsAssignableFrom(slot.GetType())
                    && !typeof(IBeginDragHandler).IsAssignableFrom(slot.GetType()),
                    "Left skill slots must only display bindings.");
            }

            LearnedSkillListView skills = One<LearnedSkillListView>(scene);
            CultivationWindowToggle window = One<CultivationWindowToggle>(scene);
            Check(Reference<GameSession>(skills, "session") == session
                && Reference<GameSession>(window, "session") == session
                && Reference<LearnedSkillListView>(window, "learnedSkills") == skills
                && Reference<技能栏控制器>(skills, "skillBar") == skillBar,
                "Skill learning, selection and casting must share the WorldShell bindings.");
            RectTransform slotPrefab = Reference<RectTransform>(skills, "slotPrefab");
            Check(slotPrefab.Find("图标")?.GetComponent<Image>() != null
                && slotPrefab.Find("图标/选中效果") != null,
                "Learned skill slot icon or selection effect is missing.");
            Check(Reference<TMP_Text>(skills, "detailLevel").gameObject.name == "技能等级",
                "The learned skill level label is missing.");
            SerializedProperty slotButtons = new SerializedObject(skills).FindProperty("slotButtons");
            Check(slotButtons.arraySize == 技能栏控制器.SlotCount, "Skill panel slot buttons are missing.");
            for (int index = 0; index < slotButtons.arraySize; index++)
            {
                Button button = slotButtons.GetArrayElementAtIndex(index).objectReferenceValue as Button;
                Check(button != null && button.gameObject.name == "技能栏" + (index + 1),
                    "Skill panel slot button order is incorrect.");
            }

            Check(quickbar.gameObject.name == "右" && quickbar.transform.childCount == 4,
                "Right quickbar does not have four slots.");
            for (int index = 0; index < quickbar.transform.childCount; index++)
            {
                Transform slot = quickbar.transform.GetChild(index);
                Image hitArea = slot.GetComponent<Image>();
                Check(hitArea != null && hitArea.raycastTarget
                    && slot.Find("HUD格子按钮")?.GetComponent<Image>() != null,
                    "Right quickbar hit area or icon is missing.");
                Check(slot.GetComponent<技能栏槽位视图>() == null,
                    "Right quickbar contains a skill slot view.");
            }

            Debug.Log("PASS: WorldShell UI deserialized slots and shared HUD bindings.");
            if (Application.isBatchMode) EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            if (Application.isBatchMode) EditorApplication.Exit(1);
            else throw;
        }
    }

    private static T One<T>(Scene scene) where T : Component
    {
        var found = new List<T>();
        foreach (GameObject root in scene.GetRootGameObjects())
            found.AddRange(root.GetComponentsInChildren<T>(true));
        Check(found.Count == 1, "Expected one scene component: " + typeof(T).Name);
        return found[0];
    }

    private static T Reference<T>(Component component, string name) where T : UnityEngine.Object
    {
        SerializedProperty property = new SerializedObject(component).FindProperty(name);
        T value = property?.objectReferenceValue as T;
        Check(value != null, component.GetType().Name + "." + name + " is unassigned.");
        return value;
    }

    private static int Number(Component component, string name)
    {
        SerializedProperty property = new SerializedObject(component).FindProperty(name);
        Check(property != null, component.GetType().Name + "." + name + " is missing.");
        return property.intValue;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
