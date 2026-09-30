using UnityEngine;

namespace Colorido.Coastline
{
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(Camera))]
    public sealed class CoastlinePostSettings : MonoBehaviour
    {
        [Tooltip("Material assigned to this camera's dedicated URP full screen renderer.")]
        public Material postMaterial;
        public bool effectEnabled = true;
        public bool compareOriginal;
        public bool softGlow = true;
        public Behaviour glowVolume;
        [Range(1,12)] public float brushRadius = 3.5f;
        [Range(0,1)] public float paintBlending = .55f;
        [Range(0,1)] public float pigmentTexture = .16f;
        [Range(0,2)] public float saturation = 1.04f;
        [Range(.5f,1.5f)] public float contrast = 1.025f;
        [Range(0,1)] public float paletteTint = .12f;
        private void OnEnable() { Apply(); }
        private void OnValidate() { Apply(); }
        private void Update() { Apply(); }
        private void OnDisable() { if (postMaterial != null) postMaterial.SetFloat("_Enabled", 0); }
        public void Apply()
        {
            if (glowVolume != null) glowVolume.enabled = effectEnabled && softGlow;
            if (postMaterial == null) return;
            postMaterial.SetFloat("_Enabled", effectEnabled ? 1 : 0);
            postMaterial.SetFloat("_Compare", compareOriginal ? 1 : 0);
            postMaterial.SetFloat("_BrushRadius", Mathf.Clamp(brushRadius,1,12));
            postMaterial.SetFloat("_Blend",paintBlending);
            postMaterial.SetFloat("_Texture",pigmentTexture);
            postMaterial.SetFloat("_Saturation",saturation);
            postMaterial.SetFloat("_Contrast",contrast);
            postMaterial.SetFloat("_Tint",paletteTint);
        }
        public void Preset(bool stronger)
        {
            effectEnabled = true;
            brushRadius = stronger ? 6 : 3.5f;
            paintBlending = stronger ? .85f : .55f;
            pigmentTexture = stronger ? .3f : .16f;
            saturation = 1.04f; contrast = 1.025f; paletteTint = .12f;
            Apply();
        }
    }
}
