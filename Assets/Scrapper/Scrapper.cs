using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rhinox.Lightspeed;
using Rhinox.Lightspeed.IO;
using Rhinox.Perceptor;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.Initialization;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.Util;

namespace Rhinox.Scrapper
{
    public static class Scrapper
    {
        private static InitializingState _initializingState;

        public static bool Initialized => _initializingState == InitializingState.Initialized;
        private static string _rootPath;
        internal static string RootPath => _rootPath;
        private static Dictionary<string, MirroredResourceCatalog> _resourceLocators;

        public static IReadOnlyCollection<string> LoadedCatalogs =>
            _resourceLocators != null ? (IReadOnlyCollection<string>) _resourceLocators.Keys : Array.Empty<string>();

        public delegate void CatalogLoadEventHandler(string catalogLocatorId);
        public static event CatalogLoadEventHandler CatalogLoaded;
        
        
        public delegate void PreloadProgressEventHandler(object[] keys, ProgressBytes progress);
        public static event PreloadProgressEventHandler PreloadProgressCallback;

        public delegate void PreloadEventHandler(object[] keys, long totalBytes);
        public static event PreloadEventHandler PreloadCompleted;
        
        public delegate void PreloadErrorEventHandler(object[] keys, string errorMsg);
        public static event PreloadErrorEventHandler PreloadFailed;

        public delegate void ErrorEventHandler(string errorMessage, Exception e = null);
        public static event ErrorEventHandler CustomErrorHandling;

        private enum InitializingState
        {
            None,
            Initializing,
            Initialized
        }

        public static IEnumerator InitializeAsync()
        {
            PLog.Debug($"{nameof(InitializeAsync)} started...");
            if (_initializingState != InitializingState.None)
            {
                PLog.Debug($"{nameof(InitializeAsync)} cancelled, current state: {_initializingState.ToString()}");
                yield break;
            }

            _initializingState = InitializingState.Initializing;
            AsyncOperationHandle initAction = Addressables.InitializeAsync(true);

            
            if (!initAction.IsValid())
            {
                _initializingState = InitializingState.None;
                const string initFailMessage = "Failed to initialize Scrapper, Addressable initialization failed...";
                PLog.Warn<ScrapperLogger>(initFailMessage);
                CustomErrorHandling?.Invoke(initFailMessage);
                yield break;
            }

            yield return initAction;
            
            
            Addressables.ClearResourceLocators();

            if (!TryInitializeCacheFolder())
            {
                _initializingState = InitializingState.None;
                const string cacheFolderFailMsg = "Failed to initialize Scrapper, cache folder could not be created/loaded...";
                PLog.Warn<ScrapperLogger>(cacheFolderFailMsg);
                CustomErrorHandling?.Invoke(cacheFolderFailMsg);
                yield break;
            }
            
            _initializingState = InitializingState.Initialized;
            PLog.Debug<ScrapperLogger>($"Scrapper initialized.");
        }

        private static bool TryInitializeCacheFolder()
        {
            string rootPath = Application.persistentDataPath;
            string path = Path.GetFullPath(Path.Combine(rootPath, "com.rhinox.open.scrapper")).Replace("\\", "/");
            if (FileHelper.DirectoryExists(path))
            {
                _rootPath = path;
                return true;
            }

            try
            {
                FileHelper.CreateDirectoryIfNotExists(path);
            }
            catch (Exception e)
            {
                PLog.Error<ScrapperLogger>($"Create Directory at '{path}' failed, reason: {e.ToString()}");
                _rootPath = null;
                return false;
            }

            _rootPath = path;
            return true;
        }

