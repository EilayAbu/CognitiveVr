using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor tool that recreates the apartment Window as a new object whose 4 panes
/// (2x2 grid) are separate, independent GameObjects.
/// </summary>
public static class WindowSeparator
{
    const string FbxPath = "Assets/Studio Apartment 2/Meshes/Window.fbx";
    const string FrameMatPath = "Assets/Studio Apartment 2/Meshes/Materials/Window.mat";
    const string MetalMatPath = "Assets/Studio Apartment 2/Meshes/Materials/Metall.mat";
    const string GlassMatPath = "Assets/Studio Apartment 2/Meshes/Materials/Windows Glass.mat";
    const string OutputMeshFolder = "Assets/Studio Apartment 2/Meshes/WindowSeparated";

    // Local-space split lines (see plan analysis).
    const float SplitY = 0.78f;   // mid horizontal mullion: bottom vs top row
    const float SplitZ = 0f;      // center mullion: left vs right column

    // Quadrant indices.
    enum Q { BL = 0, BR = 1, TL = 2, TR = 3 }
    static readonly string[] QNames = { "BL", "BR", "TL", "TR" };

    [MenuItem("Tools/Window/Create Separated Window")]
    public static void CreateSeparatedWindow()
    {
        // --- Load source meshes from the FBX (frame + glass) ---
        Mesh frameSrc = null;
        Mesh glassSrc = null;
        var subAssets = AssetDatabase.LoadAllAssetsAtPath(FbxPath);
        foreach (var a in subAssets)
        {
            if (!(a is Mesh m)) continue;
            if (m.name == "Window") frameSrc = m;
            else if (m.name == "Window Glass") glassSrc = m;
        }
        if (frameSrc == null || glassSrc == null)
        {
            Debug.LogError($"WindowSeparator: could not find 'Window' and 'Window Glass' meshes in {FbxPath}.");
            return;
        }

        var frameMat = AssetDatabase.LoadAssetAtPath<Material>(FrameMatPath);
        var metalMat = AssetDatabase.LoadAssetAtPath<Material>(MetalMatPath);
        var glassMat = AssetDatabase.LoadAssetAtPath<Material>(GlassMatPath);

        // --- Ensure output folder exists ---
        if (!AssetDatabase.IsValidFolder(OutputMeshFolder))
        {
            if (!AssetDatabase.IsValidFolder("Assets/Studio Apartment 2/Meshes"))
            {
                Debug.LogError("WindowSeparator: source Meshes folder not found.");
                return;
            }
            AssetDatabase.CreateFolder("Assets/Studio Apartment 2/Meshes", "WindowSeparated");
        }

        // --- Split both meshes into 4 quadrants ---
        var frameParts = SplitMesh(frameSrc);
        var glassParts = SplitMesh(glassSrc);

        // --- Save the split meshes as assets ---
        var frameAssets = new Mesh[4];
        var glassAssets = new Mesh[4];
        for (int q = 0; q < 4; q++)
        {
            frameAssets[q] = SaveMeshAsset(frameParts[q], $"Window_{QNames[q]}");
            glassAssets[q] = SaveMeshAsset(glassParts[q], $"Glass_{QNames[q]}");
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // --- Find the source Window (1) to copy its world transform ---
        Transform sourceXform = FindSourceWindowTransform();
        Vector3 worldPos = new Vector3(2.65f, 0.85f, -1.90f);
        Quaternion worldRot = Quaternion.identity;
        Vector3 worldScale = Vector3.one;
        Transform parent = null;
        if (sourceXform != null)
        {
            worldPos = sourceXform.position;
            worldRot = sourceXform.rotation;
            worldScale = sourceXform.lossyScale;
            parent = sourceXform.parent;
        }

        // --- Build the new scene object ---
        var root = new GameObject("Window (Separated)");
        Undo.RegisterCreatedObjectUndo(root, "Create Separated Window");
        if (parent != null) root.transform.SetParent(parent, false);
        root.transform.position = worldPos;
        root.transform.rotation = worldRot;
        root.transform.localScale = Vector3.one;

        for (int q = 0; q < 4; q++)
        {
            BuildPane((Q)q, root.transform, frameAssets[q], glassAssets[q],
                      frameMat, metalMat, glassMat);
        }

        Selection.activeGameObject = root;
        EditorGUIUtility.PingObject(root);
        Debug.Log("WindowSeparator: created 'Window (Separated)' with 4 individual panes (no VR components).");
    }

    static Transform FindSourceWindowTransform()
    {
        // Prefer the exact known object name in the loaded scene.
        foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
        {
            if (go.name == "Window (1)") return go.transform;
        }
        return null;
    }

    static void BuildPane(Q q, Transform root, Mesh frameMesh, Mesh glassMesh,
                          Material frameMat, Material metalMat, Material glassMat)
    {
        var pane = new GameObject($"Pane {QNames[(int)q]}");
        pane.transform.SetParent(root, false);
        pane.transform.localPosition = Vector3.zero;
        pane.transform.localRotation = Quaternion.identity;
        pane.transform.localScale = Vector3.one;

        // Frame child
        var frameGo = new GameObject("Frame");
        frameGo.transform.SetParent(pane.transform, false);
        frameGo.AddComponent<MeshFilter>().sharedMesh = frameMesh;
        frameGo.AddComponent<MeshRenderer>().sharedMaterials = new[] { frameMat, metalMat };

        // Glass child
        var glassGo = new GameObject("Glass");
        glassGo.transform.SetParent(pane.transform, false);
        glassGo.AddComponent<MeshFilter>().sharedMesh = glassMesh;
        glassGo.AddComponent<MeshRenderer>().sharedMaterials = new[] { glassMat };

        // Collider sized to the combined pane bounds (frame is the larger mesh).
        Bounds b = frameMesh.bounds;
        if (glassMesh.vertexCount > 0) b.Encapsulate(glassMesh.bounds);
        var box = pane.AddComponent<BoxCollider>();
        box.center = b.center;
        box.size = new Vector3(Mathf.Max(b.size.x, 0.06f), b.size.y, b.size.z);
    }

    // ---------------------------------------------------------------------
    // Mesh splitting
    // ---------------------------------------------------------------------

    static Mesh[] SplitMesh(Mesh src)
    {
        var verts = src.vertices;
        var normals = src.normals;
        var tangents = src.tangents;
        var uv = src.uv;
        var uv2 = src.uv2;
        var colors = src.colors;

        bool hasNormals = normals != null && normals.Length == verts.Length;
        bool hasTangents = tangents != null && tangents.Length == verts.Length;
        bool hasUv = uv != null && uv.Length == verts.Length;
        bool hasUv2 = uv2 != null && uv2.Length == verts.Length;
        bool hasColors = colors != null && colors.Length == verts.Length;

        int subCount = src.subMeshCount;

        // Per quadrant: a vertex remap + per-submesh triangle lists.
        var remap = new Dictionary<int, int>[4];
        var newVerts = new List<Vector3>[4];
        var newNormals = new List<Vector3>[4];
        var newTangents = new List<Vector4>[4];
        var newUv = new List<Vector2>[4];
        var newUv2 = new List<Vector2>[4];
        var newColors = new List<Color>[4];
        var newTris = new List<int>[4][];

        for (int q = 0; q < 4; q++)
        {
            remap[q] = new Dictionary<int, int>();
            newVerts[q] = new List<Vector3>();
            newNormals[q] = new List<Vector3>();
            newTangents[q] = new List<Vector4>();
            newUv[q] = new List<Vector2>();
            newUv2[q] = new List<Vector2>();
            newColors[q] = new List<Color>();
            newTris[q] = new List<int>[subCount];
            for (int s = 0; s < subCount; s++) newTris[q][s] = new List<int>();
        }

        System.Func<int, int, int> getIndex = (q, oldIdx) =>
        {
            if (remap[q].TryGetValue(oldIdx, out int ni)) return ni;
            ni = newVerts[q].Count;
            remap[q][oldIdx] = ni;
            newVerts[q].Add(verts[oldIdx]);
            if (hasNormals) newNormals[q].Add(normals[oldIdx]);
            if (hasTangents) newTangents[q].Add(tangents[oldIdx]);
            if (hasUv) newUv[q].Add(uv[oldIdx]);
            if (hasUv2) newUv2[q].Add(uv2[oldIdx]);
            if (hasColors) newColors[q].Add(colors[oldIdx]);
            return ni;
        };

        for (int s = 0; s < subCount; s++)
        {
            var tris = src.GetTriangles(s);
            for (int i = 0; i < tris.Length; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                Vector3 centroid = (verts[a] + verts[b] + verts[c]) / 3f;
                int q = QuadrantOf(centroid);
                newTris[q][s].Add(getIndex(q, a));
                newTris[q][s].Add(getIndex(q, b));
                newTris[q][s].Add(getIndex(q, c));
            }
        }

        var result = new Mesh[4];
        for (int q = 0; q < 4; q++)
        {
            var mesh = new Mesh();
            mesh.indexFormat = newVerts[q].Count > 65000
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(newVerts[q]);
            if (hasNormals) mesh.SetNormals(newNormals[q]);
            if (hasTangents) mesh.SetTangents(newTangents[q]);
            if (hasUv) mesh.SetUVs(0, newUv[q]);
            if (hasUv2) mesh.SetUVs(1, newUv2[q]);
            if (hasColors) mesh.SetColors(newColors[q]);
            mesh.subMeshCount = subCount;
            for (int s = 0; s < subCount; s++)
                mesh.SetTriangles(newTris[q][s], s);
            if (!hasNormals) mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            result[q] = mesh;
        }
        return result;
    }

    static int QuadrantOf(Vector3 c)
    {
        bool right = c.z >= SplitZ; // +Z column
        bool top = c.y >= SplitY;
        if (top) return right ? (int)Q.TL : (int)Q.TR;
        return right ? (int)Q.BL : (int)Q.BR;
    }

    static Mesh SaveMeshAsset(Mesh mesh, string name)
    {
        mesh.name = name;
        string path = $"{OutputMeshFolder}/{name}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (existing != null)
        {
            existing.Clear();
            EditorUtility.CopySerialized(mesh, existing);
            return existing;
        }
        AssetDatabase.CreateAsset(mesh, path);
        return mesh;
    }
}

