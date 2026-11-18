using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Alchemy.Presentation
{
    [DisallowMultipleComponent]
    public sealed class AlchemyRecipeRowView : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text recipeName;

        public string RecipeId { get; private set; }
        public event Action<string> Selected;

        public void Bind(string recipeId, string displayName, Sprite sprite,
            bool learned, bool selected)
        {
            RecipeId = recipeId;
            if (recipeName != null)
            {
                recipeName.text = learned ? displayName : displayName + " (未学)";
                recipeName.color = learned
                    ? new Color(.16f, .26f, .21f, 1f) : Color.gray;
            }
            if (icon != null)
            {
                icon.sprite = sprite;
                icon.color = Color.white;
                icon.preserveAspect = true;
                icon.gameObject.SetActive(sprite != null);
            }
            Image background = GetComponent<Image>();
            if (background != null)
                background.color = selected
                    ? new Color(.76f, .91f, .78f, 1f) : Color.white;
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
