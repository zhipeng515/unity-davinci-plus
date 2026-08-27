// 浏览器原生异步解码 + 解码期降采样。
//
// 【为什么】Texture2D.LoadImage 是**同步**的:整张图在 WASM 主线程上展开,
// 摊不开也打不断。2026-08-26 实测:一张 8000×2000(1600 万像素)的 PNG 解码
// 1846ms —— 这期间引擎完全停摆(画面冻住、视频卡住;声音不卡是因为 Web Audio
// 在独立线程)。
//
// 【它改变什么、不改变什么】
//   · 解码本身**不会变快** —— 同样的像素、同样的工作量,只是挪到 worker 线程。
//   · 主线程不再被冻住 —— 代价还在,但不再挡在所有人前面。
//   · 真正省下来的是**降采样**:createImageBitmap 支持在解码过程中就缩到目标
//     尺寸,8000×2000 直接解成实际需要的大小,那 64MB 的中间 RGBA 缓冲和
//     64MB 的 GPU 上传根本不会发生。
//
// 【零回读是关键】拿到 ImageBitmap 之后必须**直接 texImage2D 给 GL**。
// 走 getImageData 把像素读回 WASM 堆再 LoadRawTextureData,会多一次全量回读
// 加一次拷贝 —— 那样比现在还慢,收益全赔进去。
var NXZImageDecode = {
  $NXZID: {
    bitmaps: {},
    send: function (goName, cb, payload) {
      Module['SendMessage'](goName, cb, JSON.stringify(payload));
    },
  },

  // 解码。成功后把 ImageBitmap 暂存,只回一个带宽高的小 JSON ——
  // C# 拿到宽高才能建出正确尺寸的 Texture2D,然后再来取。
  NXZ_DecodeImageAsync: function (dataPtr, len, maxEdge, identifier, goNamePtr, cbPtr) {
    var goName = UTF8ToString(goNamePtr);
    var cb = UTF8ToString(cbPtr);
    var fail = function (why) {
      NXZID.send(goName, cb, { id: identifier, success: false, error: why || '' });
    };
    try {
      // slice:Blob 需要独立缓冲区,而且 WASM 堆可能增长搬家。
      var bytes = HEAPU8.slice(dataPtr, dataPtr + len);
      var blob = new Blob([bytes]);
      // 先只读尺寸,决定要不要降采样。createImageBitmap 解两次听着浪费,
      // 但第一次是浏览器内部的元数据解析,不是全量解码。
      createImageBitmap(blob)
        .then(function (probe) {
          var w = probe.width;
          var h = probe.height;
          var longest = Math.max(w, h);
          if (maxEdge > 0 && longest > maxEdge) {
            var scale = maxEdge / longest;
            var tw = Math.max(1, Math.round(w * scale));
            var th = Math.max(1, Math.round(h * scale));
            probe.close();
            return createImageBitmap(blob, {
              resizeWidth: tw,
              resizeHeight: th,
              resizeQuality: 'high',
              // Unity 纹理原点在左下,ImageBitmap 在左上。用 imageOrientation
              // 让浏览器翻,而不是改 UNPACK_FLIP_Y_WEBGL —— 那是全局 GL 状态,
              // 动它等于在 Unity 背后改它的渲染状态。
              imageOrientation: 'flipY',
            });
          }
          probe.close();
          return createImageBitmap(blob, { imageOrientation: 'flipY' });
        })
        .then(function (bitmap) {
          NXZID.bitmaps[identifier] = bitmap;
          NXZID.send(goName, cb, {
            id: identifier,
            success: true,
            width: bitmap.width,
            height: bitmap.height,
          });
        })
        .catch(function (e) {
          console.warn('[NXZ] createImageBitmap failed, falling back', e);
          fail(String(e));
        });
    } catch (e) {
      // 同步抛(Blob 建不出来等)也必须回调,否则 C# 侧永远等不到,
      // 那张图卡在占位状态 —— 今天已经被这类"永挂"咬过一次。
      console.warn('[NXZ] decode threw', e);
      fail(String(e));
    }
  },

  // 把暂存的 ImageBitmap 上传到 Unity 纹理的原生 GL 句柄。返回 1 成功。
  NXZ_UploadDecoded: function (identifier, texHandle, genMips) {
    var bitmap = NXZID.bitmaps[identifier];
    if (!bitmap) return 0;
    try {
      var tex = GL.textures[texHandle];
      if (!tex) return 0;
      // 🔴 存回原来的绑定。在 Unity 背后绑纹理会污染它的纹理单元缓存,
      // 表现成"别的地方随机贴错图",而且极难查。
      var prev = GLctx.getParameter(GLctx.TEXTURE_BINDING_2D);
      GLctx.bindTexture(GLctx.TEXTURE_2D, tex);
      GLctx.texImage2D(
        GLctx.TEXTURE_2D, 0, GLctx.RGBA, GLctx.RGBA, GLctx.UNSIGNED_BYTE, bitmap
      );
      if (genMips) GLctx.generateMipmap(GLctx.TEXTURE_2D);
      GLctx.bindTexture(GLctx.TEXTURE_2D, prev);
      return 1;
    } catch (e) {
      console.warn('[NXZ] texImage2D from ImageBitmap failed', e);
      return 0;
    }
  },

  NXZ_ReleaseDecoded: function (identifier) {
    var bitmap = NXZID.bitmaps[identifier];
    if (bitmap) {
      try { bitmap.close(); } catch (e) { /* 已关就算了 */ }
      delete NXZID.bitmaps[identifier];
    }
  },
};

autoAddDeps(NXZImageDecode, '$NXZID');
mergeInto(LibraryManager.library, NXZImageDecode);
