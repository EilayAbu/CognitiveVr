#if UNITY_EDITOR
using System;
using System.IO;
using System.Reflection;
using CognitiveVR.Phone;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace CognitiveVR.EditorTools
{
    /// <summary>
    /// Bakes the Phone Screen canvas hierarchy and the NotificationCard
    /// prefab from script so the result is fully visible and editable in the
    /// Unity editor (no runtime canvas construction). Meta Interaction SDK
    /// components (PointableCanvas / RayInteractable / ColliderSurface /
    /// PointableCanvasModule) are added via reflection so the script compiles
    /// even when the Meta SDK isn't present.
    /// </summary>
    public static class PhoneCanvasBuilderEditor
    {
        private const string PhonePrefabPath = "Assets/Prefabs/phone/Smartphone.prefab";
        private const string CardPrefabPath = "Assets/Prefabs/phone/NotificationCard.prefab";

        private const string ScreenName = "Screen";
        private const string PhoneSurfaceName = "PhoneSurface";
        private const string LegacyCanvasName = "PhoneCanvas";

        private const string TypePointableCanvas = "Oculus.Interaction.PointableCanvas";
        private const string TypePointableCanvasModule = "Oculus.Interaction.PointableCanvasModule";
        private const string TypeRayInteractable = "Oculus.Interaction.RayInteractable";
        private const string TypeColliderSurface = "Oculus.Interaction.Surfaces.ColliderSurface";

        // Approximate phone face footprint in world units (matches the existing
        // Smartphone.prefab BoxCollider Size: 0.075 x 0.16 x 0.012).
        private const float PhoneFaceWidth = 0.075f;
        private const float PhoneFaceHeight = 0.16f;
        private const float PhoneFaceFrontZ = 0.0061f;

        // Logical canvas size in pixels. Scale is computed so the rect covers
        // the phone face exactly.
        private const float CanvasPixelsWidth = 540f;
        private const float CanvasPixelsHeight = 1080f;

        // --------------------------------------------------------------------
        // Notification Card prefab
        // --------------------------------------------------------------------

        [MenuItem("CognitiveVR/Rebuild Notification Card Prefab")]
        public static void RebuildNotificationCardPrefab()
        {
            EnsureDirectory(CardPrefabPath);

            GameObject root = new GameObject("NotificationCard",
                typeof(RectTransform), typeof(LayoutElement), typeof(PhoneNotificationItem));

            try
            {
                RectTransform rootRect = (RectTransform)root.transform;
                rootRect.anchorMin = new Vector2(0f, 1f);
                rootRect.anchorMax = new Vector2(1f, 1f);
                rootRect.pivot = new Vector2(0.5f, 1f);
                rootRect.sizeDelta = new Vector2(0f, 160f);

                LayoutElement rootLayout = root.GetComponent<LayoutElement>();
                rootLayout.minHeight = 160f;
                rootLayout.preferredHeight = 160f;
                rootLayout.flexibleHeight = 0f;
                rootLayout.flexibleWidth = 1f;

                // Notification_Visual - the moving card body. Per spec the slot
                // is "empty" (no Image), the Visual carries the background.
                GameObject visual = new GameObject("Notification_Visual",
                    typeof(RectTransform), typeof(Image), typeof(CanvasGroup),
                    typeof(HorizontalLayoutGroup), typeof(SwipeToDelete));
                visual.transform.SetParent(root.transform, false);

                RectTransform visualRect = (RectTransform)visual.transform;
                visualRect.anchorMin = Vector2.zero;
                visualRect.anchorMax = Vector2.one;
                visualRect.offsetMin = Vector2.zero;
                visualRect.offsetMax = Vector2.zero;

                Image visualBg = visual.GetComponent<Image>();
                visualBg.color = new Color(0.16f, 0.30f, 0.50f, 0.98f);
                visualBg.raycastTarget = true;

                CanvasGroup visualCg = visual.GetComponent<CanvasGroup>();
                visualCg.blocksRaycasts = true;
                visualCg.interactable = true;
                visualCg.alpha = 1f;

                HorizontalLayoutGroup hlg = visual.GetComponent<HorizontalLayoutGroup>();
                hlg.padding = new RectOffset(16, 16, 12, 12);
                hlg.spacing = 12f;
                hlg.childAlignment = TextAnchor.MiddleCenter;
                hlg.childControlWidth = true;
                hlg.childControlHeight = true;
                hlg.childForceExpandWidth = false;
                hlg.childForceExpandHeight = true;

                // MessageContent - the notification text (Hebrew RTL via TMP).
                GameObject msg = new GameObject("MessageContent",
                    typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
                msg.transform.SetParent(visual.transform, false);

                TextMeshProUGUI msgTmp = msg.GetComponent<TextMeshProUGUI>();
                msgTmp.text = "תוכן הודעה לדוגמה";
                msgTmp.fontSize = 30f;
                msgTmp.color = Color.white;
                msgTmp.alignment = TextAlignmentOptions.MidlineRight;
                msgTmp.fontStyle = FontStyles.Normal;
                msgTmp.isRightToLeftText = true;
                msgTmp.enableWordWrapping = true;
                msgTmp.raycastTarget = false;

                LayoutElement msgLayout = msg.GetComponent<LayoutElement>();
                msgLayout.flexibleWidth = 1f;
                msgLayout.minWidth = 80f;
                msgLayout.minHeight = 80f;

                // DeleteButton - the X button at the side.
                GameObject deleteGo = new GameObject("DeleteButton",
                    typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
                deleteGo.transform.SetParent(visual.transform, false);

                Image deleteImg = deleteGo.GetComponent<Image>();
                deleteImg.color = new Color(0f, 0f, 0f, 0.45f);
                deleteImg.raycastTarget = true;

                Button deleteBtn = deleteGo.GetComponent<Button>();
                ColorBlock colors = deleteBtn.colors;
                colors.normalColor = new Color(1f, 1f, 1f, 0.95f);
                colors.highlightedColor = new Color(1f, 0.85f, 0.85f, 1f);
                colors.pressedColor = new Color(0.85f, 0.55f, 0.55f, 1f);
                deleteBtn.colors = colors;
                deleteBtn.targetGraphic = deleteImg;

                LayoutElement deleteLayout = deleteGo.GetComponent<LayoutElement>();
                deleteLayout.minWidth = 72f;
                deleteLayout.preferredWidth = 72f;
                deleteLayout.minHeight = 72f;
                deleteLayout.preferredHeight = 72f;

                GameObject label = new GameObject("Label",
                    typeof(RectTransform), typeof(TextMeshProUGUI));
                label.transform.SetParent(deleteGo.transform, false);
                RectTransform labelRect = (RectTransform)label.transform;
                StretchToFill(labelRect);

                TextMeshProUGUI labelTmp = label.GetComponent<TextMeshProUGUI>();
                labelTmp.text = "X";
                labelTmp.fontSize = 42f;
                labelTmp.fontStyle = FontStyles.Bold;
                labelTmp.alignment = TextAlignmentOptions.Center;
                labelTmp.color = Color.white;
                labelTmp.raycastTarget = false;

                // Wire SwipeToDelete and PhoneNotificationItem references.
                SwipeToDelete swipe = visual.GetComponent<SwipeToDelete>();
                SerializedObject swipeSo = new SerializedObject(swipe);
                SetObjectProperty(swipeSo, "_visual", visualRect);
                SetObjectProperty(swipeSo, "_visualCanvasGroup", visualCg);
                SetObjectProperty(swipeSo, "_deleteButton", deleteBtn);
                swipeSo.ApplyModifiedPropertiesWithoutUndo();

                PhoneNotificationItem item = root.GetComponent<PhoneNotificationItem>();
                SerializedObject itemSo = new SerializedObject(item);
                SetObjectProperty(itemSo, "_titleLabel", null);
                SetObjectProperty(itemSo, "_bodyLabel", msgTmp);
                SetObjectProperty(itemSo, "_timestampLabel", null);
                SetObjectProperty(itemSo, "_iconImage", null);
                SetObjectProperty(itemSo, "_swipe", swipe);
                itemSo.ApplyModifiedPropertiesWithoutUndo();

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, CardPrefabPath, out bool success);
                if (!success || saved == null)
                {
                    EditorUtility.DisplayDialog("Notification Card Prefab",
                        $"Failed to save prefab at {CardPrefabPath}.", "OK");
                    return;
                }

                AssetDatabase.SaveAssets();

                EditorUtility.DisplayDialog("Notification Card Prefab",
                    $"Rebuilt {CardPrefabPath}.\n\n" +
                    "Run 'CognitiveVR/Rebuild Phone Screen Prefab' next to wire this card into the Smartphone prefab.",
                    "OK");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        // --------------------------------------------------------------------
        // Phone Screen prefab
        // --------------------------------------------------------------------

        [MenuItem("CognitiveVR/Rebuild Phone Screen Prefab")]
        public static void RebuildPhoneScreenPrefab()
        {
            GameObject phonePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PhonePrefabPath);
            if (phonePrefab == null)
            {
                EditorUtility.DisplayDialog("Rebuild Phone Screen",
                    $"Could not find Smartphone prefab at {PhonePrefabPath}.", "OK");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(PhonePrefabPath);
            if (root == null)
            {
                EditorUtility.DisplayDialog("Rebuild Phone Screen",
                    $"PrefabUtility.LoadPrefabContents returned null for {PhonePrefabPath}.", "OK");
                return;
            }

            try
            {
                RemoveExistingChildByName(root.transform, ScreenName);
                RemoveExistingChildByName(root.transform, PhoneSurfaceName);
                RemoveExistingChildByName(root.transform, LegacyCanvasName);

                GameObject screen = BuildScreen(root.transform, out Canvas canvas,
                    out RectTransform contentRect, out TMP_Text clockTmp,
                    out Component pointableCanvasComp, out Component rayInteractableComp);
                GameObject phoneSurface = BuildPhoneSurface(root.transform, out Component colliderSurfaceComp);

                WireMetaSdkReferences(canvas, pointableCanvasComp, rayInteractableComp, colliderSurfaceComp);

                EnsureClockDisplay(root);
                EnsureNotificationManager(root);
                WirePhoneScreenController(root, clockTmp, contentRect);
                WireCardPrefabReference(root);

                PrefabUtility.SaveAsPrefabAsset(root, PhonePrefabPath);

                EditorUtility.DisplayDialog("Phone Screen Prefab",
                    $"Rebuilt {PhonePrefabPath}.\n\n" +
                    $"- Screen canvas: {(canvas != null ? "ok" : "MISSING")}\n" +
                    $"- PhoneSurface trigger: {(phoneSurface != null ? "ok" : "MISSING")}\n" +
                    $"- PointableCanvas: {(pointableCanvasComp != null ? "ok" : "Meta SDK type not found")}\n" +
                    $"- RayInteractable: {(rayInteractableComp != null ? "ok" : "Meta SDK type not found")}\n" +
                    $"- ColliderSurface: {(colliderSurfaceComp != null ? "ok" : "Meta SDK type not found")}\n\n" +
                    "Open the Smartphone prefab and tweak visuals as you like.",
                    "OK");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static GameObject BuildScreen(
            Transform parent,
            out Canvas canvas,
            out RectTransform contentRect,
            out TMP_Text clockTmp,
            out Component pointableCanvasComp,
            out Component rayInteractableComp)
        {
            GameObject screen = new GameObject(ScreenName,
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            screen.transform.SetParent(parent, false);

            RectTransform screenRect = (RectTransform)screen.transform;
            screenRect.sizeDelta = new Vector2(CanvasPixelsWidth, CanvasPixelsHeight);
            screenRect.localPosition = new Vector3(0f, 0f, PhoneFaceFrontZ);
            screenRect.localEulerAngles = Vector3.zero;
            float scaleX = PhoneFaceWidth / CanvasPixelsWidth;
            float scaleY = PhoneFaceHeight / CanvasPixelsHeight;
            float uniformScale = Mathf.Min(scaleX, scaleY);
            screenRect.localScale = new Vector3(uniformScale, uniformScale, uniformScale);

            canvas = screen.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;

            CanvasScaler scaler = screen.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = 1f;
            scaler.referencePixelsPerUnit = 100f;

            // Meta SDK ray UI components (reflection-added so the script
            // compiles when the SDK is removed).
            pointableCanvasComp = TryAddComponent(screen, TypePointableCanvas);
            rayInteractableComp = TryAddComponent(screen, TypeRayInteractable);

            // ScreenBackground = phone wallpaper + vertical stack (clock, scroll).
            GameObject bg = new GameObject("ScreenBackground",
                typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            bg.transform.SetParent(screen.transform, false);
            RectTransform bgRect = (RectTransform)bg.transform;
            StretchToFill(bgRect);

            Image bgImg = bg.GetComponent<Image>();
            bgImg.color = new Color(0.08f, 0.10f, 0.16f, 1f);
            bgImg.raycastTarget = false;

            VerticalLayoutGroup vlg = bg.GetComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(20, 20, 24, 24);
            vlg.spacing = 16f;
            vlg.childAlignment = TextAnchor.UpperCenter;
            vlg.childControlWidth = true;
            vlg.childControlHeight = false;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            // ClockText - fixed at the top.
            GameObject clock = new GameObject("ClockText",
                typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
            clock.transform.SetParent(bg.transform, false);
            clockTmp = clock.GetComponent<TextMeshProUGUI>();
            clockTmp.text = "08:52";
            clockTmp.fontSize = 64f;
            clockTmp.color = Color.white;
            clockTmp.alignment = TextAlignmentOptions.Center;
            clockTmp.fontStyle = FontStyles.Bold;
            clockTmp.raycastTarget = false;

            LayoutElement clockLayout = clock.GetComponent<LayoutElement>();
            clockLayout.minHeight = 96f;
            clockLayout.preferredHeight = 96f;
            clockLayout.flexibleHeight = 0f;

            // NotificationScrollView.
            GameObject scroll = new GameObject("NotificationScrollView",
                typeof(RectTransform), typeof(Image), typeof(ScrollRect), typeof(LayoutElement));
            scroll.transform.SetParent(bg.transform, false);
            Image scrollBg = scroll.GetComponent<Image>();
            scrollBg.color = new Color(0f, 0f, 0f, 0.18f);
            scrollBg.raycastTarget = true;

            ScrollRect scrollRect = scroll.GetComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 30f;
            scrollRect.horizontalScrollbar = null;
            scrollRect.verticalScrollbar = null;

            LayoutElement scrollLayout = scroll.GetComponent<LayoutElement>();
            scrollLayout.flexibleHeight = 1f;
            scrollLayout.flexibleWidth = 1f;
            scrollLayout.minHeight = 200f;

            // Viewport (with Mask).
            GameObject viewport = new GameObject("Viewport",
                typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(scroll.transform, false);
            RectTransform vpRect = (RectTransform)viewport.transform;
            StretchToFill(vpRect);
            Image vpImg = viewport.GetComponent<Image>();
            vpImg.color = new Color(1f, 1f, 1f, 0.01f);
            vpImg.raycastTarget = false;
            Mask vpMask = viewport.GetComponent<Mask>();
            vpMask.showMaskGraphic = false;

            // Content (VLG + CSF).
            GameObject content = new GameObject("Content",
                typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(viewport.transform, false);
            contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = new Vector2(0f, 0f);
            contentRect.anchoredPosition = Vector2.zero;

            VerticalLayoutGroup contentVlg = content.GetComponent<VerticalLayoutGroup>();
            contentVlg.padding = new RectOffset(8, 8, 8, 8);
            contentVlg.spacing = 8f;
            contentVlg.childAlignment = TextAnchor.UpperCenter;
            contentVlg.childControlWidth = true;
            contentVlg.childControlHeight = false;
            contentVlg.childForceExpandWidth = true;
            contentVlg.childForceExpandHeight = false;

            ContentSizeFitter csf = content.GetComponent<ContentSizeFitter>();
            csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scrollRect.viewport = vpRect;
            scrollRect.content = contentRect;

            return screen;
        }

        private static GameObject BuildPhoneSurface(Transform parent, out Component colliderSurfaceComp)
        {
            GameObject surface = new GameObject(PhoneSurfaceName, typeof(BoxCollider));
            surface.transform.SetParent(parent, false);
            surface.transform.localPosition = new Vector3(0f, 0f, PhoneFaceFrontZ);
            surface.transform.localRotation = Quaternion.identity;
            surface.transform.localScale = Vector3.one;

            BoxCollider box = surface.GetComponent<BoxCollider>();
            box.size = new Vector3(PhoneFaceWidth, PhoneFaceHeight, 0.005f);
            box.center = Vector3.zero;
            box.isTrigger = true;

            colliderSurfaceComp = TryAddComponent(surface, TypeColliderSurface);
            if (colliderSurfaceComp != null)
            {
                SerializedObject so = new SerializedObject(colliderSurfaceComp);
                SetObjectProperty(so, "_collider", box);
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            return surface;
        }

        private static void WireMetaSdkReferences(
            Canvas canvas,
            Component pointableCanvasComp,
            Component rayInteractableComp,
            Component colliderSurfaceComp)
        {
            if (pointableCanvasComp != null && canvas != null)
            {
                SerializedObject so = new SerializedObject(pointableCanvasComp);
                SetObjectProperty(so, "_canvas", canvas);
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            if (rayInteractableComp != null)
            {
                SerializedObject so = new SerializedObject(rayInteractableComp);
                // PointableElement is required for the ray to receive the
                // PointableCanvas's pointer events.
                if (pointableCanvasComp != null)
                    SetObjectProperty(so, "_pointableElement", pointableCanvasComp);
                if (colliderSurfaceComp != null)
                    SetObjectProperty(so, "_surfacePatch", colliderSurfaceComp);
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void EnsureClockDisplay(GameObject root)
        {
            if (root.GetComponent<PhoneClockDisplay>() == null)
                root.AddComponent<PhoneClockDisplay>();
        }

        private static void EnsureNotificationManager(GameObject root)
        {
            if (root.GetComponent<PhoneNotificationManager>() == null)
                root.AddComponent<PhoneNotificationManager>();
        }

        private static void WirePhoneScreenController(GameObject root, TMP_Text clockTmp, RectTransform contentRect)
        {
            PhoneScreenController controller = root.GetComponent<PhoneScreenController>();
            if (controller == null) controller = root.AddComponent<PhoneScreenController>();

            PhoneClockDisplay clock = root.GetComponent<PhoneClockDisplay>();
            PhoneNotificationManager mgr = root.GetComponent<PhoneNotificationManager>();

            SerializedObject so = new SerializedObject(controller);
            SetObjectProperty(so, "_clockText", clockTmp);
            SetObjectProperty(so, "_notificationContent", contentRect);
            SetObjectProperty(so, "_clockDisplay", clock);
            SetObjectProperty(so, "_notificationManager", mgr);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void WireCardPrefabReference(GameObject root)
        {
            PhoneNotificationManager mgr = root.GetComponent<PhoneNotificationManager>();
            if (mgr == null) return;

            PhoneNotificationItem cardPrefab = AssetDatabase.LoadAssetAtPath<PhoneNotificationItem>(CardPrefabPath);
            if (cardPrefab == null)
            {
                Debug.LogWarning(
                    $"[PhoneCanvasBuilder] Card prefab not found at {CardPrefabPath}. " +
                    "Run 'CognitiveVR/Rebuild Notification Card Prefab' first.",
                    mgr);
                return;
            }

            SerializedObject so = new SerializedObject(mgr);
            SetObjectProperty(so, "_itemPrefab", cardPrefab);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // --------------------------------------------------------------------
        // Helpers
        // --------------------------------------------------------------------

        private static void RemoveExistingChildByName(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
                UnityEngine.Object.DestroyImmediate(existing.gameObject);
        }

        private static void StretchToFill(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Component TryAddComponent(GameObject go, string typeFullName)
        {
            Type t = ResolveType(typeFullName);
            if (t == null)
            {
                Debug.LogWarning(
                    $"[PhoneCanvasBuilder] Meta SDK type '{typeFullName}' not found. " +
                    "The phone canvas will be built without it; ray pointer input will not work.",
                    go);
                return null;
            }
            Component existing = go.GetComponent(t);
            if (existing != null) return existing;
            try
            {
                return go.AddComponent(t);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[PhoneCanvasBuilder] Failed to add {typeFullName}: {ex.Message}", go);
                return null;
            }
        }

        private static void SetObjectProperty(SerializedObject so, string propertyName, UnityEngine.Object value)
        {
            SerializedProperty prop = so.FindProperty(propertyName);
            if (prop == null)
            {
                Debug.LogWarning(
                    $"[PhoneCanvasBuilder] Serialized field '{propertyName}' not found on {so.targetObject.GetType().Name}. " +
                    "Field name may have changed in a newer SDK version.");
                return;
            }
            prop.objectReferenceValue = value;
        }

        public static Type ResolveType(string fullName)
        {
            Type t = Type.GetType(fullName);
            if (t != null) return t;
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }

        private static void EnsureDirectory(string assetPath)
        {
            string dir = Path.GetDirectoryName(assetPath);
            if (string.IsNullOrEmpty(dir)) return;
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
    }
}
#endif
