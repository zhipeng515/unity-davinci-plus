#if UNITY_WEBGL

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine.Events;

namespace Nexzones.Cache
{

public class WebGLCacheManager : BaseCacheManager
{
    public WebGLCacheManager(string storyId) : base($"/_cache_/{storyId}/") 
    {
        // Uncomment if necessary for IndexedDB
        // WebFileSystemHelper.instance.IsPathExists(filePath, (exist) => { });
    }

    protected override void LoadCacheExpiry()
    {
        WebFileSystemHelper.instance.ReadData(expiryFilePath, (json) =>
        {
            if (!string.IsNullOrEmpty(json))
            {
                DeserializeExpiry(json);
            }
        });
    }

    protected override void SaveJsonToExpiryFile(string json)
    {
        WebFileSystemHelper.instance.WriteData(expiryFilePath, json, (data) => { });
    }

    public override void FindCache(string url, Action<bool, string> callback)
    {
        if (!GlobalCached) // 检查全局缓存开关
        {
            callback?.Invoke(false, null);
            return;
        }

        (var md5Name, var uniqueHash) = UrlToCacheFile(url);

        // Check for expiration
        if (cacheExpiry.TryGetValue(md5Name, out DateTime expiryDate))
        {
            if (DateTime.UtcNow > expiryDate)
            {
                // Cache expired, remove the entry
                cacheExpiry.Remove(md5Name);
                SaveCacheExpiry();
                callback?.Invoke(false, uniqueHash);
                return;
            }
        }

        WebFileSystemHelper.instance.FindData(uniqueHash, (exist) =>
        {
            callback?.Invoke(exist, uniqueHash);
        });
    }

    public override void LoadCache(string cacheFile, Action<string> callback)
    {
        if (!GlobalCached) // 检查全局缓存开关
        {
            callback?.Invoke(null);
            return;
        }

        WebFileSystemHelper.instance.ReadData(cacheFile, (content) =>
        {
            callback?.Invoke(content);
        });
    }

    public override void LoadCache(string cacheFile, Action<byte[]> callback)
    {
        if (!GlobalCached) // 检查全局缓存开关
        {
            callback?.Invoke(null);
            return;
        }

        WebFileSystemHelper.instance.ReadData(cacheFile, (content) =>
        {
            callback?.Invoke(content);
        });
    }

    public override void SaveCache(string url, byte[] content, Action callback = null, int contentOffset = 0, int contentLength = -1, int expires = int.MaxValue)
    {
        if (!GlobalCached) // 检查全局缓存开关
        {
            callback?.Invoke();
            return;
        }

        (var md5Name, var uniqueHash) = UrlToCacheFile(url);

        WebFileSystemHelper.instance.WriteData(uniqueHash, content, (data) =>
        {
            SetCacheExpiry(md5Name, expires);
            callback?.Invoke();
        }, contentOffset, contentLength);
    }

    public override void SaveCache(string url, string content, Action callback = null, int expires = int.MaxValue)
    {
        if (!GlobalCached) // 检查全局缓存开关
        {
            callback?.Invoke();
            return;
        }

        (var md5Name, var uniqueHash) = UrlToCacheFile(url);

        WebFileSystemHelper.instance.WriteData(uniqueHash, content, (data) =>
        {
            SetCacheExpiry(md5Name, expires);
            callback?.Invoke();
        });
    }

    public override void RemoveCache(string url)
    {
        if (!GlobalCached) // 检查全局缓存开关
        {
            return;
        }

        (var md5Name, var uniqueHash) = UrlToCacheFile(url);

        WebFileSystemHelper.instance.DeleteData(uniqueHash, (success) => { });

        cacheExpiry.Remove(md5Name);
        SaveCacheExpiry();
    }

    public override void ClearAllCachedFiles(UnityAction callback = null)
    {
        int totalCount = cacheExpiry.Count;
        int deleteCount = 0;
        foreach (var kvp in cacheExpiry)
        {
            var uniqueHash = kvp.Key; // 获取缓存的唯一哈希
            WebFileSystemHelper.instance.DeleteData(uniqueHash, (success) => {
                // 删除一个缓存数据后计数
                deleteCount++;

                // 当所有数据都删除完毕时执行后续操作
                if (deleteCount == totalCount)
                {
                    // 清空缓存和过期信息
                    cacheExpiry.Clear();
                    SaveCacheExpiry();

                    // 执行刷新操作
                    callback?.Invoke();
                }
            });
        }
    }
}
}
#endif
