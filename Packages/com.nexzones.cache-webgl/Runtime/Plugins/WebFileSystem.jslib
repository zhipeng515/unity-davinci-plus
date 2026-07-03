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
