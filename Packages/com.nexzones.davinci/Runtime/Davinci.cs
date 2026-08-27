using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.Networking;
using Nexzones.Cache;

/// <summary>
/// Davinci - A powerful, esay-to-use image downloading and caching library for Unity in Run-Time
/// v 1.2
/// Developed by ShamsDEV.com
/// copyright (c) ShamsDEV.com All Rights Reserved.
/// Licensed under the MIT License.
/// https://github.com/shamsdev/davinci
/// </summary>
public class Davinci : MonoBehaviour
{
    // private static bool ENABLE_GLOBAL_LOGS = true;

    private static bool enableLog = false;
    private float fadeTime = 1;
    private Vector4 border = Vector4.zero;
    private bool singleChannel = false;
    private bool cursorMode = false;
    private bool preload = false;
    private static int runningCoroutines = 0;
    private static int maxConcurrentCoroutines = 5;
    private enum RendererType
    {
        none,
        uiImage,
        rawImage,
        renderer,
        sprite
    }

    private RendererType rendererType = RendererType.none;
    private GameObject targetObj;
    private string url = null;

    private Texture2D loadingPlaceholder, errorPlaceholder;

    private UnityAction onStartAction,
        onDownloadedAction,
        onEndAction;

    private UnityAction<Texture2D> OnLoadedAction;
    private UnityAction<int> onDownloadProgressChange;
    private UnityAction<string> onErrorAction;

    private static Dictionary<string, Davinci> underProcessDavincies
        = new Dictionary<string, Davinci>();

    // ==== 解码后纹理的内存缓存(全站生效:media/spot/属性/道具图标都走本库) ====
    // 磁盘缓存命中仍要 异步读字节 + LoadImage 全量解码 + 两层协程各让一帧,
    // 每次打开面板图都晚几帧闪现。这里缓存解码完成的 Texture2D:命中即同帧贴图。
    // 生命周期归缓存所有 —— 调用方不得 Destroy 贴上的 sprite.texture;
    // LRU 超额只丢引用,真正回收交给场景切换处已有的 Resources.UnloadUnusedAssets
    // (在显示中的纹理有 sprite 引用不会被卸,避免淘汰导致白图)。
    private static readonly Dictionary<string, Texture2D> memCache = new Dictionary<string, Texture2D>();
    private static readonly LinkedList<string> memLru = new LinkedList<string>();
    private static long memBytes = 0;
    // WebGL 的 WASM 堆有限(常见 512MB~2GB,还要装视频 RT/字体/引擎),
    // 且可读纹理是 CPU(WASM 堆)+GPU 双份 —— WASM 那份才是会 OOM 的。
    // 预算保守取 48MB(按含 mip 链 ×4/3 计数)≈ 5 张 1080p 全屏图或几十张 UI 图。
    // 公开可配:按项目内存预算调整(估算含 mip 链,×4/3)。
    public static long memoryBudget = 48L * 1024 * 1024;

    /// <summary>
    /// 解码出来的纹理要不要生成 mipmap。**默认 false。**
    ///
    /// 为什么默认关:这个产品是互动影游,画面全是**屏幕空间的 UI 素材** ——
    /// 按授权尺寸绘制,没有 3D 透视缩小,mip 层根本采不到。留着只有两个后果:
    ///   ① 每张纹理多占 1/3 显存(mip 链)
    ///   ② **九宫格会坏**:九段的缩放比例天差地别(四角 1:1、左右中段可能压到
    ///      0.3 倍),GPU 逐段选 mip → 压得狠的那几段掉到低 mip,而低 mip 是邻域
    ///      平均的,圆角外的透明像素被混进边缘色 → 按钮/榜单行左右两侧各出现
    ///      一条「模糊的半透明竖条」(2026-08-05 真机复现;Web 侧逐段 drawImage
    ///      没有 mip,所以试玩一直是好的,查了很久才落到这里)。
    ///
    /// 此前是 true —— 不是需求,是 `new Texture2D(2,2)` 的默认值撞上来的
    /// (同文件另外两处建纹理都显式传了 false)。
    ///
    /// 真要用到 3D / 大幅缩小的场景,再把它打开。
    /// </summary>
    public static bool generateMipmaps = false;