        public static IEnumerator LoadCatalogAsync(string remotePath)
        {
            if (!remotePath.EndsWith(".json"))
            {
                string catalogPathErrMsg = $"Path not supported for catalog '{remotePath}', must end in .json";
                PLog.Debug<ScrapperLogger>(catalogPathErrMsg);
                CustomErrorHandling?.Invoke(catalogPathErrMsg);
                yield break;
            }

            // Handle hash file
            if (!CheckAndUpdateLocalHash(remotePath, out bool catalogChanged))
            {
                string catalogHashUpdateFailMsg = $"Could not check catalog hash at '{remotePath}', exiting...";
                PLog.Debug<ScrapperLogger>(catalogHashUpdateFailMsg);
                CustomErrorHandling?.Invoke(catalogHashUpdateFailMsg);
                yield break;
            }
            
            // Handle catalog
            string catalogCachePath = AddressableUtility.GetCachePathForCatalog(remotePath, ".json");
            string catalogMorphedPath = Path.Combine(_rootPath, Path.GetFileNameWithoutExtension(catalogCachePath), "catalog.json");
            if (catalogChanged || !FileHelper.Exists(catalogCachePath) || !FileHelper.Exists(catalogMorphedPath))
            {
                SetupCatalog(remotePath, catalogCachePath);
                catalogChanged = true; // Force clear setup, if first time running this
            }

            // Load cached catalog in Addressable system
            PLog.Debug<ScrapperLogger>($"{nameof(LoadCatalogAsync)} load content catalog at {remotePath}...");
            var catalogResult = new CatalogLoadResult();
            yield return AddressableUtility.LoadContentCatalogAsync(remotePath, catalogResult);

            if (catalogResult.Status != AsyncOperationStatus.Succeeded)
            {
                const string addressableLoadFailMsg = "Load Content Catalog failed, Addressable Failure";
                PLog.Error<ScrapperLogger>(addressableLoadFailMsg);
                CustomErrorHandling?.Invoke(addressableLoadFailMsg);
                yield break;
            }

            var resourceLocator = catalogResult.Result;

            if (_resourceLocators == null)
                _resourceLocators = new Dictionary<string, MirroredResourceCatalog>();

            if (_resourceLocators.ContainsKey(resourceLocator.LocatorId))
            {
                string resourceLocatorExistsMsg = $"ResourceLocator with id '{resourceLocator.LocatorId}' already registered, exiting...";
                PLog.Warn<ScrapperLogger>(resourceLocatorExistsMsg);
                CustomErrorHandling?.Invoke(resourceLocatorExistsMsg);
                yield break;
            }
            
            Addressables.AddResourceLocator(resourceLocator);

            var localCachePath = Path.GetFullPath(Path.Combine(_rootPath, AddressableUtility.GetCatalogHashName(remotePath)));
            var remoteDataPath = remotePath.Replace(Path.GetFileName(remotePath), "");
            var mirrorCache = new MirroredResourceCatalog(resourceLocator, new MirrorDownloader(remoteDataPath, localCachePath));
            mirrorCache.Initialize();
            if (catalogChanged)
                mirrorCache.ClearCache(); // Create new mirrorCache
            _resourceLocators.Add(resourceLocator.LocatorId, mirrorCache);
            
            CatalogLoaded?.Invoke(resourceLocator.LocatorId);
            
            PLog.Debug($"{nameof(LoadCatalogAsync)} completed...");
        }


        public static IEnumerator PreloadAsync(params object[] keys)
        {
            return PreloadAsync((Action<float>) null, keys);
        }

