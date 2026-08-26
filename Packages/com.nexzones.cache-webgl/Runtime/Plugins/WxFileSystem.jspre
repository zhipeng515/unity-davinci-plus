Module['WX'] = Module['WX'] || {};
    Module.WX.OpenDatabase = function (dbName, version, identifier, gameObjectName, callbackMethod) {
        var dbNameStr = UTF8ToString(dbName);
        var idInt = identifier;
        var goName = UTF8ToString(gameObjectName);
        var callback = UTF8ToString(callbackMethod);

        try {
            // 初始化一个数据库目录（文件夹）
            var dirPath = wx.env.USER_DATA_PATH + '/' + dbNameStr;
            wx.getFileSystemManager().accessSync(dirPath);
        } catch (e) {
            wx.getFileSystemManager().mkdirSync(dirPath); // 创建目录
        }

        Module['SendMessage'](
            goName,
            callback,
            JSON.stringify({ id: idInt, success: true, message: "Database opened (WX)" })
        );
    };

    Module.WX.WriteData = function (key, value, identifier, gameObjectName, callbackMethod) {
        var keyStr = UTF8ToString(key);
        var valueStr = UTF8ToString(value);
        var idInt = identifier;
        var goName = UTF8ToString(gameObjectName);
        var callback = UTF8ToString(callbackMethod);
        var filePath = wx.env.USER_DATA_PATH + '/' + keyStr;

        try {
            wx.getFileSystemManager().writeFileSync(filePath, valueStr); // 每个键单独存储为一个文件
            Module['SendMessage'](
                goName,
                callback,
                JSON.stringify({ id: idInt, success: true, message: "Data written successfully (WX)" })
            );
        } catch (e) {
            console.error("Error writing data in WX", e);
            Module['SendMessage'](
                goName,
                callback,
                JSON.stringify({ id: idInt, success: false, error: "Error writing data" })
            );
        }
    };

    // ── 二进制通道(与 IndexedDB 后端同一套语义)────────────────────
    // 原来这边也是拿 base64 字符串搬字节:writeFileSync(path, 字符串) /
    // readFileSync(path, 'utf8')。体积多 33%,而且两头各要一次全量编解码。
    // wx.getFileSystemManager 本就支持 ArrayBuffer,直接存原字节。
    //
    // 旧文件是 base64 文本,按二进制读出来是乱码且**看不出来**,所以新格式带
    // 4 字节魔数 'NXZB'。认不出就当未命中并删掉 —— 不做启发式猜测,也不留
    // 兼容层;上层会重新下载并按新格式写回。
    Module.WX._bin = Module.WX._bin || {};
    Module.WX._MAGIC = [0x4E, 0x58, 0x5A, 0x42]; // 'NXZB'

    Module.WX._takeBinary = function (identifier) {
        var v = Module.WX._bin[identifier];
        delete Module.WX._bin[identifier];
        return v || null;
    };

    Module.WX.WriteBinary = function (key, bytes, identifier, gameObjectName, callbackMethod) {
        var keyStr = UTF8ToString(key);
        var goName = UTF8ToString(gameObjectName);
        var callback = UTF8ToString(callbackMethod);
        var filePath = wx.env.USER_DATA_PATH + '/' + keyStr;
        var done = function (ok, err) {
            Module['SendMessage'](goName, callback,
                JSON.stringify({ id: identifier, success: ok, error: err || '' }));
        };
        try {
            var out = new Uint8Array(4 + bytes.length);
            out.set(Module.WX._MAGIC, 0);
            out.set(bytes, 4);
            wx.getFileSystemManager().writeFileSync(filePath, out.buffer);
            done(true);
        } catch (e) {
            // 同步抛也必回调,否则 C# 侧串行队列死等这一条。
            console.error('Error writing binary in WX', e);
            done(false, 'Error writing data');
        }
    };

    Module.WX.ReadBinary = function (key, identifier, gameObjectName, callbackMethod) {
        var keyStr = UTF8ToString(key);
        var goName = UTF8ToString(gameObjectName);
        var callback = UTF8ToString(callbackMethod);
        var filePath = wx.env.USER_DATA_PATH + '/' + keyStr;
        var done = function (ok, len, err) {
            Module['SendMessage'](goName, callback,
                JSON.stringify({ id: identifier, success: ok, binary: true, length: len || 0, error: err || '' }));
        };
        try {
            var fs = wx.getFileSystemManager();
            var u8 = new Uint8Array(fs.readFileSync(filePath)); // 不给 encoding 即返回 ArrayBuffer
            var m = Module.WX._MAGIC;
            if (u8.length < 4 || u8[0] !== m[0] || u8[1] !== m[1] || u8[2] !== m[2] || u8[3] !== m[3]) {
                // 没有魔数 = 旧的 base64 文本文件。同 IndexedDB 那边:**不能丢**,
                // 否则 FindCache 说有、LoadCache 给 null,上层永远卡占位图。
                // 就地解码并按新格式写回,旧文件随使用自然迁移完。
                try {
                    var text = fs.readFileSync(filePath, 'utf8');
                    var buf = new Uint8Array(wx.base64ToArrayBuffer(text));
                    Module.WX._bin[identifier] = buf;
                    var out = new Uint8Array(4 + buf.length);
                    out.set(m, 0); out.set(buf, 4);
                    try { fs.writeFileSync(filePath, out.buffer); } catch (e3) { /* 升级失败下次再来 */ }
                    done(true, buf.length);
                } catch (e2) {
                    console.error('legacy base64 decode failed (WX)', filePath, e2);
                    done(false, 0, 'legacy decode failed');
                }
                return;
            }
            Module.WX._bin[identifier] = u8.subarray(4);
            done(true, u8.length - 4);
        } catch (e) {
            done(false, 0, 'No data found');
        }
    };

    Module.WX.ReadData = function (key, identifier, gameObjectName, callbackMethod) {
        var keyStr = UTF8ToString(key);
        var idInt = identifier;
        var goName = UTF8ToString(gameObjectName);
        var callback = UTF8ToString(callbackMethod);
        var filePath = wx.env.USER_DATA_PATH + '/' + keyStr;

        try {
            var value = wx.getFileSystemManager().readFileSync(filePath, 'utf8');
            Module['SendMessage'](
                goName,
                callback,
                JSON.stringify({ id: idInt, success: true, message: value })
            );
        } catch (e) {
            console.error("Error reading data in WX", e);
            Module['SendMessage'](
                goName,
                callback,
                JSON.stringify({ id: idInt, success: false, error: "No data found" })
            );
        }
    };

    Module.WX.DeleteData = function (key, identifier, gameObjectName, callbackMethod) {
        var keyStr = UTF8ToString(key);
        var idInt = identifier;
        var goName = UTF8ToString(gameObjectName);
        var callback = UTF8ToString(callbackMethod);
        var filePath = wx.env.USER_DATA_PATH + '/' + keyStr;

        try {
            wx.getFileSystemManager().unlinkSync(filePath); // 删除对应文件
            Module['SendMessage'](
                goName,
                callback,
                JSON.stringify({ id: idInt, success: true, message: "Data deleted successfully (WX)" })
            );
        } catch (e) {
            console.error("Error deleting data in WX", e);
            Module['SendMessage'](
                goName,
                callback,
                JSON.stringify({ id: idInt, success: false, error: "No data found to delete" })
            );
        }
    };

    Module.WX.FindData = function (key, identifier, gameObjectName, callbackMethod) {
        var keyStr = UTF8ToString(key);
        var idInt = identifier;
        var goName = UTF8ToString(gameObjectName);
        var callback = UTF8ToString(callbackMethod);
        var filePath = wx.env.USER_DATA_PATH + '/' + keyStr;

        try {
            wx.getFileSystemManager().accessSync(filePath); // 检查文件是否存在
            Module['SendMessage'](
                goName,
                callback,
                JSON.stringify({ id: idInt, success: true, message: "Data exists (WX)" })
            );
        } catch (e) {
            Module['SendMessage'](
                goName,
                callback,
                JSON.stringify({ id: idInt, success: false, message: "No data found (WX)" })
            );
        }
    };

    Module.WX.SyncFileSystem = function () {
        // console.log("WX_SyncFileSystem called, no action required in WX environment.");
    };