    /// <summary>
    /// 用浏览器原生异步解码(createImageBitmap)代替同步的 Texture2D.LoadImage。
    /// **默认关**:这条路要改到原生 GL 纹理句柄那一层,只能靠真机实测确认,
    /// 没验证过之前不当默认。宿主按需打开(引擎侧挂在 ?asyncDecode=1 上)。
    ///
    /// 它不让解码变快 —— 同样的像素同样的工作量,只是挪到 worker 线程,
    /// 主线程不再被冻住。真正省下来的是 asyncDecodeMaxEdge 的解码期降采样。
    /// </summary>
    public static bool asyncDecode = false;

    /// <summary>
    /// 异步解码时把长边降到不超过这个值,0 = 不降。
    /// 降采样发生在**解码过程中**,所以超出部分的中间 RGBA 缓冲和 GPU 上传
    /// 根本不会发生(8000×2000 那张:64MB → 按需)。
    /// </summary>
    public static int asyncDecodeMaxEdge = 0;

    private string MemKey()
    {
        // 纹理**形态**的差异都要进键 —— 同一个 url 解出两种纹理时,不进键就会
        // 互相顶掉:拿到的是先缓存的那个,而且 TexBytes 记账也跟着错。
        // generateMipmaps 同理:它虽是全局开关,但运行时一改,旧格式的缓存还在,
        // 表现就是"改了没生效"。进了键,改开关自然重新解码。
        return uniqueHash
            + (singleChannel ? "|a8" : cursorMode ? "|cur" : "")
            + (generateMipmaps ? "|mip" : "");
    }

    private static long TexBytes(Texture2D t)
    {
        // 记账要跟着开关走:带 mip 链是 ×4/3,不带就是 w×h×4。
        // 此前写死 ×4/3,是因为那时 `new Texture2D(2,2)` 默认就带 mip。
        long bytes = (long)t.width * t.height * 4;
        return generateMipmaps ? bytes * 4 / 3 : bytes;
    }

    private static void MemPut(string key, Texture2D tex)
    {
        if (tex == null || string.IsNullOrEmpty(key) || memCache.ContainsKey(key)) return;
        memCache[key] = tex;
        memLru.AddFirst(key);
        memBytes += TexBytes(tex);
        while (memBytes > memoryBudget && memLru.Count > 1)
        {
            var evict = memLru.Last.Value;
            memLru.RemoveLast();
            if (memCache.TryGetValue(evict, out var t))
            {
                if (t != null) memBytes -= TexBytes(t);
                memCache.Remove(evict);
                // 只丢引用,不 Destroy —— 可能仍被某个 sprite 显示中
            }
        }
    }

    private static bool MemGet(string key, out Texture2D tex)
    {
        if (memCache.TryGetValue(key, out tex))
        {
            if (tex != null)
            {
                memLru.Remove(key);
                memLru.AddFirst(key);
                return true;
            }
            memCache.Remove(key); // 纹理被外部销毁(不应发生),清掉死条目
            memLru.Remove(key);
        }
        tex = null;
        return false;
    }

    private string uniqueHash;
    private int progress;

    // 下载完成的原始字节;供「按 URL 去重挂靠到本请求」的其它请求同步自解码复用,
    // 避免它们回头读尚未落盘的异步磁盘缓存(WebGL IDBFS)拿到空 → 图空白的竞态。
    private byte[] downloadedData;

    private bool success = false;

    /// <summary>
    /// Get instance of davinci class
    /// </summary>
    public static Davinci get()
    {
        return new GameObject("Davinci").AddComponent<Davinci>();
    }

    /// <summary>
    /// Set image url for download.
    /// </summary>
    /// <param name="url">Image Url</param>
    /// <returns></returns>
    public Davinci load(string url)
    {
        if (enableLog)
            Debug.Log("[Davinci] Url set : " + url);

        this.url = url;
        return this;
    }

