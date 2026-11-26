using UnityEngine;
using System.Runtime.InteropServices;
using System;
using System.Collections.Generic;
using System.Collections;

namespace Nexzones.Cache
{

public class WebFileSystemHelper : MonoBehaviour
{
    public static bool enableLog = false;

    // Define a helper class for parsing callback results
    [Serializable]
    private class CallbackResult
    {
        public int id;
        public bool success;
        public string message;
        public string error;
        /// 二进制通道:数据不在 message 里,而是暂存在 JS 侧,靠 NXZ_TakeBinary 取。
        public bool binary;
        public int length;
    }
#if UNITY_WEBGL
    [DllImport("__Internal")]
    private static extern void NXZ_OpenDatabase(string dbName, int version, int identifier, string gameObjectName, string callbackMethod);

    [DllImport("__Internal")]
    private static extern void NXZ_WriteData(string fileName, string value, int identifier, string gameObjectName, string callbackMethod);

    [DllImport("__Internal")]
    private static extern void NXZ_ReadData(string fileName, int identifier, string gameObjectName, string callbackMethod);

    [DllImport("__Internal")]
    private static extern void NXZ_WriteBinary(string fileName, byte[] value, int offset, int length, int identifier, string gameObjectName, string callbackMethod);

    [DllImport("__Internal")]
    private static extern void NXZ_ReadBinary(string fileName, int identifier, string gameObjectName, string callbackMethod);

    [DllImport("__Internal")]
    private static extern int NXZ_TakeBinary(int identifier, byte[] dest, int cap);

    [DllImport("__Internal")]
    private static extern void NXZ_DeleteData(string fileName, int identifier, string gameObjectName, string callbackMethod);

    [DllImport("__Internal")]
    private static extern void NXZ_FindData(string fileName, int identifier, string gameObjectName, string callbackMethod);
    
    [DllImport("__Internal")]
    public static extern void NXZ_SyncFileSystem();

    public static void SyncFileSystem() { NXZ_SyncFileSystem(); }

    /// 二进制通道:IndexedDB 与微信小游戏两个后端都已支持(jslib 里按平台分派),
    /// 所以这里只按平台开关,不再区分后端。编辑器下没有 jslib,走原路径。
    private static bool UseBinaryChannel
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        get { return true; }
#else
        get { return false; }
#endif
    }

    public static WebFileSystemHelper instance;

    // ── 操作调度 ──────────────────────────────────────────────────────────
    //
    // 🔴 这里原先是**一条严格串行的队列**:取一条 → 发出去 → 必须等它的回调
    // 回来才发下一条。于是存档读(`save_<作品id>.ndf`,就一条小操作)要排在
    // 几十上百条媒体/字体缓存的查·读·写后面:
    //
    //   · 等待时间 = 排在前面的**所有**条目之和;
    //   · 而且**每条至少一帧**(回调是靠帧循环取回来的)—— 60fps 下 300 条
    //     就是 5 秒以上,哪怕每条本身只要 1 毫秒。
    //
    // 同事报的「读取数据每两三次卡一次、一卡十几秒、最后能成功」正是这个形状:
    // 成不成功从来没问题,等多久全看按下去那一刻队列里排着多少东西
    // (Gitee IKUPQB, 2026-11-26;GameOver 里 retry 读档最容易撞上)。
    //
    // 现在:**最多 MAX_IN_FLIGHT 条同时在途**,但**同一个 key 仍然严格保序**
    // (同一个文件的写和读不会被调换)。IndexedDB 本身支持并发,而回调全程
    // 按 id 对账(`completedOperations`、`NXZ_TakeBinary` 都是按 id 存的),
    // 所以放开并发是安全的。
    //
    // 🔴 `OpenDatabase` 必须**独占**:它没完成时 `Module.dbInstance` 还没设上,
    // 并发发出去的读写会一律拿到 "Database not opened" → 上层判成"没缓存" →
    // 整库重新下载。所以它要等在途清空才发,运行期间也不放别的出去。
    private sealed class PendingOp
    {
        public Action operation;
        public Delegate callback;
        public int identifier;
        /// 这条操作动的文件名(同 key 必须保序);也是出声时报的名字。
        public string key;
        /// 独占(目前只有开库):在途清空才发,期间不放别的出去。
        public bool exclusive;
        public float enqueuedAt;
        public float startedAt;
    }

