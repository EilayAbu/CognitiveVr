#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using CognitiveVR.Core;
using CognitiveVR.Interaction;
using CognitiveVR.Phone;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;

namespace CognitiveVR.EditorTools
{
    /// <summary>
    /// One-click scene setup for the Smartphone prefab, the Backpack inventory
    /// zone, and SMS swap tracking.
    ///
    /// Drops the Smartphone prefab into the scene if missing, fixes common
    /// Backpack mis-configurations (dynamic rigidbody, no slot transforms)
    /// and resolves the SessionTimer / SmsSwapTracker references. Items are
    /// identified at runtime by their GameObject name (see SmsSwapTracker).
    /// </summary>
    public static class PhoneSetupEditor
    {
        private const string PrefabPath = "Assets/Prefabs/phone/Smartphone.prefab";

        [MenuItem("CognitiveVR/Setup Smartphone In Scene")]
        public static void SetupSmartphoneInScene()
        {
            GameObject phonePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (phonePrefab == null)
            {
                EditorUtility.DisplayDialog("Smartphone Setup",
                    $"Could not find Smartphone prefab at {PrefabPath}.",
                    "OK");
                return;
            }

            GameObject phoneInstance = GameObject.Find("Smartphone");
            if (phoneInstance == null)
            {
                phoneInstance = (GameObject)PrefabUtility.InstantiatePrefab(phonePrefab);
                phoneInstance.name = "Smartphone";
                Undo.RegisterCreatedObjectUndo(phoneInstance, "Create Smartphone instance");
                Debug.Log("[PhoneSetup] Instantiated Smartphone prefab in scene.");
            }
            else
            {
                Debug.Log("[PhoneSetup] Found existing Smartphone GameObject in scene; reusing it.");
            }

            EnsureSmsSwapTracker(phoneInstance);
            ResolvePhoneRefsToScene(phoneInstance);
            string scheduledEventsStatus = EnsureScheduledEvents();
            string backpackStatus = EnsureBackpackSetup(phoneInstance);
            string xrStatus = TryEnsurePhoneXRComponents(phoneInstance);
            string eventSystemStatus = EnsurePointableCanvasModuleOnEventSystem();

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            EditorUtility.DisplayDialog("Smartphone Setup Complete",
                $"Smartphone prefab is in the scene.\n" +
                $"Items are matched by GameObject name (configure name lists on SmsSwapTracker).\n" +
                $"SessionTimer events: {scheduledEventsStatus}\n" +
                $"Backpack: {backpackStatus}\n" +
                $"Phone XR components: {xrStatus}\n" +
                $"EventSystem input module: {eventSystemStatus}\n\n" +
                $"Next steps if XR is missing:\n" +
                $"- Run 'CognitiveVR/Rebuild Notification Card Prefab' then 'CognitiveVR/Rebuild Phone Screen Prefab' if the phone screen looks empty in the editor.\n" +
                $"- Per-slot hand-grab: each backpack slot now has a BoxCollider + Grabbable + HandGrabInteractable; if Meta SDK types weren't found, see console warnings.\n" +
                $"- Body grab suppression: while a hand hovers a slot, the backpack body's HandGrabInteractable is auto-disabled so the bag itself isn't grabbed when extracting items.\n" +
                $"- Press Play and confirm the SMS notification arrives at T+2:00 and rain push at T+4:00.",
                "OK");
        }

