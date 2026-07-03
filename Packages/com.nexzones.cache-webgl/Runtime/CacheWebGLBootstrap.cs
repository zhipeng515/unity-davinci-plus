#if UNITY_WEBGL
using UnityEngine;

namespace Nexzones.Cache
{
    /// <summary>
    /// WebGL 后端自注册:包在场即接管 CacheManager.Create 的后端选择。
    /// 底层 WebFileSystem 统一了 浏览器 IndexedDB 与 微信小游戏 FS(运行时探测)。
    /// </summary>
    static class CacheWebGLBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Register()
        {
            CacheManager.RegisterFactory(ns =>
            {
                WebFileSystemHelper.EnsureCreated();
                return new WebGLCacheManager(ns);
            });
        }
    }
}
#endif
