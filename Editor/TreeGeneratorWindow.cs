using UnityEngine;
using UnityEditor;
using UnityEditor.Presets;
using System.Collections.Generic;
using System.IO;

namespace unitycoder.treegenerator
{
    public class TreeGeneratorWindow : EditorWindow, IHasCustomMenu
    {
        const string PREF_PREFIX = "TreeGen_";

        // --- Trunk Parameters ---
        [SerializeField] float trunkHeight = 3f;
        [SerializeField] float trunkBaseRadius = 0.15f;
        [SerializeField] float trunkTopRadius = 0.06f;
        [SerializeField] float trunkBendX = 0.3f;
        [SerializeField] float trunkBendZ = 0.0f;
        [SerializeField] int trunkSegments = 8;
        [SerializeField] int trunkRadialSegments = 8;
        [SerializeField] AnimationCurve trunkRadiusCurve = AnimationCurve.Linear(0, 1, 1, 0.3f);
        [SerializeField] Color trunkColor = new Color(0.25f, 0.2f, 0.15f);

        // --- Branch Parameters ---
        [SerializeField] int branchLevels = 2;
        [SerializeField] int branchesPerNode = 3;
        [SerializeField] float branchLengthMin = 0.6f;
        [SerializeField] float branchLengthMax = 1.2f;
        [SerializeField] float branchRadiusRatio = 0.5f;
        [SerializeField] float branchAngleMin = 25f;
        [SerializeField] float branchAngleMax = 55f;
        [SerializeField] float branchStartHeight = 0.4f;
        [SerializeField] float branchBend = 0.15f;
        [SerializeField] int branchSegments = 4;
        [SerializeField] int branchRadialSegments = 6;
        [SerializeField] float branchTaper = 0.3f;

        // --- Leaf Prefab ---
        [SerializeField] float leafScaleMin = 0.2f;
        [SerializeField] float leafScaleMax = 0.8f;
        [SerializeField] bool randomLeafRotation = false;
        [SerializeField] float leafOffsetFromTip = 0.05f;

        // --- Generation ---
        [SerializeField] int randomSeed = 42;
        [SerializeField] string prefabName = "Tree";
        [SerializeField] string savePath = "Assets/GeneratedTrees";
        [SerializeField] bool autoRefresh = true;
        [SerializeField] bool combineMeshes = true; // Combine trunk/branches into single mesh

        // --- Non-serialized ---
        GameObject leafPrefab;
        string leafPrefabGUID = "";
        GameObject previewObject;
        bool needsRefresh = true;
        Vector2 scrollPos;
        bool showTrunk = true;
        bool showBranches = true;
        bool showLeaves = true;
        bool showGeneration = true;
        Material sharedTrunkMaterial;

        [MenuItem("Tools/Tree Generator")]
        public static void ShowWindow()
        {
            var win = GetWindow<TreeGeneratorWindow>("Tree Generator");
            win.minSize = new Vector2(340, 500);
        }

        // ========================================================
        // IHasCustomMenu - Preset support in window context menu
        // ========================================================
        public void AddItemsToMenu(GenericMenu menu)
        {
            menu.AddItem(new GUIContent("Save Preset..."), false, () =>
            {
                var preset = new Preset(this);
                var path = EditorUtility.SaveFilePanelInProject("Save Tree Preset", prefabName + "_Preset", "preset", "Save tree generator preset");
                if (!string.IsNullOrEmpty(path))
                {
                    AssetDatabase.CreateAsset(preset, path);
                    AssetDatabase.SaveAssets();
                    Debug.Log($"[TreeGenerator] Preset saved to: {path}");
                }
            });
            menu.AddItem(new GUIContent("Load Preset..."), false, () =>
            {
                var path = EditorUtility.OpenFilePanel("Load Tree Preset", "Assets", "preset");
                if (!string.IsNullOrEmpty(path))
                {
                    if (path.StartsWith(Application.dataPath))
                        path = "Assets" + path.Substring(Application.dataPath.Length);
                    var preset = AssetDatabase.LoadAssetAtPath<Preset>(path);
                    if (preset != null && preset.CanBeAppliedTo(this))
                    {
                        preset.ApplyTo(this);
                        needsRefresh = true;
                        SaveToEditorPrefs();
                        Repaint();
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Error", "Invalid or incompatible preset.", "OK");
                    }
                }
            });
        }

        // ========================================================
        // LIFECYCLE
        // ========================================================
        void OnEnable()
        {
            LoadFromEditorPrefs();
            SceneView.duringSceneGui += OnSceneGUI;
            needsRefresh = true;
        }

        void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            SaveToEditorPrefs();
            DestroyPreview();
        }

        void OnDestroy()
        {
            SaveToEditorPrefs();
            DestroyPreview();
        }

        void DestroyPreview()
        {
            if (sharedTrunkMaterial != null)
            {
                DestroyImmediate(sharedTrunkMaterial);
                sharedTrunkMaterial = null;
            }
            if (previewObject != null)
            {
                DestroyImmediate(previewObject);
                previewObject = null;
            }
        }