    /// 同时在途的上限。放开并发是为了不让一条小操作排在整批媒体后面,
    /// 不是为了压榨吞吐 —— 开大了只会让浏览器主线程更碎。
    private const int MAX_IN_FLIGHT = 4;

    /// 超过这个秒数(等待或执行)就出声。只在**慢的时候**响,平时全程静默。
    private const float SLOW_OP_SECONDS = 1.0f;

    private readonly List<PendingOp> waitingQueue = new List<PendingOp>();
    private readonly Dictionary<int, PendingOp> inFlight = new Dictionary<int, PendingOp>();
    private readonly HashSet<string> keysInFlight = new HashSet<string>();
    private readonly List<int> completedIdsScratch = new List<int>();
    private bool exclusiveInFlight = false;
    private Dictionary<int, CallbackResult> completedOperations = new Dictionary<int, CallbackResult>(); // Store completed operations' identifiers and return data
    private int nextId = 0;

    private void Start()
    {
        StartCoroutine(ProcessQueue()); // Ensure the coroutine runs continuously
    }

    /// <summary>幂等创建(平台工厂用):已存在直接返回,否则用默认库名创建。</summary>
    public static WebFileSystemHelper EnsureCreated(string dbName = "nexzones-cache", int version = 1, Action<bool> onReady = null)
    {
        if (instance != null) return instance;
        return Create(dbName, version, onReady ?? (_ => { }));
    }

    public static WebFileSystemHelper Create(string dbName, int version, Action<bool> action)
    {
        var go = new GameObject("WebFileSystemHelper");
        instance = go.AddComponent<WebFileSystemHelper>();
        instance.OpenDB(dbName, version, action);
        return instance;
    }

    // Open database
    public void OpenDB(string dbName, int version, Action<bool> action)
    {
        int currentId = nextId++;
        AddToQueue(currentId, () => NXZ_OpenDatabase(dbName, version, currentId, gameObject.name, "OnDatabaseOpened"), action,
            key: "<开库:" + dbName + ">", exclusive: true);
        if(enableLog) {
            Debug.Log($"WebFileSystemHelper - OpenDB {dbName} {currentId}");
        }
    }

    // Write data
    public void WriteData(string fileName, string value, Action<bool> action)
    {
        int currentId = nextId++;
        AddToQueue(currentId, () => NXZ_WriteData(fileName, value, currentId, gameObject.name, "OnDataSaved"), action, fileName);
        if(enableLog) {
            Debug.Log($"WebFileSystemHelper - WriteData(string) {fileName} {currentId}");
        }
    }

    public void WriteData(string fileName, byte[] value, Action<bool> action, int offset = 0, int lenght = -1)
    {
        int currentId = nextId++;
        int len = lenght > 0 ? lenght : value.Length - offset;
        if (UseBinaryChannel)
        {
            // 二进制直存:省掉 base64(+33%)和它前后的三次全量拷贝。
            AddToQueue(currentId, () => NXZ_WriteBinary(fileName, value, offset, len, currentId, gameObject.name, "OnDataSaved"), action, fileName);
        }
        else
        {
            string base64String = Convert.ToBase64String(value, offset, len);
            AddToQueue(currentId, () => NXZ_WriteData(fileName, base64String, currentId, gameObject.name, "OnDataSaved"), action, fileName);
        }
        if(enableLog) {
            Debug.Log($"WebFileSystemHelper - WriteData(byte[]) {fileName} {currentId}");
        }
    }

    // Read data
    public void ReadData(string fileName, Action<string> action)
    {
        int currentId = nextId++;
        AddToQueue(currentId, () => NXZ_ReadData(fileName, currentId, gameObject.name, "OnDataLoaded"), action, fileName);
        if(enableLog) {
            Debug.Log($"WebFileSystemHelper - ReadData(string) {fileName} {currentId}");
        }
    }