        /// <summary>
        /// Isolated fix-up for the backpack slots only. Unlike
        /// <see cref="SetupSmartphoneInScene"/>, this does not touch the
        /// Smartphone instance, SmsSwapTracker, SessionTimer, phone XR
        /// components, or the EventSystem input module - so it is safe to run
        /// after manually tweaking any of those without having them
        /// overwritten. It also does not touch the backpack zone's own
        /// Rigidbody, generate new slots, or rewire body-grab suppression;
        /// it only ensures each existing slot transform carries
        /// BackpackSlot + BoxCollider + Rigidbody + Grabbable +
        /// HandGrabInteractable + SnapInteractable (and strips any stale
        /// RayInteractable).
        /// </summary>
        [MenuItem("CognitiveVR/Setup Backpack Slots Only")]
        public static void SetupBackpackSlotsOnly()
        {
            BackpackInventoryZone zone = UnityEngine.Object.FindFirstObjectByType<BackpackInventoryZone>();
            if (zone == null)
            {
                EditorUtility.DisplayDialog("Backpack Slots Setup",
                    "No BackpackInventoryZone found in the scene.",
                    "OK");
                return;
            }

            FieldInfo slotListField = typeof(BackpackInventoryZone).GetField(
                "slotTransforms", BindingFlags.NonPublic | BindingFlags.Instance);

            if (slotListField == null || !(slotListField.GetValue(zone) is List<Transform> slots) ||
                slots.TrueForAll(t => t == null))
            {
                EditorUtility.DisplayDialog("Backpack Slots Setup",
                    "No slot transforms found on the BackpackInventoryZone. Generate/assign slots first.",
                    "OK");
                return;
            }

            int totalChanged = EquipSlotsWithMetaComponents(zone, slotListField);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            EditorUtility.DisplayDialog("Backpack Slots Setup Complete",
                totalChanged == 0
                    ? "All backpack slots already have the required components."
                    : $"Applied {totalChanged} component change(s) across the backpack slots.\n" +
                      "(Only slot components were touched: BackpackSlot, BoxCollider, Rigidbody, Grabbable, HandGrabInteractable, SnapInteractable; any stale RayInteractable was removed.)",
                "OK");
        }

        /// <summary>
        /// Strips SnapInteractable from every inventory item in the scene.
        /// A SnapInteractable makes its GameObject a snap *target*, so an item
        /// carrying one competes with the backpack slots: once a slot is full
        /// it drops out of the snap candidates (MaxSelectingInteractors = 1)
        /// and the next released item snaps into the stored item instead,
        /// becoming its child and shrinking to storedScale.
        /// InventoryItemMetaBridge also removes these at runtime, but this
        /// cleans the scene asset so the warning stops firing every play.
        /// </summary>
        [MenuItem("CognitiveVR/Fix Backpack Item Snap Targets")]
        public static void FixBackpackItemSnapTargets()
        {
            Type snapInteractableType = ResolveType("Oculus.Interaction.SnapInteractable");
            if (snapInteractableType == null)
            {
                EditorUtility.DisplayDialog("Fix Backpack Item Snap Targets",
                    "Meta SnapInteractable type not found (SDK missing?).",
                    "OK");
                return;
            }

            InventoryItemMetaBridge[] items = UnityEngine.Object.FindObjectsByType<InventoryItemMetaBridge>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            int removed = 0;
            foreach (InventoryItemMetaBridge item in items)
            {
                if (item == null) continue;

                // GetComponents, not GetComponentsInChildren: an item stored in
                // a slot sits under the slot, whose SnapInteractable is legit.
                foreach (Component straySnapTarget in item.GetComponents(snapInteractableType))
                {
                    if (straySnapTarget == null) continue;

                    Debug.Log($"[PhoneSetup] Removed stray SnapInteractable from inventory item '{item.name}'.", item);
                    Undo.DestroyObjectImmediate(straySnapTarget);
                    removed++;
                }
            }

            if (removed > 0)
            {
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            }

            EditorUtility.DisplayDialog("Fix Backpack Item Snap Targets",
                removed == 0
                    ? $"Checked {items.Length} inventory item(s); none carried a SnapInteractable."
                    : $"Removed {removed} stray SnapInteractable(s) across {items.Length} inventory item(s).\n" +
                      "Items are snap sources only; the backpack slots are the snap targets.",
                "OK");
        }

