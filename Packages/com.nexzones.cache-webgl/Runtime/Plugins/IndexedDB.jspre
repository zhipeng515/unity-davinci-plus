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

    // ── 二进制通道 ────────────────────────────────────────────────
    // 原来的字符串通道对大文件是灾难:一份缓存要被完整拷贝四次 ——
    //   写:Convert.ToBase64String(C#,+33%) → C# string→JS string → UTF8ToString
    //   读:JSON.stringify(几 MB 字符串,还要转义) → SendMessage 转 UTF8 进堆
    //      → JsonUtility.FromJson → Convert.FromBase64String
    // 而**读的那几次全发生在 IDBRequest 的 onsuccess 回调里**,同步、不可打断。
    // 2026-08-26 实测:一条 18.86MB 的 base64 条目 ≈ 每次读 750ms 冻帧,
    // 浏览器直接报 `[Violation] 'success' handler took 737ms`。
    // 「IndexedDB 是异步的」只保证等待不阻塞,拿到之后的搬运照样在主线程。
    //
    // 二进制通道:IndexedDB 原生支持存 Uint8Array(structured clone),于是
    // base64 整个消失,读只剩一次 HEAPU8.set,回调里几乎不做事。
    Module.IDB._bin = Module.IDB._bin || {};

    Module.IDB._takeBinary = function(identifier) {
        var v = Module.IDB._bin[identifier];
        delete Module.IDB._bin[identifier];
        return v || null;
    };

    Module.IDB.WriteBinary = function(key, bytes, identifier, gameObjectName, callbackMethod) {
        var keyStr = UTF8ToString(key);
        var goName = UTF8ToString(gameObjectName);
        var callback = UTF8ToString(callbackMethod);
        var t0 = (typeof performance !== 'undefined') ? performance.now() : 0;
        var done = function(ok, err) {
            var dt = ((typeof performance !== 'undefined') ? performance.now() : 0) - t0;
            if (dt > 50) {
                console.log('[CACHE] 写 ' + keyStr + ' ' + bytes.length + ' 字节, 回调内 ' + dt.toFixed(0) + 'ms');
            }
            Module['SendMessage'](goName, callback,
                JSON.stringify({ id: identifier, success: ok, error: err || '' }));
        };
        if (!Module['dbInstance']) { done(false, 'Database not opened'); return; }
        try {
            var store = Module['dbInstance'].transaction(['fileData'], 'readwrite').objectStore('fileData');
            var req = store.put({ id: keyStr, value: bytes });
            req.onsuccess = function() { done(true); };
            req.onerror = function(e) { console.error('WriteBinary error', e); done(false, 'Error saving data'); };
        } catch (e) {
            // 同步抛也必回调,否则 C# 侧串行队列死等这一条 → 之后所有读写全卡死。
            console.error('WriteBinary exception', e);
            done(false, 'WriteBinary exception: ' + e);
        }
    };

    Module.IDB.ReadBinary = function(key, identifier, gameObjectName, callbackMethod) {
        var keyStr = UTF8ToString(key);
        var goName = UTF8ToString(gameObjectName);
        var callback = UTF8ToString(callbackMethod);
        // 常驻哨兵:**只在慢的时候才响**。正常走二进制通道,一条 4.96MB 的条目
        // 回调内只要十几毫秒,全程静默;一旦哪天退回字符串搬运(或别的原因让
        // 回调变重),它会自己叫出来 —— 不必再靠人从头猜一遍。
        // (2026-08-26 就是靠这条日志把范围从"预载/CDN/字体"收敛到单张图上的。)
        var t0 = (typeof performance !== 'undefined') ? performance.now() : 0;
        var done = function(ok, len, err) {
            var dt = ((typeof performance !== 'undefined') ? performance.now() : 0) - t0;
            if (dt > 50) {
                console.log('[CACHE] 读 ' + keyStr + ' ' + (len || 0) + ' 字节, 回调内 ' + dt.toFixed(0) + 'ms'
                    + (ok ? '' : ' (未命中: ' + (err || '') + ')'));
            }
            Module['SendMessage'](goName, callback,
                JSON.stringify({ id: identifier, success: ok, binary: true, length: len || 0, error: err || '' }));
        };
        if (!Module['dbInstance']) { done(false, 0, 'Database not opened'); return; }
        try {
            var store = Module['dbInstance'].transaction(['fileData'], 'readonly').objectStore('fileData');
            var req = store.get(keyStr);
            req.onsuccess = function(event) {
                var r = event.target.result;
                if (!r) { done(false, 0, 'No data found'); return; }
                if (typeof r.value === 'string') {
                    // 旧的 base64 字符串条目。
                    //
                    // 🔴 这里**不能当未命中丢掉**:FindCache 只用 count() 看键在不在,
                    // 旧条目当然在 → 上层认定"已缓存"→ 来 LoadCache 却拿到 null →
                    // ApplyEncoded 不触发 → 永远卡在占位图(实测:图片全黑)。
                    // 生产侧和消费侧对"命中"的判断必须一致,否则就是静默失效。
                    //
                    // 所以就地解码,并顺手用二进制写回升级 —— 下次读就是快路径,
                    // 旧库会随使用自然迁移完。解码走 fetch(data:) 而不是 atob 循环:
                    // 原生实现、而且是**异步**的,不占这个 onsuccess 回调。
                    fetch('data:application/octet-stream;base64,' + r.value)
                        .then(function (resp) { return resp.arrayBuffer(); })
                        .then(function (ab) {
                            var u8 = new Uint8Array(ab);
                            Module.IDB._bin[identifier] = u8;
                            try {
                                Module['dbInstance'].transaction(['fileData'], 'readwrite')
                                    .objectStore('fileData').put({ id: keyStr, value: u8 });
                            } catch (e) { /* 升级失败无所谓,下次再来一遍 */ }
                            done(true, u8.length);
                        })
                        .catch(function (e) {
                            console.error('legacy base64 decode failed', keyStr, e);
                            done(false, 0, 'legacy decode failed');
                        });
                    return;
                }
                var u8 = (r.value instanceof Uint8Array) ? r.value : new Uint8Array(r.value);
                Module.IDB._bin[identifier] = u8;
                done(true, u8.length);   // 回调里只发这个小 JSON,不搬运数据本身
            };
            req.onerror = function(e) { console.error('ReadBinary error', e); done(false, 0, 'Error loading data'); };
        } catch (e) {
            console.error('ReadBinary exception', e);
            done(false, 0, 'ReadBinary exception: ' + e);
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
                    // 走到这条 = **旧的字符串通道**。大条目在这儿必然卡,
                    // 打出来才知道还有谁没迁到二进制通道。
                    var n = (result.value && result.value.length) || 0;
                    if (n > 131072) console.log('[CACHE] 读(字符串通道) ' + keyStr + ' ' + n + ' 字符 ← 这条会卡');
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
