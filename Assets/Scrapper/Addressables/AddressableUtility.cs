using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Rhinox.Perceptor;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.Initialization;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.ResourceManagement.Util;
using UnityEngineInternal;

namespace Rhinox.Scrapper
{
    public class CatalogLoadResult
    {
        public IResourceLocator Result;
        public AsyncOperationStatus Status;
    }
    
    public static class AddressableUtility
    {
        /// <summary>
        /// Loads Addressables into cache
        /// </summary>
        public static IEnumerator LoadContentCatalogAsync(string remotePath, CatalogLoadResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));
            
            AsyncOperationHandle loadCatalog = Addressables.LoadContentCatalogAsync(remotePath);
            yield return loadCatalog;

            int tryCount = 0;
            while (tryCount < 10 && loadCatalog.Status == AsyncOperationStatus.None)
            {
                ++tryCount;
                yield return new WaitForEndOfFrame();
            }

            result.Status = loadCatalog.Status == AsyncOperationStatus.None
                ? AsyncOperationStatus.Failed
                : loadCatalog.Status;

            if (result.Status == AsyncOperationStatus.Failed)
            {
                PLog.Error<ScrapperLogger>($"{nameof(LoadContentCatalogAsync)} failed - {loadCatalog.OperationException?.Message}");
                yield break;
            }

            if (!(loadCatalog.Task.Result is IResourceLocator))
            {
                result.Status = AsyncOperationStatus.Failed;
                yield break;
            }