    /// <summary>
    /// Set fading animation time.
    /// </summary>
    /// <param name="fadeTime">Fade animation time. Set 0 for disable fading.</param>
    /// <returns></returns>
    public Davinci setFadeTime(float fadeTime)
    {
        if (enableLog)
            Debug.Log("[Davinci] Fading time set : " + fadeTime);

        this.fadeTime = fadeTime;
        return this;
    }

    /// <summary>
    /// Set target Image component.
    /// </summary>
    /// <param name="image">target Unity UI image component</param>
    /// <returns></returns>
    public Davinci into(Image image)
    {
        if (image == null)
        {
            return this;
        }
        if (enableLog)
            Debug.Log("[Davinci] Target as UIImage set : " + image);

        rendererType = RendererType.uiImage;
        this.targetObj = image.gameObject;
        return this;
    }

    /// <summary>
    /// Set target RawImage component.
    /// </summary>
    /// <param name="image">target Unity UI image component</param>
    /// <returns></returns>
    public Davinci into(RawImage rawImage)
    {
        if (enableLog)
            Debug.Log("[Davinci] Target as RawImage set : " + rawImage);

        rendererType = RendererType.rawImage;
        this.targetObj = rawImage.gameObject;
        return this;
    }

    /// <summary>
    /// Set target Renderer component.
    /// </summary>
    /// <param name="renderer">target renderer component</param>
    /// <returns></returns>
    public Davinci into(Renderer renderer)
    {
        if (enableLog)
            Debug.Log("[Davinci] Target as Renderer set : " + renderer);

        rendererType = RendererType.renderer;
        this.targetObj = renderer.gameObject;
        return this;
    }

    public Davinci into(SpriteRenderer spriteRenderer)
    {
        if (enableLog)
            Debug.Log("[Davinci] Target as SpriteRenderer set : " + spriteRenderer);

        rendererType = RendererType.sprite;
        this.targetObj = spriteRenderer.gameObject;
        return this;
    }

    #region Actions
    public Davinci withStartAction(UnityAction action)
    {
        this.onStartAction = action;

        if (enableLog)
            Debug.Log("[Davinci] On start action set : " + action);

        return this;
    }

    public Davinci withDownloadedAction(UnityAction action)
    {
        this.onDownloadedAction = action;

        if (enableLog)
            Debug.Log("[Davinci] On downloaded action set : " + action);

        return this;
    }

    public Davinci withDownloadProgressChangedAction(UnityAction<int> action)
    {
        this.onDownloadProgressChange = action;

        if (enableLog)
            Debug.Log("[Davinci] On download progress changed action set : " + action);

        return this;
    }

    public Davinci withLoadedAction(UnityAction<Texture2D> action)
    {
        this.OnLoadedAction = action;

        if (enableLog)
            Debug.Log("[Davinci] On loaded action set : " + action);

        return this;
    }

    public Davinci withErrorAction(UnityAction<string> action)
    {
        this.onErrorAction = action;

        if (enableLog)
            Debug.Log("[Davinci] On error action set : " + action);

        return this;
    }

    public Davinci withEndAction(UnityAction action)
    {
        this.onEndAction = action;

        if (enableLog)
            Debug.Log("[Davinci] On end action set : " + action);

        return this;
    }
    #endregion

    /// <summary>
    /// Show or hide logs in console.
    /// </summary>
    /// <param name="enable">'true' for show logs in console.</param>
    /// <returns></returns>
    // public Davinci setEnableLog(bool enableLog)
    // {
    //     this.enableLog = enableLog;

    //     if (enableLog)
    //         Debug.Log("[Davinci] Logging enabled : " + enableLog);

    //     return this;
    // }

    /// <summary>
    /// Set the sprite of image when davinci is downloading and loading image
    /// </summary>
    /// <param name="loadingPlaceholder">loading texture</param>
    /// <returns></returns>
    public Davinci setLoadingPlaceholder(Texture2D loadingPlaceholder)
    {
        this.loadingPlaceholder = loadingPlaceholder;

        if (enableLog)
            Debug.Log("[Davinci] Loading placeholder has been set.");

        return this;
    }