        /// <summary>
        /// Finds the scene's EventSystem and replaces the default
        /// StandaloneInputModule with Meta's PointableCanvasModule so ray
        /// pointer events reach the phone canvas. Meta types are resolved via
        /// reflection so the editor script compiles without the SDK.
        /// </summary>
        private static string EnsurePointableCanvasModuleOnEventSystem()
        {
#if UNITY_2023_1_OR_NEWER
            EventSystem es = UnityEngine.Object.FindFirstObjectByType<EventSystem>();
#else
            EventSystem es = UnityEngine.Object.FindObjectOfType<EventSystem>();
#endif
            if (es == null)
            {
                GameObject esGo = new GameObject("EventSystem", typeof(EventSystem));
                Undo.RegisterCreatedObjectUndo(esGo, "Create EventSystem");
                es = esGo.GetComponent<EventSystem>();
            }

            Type pointableModuleType = PhoneCanvasBuilderEditor.ResolveType("Oculus.Interaction.PointableCanvasModule");
            if (pointableModuleType == null)
                return "Meta PointableCanvasModule type not found (SDK missing?)";

            int removed = 0;
            foreach (BaseInputModule mod in es.GetComponents<BaseInputModule>())
            {
                if (mod == null) continue;
                if (pointableModuleType.IsInstanceOfType(mod)) continue;
                Undo.DestroyObjectImmediate(mod);
                removed++;
            }

            Component existing = es.GetComponent(pointableModuleType);
            if (existing != null)
            {
                return removed > 0
                    ? $"already present; removed {removed} other input module(s)"
                    : "already present";
            }

            try
            {
                Undo.AddComponent(es.gameObject, pointableModuleType);
                return removed > 0
                    ? $"added PointableCanvasModule; removed {removed} other input module(s)"
                    : "added PointableCanvasModule";
            }
            catch (Exception ex)
            {
                return $"failed to add PointableCanvasModule: {ex.Message}";
            }
        }

        private static string EnsureScheduledEvents()
        {
            SessionTimer timer = UnityEngine.Object.FindFirstObjectByType<SessionTimer>();
            if (timer == null) return "no SessionTimer in scene";
            if (timer.ScheduledEvents == null) return "events list null on SessionTimer";

            bool hasSms = false;
            bool hasRain = false;
            int smsIndex = -1;
            int rainIndex = -1;

            for (int i = 0; i < timer.ScheduledEvents.Count; i++)
            {
                var evt = timer.ScheduledEvents[i];
                if (evt == null) continue;
                if (evt.Id == PhoneSessionEventBridge.SmsPlanChangeId) { hasSms = true; smsIndex = i; }
                if (evt.Id == PhoneSessionEventBridge.RainPushId) { hasRain = true; rainIndex = i; }
            }

            bool changed = false;

            Undo.RecordObject(timer, "Update SessionTimer scheduled events for phone");

            if (!hasSms)
            {
                timer.ScheduledEvents.Add(new SessionTimer.ScheduledEvent
                {
                    Id = PhoneSessionEventBridge.SmsPlanChangeId,
                    TriggerTime = 120f,
                    DisplayName = "הודעת SMS - שינוי תוכנית"
                });
                changed = true;
            }
            else if (Mathf.Abs(timer.ScheduledEvents[smsIndex].TriggerTime - 120f) > 0.5f)
            {
                timer.ScheduledEvents[smsIndex].TriggerTime = 120f;
                changed = true;
            }

            if (!hasRain)
            {
                timer.ScheduledEvents.Add(new SessionTimer.ScheduledEvent
                {
                    Id = PhoneSessionEventBridge.RainPushId,
                    TriggerTime = 240f,
                    DisplayName = "התראת גשם בטלפון"
                });
                changed = true;
            }
            else if (Mathf.Abs(timer.ScheduledEvents[rainIndex].TriggerTime - 240f) > 0.5f)
            {
                timer.ScheduledEvents[rainIndex].TriggerTime = 240f;
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(timer);
                return "updated sms_plan_change=120s, rain_push=240s";
            }

            return "ok (sms_plan_change @ 120s, rain_push @ 240s)";
        }

