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
    }
    
    public class AssetBundleMirror
    {
        private Dictionary<string, AssetBundleInformation> _bundleInfoByBundleName;
        private readonly IResourceLocator _locator;
        private readonly MirrorDownloader _downloader;

        public IReadOnlyCollection<AssetBundleInformation> CachedBundles => _bundleInfoByBundleName != null
            ? (IReadOnlyCollection<AssetBundleInformation>) _bundleInfoByBundleName.Values
            : Array.Empty<AssetBundleInformation>();

        public AssetBundleMirror(IResourceLocator locator, MirrorDownloader downloader)
        {
            _locator = locator;
            _downloader = downloader;

            _bundleInfoByBundleName = new Dictionary<string, AssetBundleInformation>();
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

            _bundleInfoByBundleName.Remove(bundleName);
            return true;
        }

        public IEnumerator<float> TryLoadBundlesFor(object key)
        {
            if (!_locator.Locate(key, typeof(object), out var locations)) 
                yield break;
            
            var resourceLocations = new Queue<IResourceLocation>();
            var infos = new List<AssetBundleInformation>();
                
            foreach (var location in locations)
                resourceLocations.Enqueue(location);
            yield return 0.0f;
            int count = 0;
            while (resourceLocations.Count > 0)
            {
                var location = resourceLocations.Dequeue();
                if (location.Data != null && location.Data is AssetBundleRequestOptions request)
                {
                    ++count;
                    var info = new AssetBundleInformation()
                    {
                        Location = location,
                        BundleName = request.BundleName,
                        Hash = request.Hash,
                        RemoteBundleSubpath = location.PrimaryKey.Replace($"_{request.Hash}", "")
                    };
                    infos.Add(info);

                    // TODO: Hack for loading progress
                    var type = typeof(ContentCatalogData).GetNestedType("CompactLocation", BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic);
                    if (type != null)
                    {
                        var dataField = type.GetField("m_Data", BindingFlags.Instance | BindingFlags.NonPublic);
                        if (dataField != null && type.IsInstanceOfType(location))
                        {
                            var virt = AddressableUtility.MakeVirtual(request);
                            dataField.SetValue(location, virt);
                        }
                    }

                    float progress = (float) count / Math.Max((locations.Count - 1), 1);
                    yield return Mathf.Min(0.05f + progress * 0.4f, 0.5f);
                }

                if (location.HasDependencies)
                {
                    foreach (var dep in location.Dependencies)
                        resourceLocations.Enqueue(dep);
                }
            }

            for (int i = 0; i < infos.Count; ++i)
            {
                var info = infos[i];
                if (_bundleInfoByBundleName.ContainsKey(info.BundleName))
                {
                    PLog.Trace<ScrapperLogger>(
                        $"Already downloaded bundle with name '{info.BundleName}', skipping...");
                    continue;
                }
                float chunkSize = 1.0f / Math.Max((infos.Count - 1), 1);
                float progress = (float) i / Math.Max((infos.Count - 1), 1);

                var downloadHandler = _downloader.DownloadAssetAsync(info.RemoteBundleSubpath, 180);
                yield return 0.5f + ((progress + (downloadHandler.Current * chunkSize)) * 0.5f);
                while (downloadHandler.MoveNext())
                    yield return 0.5f + ((progress + (downloadHandler.Current * chunkSize)) * 0.5f);
                
                _bundleInfoByBundleName.Add(info.BundleName, info);
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
                    _bundleInfoByBundleName.Remove(info.BundleName);
            }
        }
    }
}