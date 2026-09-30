using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CartoonProjection.Editor
{
    // Non-destructive comparison: temporary camera only; scene and source assets stay untouched.
    public static class ProjectedShapeCapture
    {
        static Camera camera;
        static RenderTexture target;
        static CartoonRendererFeature feature;
        static int frame;
        static double next;
        [MenuItem("Tools/Cartoon Projection/Projection 2D/Capture Comparison")]
        static void Begin()
        {
            if(camera || !Camera.main) return;
            foreach(var asset in AssetDatabase.LoadAllAssetsAtPath("Assets/CartoonRenderer/Settings/CartoonUniversalRenderer.asset"))
                if(asset is CartoonRendererFeature f) feature=f;
            if(!feature)return;
            var go=new GameObject("Projection validation camera") { hideFlags=HideFlags.HideAndDontSave };
            camera=go.AddComponent<Camera>(); camera.CopyFrom(Camera.main); camera.enabled=false;
            camera.transform.SetPositionAndRotation(Camera.main.transform.position,Camera.main.transform.rotation);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            target=new RenderTexture(1024,768,24,RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture=target;
            camera.aspect=target.width/(float)target.height;
            // Frame the supplied textured test asset when present, without moving the user's camera.
            var testModel=GameObject.Find("Avatar_Female_Size01_LucyPrincess_UI");
            if(testModel)
            {
                var rs=testModel.GetComponentsInChildren<Renderer>();
                if(rs.Length>0) { var bounds=rs[0].bounds; foreach(var r in rs) bounds.Encapsulate(r.bounds);
                    float distance=Mathf.Max(bounds.extents.y,bounds.extents.x/camera.aspect)/Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*.5f)*1.2f;
                    camera.transform.position=bounds.center-camera.transform.forward*distance;
                }
            }
            frame=0; next=0; EditorApplication.update+=Tick;
        }
        static void Tick()
        {
            if(EditorApplication.timeSinceStartup<next)return;
            next=EditorApplication.timeSinceStartup+.15;
            bool enabled=feature.settings.enabled, projected=feature.settings.projectedShapes, lines=feature.settings.projectionShowContours;
            try
            {
                feature.settings.enabled=frame!=0; feature.settings.projectedShapes=true; feature.settings.projectionShowContours=frame>=20;
                camera.Render();
                if(frame==0) Save("01-original.png");
                if(frame==19) Save("02-shape-redraw.png");
                if(frame==21) { Save("03-shared-contours.png"); Debug.Log("Projection comparison saved: Library/CartoonProjectionValidation. "+ProjectedShapePass.LastStatistics); }
                frame++;
            }
            catch(System.Exception e) { Debug.LogException(e); frame=22; }
            finally
            {
                feature.settings.enabled=enabled; feature.settings.projectedShapes=projected; feature.settings.projectionShowContours=lines;
                if(frame>=22) Cleanup();
            }
        }
        static void Save(string name)
        {
            var previous=RenderTexture.active; var texture=new Texture2D(target.width,target.height,TextureFormat.RGB24,false);
            try { RenderTexture.active=target; texture.ReadPixels(new Rect(0,0,target.width,target.height),0,0); texture.Apply(); Directory.CreateDirectory("Library/CartoonProjectionValidation"); File.WriteAllBytes("Library/CartoonProjectionValidation/"+name,texture.EncodeToPNG()); }
            finally { RenderTexture.active=previous; Object.DestroyImmediate(texture); }
        }
        static void Cleanup()
        {
            EditorApplication.update-=Tick;
            if(camera) Object.DestroyImmediate(camera.gameObject);
            if(target) { target.Release(); Object.DestroyImmediate(target); }
        }
    }
}
