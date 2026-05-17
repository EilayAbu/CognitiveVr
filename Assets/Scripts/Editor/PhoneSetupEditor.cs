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
    /// Backpack mis-configurations (non-trigger collider, dynamic rigidbody, no
    /// slot transforms), tags any existing laptop / tablet inventory items by
    /// name and resolves the SessionTimer / SmsSwapTracker references.
    /// </summary>
    public static class PhoneSetupEditor
    {
        private const string PrefabPath = "Assets/Prefabs/phone/Smartphone.prefab";

        private static readonly string[] LaptopNames = {
            "Laptop", "laptop", "LaptopItem", "Laptop_Pickup", "Notebook"
        };

        private static readonly string[] TabletNames = {
            "Tablet", "tablet", "TabletItem", "Tablet_Pickup", "iPad"
        };

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
            int taggedLaptops = TagItemsByName(LaptopNames, ItemId.Laptop);
            int taggedTablets = TagItemsByName(TabletNames, ItemId.Tablet);
            int wiredItemRefs = WireSmsSwapTrackerItemRefs(phoneInstance);
            string scheduledEventsStatus = EnsureScheduledEvents();
            string backpackStatus = EnsureBackpackSetup(phoneInstance);
            string xrStatus = TryEnsurePhoneXRComponents(phoneInstance);
            string eventSystemStatus = EnsurePointableCanvasModuleOnEventSystem();

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

            EditorUtility.DisplayDialog("Smartphone Setup Complete",
                $"Smartphone prefab is in the scene.\n" +
                $"Tagged {taggedLaptops} laptop item(s) and {taggedTablets} tablet item(s) with ItemId.\n" +
                $"Wired {wiredItemRefs} explicit item ref(s) on SmsSwapTracker.\n" +
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

        private static int TagItemsByName(string[] candidateNames, ItemId itemId)
        {
            int count = 0;
            HashSet<GameObject> seen = new HashSet<GameObject>();

            foreach (string n in candidateNames)
            {
                GameObject go = GameObject.Find(n);
                if (go == null) continue;
                if (!seen.Add(go)) continue;

                InventoryItemMetaBridge bridge = go.GetComponent<InventoryItemMetaBridge>();
                if (bridge == null)
                    bridge = go.GetComponentInChildren<InventoryItemMetaBridge>();

                if (bridge == null)
                {
                    Debug.LogWarning(
                        $"[PhoneSetup] Found '{n}' but it has no InventoryItemMetaBridge component. " +
                        $"Skipping {itemId} tagging.");
                    continue;
                }

                FieldInfo idField = typeof(InventoryItemMetaBridge).GetField(
                    "itemId", BindingFlags.NonPublic | BindingFlags.Instance);
                if (idField != null)
                {
                    Undo.RecordObject(bridge, $"Tag {bridge.name} as {itemId}");
                    idField.SetValue(bridge, itemId);
                    EditorUtility.SetDirty(bridge);
                    count++;
                    Debug.Log($"[PhoneSetup] Tagged '{bridge.name}' as ItemId.{itemId}.");
                }
            }

            return count;
        }

        private static string EnsureBackpackSetup(GameObject phoneInstance)
        {
            BackpackInventoryZone zone = UnityEngine.Object.FindFirstObjectByType<BackpackInventoryZone>();
            if (zone == null)
            {
                return "no BackpackInventoryZone in scene";
            }

            int fixesApplied = 0;

            Collider zoneCollider = zone.GetComponent<Collider>();
            if (zoneCollider != null && !zoneCollider.isTrigger)
            {
                Undo.RecordObject(zoneCollider, "Backpack: force collider to trigger");
                zoneCollider.isTrigger = true;
                EditorUtility.SetDirty(zoneCollider);
                fixesApplied++;
                Debug.Log($"[PhoneSetup] Forced backpack collider on '{zone.name}' to trigger.", zone);
            }

            if (zoneCollider is BoxCollider box && box.size.y < 0.01f)
            {
                Undo.RecordObject(box, "Backpack: give trigger box a non-zero Y size");
                Vector3 s = box.size;
                s.y = Mathf.Max(s.y, 8f);
                box.size = s;
                Vector3 c = box.center;
                c.y = s.y * 0.5f;
                box.center = c;
                EditorUtility.SetDirty(box);
                fixesApplied++;
                Debug.Log($"[PhoneSetup] Expanded backpack box collider Y size on '{zone.name}'.", zone);
            }

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

            FieldInfo inventoryParentField = typeof(BackpackInventoryZone).GetField(
                "inventoryParent", BindingFlags.NonPublic | BindingFlags.Instance);
            if (inventoryParentField != null)
            {
                Transform current = inventoryParentField.GetValue(zone) as Transform;
                if (current == null)
                {
                    Undo.RecordObject(zone, "Backpack: assign inventoryParent");
                    inventoryParentField.SetValue(zone, zone.transform);
                    EditorUtility.SetDirty(zone);
                    fixesApplied++;
                }
            }

            FieldInfo slotListField = typeof(BackpackInventoryZone).GetField(
                "slotTransforms", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo metaSnapField = typeof(BackpackInventoryZone).GetField(
                "metaSnapZones", BindingFlags.NonPublic | BindingFlags.Instance);

            int slotCount = 0;
            if (slotListField != null)
            {
                if (slotListField.GetValue(zone) is List<Transform> existing)
                {
                    foreach (Transform t in existing) if (t != null) slotCount++;
                }
            }
            if (slotCount == 0 && metaSnapField != null)
            {
                if (metaSnapField.GetValue(zone) is List<MonoBehaviour> existingMeta)
                {
                    foreach (MonoBehaviour mb in existingMeta) if (mb != null) slotCount++;
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

            int bodyGrabsWired = AutoWireBodyGrabSuppression(zone, inventoryParentField);
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
        /// Meta ISDK stack required for hand-grab interaction (kinematic
        /// Rigidbody + Grabbable + HandGrabInteractable + RayInteractable +
        /// BoxCollider). Meta types are added via reflection so the script
        /// still compiles if the SDK is removed.
        /// </summary>
        /// <returns>Number of components added across all slots.</returns>
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
            Type rayInteractableType = ResolveType("Oculus.Interaction.RayInteractable");

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
                    box.isTrigger = false;
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

                if (rayInteractableType != null && slotGo.GetComponent(rayInteractableType) == null)
                {
                    try
                    {
                        Undo.AddComponent(slotGo, rayInteractableType);
                        totalAdded++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"[PhoneSetup] Could not add RayInteractable to slot '{slotGo.name}': {ex.Message}", slotGo);
                    }
                }

                EditorUtility.SetDirty(slotGo);
            }

            if (totalAdded > 0)
            {
                Debug.Log($"[PhoneSetup] Equipped backpack slots with Meta components: {totalAdded} component(s) added across {slots.Count} slot(s).", zone);
            }

            return totalAdded;
        }

        /// <summary>
        /// Walks the backpack hierarchy and populates
        /// <c>backpackBodyGrabbablesToSuppress</c> on the zone with the body's
        /// Grabbable / HandGrabInteractable components, so that the runtime
        /// can disable them while a hand is hovering an inventory slot.
        /// Skips anything inside the inventory parent (the slots themselves).
        /// Does not overwrite a manually populated list.
        /// </summary>
        /// <returns>Number of body grabbables wired (0 if none / already configured).</returns>
        private static int AutoWireBodyGrabSuppression(BackpackInventoryZone zone, FieldInfo inventoryParentField)
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

            Transform inventoryParent = null;
            if (inventoryParentField != null)
            {
                inventoryParent = inventoryParentField.GetValue(zone) as Transform;
            }
            if (inventoryParent == null)
            {
                inventoryParent = zone.transform;
            }

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

        private static int WireSmsSwapTrackerItemRefs(GameObject phoneInstance)
        {
            if (phoneInstance == null) return 0;

            SmsSwapTracker tracker = phoneInstance.GetComponent<SmsSwapTracker>();
            if (tracker == null) return 0;

            int wired = 0;
            InventoryItemMetaBridge[] all = UnityEngine.Object.FindObjectsByType<InventoryItemMetaBridge>(FindObjectsSortMode.None);

            FieldInfo laptopField = typeof(SmsSwapTracker).GetField(
                "_laptop", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo tabletField = typeof(SmsSwapTracker).GetField(
                "_tablet", BindingFlags.NonPublic | BindingFlags.Instance);

            InventoryItemMetaBridge laptop = null;
            InventoryItemMetaBridge tablet = null;
            foreach (InventoryItemMetaBridge item in all)
            {
                if (item == null) continue;
                if (item.ItemId == ItemId.Laptop && laptop == null) laptop = item;
                if (item.ItemId == ItemId.Tablet && tablet == null) tablet = item;
            }

            if (laptopField != null && laptop != null && (laptopField.GetValue(tracker) as InventoryItemMetaBridge) == null)
            {
                Undo.RecordObject(tracker, "SmsSwapTracker: wire laptop");
                laptopField.SetValue(tracker, laptop);
                EditorUtility.SetDirty(tracker);
                wired++;
            }
            if (tabletField != null && tablet != null && (tabletField.GetValue(tracker) as InventoryItemMetaBridge) == null)
            {
                Undo.RecordObject(tracker, "SmsSwapTracker: wire tablet");
                tabletField.SetValue(tracker, tablet);
                EditorUtility.SetDirty(tracker);
                wired++;
            }

            if (laptop == null) Debug.LogWarning("[PhoneSetup] No InventoryItemMetaBridge tagged ItemId.Laptop found in scene.");
            if (tablet == null) Debug.LogWarning("[PhoneSetup] No InventoryItemMetaBridge tagged ItemId.Tablet found in scene. SmsSwap completion will not fire without both items.");

            return wired;
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
