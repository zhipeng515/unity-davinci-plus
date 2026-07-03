Module['WebFileSystem'] = Module['WebFileSystem'] || {};

// 获取当前全屏元素
Module.WebFileSystem.isWxMiniGame = function () {
    return typeof wx !== "undefined" && wx.getFileSystemManager !== undefined;
};
