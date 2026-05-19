#if UNITY_EDITOR
using System.Collections.Generic;
using Oculus.Interaction;
using Oculus.Interaction.Grab;
using Oculus.Interaction.GrabAPI;
using Oculus.Interaction.HandGrab;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CognitiveVR.EditorTools
{
    /// <summary>
    /// One-click utility that recreates the "chest"-style hinged-lid interaction
    /// used by the BoxLid in the Meta XR Interaction SDK <c>TransformerExamples</c>
    /// scene (Box.prefab) on a target GameObject.
    ///
    /// It adds and wires up:
    ///   - Rigidbody (kinematic, no gravity)
    ///   - MeshCollider (convex) if no collider is present
    ///   - Grabbable
    ///   - OneGrabRotateTransformer (X-axis, 0°-190° clamp)
    ///   - MaterialPropertyBlockEditor
    ///   - InteractableColorVisual (chest's normal/hover/select colors)
    ///   - InteractableGroupView
    ///   - GrabInteractable (child)
    ///   - HandGrabInteractable (child, all grab types, default rules)
    ///   - A sibling "Hinge_&lt;Lid&gt;" pivot transform with a kinematic Rigidbody,
    ///     pre-positioned at the back edge of the lid's renderer bounds.
    ///
    /// Two menu items are exposed:
    ///   - "Apply Chest Behavior To Toast.prefab" automatically opens
    ///     Assets/Prefabs/Toast.prefab, finds the top-most bread slice and
    ///     wires everything up. No selection required.
    ///   - "Setup Chest-Like Hinged Lid On Selection" runs the same setup on
    ///     whichever GameObject is currently selected (works in Prefab Mode
    ///     and in the scene).
    /// </summary>
    public static class ChestBehaviorSetupEditor
    {
        private const string ToastPrefabPath = "Assets/Prefabs/Toast.prefab";

        // Color values copied from the BoxLid's InteractableColorVisual in
        // Library/PackageCache/com.meta.xr.sdk.interaction/Runtime/Sample/Objects/Props/Box/Box.prefab.
        private static readonly Color NormalColor = new Color(0.83137256f, 0.83137256f, 0.83137256f, 1f);
        private static readonly Color HoverColor = new Color(1f, 1f, 1f, 1f);
        private static readonly Color SelectColor = new Color(0.83137256f, 0.83137256f, 0.83137256f, 1f);

        [MenuItem("CognitiveVR/Interactions/Apply Chest Behavior To Toast.prefab")]
        public static void ApplyToToastPrefab()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(ToastPrefabPath);
            if (root == null)
            {
                EditorUtility.DisplayDialog("Toast Chest Setup",
                    $"Could not load prefab at '{ToastPrefabPath}'.\n\nMake sure the file exists.",
                    "OK");
                return;
            }

            try
            {
                GameObject lid = FindTopSlice(root);
                if (lid == null)
                {
                    EditorUtility.DisplayDialog("Toast Chest Setup",
                        "Could not find an active MeshRenderer in Toast.prefab to use as the lid.",
                        "OK");
                    return;
                }

                if (lid.transform.parent == null)
                {
                    EditorUtility.DisplayDialog("Toast Chest Setup",
                        "The detected lid is the prefab root; the chest setup needs a parent " +
                        "transform to host the hinge. Aborting to avoid breaking the prefab.",
                        "OK");
                    return;
                }

                string lidPath = GetTransformPath(lid.transform, root.transform);
                ApplyChestBehavior(lid, useUndo: false);

                PrefabUtility.SaveAsPrefabAsset(root, ToastPrefabPath);

                Debug.Log($"[ChestSetup] Toast.prefab updated. Lid: '{lidPath}'. " +
                          $"Pivot: 'Hinge_{lid.name}' (sibling of the lid). Axis: X. Range: 0\u00b0-190\u00b0.");

                EditorUtility.DisplayDialog("Toast Chest Setup Complete",
                    $"Toast.prefab now opens like the chest in TransformerExamples.\n\n" +
                    $"\u2022 Lid: '{lidPath}'\n" +
                    $"\u2022 Hinge: 'Hinge_{lid.name}' (parented next to the lid).\n" +
                    $"\u2022 Rotation: X axis, 0\u00b0 - 190\u00b0.\n" +
                    $"\u2022 Grab: GrabInteractable + HandGrabInteractable (controllers and hands).\n\n" +
                    $"If the rotation direction looks wrong when you test it, just open " +
                    $"Toast.prefab and rotate the 'Hinge_{lid.name}' transform so its red (X) " +
                    $"axis runs along the back edge of the slice.",
                    "OK");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ChestSetup] Failed while modifying Toast.prefab: {ex}");
                EditorUtility.DisplayDialog("Toast Chest Setup Failed",
                    $"Could not finish the setup:\n\n{ex.Message}\n\nSee the Console for the full stack trace.",
                    "OK");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [MenuItem("CognitiveVR/Interactions/Setup Chest-Like Hinged Lid On Selection")]
        public static void SetupChestLidOnSelection()
        {
            GameObject lid = Selection.activeGameObject;
            if (lid == null)
            {
                EditorUtility.DisplayDialog("Chest Lid Setup",
                    "Select the GameObject that should act as the hinged lid " +
                    "(for example the top bread slice on the Toast prefab) and try again.",
                    "OK");
                return;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName($"Setup chest-like hinged lid on {lid.name}");
            int undoGroup = Undo.GetCurrentGroup();

            try
            {
                ApplyChestBehavior(lid, useUndo: true);

                EditorUtility.SetDirty(lid);
                if (PrefabUtility.IsPartOfPrefabInstance(lid))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(lid);
                }
                if (lid.scene.IsValid())
                {
                    EditorSceneManager.MarkSceneDirty(lid.scene);
                }

                Undo.CollapseUndoOperations(undoGroup);

                Debug.Log($"[ChestSetup] '{lid.name}' is now a chest-style hinged lid. " +
                          $"Pivot: 'Hinge_{lid.name}'. Axis: X. Angle: 0\u00b0-190\u00b0.");

                EditorUtility.DisplayDialog("Chest Lid Setup Complete",
                    $"'{lid.name}' is now a chest-style hinged lid.\n\n" +
                    $"\u2022 Pivot: 'Hinge_{lid.name}'.\n" +
                    $"\u2022 Rotation: X axis, 0\u00b0 - 190\u00b0.\n" +
                    $"\u2022 Grab: GrabInteractable + HandGrabInteractable.\n\n" +
                    $"If the rotation looks wrong, rotate the 'Hinge_{lid.name}' " +
                    $"transform so its red (X) axis runs along the hinge line.",
                    "OK");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[ChestSetup] Failed to set up '{lid.name}': {ex}");
                EditorUtility.DisplayDialog("Chest Lid Setup Failed",
                    $"Could not finish the setup:\n\n{ex.Message}\n\nSee the Console for the full stack trace.",
                    "OK");
            }
        }

        // ------------------------------------------------------------------
        // Core setup logic (shared by both menu items)
        // ------------------------------------------------------------------

        private static void ApplyChestBehavior(GameObject lid, bool useUndo)
        {
            Transform hinge = EnsureHinge(lid, useUndo);
            Rigidbody lidRigidbody = EnsureKinematicRigidbody(lid, useUndo);
            EnsureCollider(lid, useUndo);

            Grabbable grabbable = EnsureComponent<Grabbable>(lid, useUndo);
            grabbable.InjectOptionalRigidbody(lidRigidbody);
            grabbable.MaxGrabPoints = 1;

            OneGrabRotateTransformer rotate = EnsureComponent<OneGrabRotateTransformer>(lid, useUndo);
            rotate.InjectOptionalPivotTransform(hinge);
            rotate.InjectOptionalRotationAxis(OneGrabRotateTransformer.Axis.Right);
            rotate.InjectOptionalConstraints(new OneGrabRotateTransformer.OneGrabRotateConstraints
            {
                MinAngle = new FloatConstraint { Constrain = true, Value = 0f },
                MaxAngle = new FloatConstraint { Constrain = true, Value = 190f },
            });

            grabbable.InjectOptionalOneGrabTransformer(rotate);

            MaterialPropertyBlockEditor mpbEditor = EnsureMaterialPropertyBlockEditor(lid, useUndo);

            GameObject grabChild = EnsureChild(lid, "GrabInteractable", useUndo);
            GrabInteractable grab = EnsureComponent<GrabInteractable>(grabChild, useUndo);
            grab.InjectAllGrabInteractable(lidRigidbody);
            grab.InjectOptionalPointableElement(grabbable);

            GameObject handGrabChild = EnsureChild(lid, "HandGrabInteractable", useUndo);
            HandGrabInteractable handGrab = EnsureComponent<HandGrabInteractable>(handGrabChild, useUndo);
            handGrab.InjectAllHandGrabInteractable(
                GrabTypeFlags.All,
                lidRigidbody,
                GrabbingRule.DefaultPinchRule,
                GrabbingRule.DefaultPalmRule);
            handGrab.InjectOptionalPointableElement(grabbable);

            InteractableGroupView group = EnsureComponent<InteractableGroupView>(lid, useUndo);
            group.InjectAllInteractableGroupView(new List<IInteractableView> { grab, handGrab });

            InteractableColorVisual colorVisual = EnsureComponent<InteractableColorVisual>(lid, useUndo);
            colorVisual.InjectAllInteractableColorVisual(group, mpbEditor);
            colorVisual.InjectOptionalNormalColorState(MakeColorState(NormalColor));
            colorVisual.InjectOptionalHoverColorState(MakeColorState(HoverColor));
            colorVisual.InjectOptionalSelectColorState(MakeColorState(SelectColor));
        }

        // ------------------------------------------------------------------
        // Lid detection
        // ------------------------------------------------------------------

        private static GameObject FindTopSlice(GameObject root)
        {
            MeshRenderer[] renderers = root.GetComponentsInChildren<MeshRenderer>(includeInactive: false);
            MeshRenderer top = null;
            float topY = float.NegativeInfinity;

            foreach (MeshRenderer r in renderers)
            {
                if (r == null || !r.gameObject.activeInHierarchy) continue;
                if (r.GetComponent<MeshFilter>() == null) continue;

                float y = r.bounds.center.y;
                if (y > topY)
                {
                    topY = y;
                    top = r;
                }
            }

            return top != null ? top.gameObject : null;
        }

        private static string GetTransformPath(Transform t, Transform stopAt)
        {
            if (t == null) return string.Empty;
            if (t == stopAt) return t.name;

            string path = t.name;
            Transform current = t.parent;
            while (current != null && current != stopAt)
            {
                path = current.name + "/" + path;
                current = current.parent;
            }
            return path;
        }

        // ------------------------------------------------------------------
        // Hinge construction
        // ------------------------------------------------------------------

        private static Transform EnsureHinge(GameObject lid, bool useUndo)
        {
            string hingeName = $"Hinge_{lid.name}";

            Transform hinge = null;
            Transform parent = lid.transform.parent;
            if (parent != null)
            {
                hinge = parent.Find(hingeName);
            }
            if (hinge == null)
            {
                Transform childHinge = lid.transform.Find(hingeName);
                if (childHinge != null) hinge = childHinge;
            }
            if (hinge != null) return hinge;

            GameObject hingeGO = new GameObject(hingeName);
            if (useUndo) Undo.RegisterCreatedObjectUndo(hingeGO, "Create chest hinge pivot");

            Transform desiredParent = parent != null ? parent : lid.transform;
            if (useUndo)
            {
                Undo.SetTransformParent(hingeGO.transform, desiredParent, "Parent hinge");
            }
            else
            {
                hingeGO.transform.SetParent(desiredParent, worldPositionStays: false);
            }

            Vector3 worldBackEdge = ComputeBackEdgeWorldPosition(lid);
            hingeGO.transform.SetPositionAndRotation(worldBackEdge, lid.transform.rotation);
            hingeGO.transform.localScale = Vector3.one;

            Rigidbody hingeRb = AddComponentSafe<Rigidbody>(hingeGO, useUndo);
            hingeRb.isKinematic = true;
            hingeRb.useGravity = false;

            return hingeGO.transform;
        }

        private static Vector3 ComputeBackEdgeWorldPosition(GameObject lid)
        {
            Renderer renderer = lid.GetComponentInChildren<Renderer>();
            if (renderer == null) return lid.transform.position;

            Bounds bounds = renderer.bounds;
            // Back edge in world space (+Z side of the AABB). The user can rotate
            // or move the hinge afterwards if a different edge is preferable.
            return new Vector3(bounds.center.x, bounds.center.y, bounds.center.z + bounds.extents.z);
        }

        // ------------------------------------------------------------------
        // Component helpers
        // ------------------------------------------------------------------

        private static Rigidbody EnsureKinematicRigidbody(GameObject go, bool useUndo)
        {
            Rigidbody rb = go.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = AddComponentSafe<Rigidbody>(go, useUndo);
            }
            else if (useUndo)
            {
                Undo.RecordObject(rb, "Configure lid Rigidbody");
            }

            rb.isKinematic = true;
            rb.useGravity = false;
            EditorUtility.SetDirty(rb);
            return rb;
        }

        private static void EnsureCollider(GameObject lid, bool useUndo)
        {
            if (lid.GetComponent<Collider>() != null) return;

            MeshFilter mf = lid.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                MeshCollider mc = AddComponentSafe<MeshCollider>(lid, useUndo);
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = true;
                return;
            }

            BoxCollider bc = AddComponentSafe<BoxCollider>(lid, useUndo);
            Renderer renderer = lid.GetComponentInChildren<Renderer>();
            if (renderer != null)
            {
                Bounds worldBounds = renderer.bounds;
                bc.center = lid.transform.InverseTransformPoint(worldBounds.center);
                Vector3 lossyScale = lid.transform.lossyScale;
                bc.size = new Vector3(
                    SafeDivide(worldBounds.size.x, lossyScale.x),
                    SafeDivide(worldBounds.size.y, lossyScale.y),
                    SafeDivide(worldBounds.size.z, lossyScale.z));
            }
        }

        private static float SafeDivide(float a, float b)
        {
            return Mathf.Approximately(b, 0f) ? a : a / b;
        }

        private static MaterialPropertyBlockEditor EnsureMaterialPropertyBlockEditor(GameObject lid, bool useUndo)
        {
            MaterialPropertyBlockEditor mpbEditor = lid.GetComponent<MaterialPropertyBlockEditor>();
            if (mpbEditor == null)
            {
                mpbEditor = AddComponentSafe<MaterialPropertyBlockEditor>(lid, useUndo);
            }
            else if (useUndo)
            {
                Undo.RecordObject(mpbEditor, "Wire renderers to MaterialPropertyBlockEditor");
            }

            Renderer[] candidates = lid.GetComponentsInChildren<Renderer>(true);
            List<Renderer> renderers = new List<Renderer>();
            foreach (Renderer r in candidates)
            {
                MaterialPropertyBlockEditor owningEditor = r.GetComponentInParent<MaterialPropertyBlockEditor>();
                if (owningEditor == mpbEditor)
                {
                    renderers.Add(r);
                }
            }

            mpbEditor.Renderers = renderers;
            EditorUtility.SetDirty(mpbEditor);
            return mpbEditor;
        }

        private static T EnsureComponent<T>(GameObject go, bool useUndo) where T : Component
        {
            T existing = go.GetComponent<T>();
            if (existing != null)
            {
                if (useUndo) Undo.RecordObject(existing, $"Configure {typeof(T).Name}");
                return existing;
            }
            return AddComponentSafe<T>(go, useUndo);
        }

        private static GameObject EnsureChild(GameObject parent, string name, bool useUndo)
        {
            Transform existing = parent.transform.Find(name);
            if (existing != null) return existing.gameObject;

            GameObject child = new GameObject(name);
            if (useUndo)
            {
                Undo.RegisterCreatedObjectUndo(child, $"Create '{name}' child");
                Undo.SetTransformParent(child.transform, parent.transform, $"Parent '{name}' under lid");
            }
            else
            {
                child.transform.SetParent(parent.transform, worldPositionStays: false);
            }

            child.transform.localPosition = Vector3.zero;
            child.transform.localRotation = Quaternion.identity;
            child.transform.localScale = Vector3.one;
            return child;
        }

        private static T AddComponentSafe<T>(GameObject go, bool useUndo) where T : Component
        {
            return useUndo ? Undo.AddComponent<T>(go) : go.AddComponent<T>();
        }

        private static InteractableColorVisual.ColorState MakeColorState(Color color)
        {
            return new InteractableColorVisual.ColorState
            {
                Color = color,
                ColorTime = 0.1f,
                ColorCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f),
            };
        }
    }
}
#endif
