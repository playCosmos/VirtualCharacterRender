using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VCR.Runtime.Rendering
{
    /// <summary>
    /// Small runtime alpha-reference pattern for P0 standalone/OBS validation.
    ///
    /// It intentionally uses the same URP/back-buffer path as the character
    /// instead of a separate RenderTexture.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class P0AlphaTestPattern : MonoBehaviour
    {
        [SerializeField] private bool visible = true;
        [SerializeField] private Vector3 origin =
            new Vector3(-1.15f, -0.65f, 0.15f);
        [SerializeField, Min(0.05f)] private float size = 0.32f;
        [SerializeField, Min(0.01f)] private float gap = 0.08f;

        private readonly List<GameObject> _objects = new();
        private readonly List<Material> _materials = new();

        private void Start()
        {
            if (visible)
            {
                Build();
            }
        }

        [ContextMenu("Show Alpha Test Pattern")]
        public void Show()
        {
            visible = true;
            Build();
        }

        [ContextMenu("Hide Alpha Test Pattern")]
        public void Hide()
        {
            visible = false;
            Clear();
        }

        private void Build()
        {
            Clear();

            var shader =
                Shader.Find("Universal Render Pipeline/Unlit");

            if (shader == null)
            {
                Debug.LogError(
                    "VCR P0 alpha test: URP Unlit shader was not found.",
                    this);
                return;
            }

            CreatePatch(
                "Alpha 100%",
                origin,
                new Color(1f, 1f, 1f, 1f),
                shader);

            CreatePatch(
                "Alpha 50%",
                origin +
                Vector3.right * (size + gap),
                new Color(1f, 1f, 1f, 0.5f),
                shader);

            CreatePatch(
                "Alpha 25%",
                origin +
                Vector3.right * 2f * (size + gap),
                new Color(1f, 1f, 1f, 0.25f),
                shader);

            // Two overlapping 50% patches make premultiplied/straight-alpha
            // edge mistakes visually obvious in OBS/window capture.
            CreatePatch(
                "Overlap A",
                origin +
                Vector3.up * (size + gap),
                new Color(1f, 0.25f, 0.25f, 0.5f),
                shader);

            CreatePatch(
                "Overlap B",
                origin +
                Vector3.up * (size + gap) +
                new Vector3(size * 0.45f, size * 0.15f, -0.01f),
                new Color(0.25f, 0.6f, 1f, 0.5f),
                shader);
        }

        private void CreatePatch(
            string name,
            Vector3 localPosition,
            Color color,
            Shader shader)
        {
            var patch =
                GameObject.CreatePrimitive(PrimitiveType.Quad);
            patch.name = name;
            patch.transform.SetParent(transform, false);
            patch.transform.localPosition = localPosition;
            patch.transform.localRotation = Quaternion.identity;
            patch.transform.localScale =
                new Vector3(size, size, 1f);

            var collider =
                patch.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }

            var material =
                new Material(shader)
                {
                    name = "VCR P0 " + name,
                    hideFlags = HideFlags.DontSave,
                    renderQueue = (int)RenderQueue.Transparent
                };

            material.SetColor("_BaseColor", color);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat(
                "_SrcBlend",
                (float)BlendMode.SrcAlpha);
            material.SetFloat(
                "_DstBlend",
                (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword(
                "_SURFACE_TYPE_TRANSPARENT");

            patch.GetComponent<MeshRenderer>().sharedMaterial =
                material;

            _objects.Add(patch);
            _materials.Add(material);
        }

        private void Clear()
        {
            foreach (var item in _objects)
            {
                if (item != null)
                {
                    Destroy(item);
                }
            }

            foreach (var material in _materials)
            {
                if (material != null)
                {
                    Destroy(material);
                }
            }

            _objects.Clear();
            _materials.Clear();
        }

        private void OnDestroy()
        {
            Clear();
        }
    }
}
