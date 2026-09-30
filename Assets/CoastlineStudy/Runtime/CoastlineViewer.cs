using UnityEngine;

namespace Colorido.Coastline
{
    [RequireComponent(typeof(Camera))]
    public sealed class CoastlineViewer : MonoBehaviour
    {
        public Vector3 focus = new Vector3(0, 1.1f, 0);
        public bool autoRotate;
        public bool pixelate = true;
        [Range(120, 720)] public int verticalResolution = 270;
        private float yaw = 42f;
        private float pitch = 32f;
        private Camera view;
        private Vector2 previousMouse;

        private void OnEnable() { view = GetComponent<Camera>(); PositionCamera(); }
        private void Update()
        {
            if (autoRotate) { yaw += Time.deltaTime * 9f; PositionCamera(); }
        }
        // IMGUI events keep this viewer independent of the project's input backend.
        private void OnGUI()
        {
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 1) previousMouse = e.mousePosition;
            if (e.type == EventType.MouseDrag && e.button == 1)
            {
                Vector2 delta = e.mousePosition - previousMouse;
                previousMouse = e.mousePosition;
                yaw += delta.x * .3f;
                pitch = Mathf.Clamp(pitch + delta.y * .2f, 15f, 75f);
                PositionCamera(); e.Use();
            }
            if (e.type == EventType.ScrollWheel)
            {
                view.orthographicSize = Mathf.Clamp(view.orthographicSize + e.delta.y * .15f, 4f, 16f);
                e.Use();
            }
            GUILayout.BeginArea(new Rect(16, 16, 360, 235), GUI.skin.box);
            GUILayout.Label("YELLOW COAST / Model study");
            GUILayout.Label("Right drag: orbit   /   Scroll: zoom");
            autoRotate = GUILayout.Toggle(autoRotate, "Auto rotate");
            if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline == null)
                pixelate = GUILayout.Toggle(pixelate, "Pixel preview (270p)");
            else
            {
                var post = GetComponent<CoastlinePostSettings>();
                if (post != null)
                {
                    post.effectEnabled = GUILayout.Toggle(post.effectEnabled, "Painterly toon finish");
                    post.compareOriginal = GUILayout.Toggle(post.compareOriginal, "Compare paint: original left / painted right");
                    post.softGlow = GUILayout.Toggle(post.softGlow, "Soft glow (Bloom)");
                    GUILayout.BeginHorizontal();
                    if (GUILayout.Button("Soft toon")) post.Preset(false);
                    if (GUILayout.Button("Stronger strokes")) post.Preset(true);
                    GUILayout.EndHorizontal();
                    GUILayout.Label("Brush size: " + post.brushRadius.ToString("F1"));
                    post.brushRadius = GUILayout.HorizontalSlider(post.brushRadius, 1, 12);
                }
                else GUILayout.Label("Install stylized post from Tools / Colorido");
            }
            GUILayout.EndArea();
        }
        private void PositionCamera()
        {
            transform.rotation = Quaternion.Euler(pitch, yaw, 0);
            transform.position = focus - transform.forward * 24f;
        }
        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (!pixelate) { Graphics.Blit(source, destination); return; }
            int height = Mathf.Max(120, verticalResolution);
            int width = Mathf.Max(1, Mathf.RoundToInt(height * source.width / (float)source.height));
            RenderTexture small = RenderTexture.GetTemporary(width, height, 0, source.format);
            small.filterMode = FilterMode.Point;
            Graphics.Blit(source, small);
            Graphics.Blit(small, destination);
            RenderTexture.ReleaseTemporary(small);
        }
    }
}
