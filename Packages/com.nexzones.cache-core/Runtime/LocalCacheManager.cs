using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Events;

namespace Nexzones.Cache
{

public class LocalCacheManager : BaseCacheManager
{
    public LocalCacheManager(string storyId) : base(Application.persistentDataPath + $"/_cache_/{storyId}/") 
    {
        if (!Directory.Exists(filePath))
        {
            Directory.CreateDirectory(filePath);
        }
    }

    protected override void LoadCacheExpiry()
    {
        if (File.Exists(expiryFilePath))
        {
            string json = File.ReadAllText(expiryFilePath);
            DeserializeExpiry(json);
        }
    }

    protected override void SaveJsonToExpiryFile(string json)
    {
        File.WriteAllText(expiryFilePath, json);
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

        if (File.Exists(uniqueHash))
        {
            callback?.Invoke(true, uniqueHash);
        }
        else
        {
            callback?.Invoke(false, uniqueHash);
        }
    }

    public override void LoadCache(string cacheFile, Action<string> callback)
    {
        if (!GlobalCached) // 检查全局缓存开关
        {
            callback?.Invoke(null);
            return;
        }

        if (File.Exists(cacheFile))
        {
            var content = File.ReadAllText(cacheFile);
            callback?.Invoke(content);
        }
    }

    public override void LoadCache(string cacheFile, Action<byte[]> callback)
    {
        if (!GlobalCached) // 检查全局缓存开关
        {
            callback?.Invoke(null);
            return;
        }

        if (File.Exists(cacheFile))
        {
            var content = File.ReadAllBytes(cacheFile);
            callback?.Invoke(content);
        }
    }

    public override void SaveCache(string url, byte[] content, Action callback = null, int contentOffset = 0, int contentLength = -1, int expires = int.MaxValue)
    {
        if (!GlobalCached) // 检查全局缓存开关
        {
            callback?.Invoke();
            return;
        }

        (var md5Name, var uniqueHash) = UrlToCacheFile(url);

        File.WriteAllBytes(uniqueHash, content);
        SetCacheExpiry(md5Name, expires);
        callback?.Invoke();
    }

    public override void SaveCache(string url, string content, Action callback = null, int expires = int.MaxValue)
    {
        if (!GlobalCached) // 检查全局缓存开关
        {
            callback?.Invoke();
            return;
        }

        (var md5Name, var uniqueHash) = UrlToCacheFile(url);

        File.WriteAllText(uniqueHash, content);
        SetCacheExpiry(md5Name, expires);
        callback?.Invoke();
    }

    public override void RemoveCache(string url)
    {
        if (!GlobalCached) // 检查全局缓存开关
        {
            return;
        }

        (var md5Name, var uniqueHash) = UrlToCacheFile(url);

        if (File.Exists(uniqueHash))
        {
            File.Delete(uniqueHash);
        }

        cacheExpiry.Remove(md5Name);
        SaveCacheExpiry();
    }

    public override void ClearAllCachedFiles(UnityAction callback = null)
    {
        try
        {
            Directory.Delete(filePath, true);
            cacheExpiry.Clear();
            // SaveCacheExpiry();
        }
        catch (Exception ex)
        {
            Debug.LogError($"[LocalCacheManager] Error while removing cached file: {ex.Message}");
        }
    }
}
}
