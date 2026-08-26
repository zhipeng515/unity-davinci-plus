mergeInto(LibraryManager.library, {
  NXZ_IsWxMiniGame: function() {
    return Module.WebFileSystem.isWxMiniGame();
  },
  // 打开数据库（根据平台判断调用不同的实现）
  NXZ_OpenDatabase: function (
    dbName,
    version,
    identifier,
    gameObjectName,
    callbackMethod
  ) {
    if (Module.WebFileSystem.isWxMiniGame()) {
      // 微信小程序实现
      return Module.WX.OpenDatabase(
        dbName,
        version,
        identifier,
        gameObjectName,
        callbackMethod
      );
    } else {
      // IndexedDB实现
      return Module.IDB.OpenDatabase(
        dbName,
        version,
        identifier,
        gameObjectName,
        callbackMethod
      );
    }
  },

  // 写入数据（根据平台判断调用不同的实现）
  NXZ_WriteData: function (key, value, identifier, gameObjectName, callbackMethod) {
    if (Module.WebFileSystem.isWxMiniGame()) {
      // 微信小程序实现
      return Module.WX.WriteData(
        key,
        value,
        identifier,
        gameObjectName,
        callbackMethod
      );
    } else {
      // IndexedDB实现
      return Module.IDB.WriteData(
        key,
        value,
        identifier,
        gameObjectName,
        callbackMethod
      );
    }
  },

  // 读取数据（根据平台判断调用不同的实现）
  NXZ_ReadData: function (key, identifier, gameObjectName, callbackMethod) {
    if (Module.WebFileSystem.isWxMiniGame()) {
      // 微信小程序实现
      return Module.WX.ReadData(key, identifier, gameObjectName, callbackMethod);
    } else {
      // IndexedDB实现
      return Module.IDB.ReadData(key, identifier, gameObjectName, callbackMethod);
    }
  },

  // ── 二进制通道(仅 IndexedDB)──────────────────────────────────
  // 微信小游戏后端没有对应实现,C# 侧据 NXZ_IsWxMiniGame 决定不走这条,
  // 所以这里不需要 WX 分支。堆访问放在 jslib 里做:HEAPU8 在这儿才在作用域内。
  NXZ_WriteBinary: function (key, dataPtr, offset, length, identifier, gameObjectName, callbackMethod) {
    // slice 而非 subarray:structured clone 要独立缓冲区,而且 WASM 堆可能增长搬家。
    var bytes = HEAPU8.slice(dataPtr + offset, dataPtr + offset + length);
    if (Module.WebFileSystem.isWxMiniGame()) {
      return Module.WX.WriteBinary(key, bytes, identifier, gameObjectName, callbackMethod);
    }
    return Module.IDB.WriteBinary(key, bytes, identifier, gameObjectName, callbackMethod);
  },

  NXZ_ReadBinary: function (key, identifier, gameObjectName, callbackMethod) {
    if (Module.WebFileSystem.isWxMiniGame()) {
      return Module.WX.ReadBinary(key, identifier, gameObjectName, callbackMethod);
    }
    return Module.IDB.ReadBinary(key, identifier, gameObjectName, callbackMethod);
  },

  // 取走上一次 ReadBinary 暂存的字节。这是读路径上**唯一**一次拷贝。
  NXZ_TakeBinary: function (identifier, destPtr, cap) {
    var u8 = Module.WebFileSystem.isWxMiniGame()
      ? Module.WX._takeBinary(identifier)
      : Module.IDB._takeBinary(identifier);
    if (!u8) return 0;
    var n = Math.min(cap, u8.length);
    HEAPU8.set(u8.subarray(0, n), destPtr);
    return n;
  },

  // 删除数据（根据平台判断调用不同的实现）
  NXZ_DeleteData: function (key, identifier, gameObjectName, callbackMethod) {
    if (Module.WebFileSystem.isWxMiniGame()) {
      // 微信小程序实现
      return Module.WX.DeleteData(key, identifier, gameObjectName, callbackMethod);
    } else {
      // IndexedDB实现
      return Module.IDB.DeleteData(key, identifier, gameObjectName, callbackMethod);
    }
  },

  // 查找数据（根据平台判断调用不同的实现）
  NXZ_FindData: function (key, identifier, gameObjectName, callbackMethod) {
    if (Module.WebFileSystem.isWxMiniGame()) {
      // 微信小程序实现
      return Module.WX.FindData(key, identifier, gameObjectName, callbackMethod);
    } else {
      // IndexedDB实现
      return Module.IDB.FindData(key, identifier, gameObjectName, callbackMethod);
    }
  },

  // 同步数据库（根据平台判断调用不同的实现）
  NXZ_SyncFileSystem: function () {
    if (Module.WebFileSystem.isWxMiniGame()) {
      // 微信小程序实现
      Module.WX.SyncFileSystem();
    } else {
      // IndexedDB实现
      Module.IDB.SyncFileSystem();
    }
  },
});
