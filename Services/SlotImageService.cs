using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EFT.Utilities;
using Newtonsoft.Json;
using SPT.Common.Http;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DefinitiveWeaponVariants.Client.Services
{
    // Slot registration follows WTT-CommonLib's MIT-licensed approach (GrooveypenguinX).
    // The DWV transport, startup deadline and resource ownership are handled here.
    internal sealed class SlotImageService : MonoBehaviour
    {
        internal const string Endpoint = "/definitiveweaponvariants/slot-images";
        private readonly Dictionary<string, Sprite> _ownedSprites = new Dictionary<string, Sprite>();

        private IEnumerator Start()
        {
            Task<string> request;
            try
            {
                request = RequestHandler.GetJsonAsync(Endpoint);
                if (request == null) throw new InvalidOperationException("SPT returned no slot image request.");
            }
            catch (Exception exception)
            {
                Plugin.LogSource.LogWarning($"Slot pictures unavailable: {exception.Message}");
                yield break;
            }

            ObserveFault(request);
            var deadline = Time.realtimeSinceStartup + 15f;
            while (!request.IsCompleted && Time.realtimeSinceStartup < deadline)
                yield return null;

            if (!request.IsCompleted || request.IsCanceled || request.IsFaulted)
            {
                var reason = request.Exception?.GetBaseException().Message
                    ?? (request.IsCanceled ? "Request cancelled." : "Startup request timed out.");
                Plugin.LogSource.LogWarning($"Slot pictures unavailable: {reason}");
                yield break;
            }

            Dictionary<string, string> images;
            try
            {
                images = JsonConvert.DeserializeObject<Dictionary<string, string>>(request.GetAwaiter().GetResult())
                    ?? throw new InvalidOperationException("Server returned no slot image map.");
            }
            catch (Exception exception)
            {
                Plugin.LogSource.LogWarning($"Invalid slot picture response: {exception.Message}");
                yield break;
            }

            // Coroutines resume on Unity's main thread; all texture/cache work stays here.
            foreach (var image in images)
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(image.Key) || image.Key.IndexOfAny(new[] { '/', '\\' }) >= 0)
                        throw new InvalidOperationException("Invalid slot name.");
                    Register(image.Key, image.Value);
                }
                catch (Exception exception)
                {
                    Plugin.LogSource.LogWarning($"Could not load slot picture '{image.Key}': {exception.Message}");
                }
            }

            Plugin.LogSource.LogInfo($"Registered {_ownedSprites.Count} DWV slot picture(s).");
        }

        private void Register(string slotName, string encodedImage)
        {
            var key = "Slots/" + slotName;
            if (ResourcesCache._storage.ContainsKey(key)) return;

            var bytes = Convert.FromBase64String(encodedImage);
            if (bytes.Length == 0) throw new InvalidOperationException("Image is empty.");

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Sprite sprite = null;
            var registered = false;
            try
            {
                if (!ImageConversion.LoadImage(texture, bytes, true))
                    throw new InvalidOperationException("Unity could not decode the PNG/JPEG image.");

                texture.name = "DWV " + key;
                texture.filterMode = FilterMode.Bilinear;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
                sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                    new Vector2(0.5f, 0.5f), 100f);
                sprite.name = texture.name;
                sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
                ResourcesCache._storage.Add(key, sprite);
                _ownedSprites.Add(key, sprite);
                registered = true;
            }
            finally
            {
                if (!registered)
                {
                    if (sprite != null) Object.Destroy(sprite);
                    Object.Destroy(texture);
                }
            }
        }

        private static void ObserveFault(Task task)
        {
            // The request can finish after a timeout or after this component is destroyed.
            _ = task.ContinueWith(completed => { _ = completed.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private void OnDestroy()
        {
            StopAllCoroutines();
            foreach (var entry in _ownedSprites)
            {
                if (ResourcesCache._storage.TryGetValue(entry.Key, out var cached)
                    && ReferenceEquals(cached, entry.Value))
                    ResourcesCache._storage.Remove(entry.Key);

                if (entry.Value != null)
                {
                    Object.Destroy(entry.Value.texture);
                    Object.Destroy(entry.Value);
                }
            }
            _ownedSprites.Clear();
        }
    }
}
