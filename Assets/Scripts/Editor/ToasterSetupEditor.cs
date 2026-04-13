#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using CognitiveVR.Tasks;

namespace CognitiveVR.EditorTools
{
    public static class ToasterSetupEditor
    {
        [MenuItem("CognitiveVR/Setup Toaster Prefab")]
        public static void SetupToasterPrefab()
        {
            var toasterObj = GameObject.Find("Toaster");
            if (toasterObj == null)
            {
                EditorUtility.DisplayDialog("Setup Failed",
                    "Could not find a GameObject named 'Toaster' in the scene.\n" +
                    "Make sure the Toaster prefab is in the scene before running setup.",
                    "OK");
                return;
            }

            Undo.RegisterCompleteObjectUndo(toasterObj, "Setup Toaster Components");

            var cogTask = toasterObj.GetComponent<CognitiveTask>();
            if (cogTask == null)
                cogTask = Undo.AddComponent<CognitiveTask>(toasterObj);

            var taskTypeField = typeof(CognitiveTask).GetField("_taskType",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (taskTypeField != null)
                taskTypeField.SetValue(cogTask, TaskType.Toast);

            var controller = toasterObj.GetComponent<ToasterController>();
            if (controller == null)
                controller = Undo.AddComponent<ToasterController>(toasterObj);

            var smokeSetup = toasterObj.GetComponent<ToasterSmokeSetup>();
            if (smokeSetup == null)
                smokeSetup = Undo.AddComponent<ToasterSmokeSetup>(toasterObj);

            Transform upperTust = toasterObj.transform.Find("upperTust");
            if (upperTust != null)
            {
                var upperTustField = typeof(ToasterController).GetField("_upperTust",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (upperTustField != null)
                    upperTustField.SetValue(controller, upperTust);
            }
            else
            {
                Debug.LogWarning("[ToasterSetup] 'upperTust' child not found under Toaster.");
            }

            var cogTaskField = typeof(ToasterController).GetField("_cognitiveTask",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (cogTaskField != null)
                cogTaskField.SetValue(controller, cogTask);

            ApplyControllerDefaults(controller);
            SetupTriggerZone(toasterObj);
            ConfigureLidPhysics(toasterObj.transform.Find("upperTust"), toasterObj);
            SetupToastObjects(toasterObj, controller);

            EditorUtility.SetDirty(toasterObj);
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(cogTask);

            Debug.Log("[ToasterSetup] Toaster setup complete. " +
                      "Assign the 3 toast mesh objects (fresh/ready/burnt) in the Inspector if not auto-detected.");

            EditorUtility.DisplayDialog("Setup Complete",
                "Toaster components added:\n" +
                "- CognitiveTask (Toast)\n" +
                "- ToasterController\n" +
                "- ToasterSmokeSetup\n\n" +
                "Please verify the toast object references (fresh/ready/burnt) in the Inspector.",
                "OK");
        }

        private static void ApplyControllerDefaults(ToasterController controller)
        {
            const System.Reflection.BindingFlags flags =
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

            SetField(controller, "_lidClosedAngle", 80f, flags);
            SetField(controller, "_minLidAngle", 47f, flags);
            SetField(controller, "_maxLidAngle", 90f, flags);
        }

        private static void SetupTriggerZone(GameObject toasterObj)
        {
            Transform triggerTransform = toasterObj.transform.Find("ToastTrigger");
            if (triggerTransform == null)
            {
                var triggerObj = new GameObject("ToastTrigger");
                triggerObj.transform.SetParent(toasterObj.transform, false);
                triggerObj.transform.localPosition = new Vector3(0f, 0.08f, 0f);
                triggerObj.transform.localRotation = Quaternion.identity;
                triggerObj.transform.localScale = Vector3.one;
                Undo.RegisterCreatedObjectUndo(triggerObj, "Create ToastTrigger");
                triggerTransform = triggerObj.transform;
            }

            var triggerCollider = triggerTransform.GetComponent<BoxCollider>();
            if (triggerCollider == null)
                triggerCollider = Undo.AddComponent<BoxCollider>(triggerTransform.gameObject);

            triggerCollider.isTrigger = true;
            triggerCollider.center = Vector3.zero;
            triggerCollider.size = new Vector3(0.24f, 0.12f, 0.16f);

            var zone = triggerTransform.GetComponent<ToasterTriggerZone>();
            if (zone == null)
                zone = Undo.AddComponent<ToasterTriggerZone>(triggerTransform.gameObject);

            EditorUtility.SetDirty(triggerTransform.gameObject);
        }

        private static void ConfigureLidPhysics(Transform upperTust, GameObject toasterObj)
        {
            if (upperTust == null)
            {
                Debug.LogWarning("[ToasterSetup] Cannot configure lid: 'upperTust' not found.");
                return;
            }

            // Remove HingeJoint if it was added previously -- it causes instability
            var oldHinge = upperTust.GetComponent<HingeJoint>();
            if (oldHinge != null)
                Undo.DestroyObjectImmediate(oldHinge);

            // Lid Rigidbody: kinematic, no gravity, freeze everything except Z rotation
            var lidRb = upperTust.GetComponent<Rigidbody>();
            if (lidRb == null)
                lidRb = Undo.AddComponent<Rigidbody>(upperTust.gameObject);

            lidRb.isKinematic = true;
            lidRb.useGravity = false;
            lidRb.constraints =
                RigidbodyConstraints.FreezePositionX |
                RigidbodyConstraints.FreezePositionY |
                RigidbodyConstraints.FreezePositionZ |
                RigidbodyConstraints.FreezeRotationX |
                RigidbodyConstraints.FreezeRotationY;

            // Angle limits (47-90) are enforced by ToasterController.ClampLidRotation() in LateUpdate

            EditorUtility.SetDirty(upperTust.gameObject);
        }

        private static void SetField(ToasterController controller, string fieldName, float value, System.Reflection.BindingFlags flags)
        {
            var field = typeof(ToasterController).GetField(fieldName, flags);
            if (field != null)
                field.SetValue(controller, value);
        }

        private static void SetupToastObjects(GameObject toaster, ToasterController controller)
        {
            var freshField = typeof(ToasterController).GetField("_freshToast",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var readyField = typeof(ToasterController).GetField("_readyToast",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var burntField = typeof(ToasterController).GetField("_burntToast",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            string[] freshNames = { "FreshToast", "freshToast", "Toast_Fresh", "toast_fresh" };
            string[] readyNames = { "ReadyToast", "readyToast", "Toast_Ready", "toast_ready" };
            string[] burntNames = { "BurntToast", "burntToast", "Toast_Burnt", "toast_burnt" };

            GameObject fresh = FindChildByNames(toaster.transform, freshNames);
            GameObject ready = FindChildByNames(toaster.transform, readyNames);
            GameObject burnt = FindChildByNames(toaster.transform, burntNames);

            if (fresh == null || ready == null || burnt == null)
            {
                Debug.Log("[ToasterSetup] Creating toast placeholder objects. " +
                          "Replace these with your actual toast meshes.");

                if (fresh == null) fresh = CreatePlaceholder(toaster.transform, "FreshToast", new Vector3(0, 0.08f, 0));
                if (ready == null) ready = CreatePlaceholder(toaster.transform, "ReadyToast", new Vector3(0, 0.08f, 0));
                if (burnt == null) burnt = CreatePlaceholder(toaster.transform, "BurntToast", new Vector3(0, 0.08f, 0));

                ready.SetActive(false);
                burnt.SetActive(false);
            }

            if (freshField != null) freshField.SetValue(controller, fresh);
            if (readyField != null) readyField.SetValue(controller, ready);
            if (burntField != null) burntField.SetValue(controller, burnt);
        }

        private static GameObject FindChildByNames(Transform parent, string[] names)
        {
            foreach (string name in names)
            {
                Transform child = parent.Find(name);
                if (child != null) return child.gameObject;

                for (int i = 0; i < parent.childCount; i++)
                {
                    child = parent.GetChild(i).Find(name);
                    if (child != null) return child.gameObject;
                }
            }
            return null;
        }

        private static GameObject CreatePlaceholder(Transform parent, string name, Vector3 localPos)
        {
            var obj = new GameObject(name);
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = localPos;
            Undo.RegisterCreatedObjectUndo(obj, $"Create {name}");
            return obj;
        }
    }
}
#endif