        private static void EnsureSmsSwapTracker(GameObject phoneInstance)
        {
            if (phoneInstance == null) return;

            SmsSwapTracker tracker = phoneInstance.GetComponent<SmsSwapTracker>();
            if (tracker == null)
            {
                tracker = Undo.AddComponent<SmsSwapTracker>(phoneInstance);
                Debug.Log("[PhoneSetup] Added SmsSwapTracker to Smartphone.");
            }

            PhoneSessionEventBridge bridge = phoneInstance.GetComponent<PhoneSessionEventBridge>();
            if (bridge != null)
            {
                FieldInfo trackerField = typeof(PhoneSessionEventBridge).GetField(
                    "_smsSwapTracker", BindingFlags.NonPublic | BindingFlags.Instance);
                if (trackerField != null)
                    trackerField.SetValue(bridge, tracker);
            }
        }

        private static void ResolvePhoneRefsToScene(GameObject phoneInstance)
        {
            if (phoneInstance == null) return;

            SessionTimer timer = UnityEngine.Object.FindFirstObjectByType<SessionTimer>();
            if (timer == null)
            {
                Debug.LogWarning("[PhoneSetup] No SessionTimer found in scene. Add one and set start time to 08:52.");
                return;
            }

            PhoneSessionEventBridge bridge = phoneInstance.GetComponent<PhoneSessionEventBridge>();
            if (bridge != null)
            {
                FieldInfo timerField = typeof(PhoneSessionEventBridge).GetField(
                    "_sessionTimer", BindingFlags.NonPublic | BindingFlags.Instance);
                if (timerField != null)
                    timerField.SetValue(bridge, timer);
            }

            PhoneClockDisplay clock = phoneInstance.GetComponent<PhoneClockDisplay>();
            if (clock != null)
            {
                FieldInfo timerField = typeof(PhoneClockDisplay).GetField(
                    "_sessionTimer", BindingFlags.NonPublic | BindingFlags.Instance);
                if (timerField != null)
                    timerField.SetValue(clock, timer);
            }

            SmsSwapTracker tracker = phoneInstance.GetComponent<SmsSwapTracker>();
            if (tracker != null)
            {
                FieldInfo timerField = typeof(SmsSwapTracker).GetField(
                    "_sessionTimer", BindingFlags.NonPublic | BindingFlags.Instance);
                if (timerField != null)
                    timerField.SetValue(tracker, timer);
            }
        }

        private static string EnsureBackpackSetup(GameObject phoneInstance)
        {
            BackpackInventoryZone zone = UnityEngine.Object.FindFirstObjectByType<BackpackInventoryZone>();
            if (zone == null)
            {
                return "no BackpackInventoryZone in scene";
            }

            int fixesApplied = 0;

            Rigidbody packRigidbody = zone.GetComponentInParent<Rigidbody>();
            if (packRigidbody != null && (!packRigidbody.isKinematic || packRigidbody.useGravity))
            {
                Undo.RecordObject(packRigidbody, "Backpack: make rigidbody kinematic");
                packRigidbody.useGravity = false;
                packRigidbody.isKinematic = true;
                EditorUtility.SetDirty(packRigidbody);
                fixesApplied++;
                Debug.Log($"[PhoneSetup] Set parent backpack rigidbody to kinematic on '{packRigidbody.name}'.", packRigidbody);
            }

            FieldInfo slotListField = typeof(BackpackInventoryZone).GetField(
                "slotTransforms", BindingFlags.NonPublic | BindingFlags.Instance);

            int slotCount = 0;
            if (slotListField != null)
            {
                if (slotListField.GetValue(zone) is List<Transform> existing)
                {
                    foreach (Transform t in existing) if (t != null) slotCount++;
                }
            }

            if (slotCount == 0)
            {
                MethodInfo gen = typeof(BackpackInventoryZone).GetMethod(
                    "EditorGenerateSlots", BindingFlags.NonPublic | BindingFlags.Instance);
                if (gen != null)
                {
                    Undo.RecordObject(zone, "Backpack: generate 3x3 slots");
                    gen.Invoke(zone, null);
                    EditorUtility.SetDirty(zone);
                    fixesApplied++;
                    Debug.Log($"[PhoneSetup] Auto-generated 9 slots under '{zone.name}'.", zone);
                }
            }

            int slotsEquipped = EquipSlotsWithMetaComponents(zone, slotListField);
            if (slotsEquipped > 0)
            {
                fixesApplied += slotsEquipped;
            }

            int bodyGrabsWired = AutoWireBodyGrabSuppression(zone);
            if (bodyGrabsWired > 0)
            {
                fixesApplied += bodyGrabsWired;
            }

            if (phoneInstance != null)
            {
                SmsSwapTracker tracker = phoneInstance.GetComponent<SmsSwapTracker>();
                if (tracker != null)
                {
                    FieldInfo backpackField = typeof(SmsSwapTracker).GetField(
                        "_backpack", BindingFlags.NonPublic | BindingFlags.Instance);
                    if (backpackField != null && backpackField.GetValue(tracker) == null)
                    {
                        Undo.RecordObject(tracker, "SmsSwapTracker: wire backpack");
                        backpackField.SetValue(tracker, zone);
                        EditorUtility.SetDirty(tracker);
                        fixesApplied++;
                    }
                }
            }

            return fixesApplied == 0
                ? "ok (no fixes needed)"
                : $"applied {fixesApplied} fix(es) (incl. {slotsEquipped} slot Meta component(s))";
        }

