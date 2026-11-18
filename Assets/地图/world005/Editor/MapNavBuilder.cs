using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace World005 {
    public static class MapNavBuilder {
        const string Id = "world005";
        const string DirectoryPath = "Assets/地图/" + Id + "/Navigation";
        const string DataPath = DirectoryPath + "/" + Id + "_NavMesh.asset";
        const int GroundArea = 0, BlockedArea = 1;

        [Serializable] public class Zone { public string name; public Vector2[] points; public bool heightLimited; public float ceiling; }
        [Serializable] class Plan { public Zone[] blocks; }

        // Each exclusion is a convex footprint with an optional sloped waterline.
        readonly struct Cut {
            public readonly Plane[] planes;
            public readonly Vector2 min, max;
            public Cut(Vector3 a, Vector3 b, Vector3 c, bool wet, float? top = null) {
                var normal = Vector3.Cross(b - a, c - a);
                if (normal.y < 0) { var swap = b; b = c; c = swap; normal = -normal; }
                Vector3 Side(Vector3 start, Vector3 end) => Vector3.Cross(Vector3.up, end - start);
                planes = wet
                    ? new[] { new Plane(Side(a,b),a), new Plane(Side(b,c),b), new Plane(Side(c,a),c), new Plane(-normal, a + Vector3.up * .035f) }
                    : new[] { new Plane(Side(a,b),a), new Plane(Side(b,c),b), new Plane(Side(c,a),c) };
                if (top.HasValue)
                    planes = planes.Append(new Plane(Vector3.down, Vector3.up * top.Value)).ToArray();
                min = new Vector2(Mathf.Min(a.x,b.x,c.x), Mathf.Min(a.z,b.z,c.z));
                max = new Vector2(Mathf.Max(a.x,b.x,c.x), Mathf.Max(a.z,b.z,c.z));
            }
            public bool Overlaps(Vector2 lo, Vector2 hi) => lo.x < max.x && hi.x > min.x && lo.y < max.y && hi.y > min.y;
        }

        [MenuItem("Tools/World005/Bake Navigation")]
        public static void RebakeOpenMap() {
            var root = SceneManager.GetActiveScene().GetRootGameObjects().SingleOrDefault(o => o.name == Id);
            if (!root) throw new InvalidOperationException("Open the " + Id + " map scene before baking.");
            Bake(root);
            EditorSceneManager.MarkSceneDirty(root.scene);
            Debug.Log("Baked " + Id + " NavMesh. Save the scene to retain component changes.", root);
        }

        public static NavMeshData Bake(GameObject root) {
            var nav = root.GetComponent<MapNavigation>();
            if (!nav) nav = root.AddComponent<MapNavigation>();
            var prior = nav.Data;
            nav.Bind(null);
            var temporaryMeshes = new List<Mesh>();
            try {
                var inverse = root.transform.worldToLocalMatrix;
                var cuts = ReadCuts(root, inverse, includeTerrainCleanup: true);
                var sources = new List<NavMeshBuildSource>();
                var bounds = new Bounds();
                bool first = true;
                foreach (var collider in root.GetComponentsInChildren<Collider>()) {
                    if (!collider.enabled || collider.isTrigger) continue;
                    var matrix = inverse * collider.transform.localToWorldMatrix;
                    if (collider is MeshCollider mc && mc.sharedMesh) {
                        bool terrain = mc.name.StartsWith("Terrain_");
                        bool bridge = mc.name.StartsWith("Solid_Deck");
                        var mesh = mc.sharedMesh;
                        var localBounds = mesh.bounds;
                        if (terrain || bridge) {
                            Encapsulate(ref bounds, ref first, localBounds, matrix);
                        }
                        if (terrain && cuts.Count != 0) {
                            mesh = DryTerrain(mesh, matrix, cuts);
                            temporaryMeshes.Add(mesh);
                            matrix = Matrix4x4.identity;
                        }
                        if (mesh.vertexCount == 0) continue;
                        sources.Add(new NavMeshBuildSource {
                            shape = NavMeshBuildSourceShape.Mesh, sourceObject = mesh, transform = matrix,
                            area = terrain || bridge ? GroundArea : BlockedArea, component = mc
                        });
                    } else if (collider is BoxCollider box) {
                        sources.Add(new NavMeshBuildSource {
                            shape = NavMeshBuildSourceShape.Box, size = box.size, area = BlockedArea,
                            transform = matrix * Matrix4x4.Translate(box.center), component = box
                        });
                    } else throw new InvalidOperationException("Unsupported map collider " + collider.name);
                }
                if (first) throw new InvalidDataException("No source ground for " + Id);
                bounds.Expand(new Vector3(.2f, 7f, .2f));
                var settings = NavMesh.GetSettingsByID(0);
                settings.agentRadius = .36f;
                settings.agentHeight = 1.84f;
                settings.agentClimb = .35f;
                settings.agentSlope = 43f;
                settings.overrideVoxelSize = true;
                settings.voxelSize = .12f;
                settings.overrideTileSize = true;
                settings.tileSize = 256;
                settings.minRegionArea = 1f;
                settings.buildHeightMesh = true;
                var data = AssetDatabase.LoadAssetAtPath<NavMeshData>(DataPath);
                if (!data) {
                    data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
                    if (!data) throw new InvalidDataException("Baking returned no data for " + Id);
                    data.name = Id + "_NavMesh";
                    AssetDatabase.CreateAsset(data, DataPath);
                } else if (!NavMeshBuilder.UpdateNavMeshData(data, settings, sources, bounds)) {
                    throw new InvalidDataException("Could not update baked data for " + Id);
                }
                nav.Bind(data);
                EditorUtility.SetDirty(data);
                EditorUtility.SetDirty(nav);
                AssetDatabase.SaveAssets();
                Debug.Log($"{Id} baked from {sources.Count} physics sources and {cuts.Count} water / exclusion facets.", root);
                return data;
            } catch {
                nav.Bind(prior);
                throw;
            } finally {
                foreach (var mesh in temporaryMeshes) Object.DestroyImmediate(mesh);
            }
        }

        static void Encapsulate(ref Bounds bounds, ref bool first, Bounds local, Matrix4x4 matrix) {
            for (int i = 0; i < 8; i++) {
                var corner = matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                if (first) { bounds = new Bounds(corner, Vector3.zero); first = false; }
                else bounds.Encapsulate(corner);
            }
        }

        static List<Cut> ReadCuts(GameObject root, Matrix4x4 inverse, bool includeTerrainCleanup = false) {
            var cuts = new List<Cut>();
            var plan = JsonUtility.FromJson<Plan>(File.ReadAllText(DirectoryPath + "/BakeZones.json"));
            foreach (var zone in plan.blocks) AddZone(cuts, zone);
            string isolatedPath = DirectoryPath + "/DisconnectedTerrain.json";
            if (includeTerrainCleanup && File.Exists(isolatedPath)) {
                var isolated = JsonUtility.FromJson<Plan>(File.ReadAllText(isolatedPath));
                foreach (var zone in isolated.blocks) {
                    var points = zone.points;
                    if (points.Length == 3 && Mathf.Abs(Cross(points[0],points[1],points[2])) > .001f)
                        cuts.Add(new Cut(OnGround(points[0]),OnGround(points[1]),OnGround(points[2]),false,
                            zone.heightLimited ? (float?)zone.ceiling : null));
                }
            }
            foreach (var filter in root.GetComponentsInChildren<MeshFilter>()) {
                if (!filter.name.StartsWith("Surface_water") && !filter.name.StartsWith("Surface_lava")) continue;
                var matrix = inverse * filter.transform.localToWorldMatrix;
                var mesh = filter.sharedMesh;
                var points = mesh.vertices.Select(matrix.MultiplyPoint3x4).ToArray();
                var indices = mesh.triangles;
                for (int i = 0; i < indices.Length; i += 3) {
                    var a = points[indices[i]];
                    var b = points[indices[i+1]];
                    var c = points[indices[i+2]];
                    if (Mathf.Abs(Vector3.Cross(b-a,c-a).y) > .001f) cuts.Add(new Cut(a,b,c,true));
                }
            }
            return cuts;
        }

        public static int CheckExclusions(GameObject root, NavMeshTriangulation triangles) {
            var inverse = root.transform.worldToLocalMatrix;
            var cuts = ReadCuts(root,inverse);
            for (int i = 0; i < triangles.indices.Length; i += 3) {
                var a = inverse.MultiplyPoint3x4(triangles.vertices[triangles.indices[i]]);
                var b = inverse.MultiplyPoint3x4(triangles.vertices[triangles.indices[i+1]]);
                var c = inverse.MultiplyPoint3x4(triangles.vertices[triangles.indices[i+2]]);
                foreach (var point in new[] { a,b,c,(a+b+c)/3f,(a+b)/2f,(a+c)/2f,(b+c)/2f }) {
                    foreach (var cut in cuts) {
                        if (point.x < cut.min.x || point.x > cut.max.x || point.z < cut.min.y || point.z > cut.max.y) continue;
                        if (cut.planes.All(plane => plane.GetDistanceToPoint(point) > .075f))
                            throw new InvalidDataException($"Navigation leaked into a submerged or blocked zone at {point:F3}");
                    }
                }
            }
            return cuts.Count;
        }

        static Vector3 OnGround(Vector2 p) => new Vector3(p.x,0,p.y);
        static float Cross(Vector2 a, Vector2 b, Vector2 c) => (b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);

        static void AddZone(List<Cut> cuts, Zone zone) {
            var polygon = zone.points.ToList();
            float area = 0;
            for (int i = 1; i < polygon.Count - 1; i++) area += Cross(polygon[0],polygon[i],polygon[i+1]);
            if (area < 0) polygon.Reverse();
            while (polygon.Count >= 3) {
                bool found = false;
                for (int i = 0; i < polygon.Count; i++) {
                    var a = polygon[(i+polygon.Count-1)%polygon.Count];
                    var b = polygon[i];
                    var c = polygon[(i+1)%polygon.Count];
                    if (Cross(a,b,c) < .001f) continue;
                    if (polygon.Any(p => p!=a && p!=b && p!=c && Cross(a,b,p)>=0 && Cross(b,c,p)>=0 && Cross(c,a,p)>=0)) continue;
                    cuts.Add(new Cut(OnGround(a),OnGround(b),OnGround(c),false));
                    polygon.RemoveAt(i);
                    found = true;
                    break;
                }
                if (!found) throw new InvalidDataException("Could not triangulate blocked zone " + zone.name);
            }
        }

        static Mesh DryTerrain(Mesh mesh, Matrix4x4 matrix, List<Cut> cuts) {
            var points = mesh.vertices.Select(matrix.MultiplyPoint3x4).ToArray();
            var indices = mesh.triangles;
            var vertices = new List<Vector3>();
            var faces = new List<int>();
            for (int i = 0; i < indices.Length; i += 3) {
                var a = points[indices[i]];
                var b = points[indices[i+1]];
                var c = points[indices[i+2]];
                var lo = new Vector2(Mathf.Min(a.x,b.x,c.x),Mathf.Min(a.z,b.z,c.z));
                var hi = new Vector2(Mathf.Max(a.x,b.x,c.x),Mathf.Max(a.z,b.z,c.z));
                var fragments = new List<List<Vector3>> { new List<Vector3> { a,b,c } };
                foreach (var cut in cuts) {
                    if (!cut.Overlaps(lo,hi)) continue;
                    var dry = new List<List<Vector3>>();
                    foreach (var fragment in fragments) Subtract(fragment,cut.planes,dry);
                    fragments = dry;
                    if (fragments.Count == 0) break;
                }
                foreach (var fragment in fragments) {
                    int start = vertices.Count;
                    vertices.AddRange(fragment);
                    for (int j = 1; j < fragment.Count - 1; j++) {
                        if (Vector3.Cross(fragment[j]-fragment[0],fragment[j+1]-fragment[0]).sqrMagnitude < .0000001f) continue;
                        faces.Add(start); faces.Add(start+j); faces.Add(start+j+1);
                    }
                }
            }
            var dryMesh = new Mesh { name = mesh.name + "_DryBakeOnly", indexFormat = IndexFormat.UInt32 };
            dryMesh.SetVertices(vertices);
            dryMesh.SetTriangles(faces,0);
            return dryMesh;
        }

        static void Subtract(List<Vector3> polygon, Plane[] planes, List<List<Vector3>> remainder) {
            var inside = polygon;
            foreach (var plane in planes) {
                var outside = new List<Vector3>();
                var next = new List<Vector3>();
                var last = inside[inside.Count-1];
                float prior = plane.GetDistanceToPoint(last);
                foreach (var point in inside) {
                    float distance = plane.GetDistanceToPoint(point);
                    if ((distance >= 0) != (prior >= 0)) {
                        var cross = Vector3.LerpUnclamped(last,point,prior/(prior-distance));
                        next.Add(cross);
                        outside.Add(cross);
                    }
                    if (distance >= 0) next.Add(point); else outside.Add(point);
                    last = point;
                    prior = distance;
                }
                if (outside.Count >= 3) remainder.Add(outside);
                if (next.Count < 3) return;
                inside = next;
            }
        }
    }
}