            result.Result = (IResourceLocator)loadCatalog.Task.Result;
        }
        
        public static string GetCatalogHashName(string remotePath)
        {
            string hashFilePath = remotePath.Replace(".json", ".hash");
            string tmpPath = hashFilePath;
            if (ResourceManagerConfig.IsPathRemote(hashFilePath))
                tmpPath = ResourceManagerConfig.StripQueryParameters(hashFilePath);

            return tmpPath.GetHashCode().ToString(CultureInfo.InvariantCulture);
        }

        public static string GetCachePathForCatalog(string remotePath, string customExtension = ".hash")
        {
            string hashFilePath = remotePath.Replace(".json", ".hash");
            string tmpPath = hashFilePath;
            if (ResourceManagerConfig.IsPathRemote(hashFilePath))
            {
                tmpPath = ResourceManagerConfig.StripQueryParameters(hashFilePath);
            }
#if UNITY_SWITCH
            string cacheHashFilePath = hashFilePath.Replace(".hash", customExtension); // ResourceLocationBase does not allow empty string id
#else
            const string kCacheDataFolder = "{UnityEngine.Application.persistentDataPath}/com.unity.addressables/";
            // The file name of the local cached catalog + hash file is the hash code of the remote hash path, without query parameters (if any).
            string cacheHashFilePath = ResolveInternalId(kCacheDataFolder + tmpPath.GetHashCode() + customExtension);
#endif
            return cacheHashFilePath;
        }
        
        private static string ResolveInternalId(string id)
        {
            var path = AddressablesRuntimeProperties.EvaluateString(id);
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || UNITY_WSA || UNITY_XBOXONE || UNITY_GAMECORE || UNITY_PS5 || UNITY_PS4
            if (path.Length >= 260 && path.StartsWith(Application.dataPath))
                path = path.Substring(Application.dataPath.Length + 1);
#endif
            return path;
        }

        public static int FindHashCodeForLocation(IResourceLocation location)
        {
            return location.InternalId.GetHashCode() * 31 + location.ProviderId.GetHashCode();
        }
        
        /// <summary>
        /// Only checks against cache space and is not the same as disk space - see https://docs.unity3d.com/2021.2/Documentation/ScriptReference/Cache-spaceFree.html
        /// </summary>
        /// <param name="key">Addressables key to get download size for</param>
        /// <returns>true if there is valid space</returns>
        public static bool PredetermineCanDownload(object key)
        {
            var op = Addressables.GetDownloadSizeAsync(key);
            op.WaitForCompletion();
            long downloadSize = op.Result;
            Addressables.Release(op);
            // could use OS methods to get the disk space here if needed
            if (Caching.currentCacheForWriting.spaceFree < downloadSize)
            {
                PLog.Warn<ScrapperLogger>($"Memory lacking: {downloadSize - Caching.currentCacheForWriting.spaceFree}");
                return false;
            }

            return true;
        }

        
        
        public static bool IsHash128(string str)
        {
            var hash = Hash128.Parse(str);
            return hash.ToString() == str;
        }
        
        public static List<AsyncOperationHandle> GetAllAsyncOperationHandles()
        {
            // Workaround for problems:
            // https://forum.unity.com/threads/how-to-unload-everything-currently-loaded-by-addressables.1121998/
     
            var handles = new List<AsyncOperationHandle>();
     
            var resourceManagerType = Addressables.ResourceManager.GetType();
            var dictionaryMember = resourceManagerType.GetField("m_AssetOperationCache", BindingFlags.NonPublic | BindingFlags.Instance);
            var dictionary = dictionaryMember.GetValue(Addressables.ResourceManager) as IDictionary;

            var type = typeof(Addressables).Assembly.GetType("UnityEngine.ResourceManagement.Util.LocationCacheKey");
            
            if (dictionary != null)
            {
                foreach (var asyncOperationInterface in dictionary.Values)
                {
                    if (asyncOperationInterface == null)
                        continue;

                    var handle = typeof(AsyncOperationHandle).InvokeMember(nameof(AsyncOperationHandle),
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                        BindingFlags.CreateInstance,
                        null, null, new object[] {asyncOperationInterface});

                    handles.Add((AsyncOperationHandle) handle);
                }
            }

            return handles;
        }
        
        public static List<AsyncOperationHandle> GetAsyncOperationHandles(IResourceLocation location)
        {
            // Workaround for problems:
            // https://forum.unity.com/threads/how-to-unload-everything-currently-loaded-by-addressables.1121998/
     
            var handles = new List<AsyncOperationHandle>();
     
            var resourceManagerType = Addressables.ResourceManager.GetType();
            var dictionaryMember = resourceManagerType.GetField("m_AssetOperationCache", BindingFlags.NonPublic | BindingFlags.Instance);
            var dictionary = dictionaryMember.GetValue(Addressables.ResourceManager) as IDictionary;
            
            if (dictionary != null)
            {
                foreach (var key in dictionary.Keys)
                {
                    var asyncOperationInterface = dictionary[key];
                    if (asyncOperationInterface == null)
                        continue;

                    if (!IsOperationCacheKeyForLocation(key, location))
                        continue;

                    var handle = typeof(AsyncOperationHandle).InvokeMember(nameof(AsyncOperationHandle),
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                        BindingFlags.CreateInstance,
                        null, null, new object[] {asyncOperationInterface});

                    handles.Add((AsyncOperationHandle) handle);
                }
            }

            return handles;
        }
     
        public static void ReleaseAsyncOperationHandles(List<AsyncOperationHandle> handles)
        {
            foreach (var handle in handles)
            {
                if (!handle.IsDone)
                    PLog.Warn<ScrapperLogger>($"AsyncOperationHandle not completed yet. Releasing anyway!");
     
                while (handle.IsValid())
                {
                    Addressables.ResourceManager.Release(handle);
                }
            }
        }

        public static bool IsOperationCacheKeyForLocation(object operationCacheKey, IResourceLocation location)
        {
            var resourcesAssembly = typeof(ResourceManager).Assembly;
            var locationKeyType = resourcesAssembly.GetType("UnityEngine.ResourceManagement.Util.LocationCacheKey", false);
            var locationField = locationKeyType.GetField("m_Location",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            
            if (locationKeyType.IsInstanceOfType(operationCacheKey) && locationField != null)
            {
                var savedLoc = locationField.GetValue(operationCacheKey) as IResourceLocation;
                if (savedLoc == location)
                    return true;
                if (savedLoc != null)
                {
                    if (savedLoc.Data is AssetBundleRequestOptions savedRequest &&
                        location.Data is AssetBundleRequestOptions locationOptions)
                    {
                        return savedRequest.BundleName == locationOptions.BundleName &&
                               savedRequest.Hash == locationOptions.Hash;
                    }
                    return savedLoc.Equals(location);
                }
            }
            
            var idKeyType = resourcesAssembly.GetType("UnityEngine.ResourceManagement.Util.IdCacheKey", false);
            if (idKeyType != null) // Addressables 1.20.x or higher
            {
                var idField = idKeyType.GetField("ID",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (idKeyType.IsInstanceOfType(operationCacheKey) && idField != null)
                {
                    string id = Addressables.ResourceManager.TransformInternalId(location);
                    var savedLoc = idField.GetValue(operationCacheKey) as string;
                    return savedLoc == id;
                }
            }

            return false;
        }

        
        // NOTE: Hack for loading progress
        private static Type _compactLocationType;
        private static FieldInfo _locationDataField;

        private static bool TryInitializeProgressHelperTypes()
        {
            if (_compactLocationType == null)
            {
                _compactLocationType = typeof(ContentCatalogData).GetNestedType("CompactLocation",
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic);
                
                if (_compactLocationType != null)
                    _locationDataField = _compactLocationType.GetField("m_Data", BindingFlags.Instance | BindingFlags.NonPublic);
            }

            return _compactLocationType != null && _locationDataField != null;
        }
        
        public static bool FixupMirrorProgress(ref IResourceLocation location)
        {
            if (location == null)
                return false;

            if (location.Data is VirtualLocalBundleRequestOptions ||
                !(location.Data is AssetBundleRequestOptions bundleRequestOptions)) // Already fixed
                return false;

            if (!TryInitializeProgressHelperTypes())
            {
                PLog.Warn<ScrapperLogger>($"Can't find type information to fix mirror progress");
                return false;
            }
            
            if (_compactLocationType.IsInstanceOfType(location))
            {
                var virt = AddressableUtility.MakeVirtual(bundleRequestOptions);
                _locationDataField.SetValue(location, virt);
                return true;
            }

            return false;
        }
        
        private static AssetBundleRequestOptions MakeVirtual(AssetBundleRequestOptions requestOptions)
        {
            var virtualOptions = new VirtualLocalBundleRequestOptions()
            {
                Crc = requestOptions.Crc,
                Hash = requestOptions.Hash,
                ChunkedTransfer = requestOptions.ChunkedTransfer,
                RedirectLimit = requestOptions.RedirectLimit,
                RetryCount = requestOptions.RetryCount,
                Timeout = requestOptions.Timeout,
                BundleName = requestOptions.BundleName,
                AssetLoadMode = requestOptions.AssetLoadMode,
                BundleSize = requestOptions.BundleSize,
                UseCrcForCachedBundle = requestOptions.UseCrcForCachedBundle,
                ClearOtherCachedVersionsWhenLoaded = requestOptions.ClearOtherCachedVersionsWhenLoaded,
                UseUnityWebRequestForLocalBundles = requestOptions.UseUnityWebRequestForLocalBundles
            };
            return virtualOptions;
        }
    }
}