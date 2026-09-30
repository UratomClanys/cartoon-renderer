using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace CartoonProjection
{
    // The camera image is replaced by CPU-generated 2D polygon geometry.
    // Original textures are only used by the capture stage, never the redraw stage.
    public sealed class ProjectedShapePass : ScriptableRenderPass, IDisposable
    {
        sealed class State
        {
            public Camera camera;
            public Mesh mesh, lines;
            public bool busy, disposed;
            public double next;
            public Task<ProjectedShapeBuilder.Result> task;
        }
        struct Draw { public Renderer renderer; public Material material; public int submesh; }
        sealed class CaptureData { public List<Draw> draws; public int width, height; }
        sealed class ReadData { public TextureHandle color, ids; public State state; public int width,height,step,area; public float epsilon; }
        sealed class RedrawData { public Mesh mesh, lines; public Material material; }
        readonly Dictionary<int, State> states = new();
        readonly Dictionary<long, Material> materials = new();
        readonly Material redraw;
        readonly Shader captureShader;
        CartoonRenderSettings settings;
        Renderer[] renderers = Array.Empty<Renderer>();
        double refresh;
        public static string LastStatistics { get; private set; } = "Waiting for projection";

        public ProjectedShapePass()
        {
            renderPassEvent = RenderPassEvent.BeforeRenderingTransparents;
            requiresIntermediateTexture = true;
            captureShader = Shader.Find("Hidden/CartoonProjection/ProjectedCapture");
            var shader = Shader.Find("Hidden/CartoonProjection/ProjectedRedraw");
            if (shader) redraw = CoreUtils.CreateEngineMaterial(shader);
        }
        public void Setup(CartoonRenderSettings value) => settings = value;
        public override void RecordRenderGraph(RenderGraph graph, ContextContainer frame)
        {
            if (!captureShader || !redraw) return;
            var camera = frame.Get<UniversalCameraData>().camera;
            var resources = frame.Get<UniversalResourceData>();
            if (resources.isActiveTargetBackBuffer) return;
            if (!states.TryGetValue(camera.GetInstanceID(), out var state))
                states.Add(camera.GetInstanceID(), state = new State { camera=camera });
            if (state.task != null && state.task.IsCompleted)
            {
                if (state.task.IsCompletedSuccessfully)
                {
                    var r = state.task.Result;
                    if (!state.mesh) state.mesh = new Mesh { name = "Projected 2D filled polygons", indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.HideAndDontSave };
                    state.mesh.Clear(); state.mesh.vertices = r.vertices; state.mesh.colors32 = r.colors; state.mesh.triangles = r.triangles;
                    if (!state.lines) state.lines = new Mesh { name = "Simplified shared contours", indexFormat = IndexFormat.UInt32, hideFlags = HideFlags.HideAndDontSave };
                    state.lines.Clear(); state.lines.vertices = r.lines;
                    var indices = new int[r.lines.Length]; var colors = new Color32[r.lines.Length];
                    for (int i=0;i<indices.Length;i++) { indices[i]=i; colors[i]=new Color32(25,25,30,255); }
                    state.lines.colors32 = colors; state.lines.SetIndices(indices, MeshTopology.Lines, 0);
                    LastStatistics = $"{r.regions} regions; {r.sourceEdges} → {r.simplifiedEdges} contour edges; {r.triangles.Length/3} triangles; CPU {r.milliseconds:F1} ms";
                }
                else Debug.LogException(state.task.Exception);
                state.task = null; state.busy = false;
            }
            double now = Time.realtimeSinceStartupAsDouble;
            if (!state.busy && now >= state.next)
            {
                int width = Mathf.Clamp(settings.projectionWidth,128,640);
                int height = Mathf.Max(32, Mathf.RoundToInt(width * camera.pixelHeight / (float)Mathf.Max(1,camera.pixelWidth)));
                if(height>640) { width=Mathf.Max(32,Mathf.RoundToInt(width*640f/height)); height=640; }
                Color background = settings.backgroundMode == CartoonBackgroundMode.SolidColor ? settings.backgroundColor : camera.backgroundColor;
                var desc = new TextureDesc(width,height) { colorFormat=GraphicsFormat.R8G8B8A8_UNorm, clearBuffer=true, clearColor=background, filterMode=FilterMode.Point, name="Original material albedo projection" };
                var color = graph.CreateTexture(desc);
                desc.name="Projected source identities"; desc.clearColor=Color.clear;
                var ids=graph.CreateTexture(desc);
                var depth=graph.CreateTexture(new TextureDesc(width,height) { depthBufferBits=DepthBits.Depth32,clearBuffer=true,name="Projection visibility depth" });
                if (now >= refresh)
                {
                    renderers=UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None); refresh=now+1;
                    var dead=new List<int>();
                    foreach(var entry in states) if(!entry.Value.camera)
                    { entry.Value.disposed=true; CoreUtils.Destroy(entry.Value.mesh); CoreUtils.Destroy(entry.Value.lines); dead.Add(entry.Key); }
                    foreach(int key in dead) states.Remove(key);
                }
                var draws = new List<Draw>();
                var planes = GeometryUtility.CalculateFrustumPlanes(camera);
                foreach(var renderer in renderers)
                {
                    if (!renderer || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer.forceRenderingOff ||
                        (camera.cullingMask & (1 << renderer.gameObject.layer))==0 || !GeometryUtility.TestPlanesAABB(planes,renderer.bounds)) continue;
                    if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                    var sources=renderer.sharedMaterials;
                    for(int i=0;i<sources.Length;i++)
                    {
                        var source=sources[i]; if(!source || source.renderQueue>=3000) continue;
                        long key=((long)renderer.GetInstanceID()<<32)|(uint)i;
                        if(!materials.TryGetValue(key,out var mat)) { mat=CoreUtils.CreateEngineMaterial(captureShader); materials.Add(key,mat); }
                        string map=source.HasProperty("_BaseMap")?"_BaseMap":source.HasProperty("_MainTex")?"_MainTex":null;
                        Texture texture=map!=null?source.GetTexture(map):null;
                        Color tint=source.HasProperty("_BaseColor")?source.GetColor("_BaseColor"):source.HasProperty("_Color")?source.GetColor("_Color"):Color.white;
                        mat.SetTexture("_ProjectionMap",texture?texture:Texture2D.whiteTexture);
                        var scale=map!=null?source.GetTextureScale(map):Vector2.one; var offset=map!=null?source.GetTextureOffset(map):Vector2.zero;
                        mat.SetVector("_ProjectionST",new Vector4(scale.x,scale.y,offset.x,offset.y)); mat.SetColor("_ProjectionTint",tint.linear);
                        bool cut=source.IsKeywordEnabled("_ALPHATEST_ON") || source.renderQueue>=2450;
                        mat.SetFloat("_ProjectionCutoff",cut && source.HasProperty("_Cutoff")?source.GetFloat("_Cutoff"):0.001f);
                        int id=materials.Count;
                        // Stable across frames and independent of draw-list ordering.
                        if(mat.GetVector("_ProjectionId")==Vector4.zero) mat.SetVector("_ProjectionId",new Vector4((id&255)/255f,((id>>8)&255)/255f,((id>>16)&255)/255f,1));
                        draws.Add(new Draw { renderer=renderer,material=mat,submesh=i });
                    }
                }
                using(var builder=graph.AddRasterRenderPass<CaptureData>("Project original material colors",out var data))
                {
                    data.draws=draws; data.width=width; data.height=height;
                    builder.SetRenderAttachment(color,0); builder.SetRenderAttachment(ids,1); builder.SetRenderAttachmentDepth(depth,AccessFlags.Write);
                    builder.SetRenderFunc(static(CaptureData d,RasterGraphContext ctx)=> {
                        ctx.cmd.SetViewport(new Rect(0,0,d.width,d.height));
                        foreach(var draw in d.draws) if(draw.renderer) ctx.cmd.DrawRenderer(draw.renderer,draw.material,draw.submesh,0);
                    });
                }
                state.busy=true; state.next=now+1.0/Mathf.Max(1,settings.projectionUpdatesPerSecond);
                using(var builder=graph.AddUnsafePass<ReadData>("Read projection for contour reconstruction",out var data))
                {
                    data.color=color; data.ids=ids; data.state=state; data.width=width; data.height=height;
                    data.step=settings.projectionColorStep; data.area=settings.projectionMinimumArea; data.epsilon=settings.projectionContourTolerance;
                    builder.UseTexture(color,AccessFlags.Read); builder.UseTexture(ids,AccessFlags.Read); builder.AllowPassCulling(false);
                    builder.SetRenderFunc(static(ReadData d,UnsafeGraphContext ctx)=> {
                        // RenderGraph recycles pass data immediately after execution.
                        // Never retain d in asynchronous callbacks.
                        var state=d.state; int width=d.width,height=d.height,step=d.step,area=d.area;
                        float epsilon=d.epsilon;
                        Color32[] colors=null, identities=null; bool failed=false; int remaining=2;
                        void Complete() {
                            if(--remaining!=0 || state.disposed) return;
                            if(failed || colors.Length!=width*height || identities.Length!=width*height) { state.busy=false; return; }
                            state.task=Task.Run(()=>ProjectedShapeBuilder.Build(colors,identities,width,height,step,area,epsilon));
                        }
                        RTHandle c=d.color, id=d.ids;
                        ctx.cmd.RequestAsyncReadback(c.rt,0,TextureFormat.RGBA32,r=> { if(r.hasError) failed=true; else colors=r.GetData<Color32>().ToArray(); Complete(); });
                        ctx.cmd.RequestAsyncReadback(id.rt,0,TextureFormat.RGBA32,r=> { if(r.hasError) failed=true; else identities=r.GetData<Color32>().ToArray(); Complete(); });
                    });
                }
            }
            // Keep original camera image until the first complete result (never blank it).
            if(state.mesh && state.mesh.vertexCount>0)
            using(var builder=graph.AddRasterRenderPass<RedrawData>("Redraw simplified 2D polygons",out var data))
            {
                data.mesh=state.mesh; data.lines=settings.projectionShowContours?state.lines:null; data.material=redraw;
                builder.SetRenderAttachment(resources.activeColorTexture,0,AccessFlags.Write);
                builder.SetRenderFunc(static(RedrawData d,RasterGraphContext ctx)=> {
                    ctx.cmd.DrawMesh(d.mesh,Matrix4x4.identity,d.material,0,0);
                    if(d.lines) ctx.cmd.DrawMesh(d.lines,Matrix4x4.identity,d.material,0,0);
                });
            }
        }
        public void Dispose()
        {
            foreach(var state in states.Values) { state.disposed=true; CoreUtils.Destroy(state.mesh); CoreUtils.Destroy(state.lines); }
            foreach(var mat in materials.Values) CoreUtils.Destroy(mat);
            states.Clear(); materials.Clear(); CoreUtils.Destroy(redraw);
        }
    }
}
