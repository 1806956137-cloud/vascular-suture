using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PBDLearn
{
    internal static class HybridVesselPreviewCapture
    {
        public static void CaptureBatch()
        {
            const string prefabPath =
                "Assets/PBDLearn/Content/Vascular/Prefabs/HybridBranchedVessel.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
                throw new FileNotFoundException("Hybrid vessel prefab was not found.", prefabPath);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject instance = Object.Instantiate(prefab);
            MeshRenderer[] renderers =
                instance.GetComponentsInChildren<MeshRenderer>();
            if (renderers.Length == 0)
                throw new InvalidDataException(
                    "Hybrid vessel prefab has no renderable children.");
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                bounds.Encapsulate(renderers[i].bounds);

            var lightObject = new GameObject("PreviewLight");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.92f;
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.32f;
            light.transform.rotation = Quaternion.Euler(52f, -28f, 0f);
            RenderSettings.ambientLight = new Color(0.34f, 0.27f, 0.26f, 1f);

            const int width = 1200;
            const int height = 900;
            float aspect = width / (float)height;
            CaptureView(
                bounds,
                width,
                height,
                Vector3.up,
                Vector3.forward,
                Mathf.Max(
                    bounds.extents.z * 1.03f,
                    bounds.extents.x / aspect * 1.03f),
                "hybrid_vessel_preview_top.png");
            CaptureView(
                bounds,
                width,
                height,
                Vector3.back,
                Vector3.up,
                Mathf.Max(
                    bounds.extents.y * 1.22f,
                    bounds.extents.x / aspect * 1.03f),
                "hybrid_vessel_preview_side.png");
            CaptureView(
                bounds,
                width,
                height,
                new Vector3(0.90f, 0.72f, -1f),
                Vector3.up,
                bounds.extents.magnitude * 0.78f,
                "hybrid_vessel_preview_oblique.png");
        }

        private static void CaptureView(
            Bounds bounds,
            int width,
            int height,
            Vector3 cameraOffsetDirection,
            Vector3 cameraUp,
            float orthographicSize,
            string fileName)
        {
            var cameraObject = new GameObject(
                $"PreviewCamera_{Path.GetFileNameWithoutExtension(fileName)}");
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = orthographicSize;
            camera.backgroundColor = new Color(0.035f, 0.07f, 0.11f, 1f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = bounds.size.magnitude * 6f + 10f;
            camera.transform.position =
                bounds.center +
                cameraOffsetDirection.normalized *
                (bounds.size.magnitude * 2f + 2f);
            camera.transform.LookAt(bounds.center, cameraUp);

            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4,
            };
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();

            string outputPath = Path.GetFullPath(
                Path.Combine(Application.dataPath, "../tmp", fileName));
            Directory.CreateDirectory(
                Path.GetDirectoryName(outputPath));
            File.WriteAllBytes(outputPath, texture.EncodeToPNG());
            Debug.Log($"[HybridVessel] Preview captured: {outputPath}");

            RenderTexture.active = null;
            camera.targetTexture = null;
            Object.DestroyImmediate(texture);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(cameraObject);
        }
    }
}
