using System;
using UnityEngine;

[Serializable]
public class CharacterSpriteMapping
{
    public string characterId;
    public string displayName;
    public Sprite sprite;

    public bool Matches(string id)
    {
        return !string.IsNullOrWhiteSpace(characterId) &&
               string.Equals(characterId, id, StringComparison.Ordinal);
    }
}