        public static IEnumerator PreloadAsync(Action<float> progressHandler, params object[] keys)
        {
            long totalBytes = 0;
            foreach (var loaderKey in _resourceLocators.Keys)
            {
                var locator = _resourceLocators[loaderKey];
                IEnumerator<ProgressBytes> preloadEnumerator = null;
                try
                {
                    preloadEnumerator = locator.PreloadAllAssetsAsync(keys);
                }
                catch (Exception e)
                {
                    CustomErrorHandling?.Invoke($"Preload failure: {e.ToString()}", e);
                    PreloadFailed?.Invoke(keys, $"Preload failure: {e.ToString()}");
                    yield break;
                }

                ProgressBytes currentProgress = preloadEnumerator.Current;
                yield return currentProgress;
                PreloadProgressCallback?.Invoke(keys, currentProgress);
                bool repeat = false;
                do
                {
                    try
                    {
                        repeat = preloadEnumerator.MoveNext();
                    }
                    catch (Exception e)
                    {
                        CustomErrorHandling?.Invoke($"Preload failure: {e.ToString()}", e);
                        PreloadFailed?.Invoke(keys, $"Preload failure: {e.ToString()}");
                        yield break;
                    }

                    currentProgress = preloadEnumerator.Current;
                    progressHandler?.Invoke(currentProgress.Progress);
                    PreloadProgressCallback?.Invoke(keys, currentProgress);
                    yield return null;
                } 
                while (repeat);

                totalBytes += currentProgress.TotalBytes;
                yield return null;
            }

            PLog.Debug<ScrapperLogger>($"PreloadAsync finished for {string.Join(", ", keys)}");
            PreloadCompleted?.Invoke(keys, totalBytes);
        }
        
        public static IEnumerator LoadAssetAsync<T>(string key, Action<T> onCompleted, T fallbackObject = default(T), Action onFailed = null)
            where T : class
        {
            if (!Initialized)
            {
                PLog.Warn<ScrapperLogger>($"Scrapper not initialized, skipping...");
                onFailed?.Invoke();
                yield break;
            }

            foreach (var loaderKey in _resourceLocators.Keys)
            {
                var locator = _resourceLocators[loaderKey];
                if (!locator.AddressableResourceExists<T>(key))
                    continue;

                IEnumerator loadOp = null;

                try
                {
                    loadOp = locator.LoadAsset<T>(key,
                        onCompleted,
                        (fail) => { onFailed?.Invoke(); },
                        fallbackObject);
                }
                catch (Exception e)
                {
                    PLog.Error<ScrapperLogger>($"Exception on load asset '{key}' from {locator}: {e.ToString()}");
                    onFailed?.Invoke();
                }

                if (loadOp == null)
                {
                    onFailed?.Invoke();
                    PLog.Error<ScrapperLogger>($"Exception on load asset '{key}' from {locator}, operation = null");
                    yield break;
                }
                

                yield return loadOp.Current;
                
                bool repeat = false;
                do
                {
                    try
                    {
                        repeat = loadOp.MoveNext();
                    }
                    catch (Exception e)
                    {
                        PLog.Error<ScrapperLogger>($"Exception on load asset '{key}' from {locator}: {e.ToString()}");
                        onFailed?.Invoke();
                    }

                    yield return loadOp.Current;
                } 
                while (repeat);

                yield break;
            }
            
            // If this code is reached fail the load asset
            onFailed?.Invoke();
        }
        
        public static bool HasResourceOfType<T>(object key)
        {
            foreach (var catalog in _resourceLocators.Values)
            {
                if (catalog.Locator.Locate(key, typeof(T), out IList<IResourceLocation> locs))
                    return true;
            }
            return false;
        }
        
        public static bool HasResource(object key)
        {
            return HasResourceOfType<object>(key);
        }

        private static bool CheckAndUpdateLocalHash(string remotePath, out bool hashChanged)
        {
            bool localHashMatchesRemote = false;
            string hashCachePath = AddressableUtility.GetCachePathForCatalog(remotePath);
            string remoteHashPath = remotePath.Replace(".json", ".hash");
            string remoteHash = null;
            try
            {
                remoteHash = FileHelper.ReadAllText(remoteHashPath);
            }
            catch (Exception e)
            {
                PLog.Error<ScrapperLogger>($"Exception occured while downloading catalog hash file from server: {e.ToString()}");
                hashChanged = false;
                return false;
            }

            if (remoteHash == null)
            {
                PLog.Debug<ScrapperLogger>($"Failed to fetch text from '{remoteHashPath}'");
                hashChanged = false;
                return false;
            }

            try
            {
                if (!FileHelper.Exists(hashCachePath))
                {
                    var directory = Path.GetDirectoryName(hashCachePath);
                    FileHelper.CreateDirectoryIfNotExists(directory);
                    File.WriteAllText(hashCachePath, remoteHash);
                }
                else
                {
                    string localHash = FileHelper.ReadAllText(hashCachePath);
                    if (localHash != null && localHash.Equals(remoteHash))
                        localHashMatchesRemote = true;
                    else
                        File.WriteAllText(hashCachePath, remoteHash);
                }
            }
            catch (IOException exception)
            {
                PLog.Error<ScrapperLogger>($"File IO problem occured with local catalog hash file: {exception.ToString()}");
                hashChanged = false;
                return false;
            }
            catch (Exception e)
            {
                PLog.Error<ScrapperLogger>($"Exception occured while checking local catalog hash file: {e.ToString()}");
                hashChanged = false;
                return false;
            }

            hashChanged = !localHashMatchesRemote;
            return true;
        }

