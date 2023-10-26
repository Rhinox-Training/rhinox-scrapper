using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Rhinox.Lightspeed;
using Rhinox.Perceptor;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace Rhinox.Scrapper
{
    public class MirroredResourceCatalog
    {
        private Dictionary<string, AssetBundleInformation> _assetInfoByAddressAssetGuid;

        private string _locatorId;
        private readonly IResourceLocator _locator;
        public IResourceLocator Locator => _locator;
        private readonly string _catalogHash;
        private readonly AssetBundleMirror _bundleMirror;
        
        private class AssetBundleInformation
        {
            public string BundleName;
            public string Hash;
            public string RemoteBundleSubpath;
        }

        public MirroredResourceCatalog(IResourceLocator locator, MirrorDownloader downloader)
        {
            if (locator == null) throw new ArgumentNullException(nameof(locator));

            _bundleMirror = new AssetBundleMirror(locator, downloader);
            
            _locator = locator;
            _catalogHash = AddressableUtility.GetCatalogHashName(locator.LocatorId);
        }
        
        public void Initialize()
        {
            // TODO: setup bundle cache in _bundleMirror
            _bundleMirror.InitializeBundleInfo();
        }
        
        public async Task LoadAsset<T>(object key, Action<T> onCompleted, T fallbackObject = default(T))
            where T : class
        {
            var task =  _bundleMirror.TryLoadBundlesFor(key, null);
            await task;
            
            T loadedAsset = default(T);
            if (AddressableResourceExists<T>(key))
            {
                //// Due to preload -> all dependencies are already loaded
                // using (new TraceTimer($"LoadAsset-AddressableDependenciesDownloader '{key}'"))
                // {
                //     var addrDepLoader = new AddressableDependenciesDownloader();
                //     await addrDepLoader.DownloadAsync(key, null);
                // }
                
                using (new TraceTimer($"LoadAsset-LoadAssetAsync '{key}'"))
                {
                    var handle = Addressables.LoadAssetAsync<T>(key);
                    await handle.Task;

                    if (handle.Status == AsyncOperationStatus.Succeeded)
                    {
                        loadedAsset = handle.Result;
                    }
                    else
                    {
                        loadedAsset = fallbackObject;
                        PLog.Error<ScrapperLogger>($"Load of object at path {key} failed: {handle.Status}");
                    }
                }
            }
            else
            {
                PLog.Warn<ScrapperLogger>($"Asset at {key} is missing, setting {fallbackObject} as replacement");
                loadedAsset = fallbackObject;
            }

            PLog.Info<ScrapperLogger>($"Loaded '{loadedAsset}' of type '{loadedAsset?.GetType().Name ?? "None"}' from path {key}");
            onCompleted?.Invoke(loadedAsset);

            if (loadedAsset != null && loadedAsset != fallbackObject)
                Addressables.Release(loadedAsset);
        }
        
        
        public bool AddressableResourceExists<T>(object key)
        {
            return _locator.Locate(key, typeof(T), out IList<IResourceLocation> locs);
        }

        public long GetTotalByteSize(params object[] keys)
        {
            long totalBytes = 0;
            for (int i = 0; i < keys.Length; ++i)
            {
                // TODO: Overlap between keys is possible
                var key = keys[i];
                long byteCountForKey = _bundleMirror.GetByteSizeFor(key);
                totalBytes += byteCountForKey;
            }

            return totalBytes;
        }

        public async Task PreloadAllAssetsAsync(IProgress<ProgressBytes> progressHandler, CancellationToken token, params object[] keys)
        {
            float keyLength = keys.Length;
            if (keyLength == 0)
                return;

            long totalBytes = GetTotalByteSize();

            float progress = 0.0f;
            for (int i = 0; i < keys.Length; ++i)
            {
                var key = keys[i];

                float keyPartDenominator = Math.Max((keyLength - 1), 1);
                float keySectionSize = 1.0f / keyPartDenominator;
                
                float offset = i / keyPartDenominator;
                float initialSectionSize = keySectionSize;
                
                // Initial section
                progress = offset;
                var subHandler = ProgressHelper.Pipe(progressHandler, totalBytes, progress, initialSectionSize);
                await _bundleMirror.TryLoadBundlesFor(key, subHandler);
                
                // Make sure it doesn't reach '1' yet
                progress = offset + (keySectionSize * 0.999f);
                progressHandler.Report(new ProgressBytes(progress, totalBytes));

                if (token.IsCancellationRequested)
                    return;
            }

            progressHandler.Report(new ProgressBytes(1f, totalBytes));
        }

        public void ClearCache()
        {
            _bundleMirror.ClearCache();
        }
    }
}