        // ========================================================
        // EditorPrefs SAVE / LOAD
        // ========================================================
        void SaveToEditorPrefs()
        {
            EditorPrefs.SetFloat(PREF_PREFIX + "trunkHeight", trunkHeight);
            EditorPrefs.SetFloat(PREF_PREFIX + "trunkBaseRadius", trunkBaseRadius);
            EditorPrefs.SetFloat(PREF_PREFIX + "trunkTopRadius", trunkTopRadius);
            EditorPrefs.SetFloat(PREF_PREFIX + "trunkBendX", trunkBendX);
            EditorPrefs.SetFloat(PREF_PREFIX + "trunkBendZ", trunkBendZ);
            EditorPrefs.SetInt(PREF_PREFIX + "trunkSegments", trunkSegments);
            EditorPrefs.SetInt(PREF_PREFIX + "trunkRadialSegments", trunkRadialSegments);
            SaveColor(PREF_PREFIX + "trunkColor", trunkColor);
            SaveCurve(PREF_PREFIX + "trunkRadiusCurve", trunkRadiusCurve);

            EditorPrefs.SetInt(PREF_PREFIX + "branchLevels", branchLevels);
            EditorPrefs.SetInt(PREF_PREFIX + "branchesPerNode", branchesPerNode);
            EditorPrefs.SetFloat(PREF_PREFIX + "branchLengthMin", branchLengthMin);
            EditorPrefs.SetFloat(PREF_PREFIX + "branchLengthMax", branchLengthMax);
            EditorPrefs.SetFloat(PREF_PREFIX + "branchRadiusRatio", branchRadiusRatio);
            EditorPrefs.SetFloat(PREF_PREFIX + "branchAngleMin", branchAngleMin);
            EditorPrefs.SetFloat(PREF_PREFIX + "branchAngleMax", branchAngleMax);
            EditorPrefs.SetFloat(PREF_PREFIX + "branchStartHeight", branchStartHeight);
            EditorPrefs.SetFloat(PREF_PREFIX + "branchBend", branchBend);
            EditorPrefs.SetInt(PREF_PREFIX + "branchSegments", branchSegments);
            EditorPrefs.SetInt(PREF_PREFIX + "branchRadialSegments", branchRadialSegments);
            EditorPrefs.SetFloat(PREF_PREFIX + "branchTaper", branchTaper);

            EditorPrefs.SetFloat(PREF_PREFIX + "leafScaleMin", leafScaleMin);
            EditorPrefs.SetFloat(PREF_PREFIX + "leafScaleMax", leafScaleMax);
            EditorPrefs.SetBool(PREF_PREFIX + "randomLeafRotation", randomLeafRotation);
            EditorPrefs.SetFloat(PREF_PREFIX + "leafOffsetFromTip", leafOffsetFromTip);

            EditorPrefs.SetInt(PREF_PREFIX + "randomSeed", randomSeed);
            EditorPrefs.SetString(PREF_PREFIX + "prefabName", prefabName);
            EditorPrefs.SetString(PREF_PREFIX + "savePath", savePath);
            EditorPrefs.SetBool(PREF_PREFIX + "autoRefresh", autoRefresh);
            EditorPrefs.SetBool(PREF_PREFIX + "combineMeshes", combineMeshes);

            if (leafPrefab != null)
            {
                string lpath = AssetDatabase.GetAssetPath(leafPrefab);
                EditorPrefs.SetString(PREF_PREFIX + "leafPrefabGUID", AssetDatabase.AssetPathToGUID(lpath));
            }
            else
            {
                EditorPrefs.SetString(PREF_PREFIX + "leafPrefabGUID", "");
            }
        }

