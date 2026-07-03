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