        /// <summary>
        /// Walks every slot transform on the zone and ensures it carries the
        /// Meta ISDK stack required for hand-touch feedback and item snapping
        /// (kinematic Rigidbody + Grabbable + HandGrabInteractable +
        /// SnapInteractable + BoxCollider).
        /// RayInteractable is not needed for slots (hand touch is enough) and
        /// is actively removed if present from an earlier setup pass. Meta
        /// types are added via reflection so the script still compiles if the
        /// SDK is removed.
        /// </summary>
        /// <returns>Number of components added plus components removed across all slots.</returns>
        private static int EquipSlotsWithMetaComponents(BackpackInventoryZone zone, FieldInfo slotListField)
        {
            if (zone == null || slotListField == null)
            {
                return 0;
            }

            if (!(slotListField.GetValue(zone) is List<Transform> slots))
            {
                return 0;
            }

            FieldInfo sizeField = typeof(BackpackInventoryZone).GetField(
                "slotColliderSize", BindingFlags.NonPublic | BindingFlags.Instance);
            Vector3 boxSize = Vector3.one;
            if (sizeField != null && sizeField.GetValue(zone) is Vector3 configuredSize)
            {
                if (configuredSize.x < 0.5f || configuredSize.y < 0.5f || configuredSize.z < 0.5f)
                {
                    Undo.RecordObject(zone, "Backpack: migrate slot collider size");
                    sizeField.SetValue(zone, Vector3.one);
                    EditorUtility.SetDirty(zone);
                    boxSize = Vector3.one;
                    Debug.Log($"[PhoneSetup] Migrated slotColliderSize {configuredSize} -> {Vector3.one} on '{zone.name}'.", zone);
                }
                else
                {
                    boxSize = configuredSize;
                }
            }

            Type grabbableType = ResolveType("Oculus.Interaction.Grabbable");
            Type handGrabType = ResolveType("Oculus.Interaction.HandGrab.HandGrabInteractable");
            Type snapInteractableType = ResolveType("Oculus.Interaction.SnapInteractable");
            Type rayInteractableType = ResolveType("Oculus.Interaction.RayInteractable");
            int totalRemoved = 0;

            int totalAdded = 0;

            foreach (Transform slot in slots)
            {
                if (slot == null) continue;
                GameObject slotGo = slot.gameObject;

                BackpackSlot slotBehaviour = slotGo.GetComponent<BackpackSlot>();
                if (slotBehaviour == null)
                {
                    Undo.AddComponent<BackpackSlot>(slotGo);
                    totalAdded++;
                    Debug.Log($"[PhoneSetup] Added BackpackSlot to '{slotGo.name}'.", slotGo);
                }

                BoxCollider box = slotGo.GetComponent<BoxCollider>();
                if (box == null)
                {
                    box = Undo.AddComponent<BoxCollider>(slotGo);
                    totalAdded++;
                }
                if (box != null)
                {
                    Undo.RecordObject(box, "Backpack slot: configure box collider");
                    // Trigger so the slot's OnTriggerEnter/Exit item detection fires.
                    box.isTrigger = true;
                    box.size = boxSize;
                    box.center = Vector3.zero;
                    EditorUtility.SetDirty(box);
                }

                Rigidbody rb = slotGo.GetComponent<Rigidbody>();
                if (rb == null)
                {
                    rb = Undo.AddComponent<Rigidbody>(slotGo);
                    totalAdded++;
                }
                if (rb != null)
                {
                    Undo.RecordObject(rb, "Backpack slot: kinematic rigidbody");
                    rb.useGravity = false;
                    rb.isKinematic = true;
                    EditorUtility.SetDirty(rb);
                }

                if (grabbableType != null && slotGo.GetComponent(grabbableType) == null)
                {
                    try
                    {
                        Undo.AddComponent(slotGo, grabbableType);
                        totalAdded++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[PhoneSetup] Could not add Grabbable to slot '{slotGo.name}': {ex.Message}", slotGo);
                    }
                }

                if (handGrabType != null && slotGo.GetComponent(handGrabType) == null)
                {
                    try
                    {
                        Undo.AddComponent(slotGo, handGrabType);
                        totalAdded++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[PhoneSetup] Could not add HandGrabInteractable to slot '{slotGo.name}': {ex.Message}", slotGo);
                    }
                }

                // Snap target: items carry a SnapInteractor (see
                // InventoryItemMetaBridge) that snaps into this interactable
                // when released over the slot. Its Reset() picks up the
                // kinematic Rigidbody added above; one-item-per-slot limits
                // are enforced at runtime by BackpackSlot.
                if (snapInteractableType != null && slotGo.GetComponent(snapInteractableType) == null)
                {
                    try
                    {
                        Undo.AddComponent(slotGo, snapInteractableType);
                        totalAdded++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[PhoneSetup] Could not add SnapInteractable to slot '{slotGo.name}': {ex.Message}", slotGo);
                    }
                }

                // Slots only need hand touch (HandGrabInteractable); a
                // RayInteractable is not used here and can be left over from
                // an earlier setup pass, so strip it if found.
                if (rayInteractableType != null)
                {
                    Component existingRay = slotGo.GetComponent(rayInteractableType);
                    if (existingRay != null)
                    {
                        Undo.DestroyObjectImmediate(existingRay);
                        totalRemoved++;
                    }
                }

                EditorUtility.SetDirty(slotGo);
            }

            if (totalAdded > 0)
            {
                Debug.Log($"[PhoneSetup] Equipped backpack slots with Meta components: {totalAdded} component(s) added across {slots.Count} slot(s).", zone);
            }

            if (totalRemoved > 0)
            {
                Debug.Log($"[PhoneSetup] Removed {totalRemoved} stale RayInteractable component(s) from backpack slots.", zone);
            }

            return totalAdded + totalRemoved;
        }

