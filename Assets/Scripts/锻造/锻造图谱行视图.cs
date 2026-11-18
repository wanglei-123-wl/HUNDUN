using System;
using Game.Forging;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Forging.Presentation
{
    public sealed class 锻造图谱行视图 : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text recipeName;

        public string RecipeId { get; private set; }
        public event Action<string> Selected;

        public void Bind(锻造图谱 recipe, bool selected,
            bool? learned = null)
        {
            RecipeId = recipe.recipeId;
            if (recipeName != null)
            {
                recipeName.text = learned == false
                    ? recipe.name + "（未学）" : recipe.name;
                recipeName.color = learned == false
                    ? new Color(0.43f, 0.45f, 0.46f)
                    : new Color(0.08f, 0.22f, 0.32f);
            }
            if (icon != null)
            {
                icon.sprite = recipe.icon;
                icon.color = Color.white;
                icon.preserveAspect = true;
                icon.raycastTarget = false;
                icon.gameObject.SetActive(recipe.icon != null);
            }
            Image background = GetComponent<Image>();
            if (background != null)
                background.color = selected
                    ? new Color(0.78f, 0.89f, 0.93f)
                    : new Color(1f, 1f, 1f, 0.88f);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData != null
                && eventData.button == PointerEventData.InputButton.Left
                && !string.IsNullOrEmpty(RecipeId))
                Selected?.Invoke(RecipeId);
        }
    }
}
