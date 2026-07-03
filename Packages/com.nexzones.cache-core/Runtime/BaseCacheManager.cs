using System;
using UnityEngine;
using System.Collections.Generic;
using System.Text;
using UnityEngine.Events;

namespace Nexzones.Cache
{


public abstract class BaseCacheManager
{
    public static bool GlobalCached { get; set; } = true;
    protected string filePath;
    protected Dictionary<string, DateTime> cacheExpiry = new Dictionary<string, DateTime>();
    protected string expiryFilePath;

    public static BaseCacheManager instance;

    public static string CreateMD5(string input)
    {
        // Use input string to calculate MD5 hash
        using (System.Security.Cryptography.MD5 md5 = System.Security.Cryptography.MD5.Create())
        {
            byte[] inputBytes = Encoding.ASCII.GetBytes(input);
            byte[] hashBytes = md5.ComputeHash(inputBytes);

            // Convert the byte array to hexadecimal string
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < hashBytes.Length; i++)
            {
                sb.Append(hashBytes[i].ToString("X2"));
            }
            return sb.ToString();
        }
    }


    protected BaseCacheManager(string path)
    {
        filePath = path;
        expiryFilePath = filePath + "cache.expiry";
        LoadCacheExpiry();
    }

    protected abstract void LoadCacheExpiry();

    protected void SaveCacheExpiry()
    {
        SaveJsonToExpiryFile(SerializeExpiry());
    }

    // ---- 过期表序列化(JsonUtility;零外部依赖) ----
    // JsonUtility 不支持 Dictionary,用并行数组包装。旧格式(Newtonsoft 字典)
    // 解析失败时静默清空 —— 过期表丢失只导致缓存提前视为过期,安全。
    [Serializable]
    class ExpiryTable
    {
        public List<string> keys = new List<string>();
        public List<long> ticksUtc = new List<long>();
    }

    protected string SerializeExpiry()
    {
        var t = new ExpiryTable();
        foreach (var kv in cacheExpiry)
        {
            t.keys.Add(kv.Key);
            t.ticksUtc.Add(kv.Value.Ticks);
        }
        return JsonUtility.ToJson(t);
    }

    protected void DeserializeExpiry(string json)
    {
        cacheExpiry.Clear();
        if (string.IsNullOrEmpty(json)) return;
        try
        {
            var t = JsonUtility.FromJson<ExpiryTable>(json);
            if (t?.keys == null || t.ticksUtc == null) return;
            for (int i = 0; i < t.keys.Count && i < t.ticksUtc.Count; i++)
                cacheExpiry[t.keys[i]] = new DateTime(t.ticksUtc[i], DateTimeKind.Utc);
        }
        catch
        {
            // 旧格式/损坏 → 空表(缓存按已过期处理,自动重新拉取)
        }
    }

    protected abstract void SaveJsonToExpiryFile(string json);

    protected void SetCacheExpiry(string md5Name, int expires)
    {
        DateTime expiryDate = DateTime.UtcNow.AddSeconds(expires);
        cacheExpiry[md5Name] = expiryDate;
        SaveCacheExpiry();
    }

    public (string key, string cacheFile) UrlToCacheFile(string url)
    {
        // Debug.Log("BaseCacheManager UrlToCacheFile: " + url);
        Uri uri = new Uri(url);
        var md5Name = CreateMD5(uri.PathAndQuery);
        var uniqueHash = filePath + md5Name;// + Path.GetExtension(url);
        return (md5Name, uniqueHash);
    }

    public abstract void FindCache(string url, Action<bool, string> callback);
    public abstract void LoadCache(string cacheFile, Action<string> callback);
    public abstract void LoadCache(string cacheFile, Action<byte[]> callback);
    public abstract void SaveCache(string url, byte[] content, Action callback = null, int contentOffset = 0, int contentLength = -1, int expires = int.MaxValue);
    public abstract void SaveCache(string url, string content, Action callback = null, int expires = int.MaxValue);
    public abstract void RemoveCache(string url);
    public abstract void ClearAllCachedFiles(UnityAction callback = null);
}
}
