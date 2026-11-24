using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Minimap
{
    [DisallowMultipleComponent]
    public sealed class MinimapPresenter : MonoBehaviour
    {
        [SerializeField] private GameSession session;
        [SerializeField] private MinimapMapCatalog catalog;
        [SerializeField] private GameObject content;
        [SerializeField] private RectTransform viewport;
        [SerializeField] private RawImage mapImage;
        [SerializeField] private MonsterSnapshotManager monsterSync;
        [SerializeField] private MinimapMonsterMarkers monsterMarkers;
        [SerializeField] private RectTransform playerArrow;
        [SerializeField] private TMP_Text mapName;
        [SerializeField, Min(1f)] private float visibleDiameterMeters = 120f;

        private readonly List<Vector2> markerPositions = new List<Vector2>();
        private MinimapMapCatalog.Map currentMap;
        private string currentSceneId;
        private string currentMapId;
        private float pixelsPerMeter;

        public string GetMapDisplayName(string sceneId, string mapId) => catalog?.Find(sceneId, mapId)?.displayName;

        public bool IsPreparedForMap(string sceneId, string mapId) => isActiveAndEnabled
            && currentMap != null && currentSceneId == sceneId && currentMapId == mapId
            && mapImage != null && mapImage.texture == currentMap.texture && pixelsPerMeter > 0f
            && content != null && content.activeInHierarchy;

        private void LateUpdate()
        {
            if (!session.TryGetMinimapPose(out string sceneId, out string mapId,
                    out Vector2 positionXZ, out float yaw))
            {
                content.SetActive(false);
                monsterMarkers.ClearPositions();
                return;
            }

            if (sceneId != currentSceneId || mapId != currentMapId)
            {
                currentSceneId = sceneId;
                currentMapId = mapId;
                currentMap = catalog.Find(sceneId, mapId);
                mapImage.texture = currentMap?.texture;
                mapName.text = currentMap?.displayName ?? string.Empty;
                pixelsPerMeter = 0f;
            }

            content.SetActive(currentMap != null);
            if (currentMap == null)
            {
                monsterMarkers.ClearPositions();
                return;
            }

            // One UI scale for X and Z keeps the map proportional, including narrow maps.
            float scale = viewport.rect.width / visibleDiameterMeters;
            if (scale != pixelsPerMeter)
            {
                pixelsPerMeter = scale;
                mapImage.rectTransform.sizeDelta = (currentMap.maxXZ - currentMap.minXZ) * scale;
            }

            Vector2 center = (currentMap.minXZ + currentMap.maxXZ) * 0.5f;
            mapImage.rectTransform.anchoredPosition = (center - positionXZ) * scale;
            playerArrow.localRotation = Quaternion.Euler(0f, 0f, -yaw);
            UpdateMonsterMarkers(sceneId, positionXZ, scale);
        }

        private void UpdateMonsterMarkers(string sceneId, Vector2 playerPosition, float scale)
        {
            monsterSync.CopyLivingMonsterPositions(sceneId, markerPositions);
            float radius = visibleDiameterMeters * 0.5f;
            float radiusSquared = radius * radius;
            int visibleCount = 0;
            for (int index = 0; index < markerPositions.Count; index++)
            {
                Vector2 offset = markerPositions[index] - playerPosition;
                if (offset.sqrMagnitude <= radiusSquared)
                    markerPositions[visibleCount++] = offset * scale;
            }
            markerPositions.RemoveRange(visibleCount, markerPositions.Count - visibleCount);
            monsterMarkers.SetPositions(markerPositions);
        }

        private void OnDisable()
        {
            if (monsterMarkers != null)
                monsterMarkers.ClearPositions();
            content.SetActive(false);
        }
    }
}
