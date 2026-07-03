# com.nexzones.cache-core

URL→local cache abstraction with expiry. Ships the file-system backend
(desktop/mobile) and the platform factory. See the
[repo README](../../README.md) for the full picture.

```csharp
using Nexzones.Cache;
CacheManager.Init("my-app"); // sets BaseCacheManager.instance

BaseCacheManager.instance.FindCache(url, (hit, cacheFile) => { ... });
BaseCacheManager.instance.SaveCache(url, bytes, expires: 3600);
```