    public void ReadData(string fileName, Action<byte[]> action)
    {
        if (UseBinaryChannel)
        {
            int binId = nextId++;
            AddToQueue(binId, () => NXZ_ReadBinary(fileName, binId, gameObject.name, "OnDataLoaded"), action, fileName);
            if (enableLog)
            {
                Debug.Log($"WebFileSystemHelper - ReadBinary {fileName} {binId}");
            }
            return;
        }
        int currentId = nextId++;
        AddToQueue(currentId, () => NXZ_ReadData(fileName, currentId, gameObject.name, "OnDataLoaded"), action, fileName);
        if(enableLog) {
            Debug.Log($"WebFileSystemHelper - ReadData(byte[]) {fileName} {currentId}");
        }
    }

    // Delete data
    public void DeleteData(string fileName, Action<bool> action)
    {
        int currentId = nextId++;
        AddToQueue(currentId, () => NXZ_DeleteData(fileName, currentId, gameObject.name, "OnDataDeleted"), action, fileName);
        if(enableLog) {
            Debug.Log($"WebFileSystemHelper - DeleteData {fileName} {currentId}");
        }
    }

    // Check if data exists
    public void FindData(string fileName, Action<bool> action)
    {
        int currentId = nextId++;
        AddToQueue(currentId, () => NXZ_FindData(fileName, currentId, gameObject.name, "OnDataFound"), action, fileName);
        if(enableLog) {
            Debug.Log($"WebFileSystemHelper - FindData {fileName} {currentId}");
        }
    }

    // Add operation to the queue (generic)
    private void AddToQueue<T>(
        int id, Action operation, Action<T> callback = null,
        string key = null, bool exclusive = false)
    {
        waitingQueue.Add(new PendingOp
        {
            operation = operation,
            callback = callback,
            identifier = id,
            key = key ?? string.Empty,
            exclusive = exclusive,
            enqueuedAt = Time.realtimeSinceStartup,
        });
    }

    // Callback after database opened
    public void OnDatabaseOpened(string message)
    {
        HandleCallback(message);
    }

    // Callback after data saved
    public void OnDataSaved(string data)
    {
        HandleCallback(data);
    }

    // Callback after data loaded
    public void OnDataLoaded(string data)
    {
        HandleCallback(data);
    }

    // Callback after data deleted
    public void OnDataDeleted(string data)
    {
        HandleCallback(data);
    }

    // Callback after data existence checked
    public void OnDataFound(string data)
    {
        HandleCallback(data);
    }

    // Handle callback
    private void HandleCallback(string data)
    {
        var result = JsonUtility.FromJson<CallbackResult>(data);
        completedOperations[result.id] = result; // Store return data

        if (enableLog)
        {
            Debug.Log($"WebFileSystemHelper - Callback Handled - ID: {result.id}, " +
                    $"Success: {result.success}, " +
                    $"Message: {result.message}, " +
                    $"Error: {result.error}");
        }
    }

    /// <summary>
    /// 每帧:先发车(按 FIFO,受"同 key 保序 / 开库独占 / 在途上限"约束),
    /// 再收车(回调按 id 对账)。
    /// </summary>
    private IEnumerator ProcessQueue()
    {
        while (true)
        {
            DispatchReady();
            CollectCompleted();
            yield return null;
        }
    }

    private void DispatchReady()
    {
        if (exclusiveInFlight) return;
        for (int i = 0; i < waitingQueue.Count; i++)
        {
            if (inFlight.Count >= MAX_IN_FLIGHT) return;
            var op = waitingQueue[i];
            if (op == null) { waitingQueue.RemoveAt(i); i--; continue; }
            if (op.exclusive)
            {
                // 开库:等在途全部跑完才发,发了就不再放别的出去。
                if (inFlight.Count > 0) return;
                waitingQueue.RemoveAt(i);
                exclusiveInFlight = true;
                StartOp(op);
                return;
            }
            // 同一个文件的操作必须保序 —— 在途有同 key 的就让它先过。
            if (op.key.Length > 0 && keysInFlight.Contains(op.key)) continue;
            waitingQueue.RemoveAt(i);
            i--;
            StartOp(op);
        }
    }

