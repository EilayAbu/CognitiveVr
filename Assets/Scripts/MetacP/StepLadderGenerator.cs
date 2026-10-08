// Place this file in any "Editor" folder, e.g. Assets/Editor/StepLadderGenerator.cs
// Then open: Tools > Step Ladder Generator
// Builds a folding step ladder (white frame, black treads, rubber grip handle),
// with real-world scale in meters and the pivot at floor level.
// Works with URP and the Built-in pipeline.

using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;

public class StepLadderGenerator : EditorWindow
{
    int steps = 2;
    float width = 0.42f;          // distance between front rails
    float stepRise = 0.23f;       // vertical spacing between steps
    float handleHeight = 0.40f;   // how far the handle rises above the top step
    Color frameColor = new Color(0.95f, 0.95f, 0.95f);
    Color darkColor = new Color(0.10f, 0.10f, 0.10f);
    bool addRigidbody = false;
    bool savePrefab = true;

    const string Folder = "Assets/StepLadder";

    [MenuItem("Tools/Step Ladder Generator")]
    static void Open() => GetWindow<StepLadderGenerator>("Step Ladder");

    void OnGUI()
    {
        steps        = EditorGUILayout.IntSlider("Steps", steps, 1, 4);
        width        = EditorGUILayout.Slider("Width (m)", width, 0.30f, 0.60f);
        stepRise     = EditorGUILayout.Slider("Step Rise (m)", stepRise, 0.18f, 0.28f);
        handleHeight = EditorGUILayout.Slider("Handle Height (m)", handleHeight, 0.05f, 0.60f);
        frameColor   = EditorGUILayout.ColorField("Frame Color", frameColor);
        darkColor    = EditorGUILayout.ColorField("Tread / Grip Color", darkColor);
        addRigidbody = EditorGUILayout.Toggle("Add Rigidbody", addRigidbody);
        savePrefab   = EditorGUILayout.Toggle("Save as Prefab", savePrefab);

        GUILayout.Space(8);
        if (GUILayout.Button("Build Ladder", GUILayout.Height(32))) Build();
    }

