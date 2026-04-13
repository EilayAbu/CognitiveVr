using UnityEngine;

namespace CognitiveVR.Tasks
{
    /// <summary>
    /// Attach to the Toaster GameObject alongside ToasterController.
    /// Creates a ParticleSystem at runtime if none is assigned, positioned
    /// at the toaster's top slot area.
    /// </summary>
    [RequireComponent(typeof(ToasterController))]
    public class ToasterSmokeSetup : MonoBehaviour
    {
        [Tooltip("Assign an existing ParticleSystem or leave empty for auto-creation")]
        [SerializeField] private ParticleSystem _existingParticleSystem;

        [Header("Auto-Creation Settings")]
        [Tooltip("Local offset from this transform for the smoke emitter")]
        [SerializeField] private Vector3 _smokeLocalPosition = new Vector3(0f, 0.15f, 0f);
        [SerializeField] private float _coneAngle = 15f;
        [SerializeField] private float _coneRadius = 0.03f;

        private void Awake()
        {
            var controller = GetComponent<ToasterController>();
            if (controller == null) return;

            ParticleSystem ps = _existingParticleSystem;

            if (ps == null)
                ps = CreateSmokeParticleSystem();

            AssignToController(controller, ps);
        }

        private ParticleSystem CreateSmokeParticleSystem()
        {
            var smokeObj = new GameObject("SmokeEffect");
            smokeObj.transform.SetParent(transform, false);
            smokeObj.transform.localPosition = _smokeLocalPosition;

            var ps = smokeObj.AddComponent<ParticleSystem>();

            var main = ps.main;
            main.startLifetime = 2.5f;
            main.startSpeed = 0.4f;
            main.startSize = 0.03f;
            main.startColor = new Color(0.75f, 0.75f, 0.75f, 0.3f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.05f;
            main.maxParticles = 200;
            main.loop = true;
            main.playOnAwake = false;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = _coneAngle;
            shape.radius = _coneRadius;

            var sizeOverLifetime = ps.sizeOverLifetime;
            sizeOverLifetime.enabled = true;
            sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(
                    new Keyframe(0f, 0.5f),
                    new Keyframe(0.5f, 1f),
                    new Keyframe(1f, 1.5f)));

            var colorOverLifetime = ps.colorOverLifetime;
            colorOverLifetime.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0.6f, 0f), new GradientAlphaKey(0.3f, 0.5f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = gradient;

            var renderer = smokeObj.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.material = GetDefaultParticleMaterial();

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            return ps;
        }

        private Material GetDefaultParticleMaterial()
        {
            var mat = new Material(Shader.Find("Particles/Standard Unlit"));
            mat.SetFloat("_Mode", 1f); // Additive is 1, AlphaBlended is 0
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            return mat;
        }

        private void AssignToController(ToasterController controller, ParticleSystem ps)
        {
            controller.SetSmokeParticles(ps);
        }
    }
}