    private void StartOp(PendingOp op)
    {
        op.startedAt = Time.realtimeSinceStartup;
        inFlight[op.identifier] = op;
        if (op.key.Length > 0) keysInFlight.Add(op.key);
        op.operation.Invoke();
    }

    private void CollectCompleted()
    {
        if (completedOperations.Count == 0) return;
        // 先把这一帧完成的 id 挑出来再回调:回调里还会继续入队/发车,
        // 边遍历边改字典会炸。
        completedIdsScratch.Clear();
        foreach (var kv in completedOperations) completedIdsScratch.Add(kv.Key);

        for (int i = 0; i < completedIdsScratch.Count; i++)
        {
            int id = completedIdsScratch[i];
            if (!completedOperations.TryGetValue(id, out var data)) continue;
            completedOperations.Remove(id);

            if (!inFlight.TryGetValue(id, out var op) || op == null)
            {
                // 认不出的回调 = 调度表和 JS 侧对不上账。丢掉是对的(没有谁在等它),
                // 但必须出声:静默丢回调就是"上层永远等不到"那一类的病根。
                Debug.LogWarning($"[缓存队列] 收到不在途的回调 id={id} —— 已丢弃");
                continue;
            }
            inFlight.Remove(id);
            if (op.key.Length > 0) keysInFlight.Remove(op.key);
            if (op.exclusive) exclusiveInFlight = false;

            ReportIfSlow(op);

            if (op.callback != null && data != null)
            {
                InvokeCallback(op.callback, data); // Call the callback based on the specific type
            }
        }
    }

    /// <summary>
    /// 🔴 **排队要能被看见**(Gitee IKUPQB, 2026-11-26)。
    ///
    /// 现成的 `[CACHE] 读/写 … 回调内 XXms` 探针只量**回调体内**的时间,
    /// 排队一秒不量 —— 于是人等十秒、日志里一切正常。这一行补的就是那一段:
    /// 等了多久、跑了多久、队列里还剩几条、谁在前面占着。
    ///
    /// 常开(不挂 enableLog):只在超过 SLOW_OP_SECONDS 时才响,平时静默。
    /// </summary>
    private void ReportIfSlow(PendingOp op)
    {
        float waited = op.startedAt - op.enqueuedAt;
        float ran = Time.realtimeSinceStartup - op.startedAt;
        if (waited < SLOW_OP_SECONDS && ran < SLOW_OP_SECONDS) return;

        string blocker = "-";
        float oldest = float.MaxValue;
        foreach (var kv in inFlight)
        {
            var other = kv.Value;
            if (other == null || other.startedAt >= oldest) continue;
            oldest = other.startedAt;
            blocker = other.key.Length > 0 ? other.key : "(无名)";
        }

        Debug.LogWarning(
            $"[缓存队列] {(op.key.Length > 0 ? op.key : "(无名)")} " +
            $"等待 {waited * 1000f:F0}ms 执行 {ran * 1000f:F0}ms | " +
            $"队列里还有 {waitingQueue.Count} 条, 在途 {inFlight.Count} 条, " +
            $"最早在途={blocker}");
    }

    // Invoke generic callback
    private void InvokeCallback(Delegate callback, CallbackResult result)
    {
        // Handle based on the type of callback
        if (callback is Action<byte[]> byteArrayCallback)
        {
            byte[] byteArray = null;
            if (result.binary)
            {
                // 二进制通道:数据在 JS 侧暂存,这里一次 HEAPU8.set 取回来。
                // 取不满就当没有 —— 宁可回落到重新下载,也不给上层半截数据。
                if (result.success && result.length > 0)
                {
                    var buf = new byte[result.length];
                    int got = NXZ_TakeBinary(result.id, buf, result.length);
                    byteArray = got == result.length ? buf : null;
                }
            }
            else if (result.message != null)
            {
                byteArray = Convert.FromBase64String(result.message);
            }
            byteArrayCallback.Invoke(byteArray);
        }
        else if (callback is Action<string> stringCallback)
        {
            stringCallback.Invoke(result.message);
        }
        else if (callback is Action<bool> boolCallback)
        {
            boolCallback.Invoke(result.success);
        }
    }
#endif
}
}