        void LoadFromEditorPrefs()
        {
            if (!EditorPrefs.HasKey(PREF_PREFIX + "trunkHeight")) return;

            trunkHeight = EditorPrefs.GetFloat(PREF_PREFIX + "trunkHeight", trunkHeight);
            trunkBaseRadius = EditorPrefs.GetFloat(PREF_PREFIX + "trunkBaseRadius", trunkBaseRadius);
            trunkTopRadius = EditorPrefs.GetFloat(PREF_PREFIX + "trunkTopRadius", trunkTopRadius);
            trunkBendX = EditorPrefs.GetFloat(PREF_PREFIX + "trunkBendX", trunkBendX);
            trunkBendZ = EditorPrefs.GetFloat(PREF_PREFIX + "trunkBendZ", trunkBendZ);
            trunkSegments = EditorPrefs.GetInt(PREF_PREFIX + "trunkSegments", trunkSegments);
            trunkRadialSegments = EditorPrefs.GetInt(PREF_PREFIX + "trunkRadialSegments", trunkRadialSegments);
            trunkColor = LoadColor(PREF_PREFIX + "trunkColor", trunkColor);
            trunkRadiusCurve = LoadCurve(PREF_PREFIX + "trunkRadiusCurve", trunkRadiusCurve);

            branchLevels = EditorPrefs.GetInt(PREF_PREFIX + "branchLevels", branchLevels);
            branchesPerNode = EditorPrefs.GetInt(PREF_PREFIX + "branchesPerNode", branchesPerNode);
            branchLengthMin = EditorPrefs.GetFloat(PREF_PREFIX + "branchLengthMin", branchLengthMin);
            branchLengthMax = EditorPrefs.GetFloat(PREF_PREFIX + "branchLengthMax", branchLengthMax);
            branchRadiusRatio = EditorPrefs.GetFloat(PREF_PREFIX + "branchRadiusRatio", branchRadiusRatio);
            branchAngleMin = EditorPrefs.GetFloat(PREF_PREFIX + "branchAngleMin", branchAngleMin);
            branchAngleMax = EditorPrefs.GetFloat(PREF_PREFIX + "branchAngleMax", branchAngleMax);
            branchStartHeight = EditorPrefs.GetFloat(PREF_PREFIX + "branchStartHeight", branchStartHeight);
            branchBend = EditorPrefs.GetFloat(PREF_PREFIX + "branchBend", branchBend);
            branchSegments = EditorPrefs.GetInt(PREF_PREFIX + "branchSegments", branchSegments);
            branchRadialSegments = EditorPrefs.GetInt(PREF_PREFIX + "branchRadialSegments", branchRadialSegments);
            branchTaper = EditorPrefs.GetFloat(PREF_PREFIX + "branchTaper", branchTaper);

            leafScaleMin = EditorPrefs.GetFloat(PREF_PREFIX + "leafScaleMin", leafScaleMin);
            leafScaleMax = EditorPrefs.GetFloat(PREF_PREFIX + "leafScaleMax", leafScaleMax);
            randomLeafRotation = EditorPrefs.GetBool(PREF_PREFIX + "randomLeafRotation", randomLeafRotation);
            leafOffsetFromTip = EditorPrefs.GetFloat(PREF_PREFIX + "leafOffsetFromTip", leafOffsetFromTip);

            randomSeed = EditorPrefs.GetInt(PREF_PREFIX + "randomSeed", randomSeed);
            prefabName = EditorPrefs.GetString(PREF_PREFIX + "prefabName", prefabName);
            savePath = EditorPrefs.GetString(PREF_PREFIX + "savePath", savePath);
            autoRefresh = EditorPrefs.GetBool(PREF_PREFIX + "autoRefresh", autoRefresh);
            combineMeshes = EditorPrefs.GetBool(PREF_PREFIX + "combineMeshes", combineMeshes);

            leafPrefabGUID = EditorPrefs.GetString(PREF_PREFIX + "leafPrefabGUID", "");
            if (!string.IsNullOrEmpty(leafPrefabGUID))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(leafPrefabGUID);
                if (!string.IsNullOrEmpty(assetPath))
                    leafPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            }
        }

        void SaveColor(string key, Color c)
        {
            EditorPrefs.SetFloat(key + "_r", c.r);
            EditorPrefs.SetFloat(key + "_g", c.g);
            EditorPrefs.SetFloat(key + "_b", c.b);
            EditorPrefs.SetFloat(key + "_a", c.a);
        }

        Color LoadColor(string key, Color fallback)
        {
            if (!EditorPrefs.HasKey(key + "_r")) return fallback;
            return new Color(
                EditorPrefs.GetFloat(key + "_r"),
                EditorPrefs.GetFloat(key + "_g"),
                EditorPrefs.GetFloat(key + "_b"),
                EditorPrefs.GetFloat(key + "_a")
            );
        }

        void SaveCurve(string key, AnimationCurve curve)
        {
            var wrapper = new CurveWrapper { curve = curve };
            EditorPrefs.SetString(key, JsonUtility.ToJson(wrapper));
        }

        AnimationCurve LoadCurve(string key, AnimationCurve fallback)
        {
            string json = EditorPrefs.GetString(key, "");
            if (string.IsNullOrEmpty(json)) return fallback;
            try
            {
                var wrapper = JsonUtility.FromJson<CurveWrapper>(json);
                return wrapper.curve ?? fallback;
            }
            catch { return fallback; }
        }

        [System.Serializable]
        class CurveWrapper { public AnimationCurve curve; }

        // ========================================================
        // GUI - Fixed layout: all elements always drawn (disabled when N/A)
        // This prevents the GUILayout Begin/End mismatch error
        // ========================================================
        void OnGUI()
        {
            bool changed = false;

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            // Preset buttons
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Save Preset", GUILayout.Width(90), GUILayout.Height(20)))
            {
                var preset = new Preset(this);
                var path = EditorUtility.SaveFilePanelInProject("Save Tree Preset", prefabName + "_Preset", "preset", "Save");
                if (!string.IsNullOrEmpty(path))
                {
                    AssetDatabase.CreateAsset(preset, path);
                    AssetDatabase.SaveAssets();
                }
            }
            if (GUILayout.Button("Load Preset", GUILayout.Width(90), GUILayout.Height(20)))
            {
                var path = EditorUtility.OpenFilePanel("Load Tree Preset", "Assets", "preset");
                if (!string.IsNullOrEmpty(path))
                {
                    if (path.StartsWith(Application.dataPath))
                        path = "Assets" + path.Substring(Application.dataPath.Length);
                    var preset = AssetDatabase.LoadAssetAtPath<Preset>(path);
                    if (preset != null && preset.CanBeAppliedTo(this))
                    {
                        preset.ApplyTo(this);
                        needsRefresh = true;
                        Repaint();
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // ============ TRUNK ============
            showTrunk = EditorGUILayout.Foldout(showTrunk, "Trunk", true, EditorStyles.foldoutHeader);
            if (showTrunk)
            {
                EditorGUI.indentLevel++;
                changed |= DrawFloat(ref trunkHeight, "Height", 0.5f, 10f);
                changed |= DrawFloat(ref trunkBaseRadius, "Base Radius", 0.02f, 0.6f);
                changed |= DrawFloat(ref trunkTopRadius, "Top Radius", 0.01f, 0.4f);
                changed |= DrawFloat(ref trunkBendX, "Bend X", -2f, 2f);
                changed |= DrawFloat(ref trunkBendZ, "Bend Z", -2f, 2f);
                changed |= DrawInt(ref trunkSegments, "Height Segments", 3, 20);
                changed |= DrawInt(ref trunkRadialSegments, "Radial Segments", 4, 16);

                EditorGUI.BeginChangeCheck();
                trunkRadiusCurve = EditorGUILayout.CurveField("Radius Curve", trunkRadiusCurve);
                if (EditorGUI.EndChangeCheck()) changed = true;

                changed |= DrawColor(ref trunkColor, "Trunk Color");
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);

            // ============ BRANCHES ============
            showBranches = EditorGUILayout.Foldout(showBranches, "Branches", true, EditorStyles.foldoutHeader);
            if (showBranches)
            {
                EditorGUI.indentLevel++;
                changed |= DrawInt(ref branchLevels, "Branch Levels", 1, 4);
                changed |= DrawInt(ref branchesPerNode, "Branches Per Node", 1, 6);
                changed |= DrawFloat(ref branchLengthMin, "Length Min", 0.1f, 3f);
                changed |= DrawFloat(ref branchLengthMax, "Length Max", 0.1f, 4f);
                changed |= DrawFloat(ref branchRadiusRatio, "Radius Ratio", 0.1f, 0.9f);
                changed |= DrawFloat(ref branchAngleMin, "Angle Min", 5f, 80f);
                changed |= DrawFloat(ref branchAngleMax, "Angle Max", 10f, 90f);
                changed |= DrawFloat(ref branchStartHeight, "Start Height (0-1)", 0.1f, 0.95f);
                changed |= DrawFloat(ref branchBend, "Branch Bend", -0.5f, 0.5f);
                changed |= DrawInt(ref branchSegments, "Branch Segments", 2, 10);
                changed |= DrawInt(ref branchRadialSegments, "Branch Radial Segs", 3, 12);
                changed |= DrawFloat(ref branchTaper, "Taper", 0.05f, 0.8f);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);

            // ============ LEAVES ============
            // Always draw all elements to keep layout count stable
            showLeaves = EditorGUILayout.Foldout(showLeaves, "Leaves (Optional)", true, EditorStyles.foldoutHeader);
            if (showLeaves)
            {
                EditorGUI.indentLevel++;
                EditorGUI.BeginChangeCheck();
                leafPrefab = (GameObject)EditorGUILayout.ObjectField("Leaf Prefab", leafPrefab, typeof(GameObject), false);
                if (EditorGUI.EndChangeCheck()) changed = true;

                // Always draw these fields, just disable if no prefab
                bool hasPrefab = leafPrefab != null;
                EditorGUI.BeginDisabledGroup(!hasPrefab);
                changed |= DrawFloat(ref leafScaleMin, "Scale Min", 0.1f, 3f);
                changed |= DrawFloat(ref leafScaleMax, "Scale Max", 0.1f, 5f);
                changed |= DrawFloat(ref leafOffsetFromTip, "Offset From Tip", 0f, 0.5f);
                EditorGUI.BeginChangeCheck();
                randomLeafRotation = EditorGUILayout.Toggle("Random Rotation", randomLeafRotation);
                if (EditorGUI.EndChangeCheck()) changed = true;
                EditorGUI.EndDisabledGroup();

                if (!hasPrefab)
                    EditorGUILayout.HelpBox("Assign a leaf/bush prefab to spawn foliage at branch tips.", MessageType.None);

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);

            // ============ GENERATION ============
            showGeneration = EditorGUILayout.Foldout(showGeneration, "Generation", true, EditorStyles.foldoutHeader);
            if (showGeneration)
            {
                EditorGUI.indentLevel++;
                changed |= DrawInt(ref randomSeed, "Random Seed", 0, 99999);

                EditorGUI.BeginChangeCheck();
                autoRefresh = EditorGUILayout.Toggle("Auto Refresh Preview", autoRefresh);
                if (EditorGUI.EndChangeCheck()) changed = true;

                EditorGUI.BeginChangeCheck();
                prefabName = EditorGUILayout.TextField("Prefab Name", prefabName);
                savePath = EditorGUILayout.TextField("Save Path", savePath);
                if (EditorGUI.EndChangeCheck()) changed = true;

                EditorGUI.BeginChangeCheck();
                combineMeshes = EditorGUILayout.Toggle("Combine Trunk/Branches Mesh", combineMeshes);
                if (EditorGUI.EndChangeCheck()) changed = true;
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(8);

            if (changed && autoRefresh)
                needsRefresh = true;

            // Buttons
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Refresh Preview", GUILayout.Height(28)))
                needsRefresh = true;
            if (GUILayout.Button("Randomize Seed", GUILayout.Height(28)))
            {
                randomSeed = Random.Range(0, 99999);
                needsRefresh = true;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset Defaults", GUILayout.Height(24)))
            {
                if (EditorUtility.DisplayDialog("Reset", "Reset all parameters to defaults?", "Yes", "Cancel"))
                {
                    ResetDefaults();
                    needsRefresh = true;
                }
            }
            if (GUILayout.Button("Clear Preview", GUILayout.Height(24)))
            {
                DestroyPreview();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            GUI.backgroundColor = new Color(0.3f, 0.8f, 0.4f);
            if (GUILayout.Button("Create Prefab", GUILayout.Height(36)))
            {
                CreatePrefab();
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.Space(4);

            if (previewObject != null)
                EditorGUILayout.HelpBox("Preview active in Scene view.", MessageType.Info);

            EditorGUILayout.EndScrollView();

            // Deferred refresh - use delayCall to avoid layout issues
            if (needsRefresh && Event.current.type == EventType.Repaint)
            {
                needsRefresh = false;
                EditorApplication.delayCall += () =>
                {
                    if (this != null)
                    {
                        RegeneratePreview();
                        SaveToEditorPrefs();
                    }
                };
            }
        }

        void ResetDefaults()
        {
            trunkHeight = 3f;
            trunkBaseRadius = 0.15f;
            trunkTopRadius = 0.06f;
            trunkBendX = 0.3f;
            trunkBendZ = 0f;
            trunkSegments = 8;
            trunkRadialSegments = 8;
            trunkRadiusCurve = AnimationCurve.Linear(0, 1, 1, 0.3f);
            trunkColor = new Color(0.25f, 0.2f, 0.15f);
            branchLevels = 2;
            branchesPerNode = 3;
            branchLengthMin = 0.6f;
            branchLengthMax = 1.2f;
            branchRadiusRatio = 0.5f;
            branchAngleMin = 25f;
            branchAngleMax = 55f;
            branchStartHeight = 0.4f;
            branchBend = 0.15f;
            branchSegments = 4;
            branchRadialSegments = 6;
            branchTaper = 0.3f;
            leafScaleMin = 0.6f;
            leafScaleMax = 1.2f;
            randomLeafRotation = true;
            leafOffsetFromTip = 0.05f;
            randomSeed = 42;
            autoRefresh = true;
            combineMeshes = true;
        }

        bool DrawFloat(ref float val, string label, float min, float max)
        {
            EditorGUI.BeginChangeCheck();
            val = EditorGUILayout.Slider(label, val, min, max);
            return EditorGUI.EndChangeCheck();
        }

        bool DrawInt(ref int val, string label, int min, int max)
        {
            EditorGUI.BeginChangeCheck();
            val = EditorGUILayout.IntSlider(label, val, min, max);
            return EditorGUI.EndChangeCheck();
        }

        bool DrawColor(ref Color val, string label)
        {
            EditorGUI.BeginChangeCheck();
            val = EditorGUILayout.ColorField(label, val);
            return EditorGUI.EndChangeCheck();
        }

        // ========================================================
        // PREVIEW
        // ========================================================
        void RegeneratePreview()
        {
            DestroyPreview();
            previewObject = GenerateTree(false);
            previewObject.name = "[Preview] " + prefabName;
            previewObject.hideFlags = HideFlags.DontSave;
            previewObject.tag = "EditorOnly";
            Selection.activeGameObject = previewObject;
            SceneView.RepaintAll();
        }

        void OnSceneGUI(SceneView sceneView)
        {
            if (previewObject == null) return;
            Handles.color = Color.yellow;
            Handles.DrawWireDisc(previewObject.transform.position, Vector3.up, trunkBaseRadius * 2);
        }

        // ========================================================
        // TREE GENERATION
        // ========================================================
        GameObject GenerateTree(bool forPrefab)
        {
            Random.State prevState = Random.state;
            Random.InitState(randomSeed);

            GameObject root = new GameObject(prefabName);

            // Create shared Standard material with trunk color
            sharedTrunkMaterial = new Material(Shader.Find("Standard"));
            sharedTrunkMaterial.name = "TreeBark";
            sharedTrunkMaterial.color = trunkColor;
            sharedTrunkMaterial.SetFloat("_Glossiness", 0.15f);

            var trunkSpine = BuildTrunkSpine();
            var trunkData = GenerateTubeMesh(trunkSpine, trunkBaseRadius, trunkTopRadius, trunkRadiusCurve, trunkSegments, trunkRadialSegments);
            CreateMeshObject("Trunk", trunkData, root.transform, forPrefab);

            var branchTips = new List<BranchTip>();
            GenerateBranches(root.transform, trunkSpine, trunkBaseRadius, trunkTopRadius, 0, branchTips, forPrefab);

            // Attach leaf prefabs
            if (leafPrefab != null && branchTips.Count > 0)
            {
                GameObject leavesParent = new GameObject("Leaves");
                leavesParent.transform.SetParent(root.transform, false);

                foreach (var tip in branchTips)
                {
                    GameObject leaf;
                    if (forPrefab)
                        leaf = (GameObject)PrefabUtility.InstantiatePrefab(leafPrefab, leavesParent.transform);
                    else
                        leaf = Instantiate(leafPrefab, leavesParent.transform);

                    leaf.transform.position = tip.position + tip.direction * leafOffsetFromTip;

                    float scale = Random.Range(leafScaleMin, leafScaleMax);
                    leaf.transform.localScale = Vector3.one * scale;

                    if (randomLeafRotation)
                    {
                        leaf.transform.rotation = Quaternion.Euler(
                            Random.Range(-15f, 15f),
                            Random.Range(0f, 360f),
                            Random.Range(-15f, 15f)
                        );
                    }
                    else
                    {
                        leaf.transform.rotation = Quaternion.identity;
                    }
                }
            }

            Random.state = prevState;
            return root;
        }

        struct BranchTip
        {
            public Vector3 position;
            public Vector3 direction;
        }

        // ========================================================
        // TRUNK SPINE (bidirectional bend on both axes)
        // ========================================================
        Vector3[] BuildTrunkSpine()
        {
            var pts = new Vector3[trunkSegments + 1];
            float baseOffset = -1f * trunkBaseRadius; // Extend below ground
            for (int i = 0; i <= trunkSegments; i++)
            {
                float t = (float)i / trunkSegments;
                float y = t * trunkHeight;
                if (i == 0) y += baseOffset; // Only offset the first point
                float xBend = Mathf.Sin(t * Mathf.PI * 0.7f) * trunkBendX;
                float zBend = Mathf.Sin(t * Mathf.PI * 0.5f) * trunkBendZ;
                pts[i] = new Vector3(xBend, y, zBend);
            }
            return pts;
        }

        // ========================================================
        // BRANCH GENERATION (recursive, with proper radius tapering)
        // ========================================================
        void GenerateBranches(Transform parent, Vector3[] parentSpine, float parentBaseRadius, float parentTipRadius,
            int level, List<BranchTip> tips, bool forPrefab)
        {
            if (level >= branchLevels) return;

            int startSeg = Mathf.CeilToInt(branchStartHeight * (parentSpine.Length - 1));
            if (level > 0) startSeg = Mathf.Max(1, parentSpine.Length / 3);

            int count = branchesPerNode;
            if (level > 0) count = Mathf.Max(1, count - 1);

            float angleOffset = Random.Range(0f, 360f);

            for (int seg = startSeg; seg < parentSpine.Length; seg++)
            {
                if (level == 0 && Random.value < 0.3f) continue;
                if (level > 0 && Random.value < 0.5f) continue;

                // Radius at this point along parent
                float segT = (parentSpine.Length > 1) ? (float)seg / (parentSpine.Length - 1) : 0f;
                float parentRadiusHere = Mathf.Lerp(parentBaseRadius, parentTipRadius, segT);

                for (int b = 0; b < count; b++)
                {
                    float angle = angleOffset + (360f / count) * b + Random.Range(-25f, 25f);
                    float bAngle = Random.Range(branchAngleMin, branchAngleMax);
                    float branchLen = Random.Range(branchLengthMin, branchLengthMax);
                    if (level > 0) branchLen *= 0.6f;

                    float bBaseRadius = parentRadiusHere * branchRadiusRatio;
                    if (level > 0) bBaseRadius *= 0.6f;
                    float bTipRadius = bBaseRadius * branchTaper;

                    Vector3 origin = parentSpine[seg];

                    Vector3 parentDir = Vector3.up;
                    if (seg > 0)
                        parentDir = (parentSpine[seg] - parentSpine[seg - 1]).normalized;

                    Quaternion rot = Quaternion.AngleAxis(angle, parentDir) * Quaternion.AngleAxis(bAngle, Vector3.right);
                    Vector3 branchDir = rot * parentDir;

                    int bSegs = branchSegments;
                    if (level > 0) bSegs = Mathf.Max(2, bSegs - 1);
                    var spine = new Vector3[bSegs + 1];
                    for (int i = 0; i <= bSegs; i++)
                    {
                        float t = (float)i / bSegs;
                        Vector3 bendOffset = Vector3.up * (Mathf.Sin(t * Mathf.PI) * branchBend * branchLen);
                        spine[i] = origin + branchDir * (t * branchLen) + bendOffset;
                    }

                    var bCurve = AnimationCurve.Linear(0, 1, 1, 1);
                    var meshData = GenerateTubeMesh(spine, bBaseRadius, bTipRadius, bCurve, bSegs, branchRadialSegments);
                    CreateMeshObject($"Branch_L{level}_{seg}_{b}", meshData, parent, forPrefab);

                    bool isTerminal = (level == branchLevels - 1);
                    if (isTerminal)
                    {
                        tips.Add(new BranchTip
                        {
                            position = spine[spine.Length - 1],
                            direction = (spine[spine.Length - 1] - spine[spine.Length - 2]).normalized
                        });
                    }
                    else
                    {
                        GenerateBranches(parent, spine, bBaseRadius, bTipRadius, level + 1, tips, forPrefab);
                    }
                }

                angleOffset += Random.Range(60f, 140f);
            }
        }

        // ========================================================
        // TUBE MESH (proper base→tip radius interpolation)
        // ========================================================
        struct TubeMeshData { public Mesh mesh; }

        TubeMeshData GenerateTubeMesh(Vector3[] spine, float baseRadius, float tipRadius,
            AnimationCurve radiusCurve, int segments, int radialSegs)
        {
            int vertCount = (segments + 1) * (radialSegs + 1);
            var vertices = new Vector3[vertCount];
            var normals = new Vector3[vertCount];
            var uvs = new Vector2[vertCount];

            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                Vector3 center = spine[Mathf.Min(i, spine.Length - 1)];

                Vector3 fwd;
                if (i < segments && i + 1 < spine.Length)
                    fwd = (spine[i + 1] - spine[i]).normalized;
                else if (i > 0)
                    fwd = (spine[Mathf.Min(i, spine.Length - 1)] - spine[i - 1]).normalized;
                else
                    fwd = Vector3.up;

                if (fwd.sqrMagnitude < 0.001f) fwd = Vector3.up;

                Vector3 side = Vector3.Cross(fwd, Vector3.forward);
                if (side.sqrMagnitude < 0.001f)
                    side = Vector3.Cross(fwd, Vector3.right);
                side.Normalize();
                Vector3 up = Vector3.Cross(side, fwd).normalized;

                // Lerp base→tip, then apply the curve on top
                float radius = Mathf.Lerp(baseRadius, tipRadius, t) * radiusCurve.Evaluate(t);

                // Organic wobble
                float wobble = 1f + Mathf.Sin(t * 13.7f + i * 2.1f) * 0.04f;
                radius *= wobble;

                for (int j = 0; j <= radialSegs; j++)
                {
                    float angle = (float)j / radialSegs * Mathf.PI * 2f;
                    Vector3 offset = (Mathf.Cos(angle) * side + Mathf.Sin(angle) * up) * radius;

                    float bark = 1f + Mathf.Sin(angle * 5f + t * 8f) * 0.03f;
                    offset *= bark;

                    int idx = i * (radialSegs + 1) + j;
                    vertices[idx] = center + offset;
                    normals[idx] = offset.normalized;
                    uvs[idx] = new Vector2((float)j / radialSegs, t);
                }
            }

            var triangles = new List<int>();
            for (int i = 0; i < segments; i++)
            {
                for (int j = 0; j < radialSegs; j++)
                {
                    int a = i * (radialSegs + 1) + j;
                    int b = a + 1;
                    int c = a + radialSegs + 1;
                    int d = c + 1;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }

            // Bottom cap
            var vertList = new List<Vector3>(vertices);
            var normList = new List<Vector3>(normals);
            var uvList = new List<Vector2>(uvs);

            vertList.Add(spine[0]);
            normList.Add(-Vector3.up);
            uvList.Add(new Vector2(0.5f, 0));
            int bci = vertList.Count - 1;

            for (int j = 0; j < radialSegs; j++)
            {
                int a = j;
                int b = (j + 1) % (radialSegs + 1);
                triangles.Add(bci); triangles.Add(a); triangles.Add(b);
            }

            Mesh mesh = new Mesh();
            mesh.name = "TubeMesh";
            mesh.vertices = vertList.ToArray();
            mesh.normals = normList.ToArray();
            mesh.uv = uvList.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();

            return new TubeMeshData { mesh = mesh };
        }

        // ========================================================
        // CREATE MESH OBJECT (Standard shader, trunk color)
        // ========================================================
        GameObject CreateMeshObject(string name, TubeMeshData data, Transform parent, bool forPrefab)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = data.mesh;

            var mr = go.AddComponent<MeshRenderer>();

            if (!forPrefab)
            {
                mr.sharedMaterial = sharedTrunkMaterial;
            }
            // If forPrefab, do not assign any material here. Assignment is handled in CreatePrefab after asset creation.

            return go;
        }

        // ========================================================
        // CREATE PREFAB
        // ========================================================
        void CreatePrefab()
        {
            EnsureFolderExists(savePath);

            GameObject treeObj = GenerateTree(true);
            treeObj.name = prefabName;

            string meshFolder = AssetPathCombine(savePath, prefabName + "_Meshes");
            EnsureFolderExists(meshFolder);

            // Create bark material asset
            var trunkMat = new Material(Shader.Find("Standard"));
            trunkMat.name = prefabName + "_Bark";
            trunkMat.color = trunkColor;
            trunkMat.SetFloat("_Glossiness", 0.15f);

            string matPath = AssetPathCombine(savePath, prefabName + "_Bark.mat");
            matPath = AssetDatabase.GenerateUniqueAssetPath(matPath);
            AssetDatabase.CreateAsset(trunkMat, matPath);

            if (combineMeshes)
            {
                CombineTreeMeshes(treeObj, trunkMat, meshFolder);
            }
            else
            {
                SaveAndAssignMeshes(treeObj.transform, meshFolder, trunkMat);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string prefabPath = AssetPathCombine(savePath, prefabName + ".prefab");
            prefabPath = AssetDatabase.GenerateUniqueAssetPath(prefabPath);

            PrefabUtility.SaveAsPrefabAssetAndConnect(treeObj, prefabPath, InteractionMode.UserAction);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[TreeGenerator] Prefab saved to: " + prefabPath);

            var prefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefabAsset != null)
                EditorGUIUtility.PingObject(prefabAsset);

            DestroyImmediate(treeObj);
        }

        static string AssetPathCombine(params string[] parts)
        {
            return string.Join("/", parts).Replace("\\", "/");
        }

        void SaveAndAssignMeshes(Transform root, string meshFolder, Material trunkMat)
        {
            int meshIndex = 0;

            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null)
                    continue;

                bool isGeneratedWoodPart =
                    mf.gameObject.name == "Trunk" ||
                    mf.gameObject.name.StartsWith("Branch_");

                if (!EditorUtility.IsPersistent(mf.sharedMesh))
                {
                    string meshPath = AssetPathCombine(meshFolder, "mesh_" + meshIndex + ".asset");
                    meshPath = AssetDatabase.GenerateUniqueAssetPath(meshPath);
                    AssetDatabase.CreateAsset(mf.sharedMesh, meshPath);

                    Mesh savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
                    if (savedMesh != null)
                        mf.sharedMesh = savedMesh;

                    meshIndex++;
                }

                if (isGeneratedWoodPart)
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr != null)
                        mr.sharedMaterial = trunkMat;
                }
            }
        }

        void CombineTreeMeshes(GameObject treeObj, Material trunkMat, string meshFolder)
        {
            var sourceFilters = new List<MeshFilter>();

            foreach (var mf in treeObj.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh != null && mf.sharedMesh.name == "TubeMesh")
                    sourceFilters.Add(mf);
            }

            if (sourceFilters.Count == 0)
                return;

            var combine = new CombineInstance[sourceFilters.Count];
            Matrix4x4 rootWorldToLocal = treeObj.transform.worldToLocalMatrix;

            for (int i = 0; i < sourceFilters.Count; i++)
            {
                combine[i] = new CombineInstance
                {
                    mesh = sourceFilters[i].sharedMesh,
                    transform = rootWorldToLocal * sourceFilters[i].transform.localToWorldMatrix
                };
            }

            var combinedMesh = new Mesh();
            combinedMesh.name = prefabName + "_CombinedMesh";
            combinedMesh.CombineMeshes(combine, true, true);
            combinedMesh.RecalculateBounds();
            combinedMesh.RecalculateNormals();

            string combinedMeshPath = AssetPathCombine(meshFolder, prefabName + "_Combined.asset");
            combinedMeshPath = AssetDatabase.GenerateUniqueAssetPath(combinedMeshPath);
            AssetDatabase.CreateAsset(combinedMesh, combinedMeshPath);

            // Remove old generated trunk/branch objects
            foreach (var mf in sourceFilters)
            {
                if (mf != null && mf.gameObject != null)
                    DestroyImmediate(mf.gameObject);
            }

            // Create single combined object
            var combinedGO = new GameObject("TreeCombined");
            combinedGO.transform.SetParent(treeObj.transform, false);

            var mfCombined = combinedGO.AddComponent<MeshFilter>();
            mfCombined.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(combinedMeshPath);

            var mrCombined = combinedGO.AddComponent<MeshRenderer>();
            mrCombined.sharedMaterial = trunkMat;
        }

        static void EnsureFolderExists(string assetPath)
        {
            assetPath = assetPath.Replace("\\", "/");

            if (AssetDatabase.IsValidFolder(assetPath))
                return;

            string[] split = assetPath.Split('/');
            string current = split[0];

            for (int i = 1; i < split.Length; i++)
            {
                string next = current + "/" + split[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, split[i]);
                current = next;
            }
        }

        void SaveMeshesRecursive(Transform t, string folder, ref int index)
        {
            var mf = t.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                // Only save mesh if it is not already an asset (not persistent)
                if (!EditorUtility.IsPersistent(mf.sharedMesh))
                {
                    string meshPath = Path.Combine(folder, $"mesh_{index}.asset");
                    meshPath = AssetDatabase.GenerateUniqueAssetPath(meshPath);
                    AssetDatabase.CreateAsset(mf.sharedMesh, meshPath);
                    index++;
                }
            }

            for (int i = 0; i < t.childCount; i++)
                SaveMeshesRecursive(t.GetChild(i), folder, ref index);
        }
    }
}