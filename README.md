# Davinci+ — Unity runtime image loading with a real cache story

A maintained, **drop-in compatible** fork of [shamsdev/davinci](https://github.com/shamsdev/davinci)
plus the cache infrastructure it always needed. Battle-tested in
[Nexzones](https://www.nexzones.com) (interactive-video engine shipping on
Web / Windows / macOS / Linux / WeChat minigame).

```
network ──► disk cache ──► in-memory LRU (decoded Texture2D) ──► same-frame display
             │
             ├─ file system            (desktop / mobile)
             └─ WebFileSystem          (web: one API over two backends)
                  ├─ browser IndexedDB
                  └─ WeChat minigame FS
```

## Why

- `UnityWebRequestTexture` has no cache.
- Even with a disk cache, every display pays async read + full PNG decode +
  coroutine hops — images pop in frames late. The in-memory LRU serves
  decoded textures **same-frame**.
- WeChat minigames get the exact same API (runtime-detected backend).

### Why direct IndexedDB instead of Unity's idbfs?

idbfs (what backs `Application.persistentDataPath` on WebGL) is a
**memory filesystem mirrored to IndexedDB by explicit `syncfs()`** — great
for small, low-frequency data (saves, prefs), wrong for a media cache:

| | idbfs | direct IndexedDB (this package) |
|---|---|---|
| Durability | writes sit in memory until `syncfs` flushes; crash/close before flush loses data, and the flush timing is yours to get right | every write is an IndexedDB transaction — committed = persisted |
| WASM heap | **double copy**: MEMFS keeps the whole cache in the heap *and* IndexedDB — tens of MB of media eat tens of MB of heap | blobs live in IndexedDB only; heap cost ≈ 0 |
| Sync cost | `syncfs` mirrors the whole FS; grows with cache size | touches only the entries you use |
| Startup | mounting loads the mirror into memory — boot slows as the cache grows | no boot cost |
| Multi-tab | last flush wins | transactional isolation |
| API | transparent `System.IO` | async callbacks (hence the ordered op queue in this package) |

## Packages

| Package | What | Install when |
|---|---|---|
| `com.nexzones.cache-core` | URL→cache abstraction + file-system backend + platform factory | always |
| `com.nexzones.cache-webgl` | IndexedDB / WeChat FS backend (self-registers) | building for WebGL / WeChat |
| `com.nexzones.davinci` | the image loader | always |

Install via UPM git URL (Package Manager → Add package from git URL):

```
https://github.com/zhipeng515/unity-davinci-plus.git?path=Packages/com.nexzones.cache-core
https://github.com/zhipeng515/unity-davinci-plus.git?path=Packages/com.nexzones.cache-webgl
https://github.com/zhipeng515/unity-davinci-plus.git?path=Packages/com.nexzones.davinci
```

Multi-platform projects install all three: the WebGL package compiles only
for WebGL targets (`defineConstraints`) and self-registers its backend at
runtime — no conditional code on your side.

## Quick start

```csharp
using Nexzones.Cache;

// once at boot (namespace isolates multi-tenant caches)
CacheManager.Init("my-app");

// the familiar Davinci fluent API — unchanged from upstream
Davinci.get()
    .load("https://example.com/cover.png")
    .into(myImage)               // Image / RawImage / SpriteRenderer / Renderer
    .setFadeTime(0.2f)
    .withLoadedAction(tex => Debug.Log("loaded"))
    .start();

// new: warm the in-memory cache ahead of time (e.g. at scene start)
Davinci.get().setPreload(true).load(url).start();

// optional: raise the decoded-texture cache budget for image-heavy apps.
// Default is 48 MB; each cached texture is counted with its mip chain (w*h*4 * 4/3).
Davinci.memoryBudget = 64L * 1024 * 1024; // e.g. bump to 64 MB
```

## What the fork adds over upstream

- **In-memory LRU texture cache** — decoded `Texture2D` served same-frame on
  hit; eviction drops references only (display-safe), reclaimed by Unity's
  `Resources.UnloadUnusedAssets`.
- **Preload API** — decode into the cache before you need it.
- **Loaded callback fires at texture-assignment time** (before the fade), so
  activation logic doesn't wait out the animation invisibly.
- **WebGL that works** — direct IndexedDB (not idbfs), ordered async op queue,
  WeChat minigame FS behind the same API.
- Expiry-aware disk cache, zero external dependencies (no Json.NET).

## Notes

- Texture ownership: textures live in the cache. Don't `Destroy(sprite.texture)`
  on textures Davinci gave you.
- Browser + desktop paths are production-tested; the WeChat path ships from the
  same production codebase — issues welcome.
- Predecessor: [IndexedDBHelper](https://github.com/zhipeng515/IndexedDBHelper)
  grew into `com.nexzones.cache-webgl`.

MIT — see [LICENSE](LICENSE) (upstream Davinci attribution preserved).