    /// <summary>
    /// Set image sprite when some error occurred during downloading or loading image
    /// </summary>
    /// <param name="errorPlaceholder">error texture</param>
    /// <returns></returns>
    public Davinci setErrorPlaceholder(Texture2D errorPlaceholder)
    {
        this.errorPlaceholder = errorPlaceholder;

        if (enableLog)
            Debug.Log("[Davinci] Error placeholder has been set.");

        return this;
    }

    /// <summary>
    /// Set border
    /// </summary>
    /// <returns></returns>
    public Davinci setBorder(Vector4 border)
    {
        this.border = border;

        if (enableLog)
            Debug.Log("[Davinci] Set border : " + border);

        return this;
    }

    /// <summary>
    /// Set singleChannel
    /// </summary>
    /// <returns></returns>
    public Davinci setSingleChannel(bool singleChannel)
    {
        this.singleChannel = singleChannel;

        if (enableLog)
            Debug.Log("[Davinci] SingleChannel enabled : " + singleChannel);

        return this;
    }
    
    /// <summary>
    /// Set setCursorMode
    /// </summary>
    /// <returns></returns>
    public Davinci setCursorMode(bool cursorMode)
    {
        this.cursorMode = cursorMode;

        if (enableLog)
            Debug.Log("[Davinci] cursorMode enabled : " + cursorMode);

        return this;
    }

/// <summary>
    /// Set setPreload
    /// </summary>
    /// <returns></returns>
    public Davinci setPreload(bool preload)
    {
        this.preload = preload;

        if (enableLog)
            Debug.Log("[Davinci] preload enabled : " + preload);

        return this;
    }

    /// <summary>
    /// Start davinci process.
    /// </summary>
    public void start()
    {
        if (url == null)
        {
            error("Url has not been set. Use 'load' funtion to set image url.");
            return;
        }

        try
        {
            Uri uri = new Uri(url);
            this.url = uri.AbsoluteUri;
        }
        catch (Exception ex)
        {
            error($"{ex} Url is not correct.");
            return;
        }

        // if (rendererType == RendererType.none || targetObj == null)
        // {
        //     error("Target has not been set. Use 'into' function to set target component.");
        //     return;
        // }

        if (enableLog)
            Debug.Log("[Davinci] Start Working.");

        // 内存缓存命中:跳过占位图/磁盘/解码/并发闸,同帧贴图
        // (SetupImage 的赋值都在首个 yield 之前,StartCoroutine 同步执行到那里)。
        (uniqueHash, _) = BaseCacheManager.instance.UrlToCacheFile(url);
        if (MemGet(MemKey(), out var memTex))
        {
            if (onStartAction != null)
                onStartAction.Invoke();
            if (onDownloadedAction != null)
                onDownloadedAction.Invoke();
            if (preload)
            {
                success = true;
                finish();
            }
            else
            {
                StartCoroutine(SetupImage(memTex));
            }
            return;
        }

        if (loadingPlaceholder != null)
            SetLoadingImage();

        if (onStartAction != null)
            onStartAction.Invoke();

        BaseCacheManager.instance.FindCache(url, (exist, cacheFile) => {
            if (exist)
            {
                if (onDownloadedAction != null)
                    onDownloadedAction.Invoke();

                // preload 也走解码 → 进内存缓存(SetupImage 对无 target 不贴图,
                // 只回调+销毁自身),这样场景预热后打开面板即同帧命中。
                loadSpriteToImage();
            }
            else
            {
                StopAllCoroutines();
                StartCoroutine(Downloader());
            }
        });
    }