        /// <summary>
        /// Walks the backpack hierarchy and populates
        /// <c>backpackBodyGrabbablesToSuppress</c> on the zone with the body's
        /// Grabbable / HandGrabInteractable components, so that the runtime
        /// can disable them while a hand is hovering an inventory slot.
        /// Skips anything under the zone transform (the slots themselves).
        /// Does not overwrite a manually populated list.
        /// </summary>
        /// <returns>Number of body grabbables wired (0 if none / already configured).</returns>
        private static int AutoWireBodyGrabSuppression(BackpackInventoryZone zone)
        {
            if (zone == null) return 0;

            FieldInfo suppressField = typeof(BackpackInventoryZone).GetField(
                "backpackBodyGrabbablesToSuppress", BindingFlags.NonPublic | BindingFlags.Instance);
            if (suppressField == null) return 0;

            if (!(suppressField.GetValue(zone) is List<Behaviour> currentList))
            {
                currentList = new List<Behaviour>();
            }

            int existingNonNull = 0;
            foreach (Behaviour b in currentList)
            {
                if (b != null) existingNonNull++;
            }
            if (existingNonNull > 0)
            {
                return 0;
            }

            Transform inventoryParent = zone.transform;

            Transform backpackRoot = zone.transform;
            while (backpackRoot.parent != null)
            {
                backpackRoot = backpackRoot.parent;
            }

            Type grabbableType = ResolveType("Oculus.Interaction.Grabbable");
            Type handGrabType = ResolveType("Oculus.Interaction.HandGrab.HandGrabInteractable");

            Component[] allComponents = backpackRoot.GetComponentsInChildren<Component>(true);
            List<Behaviour> discovered = new List<Behaviour>();

            foreach (Component comp in allComponents)
            {
                if (comp == null) continue;
                if (!(comp is Behaviour behaviour)) continue;

                bool isTargetType = false;
                if (grabbableType != null && grabbableType.IsInstanceOfType(comp)) isTargetType = true;
                if (!isTargetType && handGrabType != null && handGrabType.IsInstanceOfType(comp)) isTargetType = true;
                if (!isTargetType) continue;

                if (behaviour.transform == inventoryParent || behaviour.transform.IsChildOf(inventoryParent))
                {
                    continue;
                }

                if (behaviour.GetComponent<BackpackSlot>() != null)
                {
                    continue;
                }

                if (behaviour.GetComponent<InventoryItemMetaBridge>() != null ||
                    behaviour.GetComponentInParent<InventoryItemMetaBridge>() != null)
                {
                    continue;
                }

                discovered.Add(behaviour);
            }

            if (discovered.Count == 0)
            {
                Debug.LogWarning($"[PhoneSetup] Could not auto-discover any body Grabbable / HandGrabInteractable on backpack root '{backpackRoot.name}'. Per-slot body grab suppression will be inactive until the list is populated manually on the BackpackInventoryZone.", zone);
                return 0;
            }

            Undo.RecordObject(zone, "Backpack: auto-wire body grab suppression");
            currentList.Clear();
            currentList.AddRange(discovered);
            suppressField.SetValue(zone, currentList);
            EditorUtility.SetDirty(zone);

            Debug.Log($"[PhoneSetup] Auto-wired {discovered.Count} body grabbable(s) to suppress on backpack '{backpackRoot.name}'.", zone);
            return discovered.Count;
        }

