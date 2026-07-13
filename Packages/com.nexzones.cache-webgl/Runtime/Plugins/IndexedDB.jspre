Module['IDB'] = Module['IDB'] || {};

Module.IDB.OpenDatabase = function(dbName, version, identifier, gameObjectName, callbackMethod) {
        var dbNameStr = UTF8ToString(dbName);    
        var versionNum = version;                
        var idInt = identifier; 
        var goName = UTF8ToString(gameObjectName); 
        var callback = UTF8ToString(callbackMethod); 

        try {
            if (typeof indexedDB === "undefined") {
                indexedDB = IDBFS.indexedDB
            }
            var request = indexedDB.open(dbNameStr, versionNum);

            request.onupgradeneeded = function(event) {
                var db = event.target.result;
                if (!db.objectStoreNames.contains('fileData')) {
                    var store = db.createObjectStore('fileData', { keyPath: 'id' });
                    store.createIndex('idIndex', 'id', { unique: true });
                }
            };

            request.onsuccess = function(event) {
                var db = event.target.result;
                Module['dbInstance'] = db;
                Module['SendMessage'](goName, callback, JSON.stringify({ id: idInt, success: true, message: "Database opened" }));
            };

            request.onerror = function(event) {
                console.error("IndexedDB error:", event);
                Module['SendMessage'](goName, callback, JSON.stringify({ id: idInt, success: false, error: "Error opening database" }));
            };
        } catch (e) {
            // indexedDB.open 同步抛(SecurityError/隐私模式等)也必回调,否则 init op
            // 永挂、串行队列卡死。回调 false 让上层按"无缓存"降级。
            console.error("OpenDatabase exception", e);
            Module['SendMessage'](goName, callback, JSON.stringify({ id: idInt, success: false, error: "OpenDatabase exception: " + e }));
        }
    };

    Module.IDB.WriteData = function(key, value, identifier, gameObjectName, callbackMethod) {
        var keyStr = UTF8ToString(key);
        var valueStr = UTF8ToString(value);
        var idInt = identifier; 
        var goName = UTF8ToString(gameObjectName);
        var callback = UTF8ToString(callbackMethod);

        if (!Module['dbInstance']) {
            console.error("Database is not opened");
            Module['SendMessage'](goName, callback, JSON.stringify({ id: idInt, success: false, error: "Database not opened" }));
            return;
        }

        try {
            var transaction = Module['dbInstance'].transaction(['fileData'], 'readwrite');
            var store = transaction.objectStore('fileData');
            var data = { id: keyStr, value: valueStr };
            var putRequest = store.put(data);

            putRequest.onsuccess = function() {
                Module['SendMessage'](goName, callback, JSON.stringify({ id: idInt, success: true, message: "Data saved successfully" }));
            };

            putRequest.onerror = function(event) {
                console.error("Error saving data", event);
                Module['SendMessage'](goName, callback, JSON.stringify({ id: idInt, success: false, error: "Error saving data" }));
            };
        } catch (e) {
            // 同步抛异常也必回调,否则 ProcessQueue 死等此 op 的回调 → 整条串行 IDB 队列
            // (之后所有图片的读/查/写)全卡死。SaveCache 每次下载都写,这条尤其关键。
            console.error("WriteData exception", e);
            Module['SendMessage'](goName, callback, JSON.stringify({ id: idInt, success: false, error: "WriteData exception: " + e }));
        }
    };

    Module.IDB.ReadData = function(key, identifier, gameObjectName, callbackMethod) {
        var keyStr = UTF8ToString(key);
        var idInt = identifier; 
        var goName = UTF8ToString(gameObjectName);
        var callback = UTF8ToString(callbackMethod);

        if (!Module['dbInstance']) {
            console.error("Database is not opened");
            Module['SendMessage'](goName, callback, JSON.stringify({ id: idInt, success: false, error: "Database not opened" }));
            return;
        }

        try {
            var transaction = Module['dbInstance'].transaction(['fileData'], 'readonly');
            var store = transaction.objectStore('fileData');
            var getRequest = store.get(keyStr);

            getRequest.onsuccess = function(event) {
                var result = event.target.result;
                if (result) {
                    Module['SendMessage'](goName, callback, JSON.stringify({ id: idInt, success: true, message: result.value }));
                } else {
                    Module['SendMessage'](goName, callback, JSON.stringify({ id: idInt, success: false, error: "No data found" }));
                }
            };

            getRequest.onerror = function(event) {
                console.error("Error loading data", event);
                Module['SendMessage'](goName, callback, JSON.stringify({ id: idInt, success: false, error: "Error loading data" }));
            };
        } catch (e) {
            // 同步抛异常(DB 关闭中 / objectStore 缺失等):也必须回调,否则 C# 侧
            // 队列条目永挂 → LoadCache 断 → ApplyEncoded 不触发 → 卡占位图。
            // 回调 false 让上层走兜底(下载重取)。
            console.error("ReadData exception", e);
            Module['SendMessage'](goName, callback, JSON.stringify({ id: idInt, success: false, error: "ReadData exception: " + e }));
        }
    };

    Module.IDB.DeleteData = function(key, identifier, gameObjectName, callbackMethod) {
        var keyStr = UTF8ToString(key);
        var idInt = identifier; 
        var goName = UTF8ToString(gameObjectName);    
        var callback = UTF8ToString(callbackMethod);  

        if (!Module['dbInstance']) {
            console.error("Database is not opened");
            Module['SendMessage'](goName, callback, JSON.stringify({ id: idInt, success: false, error: "Database not opened" }));
            return;
        }

        try {
            var transaction = Module['dbInstance'].transaction(['fileData'], 'readwrite');
            var store = transaction.objectStore('fileData');
            var deleteRequest = store.delete(keyStr);

            deleteRequest.onsuccess = function(event) {
                Module['SendMessage'](goName, callback, JSON.stringify({ id: idInt, success: true, message: "Data deleted successfully" }));
            };

            deleteRequest.onerror = function(event) {
                console.error("Error deleting file", event);
                Module['SendMessage'](goName, callback, JSON.stringify({ id: idInt, success: false, error: "Error deleting file" }));
            };
        } catch (e) {
            // 同步抛也必回调,否则串行 IDB 队列卡死。
            console.error("DeleteData exception", e);
            Module['SendMessage'](goName, callback, JSON.stringify({ id: idInt, success: false, error: "DeleteData exception: " + e }));
        }
    };

    Module.IDB.FindData = function(key, identifier, gameObjectName, callbackMethod) {
        var keyStr = UTF8ToString(key);
        var idInt = identifier; 
        var goName = UTF8ToString(gameObjectName);   
        var callback = UTF8ToString(callbackMethod); 
    
        if (!Module['dbInstance']) {
            console.error("Database is not opened");
            Module['SendMessage'](goName, callback, JSON.stringify({ id: idInt, success: false, error: "Database not opened" }));
            return;
        }
    
        try {
            var transaction = Module['dbInstance'].transaction(['fileData'], 'readonly');
            var store = transaction.objectStore('fileData');
            var countRequest = store.count(keyStr);
            countRequest.onsuccess = function(event) {
                var exists = event.target.result > 0;
                if (exists) {
                    Module['SendMessage'](goName, callback, JSON.stringify({ id: idInt, success: true, message: "Data exists" }));
                } else {
                    Module['SendMessage'](goName, callback, JSON.stringify({ id: idInt, success: false, message: "No data found" }));
                }
            };

            countRequest.onerror = function(event) {
                console.error("Error checking file", event);
                Module['SendMessage'](goName, callback, JSON.stringify({ id: idInt, success: false, error: "Error checking file" }));
            };
        } catch (e) {
            // 同步抛异常也必回调,否则 FindCache 断 → 上层永挂。回调 false=当作没缓存,走下载。
            console.error("FindData exception", e);
            Module['SendMessage'](goName, callback, JSON.stringify({ id: idInt, success: false, error: "FindData exception: " + e }));
        }
    };

    Module.IDB.SyncFileSystem = function () {
        FS.syncfs(false, function (err) {
            if (err) console.log("syncfs error: " + err);
        });
    };
