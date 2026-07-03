using Nexzones.Cache;
using UnityEngine;
using UnityEngine.UI;

// Minimal end-to-end: init cache, load an image with fade, preload another.
public class BasicUsage : MonoBehaviour
{
    public Image target;
    public string url = "https://picsum.photos/512";

    void Start()
    {
        CacheManager.Init("davinci-sample");

        Davinci.get()
            .load(url)
            .into(target)
            .setFadeTime(0.2f)
            .withLoadedAction(tex => Debug.Log($"loaded {tex.width}x{tex.height}"))
            .start();

        // Warm something you'll need soon:
        Davinci.get().setPreload(true).load(url + "?next").start();
    }
}
