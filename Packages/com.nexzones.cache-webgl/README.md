# com.nexzones.cache-webgl

WebGL backend for `com.nexzones.cache-core`: **WebFileSystem** — one async
file API over two runtime-detected backends:

- browser **IndexedDB** (direct; not idbfs — no SyncFileSystem timing issues)
- **WeChat minigame** file system (`wx.*`)

Ordered operation queue guarantees callback order. Self-registers into
`CacheManager` at load — installing the package is all you do.

jslib exports are prefixed `NXZ_` to avoid symbol collisions with host
projects.
