using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Rhinox.Lightspeed.Collections;
using Rhinox.Perceptor;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.Exceptions;

namespace Rhinox.Scrapper
{
    public class AddressableDependenciesDownloader : IDisposable
    {
        private bool _failed;
        private float _timeSinceWaiting = -1.0f;
        
        private const double TASK_STALL_TIMEOUT = 60.0;

        public delegate void ProgressHandler(object key, float progress, long processedByteCount);
        public event ProgressHandler ProgressCallback;

        public AddressableDependenciesDownloader()
        {
            ResourceManager.ExceptionHandler += CustomExceptionHandler;
            _timeSinceWaiting = -1.0f;
        }

        public void Dispose()
        {
            ResourceManager.ExceptionHandler -= CustomExceptionHandler;
        }
        
        //Gets called for every error scenario encountered during an operation.
        //A common use case for this is having InvalidKeyExceptions fail silently when a location is missing for a given key.
        private void CustomExceptionHandler(AsyncOperationHandle handle, Exception exception)
        {
            PLog.Error<ScrapperLogger>($"Resource download exception: {exception.ToString()}");
            _failed = true;
        }

        public IEnumerator<float> DownloadAsync(object key, Action<float> progressCallback = null)
        {
            bool fullyDownloaded = false;
            _failed = false;
            _timeSinceWaiting = -1.0f;
            while (!fullyDownloaded)
            {
                if (_failed)
                    PLog.Warn<ScrapperLogger>($"Restart DownloadDependenciesAsync");
                AsyncOperationHandle downloadDependencies = Addressables.DownloadDependenciesAsync(key, false);
                DownloadStatus status = downloadDependencies.GetDownloadStatus();
                long downloadedBytes = 0;
                const int maxSamples = 15;
                LimitedQueue<long> downloadByteSamples = new LimitedQueue<long>(maxSamples);
                var lastCacheTime = DateTime.Now;
                _failed = false;
                while (!downloadDependencies.IsDone && !_failed)
                {
                    try
                    {
                        status = downloadDependencies.GetDownloadStatus();
                        downloadedBytes = status.DownloadedBytes;
                        var currentTime = DateTime.Now;
                        var curSeconds = (currentTime - lastCacheTime).TotalSeconds;
                        if (curSeconds >= 2.0)
                        {
                            downloadByteSamples.Enqueue(downloadedBytes);
                            lastCacheTime = DateTime.Now;
                        }

                        string error = GetDownloadError(downloadDependencies);
                        if (error != null)
                        {
                            PLog.Error<ScrapperLogger>("Download ERROR:" + error);
                            _failed = true;
                            break;
                        }

                        bool checkStall = CheckStalledOperation(downloadDependencies);
                        if (checkStall)
                        {
                            PLog.Error<ScrapperLogger>($"Task {downloadDependencies.Task} has stalled, WaitingForActivation");
                            _failed = true;
                            _timeSinceWaiting = -1.0f;
                            break;
                        }

                        PLog.Trace<ScrapperLogger>($"Download of addressables for {key} in progress {status.Percent * 100:##0.##}% at {currentTime.ToShortTimeString()} - {status.DownloadedBytes}");
                        progressCallback?.Invoke(status.Percent);
                        ProgressCallback?.Invoke(key, status.Percent, status.DownloadedBytes);

                        if (downloadByteSamples.Count >= maxSamples)
                        {
                            long avg = downloadByteSamples.Sum() / downloadByteSamples.Count;
                            if (avg == downloadedBytes) // Stalled for 2 * maxSamples seconds
                            {
                                _failed = true;
                                break;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        PLog.Error($"Exception on download status: {e.ToString()}");
                        _failed = true;
                    }

                    yield return status.Percent;
                }

                if (!_failed)
                {
                    progressCallback?.Invoke(status.Percent);
                    ProgressCallback?.Invoke(key, status.Percent, status.DownloadedBytes);
                    
                    yield return status.Percent;

                    fullyDownloaded = true;
                }
                else
                {
                    if (downloadDependencies.IsValid())
                    {
                        PLog.Warn<ScrapperLogger>($"Releasing addressable AsyncOperation {downloadDependencies}...");
                        Addressables.Release(downloadDependencies);
                    }
            
                    yield return status.Percent;
                }

            }

            PLog.Info<ScrapperLogger>($"Finished download of addressables for {key}");
            yield return 1.0f;
        }
        
        private string GetDownloadError(AsyncOperationHandle fromHandle)
        {
            if (fromHandle.Status != AsyncOperationStatus.Failed)
                return null;
     
            RemoteProviderException remoteException;
            Exception e = fromHandle.OperationException;
            while (e != null)
            {
                remoteException = e as RemoteProviderException;
                if (remoteException != null)
                    return remoteException.WebRequestResult.Error;
                e = e.InnerException;
            }
            return null;
        }

        private bool CheckStalledOperation(AsyncOperationHandle handle)
        {
            bool waitingForActivation = handle.Task.Status == TaskStatus.WaitingForActivation;
            if (!waitingForActivation)
            {
                _timeSinceWaiting = -1.0f;
                return false;
            }
            
            if (_timeSinceWaiting < 0.0f)
                _timeSinceWaiting = Time.realtimeSinceStartup;
            else
            {
                if ((Time.realtimeSinceStartup - _timeSinceWaiting) > TASK_STALL_TIMEOUT)
                    return true;
            }

            return false;
        }
    }
}