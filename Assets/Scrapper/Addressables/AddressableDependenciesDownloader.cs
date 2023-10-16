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
        
        private const double TASK_STALL_TIMEOUT = 60.0;

        public delegate void ProgressHandler(object key, float progress, long processedByteCount);
        public event ProgressHandler ProgressCallback;

        public AddressableDependenciesDownloader()
        {
            ResourceManager.ExceptionHandler += CustomExceptionHandler;
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

        public async Task DownloadAsync(object key, IProgress<float> progressHandler)
        {
            _failed = false;
            
            if (_failed)
                PLog.Warn<ScrapperLogger>($"Restart DownloadDependenciesAsync");
            
            AsyncOperationHandle<long> getDownloadSize = Addressables.GetDownloadSizeAsync(key);
            await getDownloadSize.Task;

            // if (getDownloadSize.Result == 0)
            //     return;
            
            AsyncOperationHandle downloadDependencies = Addressables.DownloadDependenciesAsync(key, false);
            if (progressHandler != null)
            {
                while (!downloadDependencies.IsDone && !_failed)
                {
                    await Task.Delay(10);
                
                    string error = GetDownloadError(downloadDependencies);
                    if (error != null)
                    {
                        PLog.Error<ScrapperLogger>("Download ERROR:" + error);
                        _failed = true;
                    }
                
                    var status = downloadDependencies.GetDownloadStatus();
                    progressHandler.Report(status.Percent);
                
                    ProgressCallback?.Invoke(key, status.Percent, status.DownloadedBytes);
                }
            }
            else
                await downloadDependencies.Task;
 
            var finalStatus = downloadDependencies.GetDownloadStatus();

            progressHandler?.Report(finalStatus.Percent);
            ProgressCallback?.Invoke(key, finalStatus.Percent, finalStatus.DownloadedBytes);
            
            if (_failed)
            {
                if (downloadDependencies.IsValid())
                {
                    PLog.Warn<ScrapperLogger>($"Releasing addressable AsyncOperation {downloadDependencies}...");
                    Addressables.Release(downloadDependencies);
                }
            }
            
            PLog.Info<ScrapperLogger>($"Finished download of addressables for {key}");
            progressHandler?.Report(1);
            ProgressCallback?.Invoke(key, 1, finalStatus.DownloadedBytes);
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
    }
}