        /// <summary>
        /// Best-effort: try to add Meta XR Interaction components to the
        /// Smartphone via reflection so the script compiles even if the SDK is
        /// removed from the project. Logs guidance if a type isn't present.
        /// </summary>
        private static string TryEnsurePhoneXRComponents(GameObject phoneInstance)
        {
            if (phoneInstance == null) return "no phone instance";

            int added = 0;
            int alreadyPresent = 0;
            List<string> missing = new List<string>();

            string[] requiredTypeNames = new[]
            {
                "Oculus.Interaction.Grabbable",
                "Oculus.Interaction.HandGrab.HandGrabInteractable",
                "Oculus.Interaction.RayInteractable",
            };

            foreach (string typeName in requiredTypeNames)
            {
                Type type = ResolveType(typeName);
                if (type == null)
                {
                    missing.Add(typeName);
                    continue;
                }

                Component existing = phoneInstance.GetComponent(type);
                if (existing != null)
                {
                    alreadyPresent++;
                    continue;
                }

                try
                {
                    Undo.AddComponent(phoneInstance, type);
                    added++;
                    Debug.Log($"[PhoneSetup] Added {type.Name} to Smartphone.", phoneInstance);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[PhoneSetup] Could not add {type.Name}: {ex.Message}", phoneInstance);
                    missing.Add(typeName + " (add failed)");
                }
            }

            return added == 0 && missing.Count == 0
                ? $"ok ({alreadyPresent} already present)"
                : $"added {added}, present {alreadyPresent}, missing {missing.Count}: {string.Join(", ", missing)}";
        }

        private static Type ResolveType(string fullName)
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
    }
}
#endif
