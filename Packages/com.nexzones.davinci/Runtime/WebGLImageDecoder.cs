using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

/// <summary>
/// 浏览器原生异步解码。**能力在这里,开关不在这里** —— 由宿主决定开不开
/// (`Davinci.asyncDecode`),包本身不预设策略。
///
/// 解决的是:Texture2D.LoadImage 同步阻塞主线程。一张 8000×2000 的 PNG
/// 实测 1846ms,这期间整个引擎停摆。挪到 worker 线程之后代价还在,但不再
/// 挡在所有人前面;真正省下来的是解码期降采样(见 WebGLImageDecode.jslib)。
///
/// 非 WebGL 平台不可用(createImageBitmap 是浏览器 API),`IsAvailable`
/// 会返回 false,调用方走原来的同步路径。
/// </summary>
public class WebGLImageDecoder : MonoBehaviour
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void NXZ_DecodeImageAsync(
        byte[] data, int len, int maxEdge, int identifier,
        string gameObjectName, string callbackMethod);

    [DllImport("__Internal")]
    private static extern int NXZ_UploadDecoded(int identifier, int texHandle, int genMips);

    [DllImport("__Internal")]
    private static extern void NXZ_ReleaseDecoded(int identifier);
#endif

    [Serializable]
    private class DecodeResult
    {
        public int id;
        public bool success;
        public int width;
        public int height;
        public string error;
    }

    public static bool IsAvailable
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        get { return true; }
#else
        get { return false; }
#endif
    }

    static WebGLImageDecoder instance;
    readonly Dictionary<int, Action<Texture2D>> pending = new Dictionary<int, Action<Texture2D>>();
    readonly Dictionary<int, bool> pendingMips = new Dictionary<int, bool>();
    int nextId;

    static WebGLImageDecoder Instance()
    {
        if (instance == null)
        {
            var go = new GameObject("NXZImageDecoder");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<WebGLImageDecoder>();
        }
        return instance;
    }

    /// <summary>
    /// 异步解码。`maxEdge > 0` 时在**解码过程中**降采样到长边不超过它 ——
    /// 那 64MB 的中间缓冲和 GPU 上传根本不会发生。
    /// 失败(含平台不支持)时回调 null,调用方必须能回落到同步路径。
    /// </summary>
    public static void Decode(byte[] data, int maxEdge, bool generateMipmaps,
                              Action<Texture2D> done)
    {
        if (!IsAvailable || data == null || data.Length == 0)
        {
            done?.Invoke(null);
            return;
        }
#if UNITY_WEBGL && !UNITY_EDITOR
        var self = Instance();
        int id = ++self.nextId;
        self.pending[id] = done;
        self.pendingMips[id] = generateMipmaps;
        try
        {
            NXZ_DecodeImageAsync(data, data.Length, maxEdge, id,
                                 self.gameObject.name, "OnDecoded");
        }
        catch (Exception e)
        {
            // 同步抛也必须把回调放掉,否则这张图永远停在占位状态。
            Debug.LogWarning("[Davinci] NXZ_DecodeImageAsync threw: " + e.Message);
            self.pending.Remove(id);
            self.pendingMips.Remove(id);
            done?.Invoke(null);
        }
#else
        done?.Invoke(null);
#endif
    }

    // JS 侧 SendMessage 回来的入口。
    public void OnDecoded(string json)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        DecodeResult r;
        try { r = JsonUtility.FromJson<DecodeResult>(json); }
        catch { return; }
        if (!pending.TryGetValue(r.id, out var cb)) return;
        pending.Remove(r.id);
        pendingMips.TryGetValue(r.id, out bool mips);
        pendingMips.Remove(r.id);

        if (!r.success || r.width <= 0 || r.height <= 0)
        {
            NXZ_ReleaseDecoded(r.id);
            cb?.Invoke(null);   // 回落到同步解码
            return;
        }

        Texture2D tex = null;
        try
        {
            tex = new Texture2D(r.width, r.height, TextureFormat.RGBA32, mips);
            // Apply 一次逼 Unity 真正建出 GL 纹理,否则 GetNativeTexturePtr
            // 拿到的句柄还没对应任何对象,上传会静默落空。
            tex.Apply(false, false);
            int handle = (int)(long)tex.GetNativeTexturePtr();
            int ok = handle != 0 ? NXZ_UploadDecoded(r.id, handle, mips ? 1 : 0) : 0;
            NXZ_ReleaseDecoded(r.id);
            if (ok != 1)
            {
                Destroy(tex);
                cb?.Invoke(null);   // 上传失败也回落,别给上层一张空纹理
                return;
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Davinci] upload decoded texture failed: " + e.Message);
            NXZ_ReleaseDecoded(r.id);
            if (tex != null) Destroy(tex);
            cb?.Invoke(null);
            return;
        }
        cb?.Invoke(tex);
#endif
    }
}
