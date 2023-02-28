using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Rhinox.Lightspeed;
using Rhinox.Lightspeed.IO;
using Rhinox.Perceptor;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace Rhinox.Scrapper
{
    public class AssetBundleInformation
    {
        public string BundleName;
        public string Hash;
        public string RemoteBundleSubpath;
        public IResourceLocation Location;
        public bool IsCached { get; private set; } 

        public void MarkCached(bool state)
        {
            if (IsCached == state)
                return;
            IsCached = state;
        }
    }
    
    public class AssetBundleMirror
    {
        private struct AssetBundleLocation
        {
            public IResourceLocation Location;
            public AssetBundleRequestOptions Request;
        }
        
        private Dictionary<string, AssetBundleInformation> _bundleInfoByBundleName;
        private readonly IResourceLocator _locator;
        private readonly MirrorDownloader _downloader;
        private bool _initialized;
        public bool Initialized => _initialized;

        public IReadOnlyCollection<AssetBundleInformation> CachedBundles => _bundleInfoByBundleName != null
            ? (IReadOnlyCollection<AssetBundleInformation>) _bundleInfoByBundleName.Values
            : Array.Empty<AssetBundleInformation>();

        public AssetBundleMirror(IResourceLocator locator, MirrorDownloader downloader)
        {
            _locator = locator;
            _downloader = downloader;

            _bundleInfoByBundleName = new Dictionary<string, AssetBundleInformation>();
        }
        
        public void InitializeBundleInfo()
        {
            if (_initialized)
                return;
            
            _bundleInfoByBundleName.Clear();
            
            foreach (var bundle in FindAllAssetBundles())
            {
                if (_bundleInfoByBundleName.ContainsKey(bundle.Request.BundleName))
                {
                    PLog.Trace<ScrapperLogger>($"Already cached bundle with name '{bundle.Request.BundleName}', skipping...");
                    continue;
                }
                
                // Create entry
                var info = Create(bundle);
                _bundleInfoByBundleName.Add(info.BundleName, info);
            }

            _initialized = true;
        }

        private static AssetBundleInformation Create(AssetBundleLocation bundle)
        {
            var info = new AssetBundleInformation()
            {
                Location = bundle.Location,
                BundleName = bundle.Request.BundleName,
                Hash = bundle.Request.Hash,
                RemoteBundleSubpath = bundle.Location.PrimaryKey.Replace($"_{bundle.Request.Hash}", "")
            };
            return info;
        }

        public bool HasCachedBundle(string bundleName)
        {
            if (_bundleInfoByBundleName == null || !_bundleInfoByBundleName.ContainsKey(bundleName))
                return false;
            
            var info = _bundleInfoByBundleName[bundleName];
            if (info == null)
                return false;
            
            return _downloader.TargetExists(info.RemoteBundleSubpath);
        }

        public bool InvalidateBundle(string bundleName)
        {
            if (_bundleInfoByBundleName == null || !_bundleInfoByBundleName.ContainsKey(bundleName))
                return false;

            var info = _bundleInfoByBundleName[bundleName];
            if (info == null)
                return false;

            if (!_downloader.ClearTargetAsset(info.RemoteBundleSubpath))
                return false;

            info.MarkCached(false);
            return true;
        }

        public long GetByteSizeFor(object key)
        {
            var bundles = FindAssetBundlesFor(key).ToArray();
            long byteSize = 0;
            foreach (var bundle in bundles)
                byteSize += bundle.Request.BundleSize;
            return byteSize;
        }

        public IEnumerator<float> TryLoadBundlesFor(object key)
        {
            var bundles = FindAssetBundlesFor(key).ToArray();
            float bundleCountDenominator = (float) Mathf.Max((bundles.Length - 1), 1);

            const float firstPart = .5f;
            const float secondPart = 1f - firstPart;
            
            for (int i = 0; i < bundles.Length; ++i)
            {
                var bundle = bundles[i];
                if (!_bundleInfoByBundleName.ContainsKey(bundle.Request.BundleName))
                {
                    PLog.Warn<ScrapperLogger>($"Bundle info missing for bundle? Creating anyway");
                    var info = Create(bundle);
                    _bundleInfoByBundleName.Add(info.BundleName, info);
                }

                var bundleInfo = _bundleInfoByBundleName[bundle.Request.BundleName];
                AddressableUtility.FixupMirrorProgress(ref bundleInfo.Location);

                float progress = i / bundleCountDenominator;
                yield return Mathf.Min((firstPart * .1f) + progress * (firstPart * .9f), firstPart);
            }

            for (int i = 0; i < bundles.Length; ++i)
            {
                var bundle = bundles[i];
                var info = _bundleInfoByBundleName[bundle.Request.BundleName];
                
                if (info.IsCached)
                {
                    PLog.Trace<ScrapperLogger>($"Already downloaded bundle with name '{info.BundleName}', skipping...");
                    continue;
                }

                float chunkSize = 1.0f / bundleCountDenominator;
                float progress = i / bundleCountDenominator;

                var downloadHandler = _downloader.DownloadAssetAsync(info.RemoteBundleSubpath, 180);
                yield return firstPart + ((progress + (downloadHandler.Current * chunkSize)) * secondPart);
                while (downloadHandler.MoveNext())
                    yield return firstPart + ((progress + (downloadHandler.Current * chunkSize)) * secondPart);

                info.MarkCached(true);
            }

            yield return 1.0f;
        }

        public void ClearCache()
        {
            if (_bundleInfoByBundleName == null)
                return;

            string rootPath = Path.GetFullPath(Path.Combine(Application.persistentDataPath,
                $"../../Unity/{Application.companyName}_{Application.productName}"));
            foreach (var info in _bundleInfoByBundleName.Values.ToArray())
            {
                // Release addressable handles
                var handles = AddressableUtility.GetAsyncOperationHandles(info.Location);
                if (!handles.IsNullOrEmpty())
                    AddressableUtility.ReleaseAsyncOperationHandles(handles);
                
                string cachedFolderPath = Path.Combine(rootPath, info.BundleName);
                FileHelper.DeleteDirectoryIfExists(cachedFolderPath);
                
                if (!_downloader.ClearTargetAsset(info.RemoteBundleSubpath))
                    PLog.Error<ScrapperLogger>($"Failed to clear '{info.RemoteBundleSubpath}' at {_downloader.LocalPath}");
                else
                {
                    info.MarkCached(false);
                }
            }
        }
        
        
        // =============================================================================================================
        // Helpers
        private IEnumerable<AssetBundleLocation> FindAllAssetBundles()
        {
            foreach (var key in _locator.Keys)
            {
                if (key is string strKey)
                {
                    // Skip asset guids
                    if (!System.Guid.TryParse(strKey, out System.Guid g))
                        continue;

                    foreach (var assetBundle in FindAssetBundlesFor(strKey))
                        yield return assetBundle;
                }
            }
        }

        private IEnumerable<AssetBundleLocation> FindAssetBundlesFor(object key)
        {
            if (!_locator.Locate(key, typeof(object), out var locations))
                yield break;

            var resourceLocations = new Queue<IResourceLocation>();
            foreach (var location in locations)
                resourceLocations.Enqueue(location);
            
            while (resourceLocations.Count > 0)
            {
                var location = resourceLocations.Dequeue();
                if (location.Data != null && location.Data is AssetBundleRequestOptions request)
                {
                    yield return new AssetBundleLocation()
                    {
                        Location = location,
                        Request = request
                    };
                }

                if (location.HasDependencies)
                {
                    foreach (var dep in location.Dependencies)
                        resourceLocations.Enqueue(dep);
                }
            }
        }
    }
}