    private IEnumerator Downloader()
    {
        (uniqueHash, _) = BaseCacheManager.instance.UrlToCacheFile(url);
        if(BaseCacheManager.GlobalCached) {
            if (underProcessDavincies.ContainsKey(uniqueHash))
            {
                Davinci sameProcess = underProcessDavincies[uniqueHash];
                sameProcess.onDownloadedAction += () =>
                {
                    if (onDownloadedAction != null)
                        onDownloadedAction.Invoke();

                    // 复用主请求下载好的原始字节同步自解码,规避「读尚未落盘的异步缓存拿到空」
                    // 的间歇竞态(spot 图空白、热区在)。字节为空(主请求出错等)才回退原路径,
                    // 绝不去 StartCoroutine(Downloader) 重入——那会挂到已 Invoke 完的主请求上卡死。
                    loadSpriteToImageFromData(sameProcess.downloadedData);
                };
                yield break;
            }
            underProcessDavincies.Add(uniqueHash, this);
        }

        float startTime = Time.realtimeSinceStartup;
        if (enableLog)
            Debug.Log("[Davinci] Download started.");

        var www = UnityWebRequestTexture.GetTexture(url);
        yield return www.SendWebRequest();

        while (!www.isDone)
        {
            if (www.error != null)
            {
                error("Error while downloading the image : " + www.error);
                if(BaseCacheManager.GlobalCached) {
                    underProcessDavincies.Remove(uniqueHash);
                }
                yield break;
            }

            progress = Mathf.FloorToInt(www.downloadProgress * 100);
            if (onDownloadProgressChange != null)
                onDownloadProgressChange.Invoke(progress);

            if (enableLog)
                Debug.Log("[Davinci] Downloading progress : " + progress + "%");

            yield return null;
        }

        if (www.error == null) {
            var raw = www.downloadHandler.data; // 取一次复用(每次访问可能拷贝)
            BaseCacheManager.instance.SaveCache(url, raw);
            // 去重挂靠的请求靠这份原始字节各自同步解码(见上面 Downloader 去重分支),
            // 必须在下面 onDownloadedAction.Invoke() 触发它们之前就绪。
            downloadedData = raw;
        }

        if (onDownloadedAction != null)
            onDownloadedAction.Invoke();

        float elapsedTime = Time.realtimeSinceStartup - startTime;
        if (enableLog)
            Debug.Log($"[Davinci] End Download image.{elapsedTime}");

        {
            // preload 同样解码进内存缓存(下载 handler 已解码,直接复用)
            var texture = DownloadHandlerTexture.GetContent(www);
            loadSpriteToImage(texture);
        }

        www.Dispose();
        www = null;
        downloadedData = null;

        if(BaseCacheManager.GlobalCached) {
            underProcessDavincies.Remove(uniqueHash);
        }
    }

    // 去重挂靠的请求:用主请求下载好的原始字节自解一张纹理再走正常贴图路径,
    // 规避读尚未落盘的异步缓存拿到空的竞态。字节为空(主请求出错等)→ 回退到原始
    // loadSpriteToImage()(读缓存),保持与改动前完全一致的行为,不重入下载、不挂死。

