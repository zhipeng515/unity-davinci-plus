using System;

namespace Nexzones.Cache
{
    /// <summary>
    /// 平台后端工厂。默认给 LocalCacheManager(文件系统);平台包(如
    /// com.nexzones.cache-webgl)在场时通过 RegisterFactory 自注册接管 ——
    /// 多平台工程三包全装,按构建目标自动选后端,调用方零条件编译。
    /// </summary>
    public static class CacheManager
    {
        static Func<string, BaseCacheManager> registeredFactory;

        /// <summary>平台包自注册入口(RuntimeInitializeOnLoadMethod 时机调用)。</summary>
        public static void RegisterFactory(Func<string, BaseCacheManager> factory)
        {
            registeredFactory = factory;
        }

        /// <summary>创建一个缓存实例;cacheNamespace 用于多租户/多故事隔离。</summary>
        public static BaseCacheManager Create(string cacheNamespace)
        {
            return registeredFactory != null
                ? registeredFactory(cacheNamespace)
                : new LocalCacheManager(cacheNamespace);
        }

        /// <summary>创建并设为全局 BaseCacheManager.instance(Davinci 等库默认读它)。</summary>
        public static BaseCacheManager Init(string cacheNamespace)
        {
            BaseCacheManager.instance = Create(cacheNamespace);
            return BaseCacheManager.instance;
        }
    }
}
