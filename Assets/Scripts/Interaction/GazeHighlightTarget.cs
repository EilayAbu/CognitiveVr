using System.Collections.Generic;
using UnityEngine;

namespace CognitiveVR.Interaction
{
    /// <summary>
    /// Target marker for gaze-driven highlights.
    /// Applies shader outline properties when available and can fall back to emission.
    /// </summary>
    [DisallowMultipleComponent]
    public class GazeHighlightTarget : MonoBehaviour
    {
        [Header("Target Renderers")]
        [SerializeField] private Renderer[] _renderers;
        [SerializeField] private bool _includeChildren = true;

        [Header("Highlight Style")]
        [SerializeField] private Color _outlineColor = new Color(1f, 0.9f, 0.15f, 1f);
        [SerializeField] private float _outlineWidth = 2f;
        [SerializeField] private bool _fallbackToEmission = true;
        [SerializeField] private float _emissionIntensity = 0.85f;

        private static readonly int OutlineColorId = Shader.PropertyToID("_OutlineColor");
        private static readonly int OutlineWidthId = Shader.PropertyToID("_OutlineWidth");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private readonly List<RendererEntry> _entries = new List<RendererEntry>(8);
        private MaterialPropertyBlock _propertyBlock;
        private bool _isHighlighted;

        public bool IsHighlighted => _isHighlighted;

        private void Awake()
        {
            CacheRenderers();
            BuildRendererEntries();
        }

        public void SetHighlighted(bool highlighted)
        {
            if (_isHighlighted == highlighted)
                return;

            _isHighlighted = highlighted;
            ApplyHighlightState();
        }

        private void OnDisable()
        {
            if (_isHighlighted)
            {
                _isHighlighted = false;
                ApplyHighlightState();
            }
        }

        private void CacheRenderers()
        {
            if (_renderers != null && _renderers.Length > 0)
                return;

            if (_includeChildren)
            {
                _renderers = GetComponentsInChildren<Renderer>(true);
            }
            else
            {
                Renderer own = GetComponent<Renderer>();
                _renderers = own != null ? new[] { own } : new Renderer[0];
            }
        }

        private void BuildRendererEntries()
        {
            _entries.Clear();
            _propertyBlock ??= new MaterialPropertyBlock();

            if (_renderers == null || _renderers.Length == 0)
                return;

            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i];
                if (renderer == null)
                    continue;

                bool supportsOutline = false;
                bool supportsEmission = false;
                Material[] mats = renderer.sharedMaterials;
                for (int m = 0; m < mats.Length; m++)
                {
                    Material mat = mats[m];
                    if (mat == null)
                        continue;

                    if (!supportsOutline && mat.HasProperty(OutlineColorId) && mat.HasProperty(OutlineWidthId))
                        supportsOutline = true;

                    if (!supportsEmission && mat.HasProperty(EmissionColorId))
                        supportsEmission = true;

                    if (supportsOutline && supportsEmission)
                        break;
                }

                _entries.Add(new RendererEntry(renderer, supportsOutline, supportsEmission));
            }
        }

        private void ApplyHighlightState()
        {
            if (_entries.Count == 0)
                return;

            for (int i = 0; i < _entries.Count; i++)
            {
                RendererEntry entry = _entries[i];
                if (entry.Renderer == null)
                    continue;

                entry.Renderer.GetPropertyBlock(_propertyBlock);

                if (_isHighlighted)
                {
                    if (entry.SupportsOutline)
                    {
                        _propertyBlock.SetColor(OutlineColorId, _outlineColor);
                        _propertyBlock.SetFloat(OutlineWidthId, _outlineWidth);
                    }

                    if (entry.SupportsEmission && (_fallbackToEmission || !entry.SupportsOutline))
                    {
                        _propertyBlock.SetColor(EmissionColorId, _outlineColor * Mathf.Max(0f, _emissionIntensity));
                    }
                }
                else
                {
                    if (entry.SupportsOutline)
                    {
                        _propertyBlock.SetFloat(OutlineWidthId, 0f);
                    }

                    if (entry.SupportsEmission && (_fallbackToEmission || !entry.SupportsOutline))
                    {
                        _propertyBlock.SetColor(EmissionColorId, Color.black);
                    }
                }

                entry.Renderer.SetPropertyBlock(_propertyBlock);
            }
        }

        private readonly struct RendererEntry
        {
            public readonly Renderer Renderer;
            public readonly bool SupportsOutline;
            public readonly bool SupportsEmission;

            public RendererEntry(Renderer renderer, bool supportsOutline, bool supportsEmission)
            {
                Renderer = renderer;
                SupportsOutline = supportsOutline;
                SupportsEmission = supportsEmission;
            }
        }
    }
}
