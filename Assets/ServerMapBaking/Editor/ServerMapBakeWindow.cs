using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ServerMapBaking.Editor
{
    internal sealed class ServerMapBakeWindow : EditorWindow
    {
        private readonly ServerMapBakeSettings settings = new ServerMapBakeSettings();
        private ServerMapRoot root;
        private Vector2 scrollPosition;
        private List<ValidationIssue> validationIssues = new List<ValidationIssue>();
        private string statusMessage = "Select a ServerMapRoot, then validate before baking.";

        [MenuItem("Tools/Server Map Baker")]
        private static void Open()
        {
            ServerMapBakeWindow window = GetWindow<ServerMapBakeWindow>("Server Map Baker");
            window.minSize = new Vector2(520f, 420f);
            window.Show();
        }

        private void OnEnable()
        {
            FindRootIfNeeded();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Server Map Source", EditorStyles.boldLabel);
            root = (ServerMapRoot)EditorGUILayout.ObjectField("Server Map Root", root, typeof(ServerMapRoot), true);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Find In Active Scene"))
                {
                    FindRootIfNeeded(true);
                }

                if (GUILayout.Button("Select Root") && root != null)
                {
                    Selection.activeObject = root.gameObject;
                    EditorGUIUtility.PingObject(root.gameObject);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            settings.OutputFolder = EditorGUILayout.TextField("Output Folder", settings.OutputFolder);
            EditorGUILayout.HelpBox(
                "V1 writes manifest.json, heightfield.bin, walkable.bin, colliders.bin, navmesh.bin and bake.report.txt under one sceneId_mapId folder.",
                MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            settings.DrawTerrain = EditorGUILayout.Toggle("Draw Heightfield", settings.DrawTerrain);
            settings.DrawColliders = EditorGUILayout.Toggle("Draw Colliders", settings.DrawColliders);
            settings.DrawNavMesh = EditorGUILayout.Toggle("Draw NavMesh", settings.DrawNavMesh);
            settings.DrawRegions = EditorGUILayout.Toggle("Draw Regions", settings.DrawRegions);
            settings.TerrainPreviewSamples = EditorGUILayout.IntSlider("Grid Preview Samples", settings.TerrainPreviewSamples, 8, 128);

            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Validate", GUILayout.Height(30f)))
                {
                    RunValidation();
                }

                if (GUILayout.Button("Bake", GUILayout.Height(30f)))
                {
                    RunBake();
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Load Baked Preview"))
                {
                    LoadExpectedPreview();
                }

                if (GUILayout.Button("Browse Manifest"))
                {
                    BrowsePreview();
                }

                using (new EditorGUI.DisabledScope(!ServerMapPreview.IsLoaded))
                {
                    if (GUILayout.Button("Clear Preview"))
                    {
                        ServerMapPreview.Clear();
                        statusMessage = "Preview cleared.";
                    }
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(statusMessage, MessageType.None);
            DrawValidationIssues();
        }

        private void RunValidation()
        {
            validationIssues = ServerMapValidator.Validate(root);
            int errors = CountIssues(ValidationSeverity.Error);
            int warnings = CountIssues(ValidationSeverity.Warning);
            statusMessage = "Validation finished: " + errors + " error(s), " + warnings + " warning(s).";
        }

        private void RunBake()
        {
            validationIssues = ServerMapValidator.Validate(root);
            if (ServerMapValidator.HasErrors(validationIssues))
            {
                statusMessage = "Bake stopped because validation contains errors.";
                EditorUtility.DisplayDialog("Server Map Baker", statusMessage, "OK");
                return;
            }

            string manifestPath = ServerMapBinaryWriter.ResolveManifestFile(root, settings.OutputFolder);
            if (File.Exists(manifestPath) && !EditorUtility.DisplayDialog(
                    "Overwrite baked server map?",
                    "The following generated map already exists:\n\n" + ServerMapBinaryWriter.ResolveOutputDirectory(root, settings.OutputFolder),
                    "Overwrite",
                    "Cancel"))
            {
                statusMessage = "Bake cancelled. No files were changed.";
                return;
            }

            try
            {
                ServerMapBakeResult result = ServerMapBaker.Bake(root, settings.OutputFolder);
                statusMessage = "Bake completed: " + result.Paths.ManifestPath;
                ServerMapPreview.Load(result.Paths.ManifestPath, root, settings);
                EditorUtility.RevealInFinder(result.Paths.OutputDirectory);
            }
            catch (Exception exception)
            {
                statusMessage = "Bake failed: " + exception.Message;
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Server Map Baker", statusMessage, "OK");
            }
        }

        private void LoadExpectedPreview()
        {
            if (root == null)
            {
                statusMessage = "Select a ServerMapRoot before loading a preview.";
                return;
            }

            LoadPreview(ServerMapBinaryWriter.ResolveManifestFile(root, settings.OutputFolder));
        }

        private void BrowsePreview()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string path = EditorUtility.OpenFilePanel("Open Server Map Manifest", projectRoot, "json");
            if (!string.IsNullOrEmpty(path))
            {
                LoadPreview(path);
            }
        }

        private void LoadPreview(string path)
        {
            if (!File.Exists(path))
            {
                statusMessage = "Manifest does not exist: " + path;
                return;
            }

            try
            {
                ServerMapPreview.Load(path, root, settings);
                statusMessage = "Preview loaded: " + path;
            }
            catch (Exception exception)
            {
                statusMessage = "Preview failed: " + exception.Message;
                Debug.LogException(exception);
            }
        }

        private void DrawValidationIssues()
        {
            if (validationIssues == null || validationIssues.Count == 0)
            {
                return;
            }

            EditorGUILayout.LabelField("Validation Results", EditorStyles.boldLabel);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.MinHeight(100f));
            for (int index = 0; index < validationIssues.Count; index++)
            {
                ValidationIssue issue = validationIssues[index];
                MessageType messageType = issue.Severity == ValidationSeverity.Error
                    ? MessageType.Error
                    : issue.Severity == ValidationSeverity.Warning
                        ? MessageType.Warning
                        : MessageType.Info;
                EditorGUILayout.HelpBox(issue.Message, messageType);
            }

            EditorGUILayout.EndScrollView();
        }

        private int CountIssues(ValidationSeverity severity)
        {
            int count = 0;
            for (int index = 0; index < validationIssues.Count; index++)
            {
                if (validationIssues[index].Severity == severity)
                {
                    count++;
                }
            }

            return count;
        }

        private void FindRootIfNeeded(bool force = false)
        {
            if (root != null && !force)
            {
                return;
            }

            ServerMapRoot[] roots = UnityEngine.Object.FindObjectsOfType<ServerMapRoot>(true);
            root = roots.Length > 0 ? roots[0] : null;
            statusMessage = root == null
                ? "No ServerMapRoot was found. Add the component manually when you are ready to configure a map."
                : "Found ServerMapRoot: " + root.name;
        }
    }
}
