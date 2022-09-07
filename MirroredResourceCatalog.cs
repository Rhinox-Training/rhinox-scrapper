using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
        
        
        public IEnumerator LoadAsset<T>(object key, Action<T> onCompleted, Action<object> onFailed = null, T fallbackObject = default(T))
        {
            yield return _bundleMirror.TryLoadBundlesFor(key);
            
            T loadedAsset = default(T);
            if (AddressableResourceExists<T>(key))
            {
                var loadAsset = Addressables.LoadAssetAsync<T>(key);

                yield return loadAsset;
                if(loadAsset.Status != AsyncOperationStatus.Succeeded)
                    PLog.Error<ScrapperLogger>($"Load of object at path {key} failed: {loadAsset.Status}");
                loadedAsset = loadAsset.Result;
            }
            else
            {
                PLog.Warn<ScrapperLogger>($"Asset at {key} is missing, setting {fallbackObject} as replacement");
                loadedAsset = fallbackObject;
            }

            PLog.Info<ScrapperLogger>($"Loaded '{loadedAsset}' of type '{loadedAsset?.GetType().Name ?? "None"}' from path {key}");
            onCompleted?.Invoke(loadedAsset);

            Addressables.Release(loadedAsset);
        }
        
        
        private bool AddressableResourceExists<T>(object key)
        {
            foreach (var l in Addressables.ResourceLocators)
            {
                if (l.Locate(key, typeof(T), out IList<IResourceLocation> locs))
                    return true;
            }
            return false;
        }

        public IEnumerator PreloadAllAssetsAsync(params object[] keys)
        {
            float keyLength = keys.Length;
            if (keyLength == 0)
            {
                yield break;
            }

            for (int i = 0; i < keys.Length; ++i)
            {
                var key = keys[i];

                float keySectionSize = 1.0f / Math.Max((keyLength - 1), 1);
                float offset = i / Math.Max((keyLength - 1), 1);
                
                
                var enumeratedLoad = _bundleMirror.TryLoadBundlesFor(key);
                yield return offset + (enumeratedLoad.Current * keySectionSize * 0.5f);
                while (enumeratedLoad.MoveNext())
                    yield return offset + (enumeratedLoad.Current * keySectionSize * 0.5f);
                yield return offset + (keySectionSize * 0.5f);

                var addrDepLoader = new AddressableDependenciesDownloader();
                var depLoaderEnumerator = addrDepLoader.DownloadAsync(key);
                yield return offset + ((0.5f + (depLoaderEnumerator.Current * 0.5f)) * keySectionSize);
                while (depLoaderEnumerator.MoveNext())
                    yield return offset + ((0.5f + (depLoaderEnumerator.Current * 0.5f)) * keySectionSize);
                
                yield return offset + (keySectionSize * 0.999f);
            }

            yield return 1.0f;
        }

        public void ClearCache()
        {
            // TODO: delete cache of project
            // TODO: delete guid files in AppData
            
            
            _bundleMirror.ClearCache();
        }
    }
}