        private static void SetupCatalog(string remotePath, string catalogCachePath)
        { 
            string catalogContent = FileHelper.ReadAllText(remotePath);

            string folderPath = Path.Combine(_rootPath, Path.GetFileNameWithoutExtension(catalogCachePath));
            FileHelper.CreateDirectoryIfNotExists(folderPath);
            
            File.WriteAllText(Path.Combine(folderPath, "catalog.json"), catalogContent);
            var catalogJson = JObject.Parse(catalogContent);
            JArray jArray = catalogJson["m_InternalIdPrefixes"] as JArray;
            if (jArray != null)
            {
                string catalogRootPath = remotePath.Replace(Path.GetFileName(remotePath), "");
                if (catalogRootPath.EndsWith("/"))
                    catalogRootPath = catalogRootPath.Substring(0, catalogRootPath.Length - 1);
                if (catalogRootPath.EndsWith("\\"))
                    catalogRootPath = catalogRootPath.Substring(0, catalogRootPath.Length - 1);
                var catalogRootUri = new Uri(catalogRootPath);
                for (int i = 0; i < jArray.Count; ++i)
                {
                    var entry = jArray[i] as JValue;
                    if (entry == null)
                        continue;
                    string entryStr = entry.Value<string>();
                    if (entryStr == null || !ResourceManagerConfig.IsPathRemote(entryStr))
                        continue;

                    int entryIndex = entryStr.IndexOf(catalogRootUri.AbsolutePath, StringComparison.InvariantCulture);
                    if (entryIndex != -1)
                    {
                        string manipulateString = entryStr.Substring(entryIndex, entryStr.Length - entryIndex);
                        manipulateString = manipulateString.Replace(catalogRootUri.AbsolutePath,
                            $"file:///{_rootPath}/{Path.GetFileNameWithoutExtension(catalogCachePath)}");
                        if (manipulateString.StartsWith("file:////"))
                            manipulateString = manipulateString.Replace("file:////", "file:///");
                        jArray[i] = JValue.CreateString(manipulateString);
                    }

                }
            }
            File.WriteAllText(catalogCachePath, catalogJson.ToString(Formatting.None));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="extensionFilter">Include '.prefab', '.mat', etc.</param>
        /// <returns></returns>
        public static IReadOnlyCollection<string> GetLoadedResourceKeys(string extensionFilter = null)
        {
            if (_resourceLocators == null)
                return Array.Empty<string>();

            List<string> keys = new List<string>();
            foreach (var resourceLocatorCache in _resourceLocators.Values)
            {
                var addressableCache = resourceLocatorCache.Locator;

                foreach (var l in addressableCache.Keys)
                {
                    if (l is string s)
                    {
                        // Skip asset guids
                        if (System.Guid.TryParse(s, out System.Guid g))
                            continue;

                        if (extensionFilter != null && !s.EndsWith(extensionFilter))
                            continue;
                        keys.Add(s);
                    }
                }
            }

            return keys;
        }

        public static void ClearCache()
        {
            UnityEngine.Caching.ClearCache();
            foreach (var locator in _resourceLocators.Values)
            {
                locator.ClearCache();
            }
        }
    }
}