using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Minimap
{
    [DisallowMultipleComponent]
    public sealed class MinimapMonsterMarkers : MaskableGraphic
    {
        private const int Sides = 8;
        private const float FillRadius = 2.8f;
        private const float OutlineRadius = 3.6f;
        private static readonly Color32 OutlineColor = new Color32(58, 15, 19, 255);
        private static readonly Vector2[] Directions = CreateDirections();

        private readonly List<Vector2> positions = new List<Vector2>();

        internal void SetPositions(List<Vector2> markers)
        {
            if (positions.Count == markers.Count)
            {
                int index = 0;
                while (index < positions.Count && positions[index] == markers[index])
                    index++;
                if (index == positions.Count)
                    return;
            }

            positions.Clear();
            positions.AddRange(markers);
            SetVerticesDirty();
        }

        internal void ClearPositions()
        {
            if (positions.Count == 0)
                return;

            positions.Clear();
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vertices)
        {
            vertices.Clear();
            Color32 fillColor = color;
            foreach (Vector2 point in positions)
            {
                int center = vertices.currentVertCount;
                vertices.AddVert(point, fillColor, Vector2.zero);
                foreach (Vector2 direction in Directions)
                {
                    vertices.AddVert(point + direction * FillRadius, fillColor, Vector2.zero);
                    vertices.AddVert(point + direction * OutlineRadius, OutlineColor, Vector2.zero);
                }

                for (int side = 0; side < Sides; side++)
                {
                    int edge = center + 1 + side * 2;
                    int nextEdge = center + 1 + ((side + 1) % Sides) * 2;
                    vertices.AddTriangle(center, edge, nextEdge);
                    vertices.AddTriangle(edge, edge + 1, nextEdge + 1);
                    vertices.AddTriangle(edge, nextEdge + 1, nextEdge);
                }
            }
        }

        private static Vector2[] CreateDirections()
        {
            var directions = new Vector2[Sides];
            for (int side = 0; side < Sides; side++)
            {
                float angle = -side * (2f * Mathf.PI / Sides);
                directions[side] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            }
            return directions;
        }
    }
}