    void Build()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "StepLadder");
        Material frameMat = GetMat("Ladder_Frame", frameColor, 0.15f, 0.55f);
        Material darkMat  = GetMat("Ladder_Rubber", darkColor, 0f, 0.15f);

        var root = new GameObject("StepLadder").transform;

        // ---- dimensions ----
        float r = 0.0125f;                 // tube radius (25 mm tube)
        float lean = 0.28f;                // front rail slope (z per meter of height)
        float halfW = width * 0.5f;
        float hTop = steps * stepRise;     // height of top step surface
        float stepDepth = 0.21f, topDepth = 0.27f, stepThick = 0.03f;
        float railTopY = hTop + handleHeight;

        float FrontZ(float y) => (hTop - y) * lean;   // front rails pass z=0 at top-step height

        float topStepFrontZ = FrontZ(hTop) + 0.015f;
        float rearTopY  = hTop - stepThick;
        float rearTopZ  = topStepFrontZ - topDepth + 0.03f;   // under back edge of top step
        float rearFootZ = rearTopZ - hTop * 0.45f;
        float rearHalfW = halfW - 0.03f;

        float RearZ(float y) => Mathf.Lerp(rearFootZ, rearTopZ, y / rearTopY);

        // ---- front rails ----
        for (int s = -1; s <= 1; s += 2)
        {
            Rod(root, "FrontRail", new Vector3(s * halfW, 0, FrontZ(0)),
                                   new Vector3(s * halfW, railTopY, FrontZ(railTopY)), r, frameMat);
            Box(root, "Foot", new Vector3(s * halfW, 0.012f, FrontZ(0)), new Vector3(0.04f, 0.025f, 0.055f), darkMat);
        }

        // ---- handle arch with rubber grip ----
        Vector3 railDir = new Vector3(0, 1, -lean).normalized;
        Vector3 c = new Vector3(0, railTopY, FrontZ(railTopY));
        float gripR = r * 1.7f;
        int seg = 14;
        Vector3 prev = c + Vector3.left * halfW;
        Ball(root, prev, gripR, darkMat);
        for (int i = 1; i <= seg; i++)
        {
            float t = Mathf.PI * (1f - i / (float)seg);
            Vector3 p = c + Vector3.right * (Mathf.Cos(t) * halfW) + railDir * (Mathf.Sin(t) * halfW * 0.55f);
            Rod(root, "HandleGrip", prev, p, gripR, darkMat);
            Ball(root, p, gripR, darkMat);
            prev = p;
        }

        // ---- steps (white pan + black tread) ----
        for (int i = 1; i <= steps; i++)
        {
            float y = i * stepRise;
            float depth = (i == steps) ? topDepth : stepDepth;
            float zFront = FrontZ(y) + 0.015f;
            float zCenter = zFront - depth * 0.5f;
            float innerW = width - 2f * r;

            Box(root, $"Step{i}", new Vector3(0, y - stepThick * 0.5f, zCenter),
                new Vector3(innerW, stepThick, depth), frameMat);
            var tread = Box(root, $"Step{i}_Tread", new Vector3(0, y + 0.003f, zCenter),
                new Vector3(innerW - 0.03f, 0.006f, depth - 0.03f), darkMat);
            Object.DestroyImmediate(tread.GetComponent<Collider>());
        }

        // ---- rear legs ----
        for (int s = -1; s <= 1; s += 2)
        {
            Rod(root, "RearLeg", new Vector3(s * rearHalfW, 0, rearFootZ),
                                 new Vector3(s * rearHalfW, rearTopY, rearTopZ), r, frameMat);
            Box(root, "Foot", new Vector3(s * rearHalfW, 0.012f, rearFootZ), new Vector3(0.04f, 0.025f, 0.055f), darkMat);

            // side spreader link between front rail and rear leg
            float ly = Mathf.Min(0.2f, stepRise * 0.8f);
            Rod(root, "SideLink", new Vector3(s * halfW, ly, FrontZ(ly)),
                                  new Vector3(s * rearHalfW, ly, RearZ(ly)), r * 0.6f, frameMat);
        }

        // rear cross brace
        float by = 0.12f;
        Rod(root, "RearBrace", new Vector3(-rearHalfW, by, RearZ(by)),
                               new Vector3( rearHalfW, by, RearZ(by)), r * 0.8f, frameMat);

        if (addRigidbody) root.gameObject.AddComponent<Rigidbody>().mass = 3.5f;

        Undo.RegisterCreatedObjectUndo(root.gameObject, "Create Step Ladder");
        Selection.activeGameObject = root.gameObject;

        if (savePrefab)
        {
            string path = AssetDatabase.GenerateUniqueAssetPath($"{Folder}/StepLadder.prefab");
            PrefabUtility.SaveAsPrefabAssetAndConnect(root.gameObject, path, InteractionMode.UserAction);
            Debug.Log($"Step ladder saved to {path}");
        }
    }

    // ---------- helpers ----------

    static GameObject Rod(Transform parent, string name, Vector3 a, Vector3 b, float radius, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(parent, false);
        Vector3 d = b - a;
        go.transform.localPosition = (a + b) * 0.5f;
        go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, d);
        go.transform.localScale = new Vector3(radius * 2f, d.magnitude * 0.5f, radius * 2f);
        go.GetComponent<Renderer>().sharedMaterial = m;
        return go;
    }

    static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = center;
        go.transform.localScale = size;
        go.GetComponent<Renderer>().sharedMaterial = m;
        return go;
    }

    static void Ball(Transform parent, Vector3 pos, float radius, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "Joint";
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = Vector3.one * radius * 2f;
        go.GetComponent<Renderer>().sharedMaterial = m;
        Object.DestroyImmediate(go.GetComponent<Collider>());
    }

    static Material GetMat(string name, Color col, float metallic, float smooth)
    {
        string path = $"{Folder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            bool srp = GraphicsSettings.currentRenderPipeline != null;
            Shader sh = Shader.Find(srp ? "Universal Render Pipeline/Lit" : "Standard");
            if (sh == null) sh = Shader.Find("Standard");
            mat = new Material(sh);
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.color = col;
        if (mat.HasProperty("_BaseColor"))  mat.SetColor("_BaseColor", col);   // URP
        if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic", metallic);
        if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smooth); // URP
        if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smooth); // Built-in
        EditorUtility.SetDirty(mat);
        return mat;
    }
}
