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
    }
#if UNITY_WEBGL
    [DllImport("__Internal")]
    private static extern void NXZ_OpenDatabase(string dbName, int version, int identifier, string gameObjectName, string callbackMethod);

    [DllImport("__Internal")]
    private static extern void NXZ_WriteData(string fileName, string value, int identifier, string gameObjectName, string callbackMethod);

    [DllImport("__Internal")]
    private static extern void NXZ_ReadData(string fileName, int identifier, string gameObjectName, string callbackMethod);

    [DllImport("__Internal")]
    private static extern void NXZ_DeleteData(string fileName, int identifier, string gameObjectName, string callbackMethod);

    [DllImport("__Internal")]
    private static extern void NXZ_FindData(string fileName, int identifier, string gameObjectName, string callbackMethod);
    
    [DllImport("__Internal")]
    public static extern void NXZ_SyncFileSystem();

    public static void SyncFileSystem() { NXZ_SyncFileSystem(); }

    public static WebFileSystemHelper instance;

    // Operation queue and completion identifier
    private Queue<(Action operation, Delegate callback, int identifier)> operationQueue = new Queue<(Action, Delegate, int)>();
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
        AddToQueue(currentId, () => NXZ_OpenDatabase(dbName, version, currentId, gameObject.name, "OnDatabaseOpened"), action);
        if(enableLog) {
            Debug.Log($"WebFileSystemHelper - OpenDB {dbName} {currentId}");
        }
    }

    // Write data
    public void WriteData(string fileName, string value, Action<bool> action)
    {
        int currentId = nextId++;
        AddToQueue(currentId, () => NXZ_WriteData(fileName, value, currentId, gameObject.name, "OnDataSaved"), action);
        if(enableLog) {
            Debug.Log($"WebFileSystemHelper - WriteData(string) {fileName} {currentId}");
        }
    }

    public void WriteData(string fileName, byte[] value, Action<bool> action, int offset = 0, int lenght = -1)
    {
        int currentId = nextId++;
        string base64String = Convert.ToBase64String(value, offset, lenght > 0 ? lenght : value.Length - offset);
        AddToQueue(currentId, () => NXZ_WriteData(fileName, base64String, currentId, gameObject.name, "OnDataSaved"), action);
        if(enableLog) {
            Debug.Log($"WebFileSystemHelper - WriteData(byte[]) {fileName} {currentId}");
        }
    }

    // Read data
    public void ReadData(string fileName, Action<string> action)
    {
        int currentId = nextId++;
        AddToQueue(currentId, () => NXZ_ReadData(fileName, currentId, gameObject.name, "OnDataLoaded"), action);
        if(enableLog) {
            Debug.Log($"WebFileSystemHelper - ReadData(string) {fileName} {currentId}");
        }
    }

    public void ReadData(string fileName, Action<byte[]> action)
    {
        int currentId = nextId++;
        AddToQueue(currentId, () => NXZ_ReadData(fileName, currentId, gameObject.name, "OnDataLoaded"), action);
        if(enableLog) {
            Debug.Log($"WebFileSystemHelper - ReadData(byte[]) {fileName} {currentId}");
        }
    }

    // Delete data
    public void DeleteData(string fileName, Action<bool> action)
    {
        int currentId = nextId++;
        AddToQueue(currentId, () => NXZ_DeleteData(fileName, currentId, gameObject.name, "OnDataDeleted"), action);
        if(enableLog) {
            Debug.Log($"WebFileSystemHelper - DeleteData {fileName} {currentId}");
        }
    }

    // Check if data exists
    public void FindData(string fileName, Action<bool> action)
    {
        int currentId = nextId++;
        AddToQueue(currentId, () => NXZ_FindData(fileName, currentId, gameObject.name, "OnDataFound"), action);
        if(enableLog) {
            Debug.Log($"WebFileSystemHelper - FindData {fileName} {currentId}");
        }
    }

    // Add operation to the queue (generic)
    private void AddToQueue<T>(int id, Action operation, Action<T> callback = null)
    {
        operationQueue.Enqueue((operation, callback, id));
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

    // Coroutine: Process operations in the queue and ensure callback order is consistent
    private IEnumerator ProcessQueue()
    {
        while (true)
        {
            if (operationQueue.Count > 0)
            {
                // Get operation from the queue
                var (operation, callback, identifier) = operationQueue.Dequeue();
                operation.Invoke(); // Execute operation

                // Wait for the JavaScript callback to complete before executing the corresponding callback
                CallbackResult data = null;
                while (!completedOperations.TryGetValue(identifier, out data))
                {
                    yield return null; // Wait for callback completion
                }

                // Execute the corresponding callback, passing data returned from JavaScript
                if (callback != null && data != null)
                {
                    InvokeCallback(callback, data); // Call the callback based on the specific type
                }

                // Remove processed operation
                completedOperations.Remove(identifier);
            }
            else
            {
                // If the queue is empty, pause for a frame and then check the queue again
                yield return null;
            }
        }
    }

    // Invoke generic callback
    private void InvokeCallback(Delegate callback, CallbackResult result)
    {
        // Handle based on the type of callback
        if (callback is Action<byte[]> byteArrayCallback)
        {
            byte[] byteArray = null;
            if(result.message != null) {
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