    // 诊断:LoadImage 是**同步**解码,整张 PNG 在主线程上展开,摊不开也打不断。
    // 2026-08-26 实测:一张 4,955,003 字节的 PNG 之后紧跟着一帧 1846ms 的停顿。
    // 这里把「多大、多久、解出来多少像素」直接打出来 —— 素材尺寸是创作者可控的,
    // 但只有先看得见,才谈得上治。
    private static void LoadImageTimed(Texture2D tex, byte[] data, string url)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        tex.LoadImage(data);
        sw.Stop();
        if (sw.ElapsedMilliseconds > 30)
        {
            Debug.Log($"[DECODE] {(data == null ? 0 : data.Length)} 字节 → {tex.width}x{tex.height}"
                + $" 用了 {sw.ElapsedMilliseconds}ms | {url}");
        }
    }

    private void loadSpriteToImageFromData(byte[] data)
    {
        if (data == null || data.Length == 0) { loadSpriteToImage(); return; }
        if (asyncDecode && WebGLImageDecoder.IsAvailable)
        {
            WebGLImageDecoder.Decode(data, asyncDecodeMaxEdge, generateMipmaps, (tex) =>
            {
                // 回落必须是**真回落**:解码失败/上传失败一律走回同步路径,
                // 而不是把 null 交给上层 —— 那样图片会永远停在占位状态。
                if (tex == null)
                {
                    var sync = new Texture2D(2, 2, TextureFormat.RGBA32, generateMipmaps);
                    LoadImageTimed(sync, data, url);
                    loadSpriteToImage(sync);
                }
                else loadSpriteToImage(tex);
            });
            return;
        }
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, generateMipmaps);
        LoadImageTimed(t, data, url);
        loadSpriteToImage(t);
    }

    private void loadSpriteToImage(Texture2D texture = null)
    {
        progress = 100;
        if (onDownloadProgressChange != null)
            onDownloadProgressChange.Invoke(progress);

        if (enableLog)
            Debug.Log("[Davinci] Downloading progress : " + progress + "%");

        StopAllCoroutines();
        StartCoroutine(ImageLoader(texture));
    }

    private void SetLoadingImage()
    {
        switch (rendererType)
        {
            case RendererType.renderer:
                Renderer renderer = targetObj.GetComponent<Renderer>();
                renderer.material.mainTexture = loadingPlaceholder;
                break;

            case RendererType.uiImage:
                Image image = targetObj.GetComponent<Image>();
                Sprite sprite = Sprite.Create(loadingPlaceholder,
                    new Rect(0, 0, loadingPlaceholder.width, loadingPlaceholder.height),
                    new Vector2(0.5f, 0.5f));
                image.sprite = sprite;

                break;
            case RendererType.rawImage:
                RawImage rawImage = targetObj.GetComponent<RawImage>();
                rawImage.texture = loadingPlaceholder;

                break;

            case RendererType.sprite:
                SpriteRenderer spriteRenderer = targetObj.GetComponent<SpriteRenderer>();
                Sprite spriteImage = Sprite.Create(loadingPlaceholder,
                    new Rect(0, 0, loadingPlaceholder.width, loadingPlaceholder.height),
                    new Vector2(0.5f, 0.5f));

                spriteRenderer.sprite = spriteImage;
                break;
        }

    }

    private IEnumerator SetupImage(Texture2D texture = null)
    {
        Color color;
        // "已加载"回调必须在【赋值之后、淡入循环之前】触发:
        // - 原版放在淡入之后 → 靠回调 SetActive 的调用方(MediaElement)要多等
        //   整个 fadeTime,淡入全程在隐藏状态跑完,表现为"图晚出现且没有淡入";
        // - 也不能放在赋值之前 → LoadStorySprite 等回调里读 image.sprite 取
        //   贴好的 sprite,提前触发会拿到空(spot 默认按钮变白的回归)。
        bool loadedInvoked = false;
        void InvokeLoadedOnce()
        {
            if (loadedInvoked) return;
            loadedInvoked = true;
            if (OnLoadedAction != null)
                OnLoadedAction.Invoke(texture);
        }
        if (targetObj != null)
            switch (rendererType)
            {
                case RendererType.renderer:
                    Renderer renderer = targetObj.GetComponent<Renderer>();

                    if (renderer == null || renderer.material == null)
                        break;

                    renderer.material.mainTexture = texture;
                    InvokeLoadedOnce();
                    float maxAlpha;

                    if (fadeTime > 0 && renderer.material.HasProperty("_Color"))
                    {
                        color = renderer.material.color;
                        maxAlpha = color.a;

                        color.a = 0;

                        renderer.material.color = color;
                        float time = Time.time;
                        while (color.a < maxAlpha)
                        {
                            color.a = Mathf.Lerp(0, maxAlpha, (Time.time - time) / fadeTime);

                            if (renderer != null)
                                renderer.material.color = color;

                            yield return null;
                        }
                    }

                    break;

                case RendererType.uiImage:
                    Image image = targetObj.GetComponent<Image>();

                    if (image == null)
                        break;

                    // 九宫格必须用 FullRect:Tight 的网格贴着不透明像素走,带 border 的
                    // 精灵按 Sliced 绘制时四角/四边会被裁错。无 border 的普通图保持
                    // Tight(少绘制透明区域,原有优化不动)。
                    Sprite sprite = Sprite.Create(texture,
                        new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f, 0,
                        border == Vector4.zero ? SpriteMeshType.Tight : SpriteMeshType.FullRect, border);

                    image.sprite = sprite;
                    InvokeLoadedOnce();
                    color = image.color;
                    maxAlpha = color.a;

                    if (fadeTime > 0)
                    {
                        color.a = 0;
                        image.color = color;

                        float time = Time.time;
                        while (color.a < maxAlpha)
                        {
                            color.a = Mathf.Lerp(0, maxAlpha, (Time.time - time) / fadeTime);

                            if (image != null)
                                image.color = color;
                            yield return null;
                        }
                    }
                    break;
                case RendererType.rawImage:
                    RawImage rawImage = targetObj.GetComponent<RawImage>();

                    if (rawImage == null)
                        break;

                    rawImage.texture = texture;
                    InvokeLoadedOnce();

                    if (fadeTime > 0)
                    {
                        color = rawImage.color;
                        maxAlpha = color.a;

                        color.a = 0;

                        rawImage.color = color;
                        float time = Time.time;
                        while (color.a < maxAlpha)
                        {
                            color.a = Mathf.Lerp(0, maxAlpha, (Time.time - time) / fadeTime);

                            if (rawImage != null)
                                rawImage.color = color;

                            yield return null;
                        }
                    }

                    break;
                case RendererType.sprite:
                    SpriteRenderer spriteRenderer = targetObj.GetComponent<SpriteRenderer>();

                    if (spriteRenderer == null)
                        break;

                    // 同上:带 border 时必须 FullRect,否则九宫格切边不正确
                    Sprite spriteImage = Sprite.Create(texture,
                        new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f, 0,
                        border == Vector4.zero ? SpriteMeshType.Tight : SpriteMeshType.FullRect, border);

                    spriteRenderer.sprite = spriteImage;
                    InvokeLoadedOnce();
                    color = spriteRenderer.color;
                    maxAlpha = color.a;

                    if (fadeTime > 0)
                    {
                        color.a = 0;
                        spriteRenderer.color = color;

                        float time = Time.time;
                        while (color.a < maxAlpha)
                        {
                            color.a = Mathf.Lerp(0, maxAlpha, (Time.time - time) / fadeTime);

                            if (spriteRenderer != null)
                                spriteRenderer.color = color;
                            yield return null;
                        }
                    }
                    break;
            }

        // 兜底:无 target(preload)/组件缺失提前 break 的路径也要回调
        InvokeLoadedOnce();

        if (enableLog)
            Debug.Log("[Davinci] Image has been loaded.");

        success = true;
        finish();
    }

    private Texture2D ConvertToGrayscaleTexture(Texture2D originalTexture)
    {
        int width = originalTexture.width;
        int height = originalTexture.height;

        // 获取原始纹理的所有像素数据
        Color[] pixels = originalTexture.GetPixels();

        // 创建 Alpha8 格式的单通道纹理
        Texture2D alphaTex = new Texture2D(width, height, TextureFormat.Alpha8, false);

        // 创建一个新的 Color 数组来存储结果
        byte[] alphaPixels = new byte[pixels.Length];

        // 遍历所有像素，将灰度值存入 Alpha 通道
        for (int i = 0; i < pixels.Length; i++)
        {
            float grayscale = pixels[i].grayscale; // 计算灰度
            alphaPixels[i] = (byte)(grayscale * 255); // 灰度值转化为 0 到 255 之间的整数
        }

        // 设置 Alpha8 纹理的像素数据
        alphaTex.SetPixelData(alphaPixels, 0);
        alphaTex.Apply();

        return alphaTex;
    }

    private Texture2D ConvertToCursorTexture(Texture2D originalTexture)
    {
        int width = originalTexture.width;
        int height = originalTexture.height;

        // 获取原始纹理的所有像素数据
        Color[] pixels = originalTexture.GetPixels();

        // 创建 RGBA32 格式的纹理
        Texture2D rgbaTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
        // 设置纹理的 filterMode 和 wrapMode
        rgbaTex.filterMode = FilterMode.Point; // 防止模糊效果
        rgbaTex.wrapMode = TextureWrapMode.Repeat; // 根据需求设置 WrapMode
        // rgbaTex.alphaIsTransparency = true;

        // 创建一个新的颜色数组，直接操作像素数据
        Color[] cursorPixels = new Color[pixels.Length];

        // 遍历所有像素，将其值复制到新的数组中
        for (int i = 0; i < pixels.Length; i++)
        {
            cursorPixels[i] = pixels[i];
        }

        // 将像素数据设置到新的纹理
        rgbaTex.SetPixels(cursorPixels);
        
        // 应用更改并生成最终纹理
        rgbaTex.Apply();

        // // 允许透明度，并去除 Mipmap
        // rgbaTex.Apply(true, true);

        return rgbaTex;
    }

    private IEnumerator ImageLoader(Texture2D texture = null)
    {
        float startTime = Time.realtimeSinceStartup;
        if (enableLog)
            Debug.Log("[Davinci] Start loading image.");

        while (runningCoroutines >= maxConcurrentCoroutines)
        {
            yield return new WaitForEndOfFrame();
        }

        if (texture == null)
        {
            (var md5Name, var uniqueHash) = BaseCacheManager.instance.UrlToCacheFile(url);
            BaseCacheManager.instance.LoadCache(uniqueHash, (byte[] data) =>
            {
                if (asyncDecode && WebGLImageDecoder.IsAvailable && data != null && data.Length > 0)
                {
                    // 下面的 while 本来就在等 texture 变非空,异步解码天然嵌得进去。
                    WebGLImageDecoder.Decode(data, asyncDecodeMaxEdge, generateMipmaps, (tex) =>
                    {
                        if (tex == null)
                        {
                            var sync = new Texture2D(2, 2, TextureFormat.RGBA32, generateMipmaps);
                            LoadImageTimed(sync, data, url);
                            texture = sync;   // 回落到同步,不能把 null 留给上层
                        }
                        else texture = tex;
                    });
                    return;
                }
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, generateMipmaps);
                //..this will auto-resize the texture dimensions.
                LoadImageTimed(texture, data, url);
            });
            while (texture == null)
            {
                yield return null;
            }
        }
        if (singleChannel) {
            Texture2D oldTexture = texture;
            texture = ConvertToGrayscaleTexture(texture);
            Destroy(oldTexture);
        } else if (cursorMode) {
            Texture2D oldTexture = texture;
            texture = ConvertToCursorTexture(texture);
            Destroy(oldTexture);
        }
        // 解码/变换完成的纹理进内存缓存;占位图(loading/error)不缓存,
        // 否则错误占位会顶掉真图的缓存位。
        if (texture != errorPlaceholder && texture != loadingPlaceholder)
        {
            MemPut(MemKey(), texture);
        }
        runningCoroutines++;
        yield return SetupImage(texture);
        runningCoroutines--;
        
        float elapsedTime = Time.realtimeSinceStartup - startTime;
        if (enableLog)
            Debug.Log($"[Davinci] End loading image.{elapsedTime}");
    }
    private void error(string message)
    {
        success = false;

        if (enableLog)
            Debug.LogError("[Davinci] Error : " + message);

        if (onErrorAction != null)
            onErrorAction.Invoke(message);

        if (errorPlaceholder != null)
            StartCoroutine(ImageLoader(errorPlaceholder));
        else finish();
    }

    private void finish()
    {
        if (enableLog)
            Debug.Log("[Davinci] Operation has been finished.");

        if (onEndAction != null)
            onEndAction.Invoke();

        Invoke("destroyer", 0.5f);
    }

    private void destroyer()
    {
        Destroy(gameObject);
    }
}