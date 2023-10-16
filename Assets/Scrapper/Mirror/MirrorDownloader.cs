using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Threading.Tasks;
using Rhinox.Lightspeed;
using Rhinox.Lightspeed.IO;
using Rhinox.Perceptor;
using UnityEngine.Networking;

namespace Rhinox.Scrapper
{
    public class MirrorDownloader
    {
        private DownloadHandler _downloadHandler;
        public string RemoteAddress { get; }
        public string LocalPath { get; }

        public MirrorDownloader(string remoteAddress, string localPath)
        {
            RemoteAddress = remoteAddress;
            LocalPath = localPath; 
        }

        public bool DownloadAsset(string relativeFilePath, bool overwrite = false)
        {
            if (string.IsNullOrWhiteSpace(relativeFilePath))
                return false;
            
            string path = Path.Combine(RemoteAddress, relativeFilePath);
            string targetPath = Path.GetFullPath(Path.Combine(LocalPath, relativeFilePath));

            if (FileHelper.Exists(targetPath) && !overwrite)
            {
                PLog.Trace<ScrapperLogger>($"File at '{targetPath}' already exists, but overwrite set to false...");
                return false;
            }

            var bytes = FileHelper.ReadAllBytes(path);
            if (bytes.Length == 0)
            {
                PLog.Trace<ScrapperLogger>($"No file at '{path}', cannot download...");
                return false;
            }
            
            try
            {
                FileHelper.CreateDirectoryIfNotExists(Path.GetDirectoryName(targetPath));
                File.WriteAllBytes(targetPath, bytes);
            }
            catch (Exception e)
            {
                PLog.Error<ScrapperLogger>($"File.WriteAllBytes failed '{targetPath}', reason: {e.ToString()}");
                return false;
            }
            return true;
        }
        
        public async Task DownloadAssetAsync(string relativeFilePath, IProgress<float> progressHandler, int timeout, int retryCount = 3, bool overwrite = false)
        {
            if (_downloadHandler != null)
            {
                PLog.Error<ScrapperLogger>($"Download still running, skipping execute of {relativeFilePath}...");
                return;
            }
            
            if (string.IsNullOrWhiteSpace(relativeFilePath))
            {
                PLog.Trace<ScrapperLogger>($"{nameof(relativeFilePath)} was empty, skipping...");
                return;
            }

            string path = Path.Combine(RemoteAddress, relativeFilePath);
            string targetPath = Path.GetFullPath(Path.Combine(LocalPath, relativeFilePath));

            if (FileHelper.Exists(targetPath) && !overwrite)
            {
                PLog.Trace<ScrapperLogger>($"File at '{targetPath}' already exists, but overwrite set to false...");
                return;
            }

            int tryCount = 0;
            UnityWebRequest www;
            do
            {
                PLog.Debug<ScrapperLogger>($"Downloading {path}...");
                www = UnityWebRequest.Get(path);
                await ExecuteWebRequest(www, progressHandler, timeout);

                if (_downloadHandler == null)
                    tryCount++;
                
            } while (tryCount < retryCount && _downloadHandler == null);

            if (_downloadHandler == null)
            {
                PLog.Error<ScrapperLogger>($"Download still failed after {retryCount} retries, can't download resource '{relativeFilePath}'...");
                return;
            }
            
            if (!www.IsRequestValid(out string error))
            {
                PLog.Error<ScrapperLogger>($"Download failed with error '{error}', can't download resource '{relativeFilePath}'...");
                return;
            } 

            var bytes = _downloadHandler.data;
            _downloadHandler = null;
            if (bytes.Length == 0)
            {
                PLog.Trace<ScrapperLogger>($"No file at '{path}', cannot download...");
                return;
            }
            
            try
            {
                FileHelper.CreateDirectoryIfNotExists(Path.GetDirectoryName(targetPath));
                await File.WriteAllBytesAsync(targetPath, bytes);
            }
            catch (Exception e)
            {
                PLog.Error<ScrapperLogger>($"File.WriteAllBytes failed '{targetPath}', reason: {e.ToString()}");
            }
        }
        
        private static async Task ExecuteWebRequest(UnityWebRequest www, IProgress<float> downloadProgress, int timeOut)
        {
            // path = path.Replace("http://", "file:///");
            // path = path.Replace("https://", "file:///");
            www.timeout = timeOut;
            var task = www.SendWebRequest();

            while (!task.isDone)
            {
                downloadProgress.Report(www.downloadProgress);
                await Task.Delay(100); 
            }
        }

        public bool ClearTargetAsset(string relativeFilePath)
        {
            if (string.IsNullOrWhiteSpace(relativeFilePath))
                return false;
            
            string targetPath = Path.GetFullPath(Path.Combine(LocalPath, relativeFilePath));
            if (!FileHelper.Exists(targetPath))
                return true;

            try
            {
                File.Delete(targetPath);
            }
            catch (Exception e)
            {
                PLog.Error<ScrapperLogger>($"File.Delete failed '{targetPath}', reason: {e.ToString()}");
                return false;
            }

            return true;
        }

        public bool TargetExists(string relativeFilePath)
        {
            if (string.IsNullOrWhiteSpace(relativeFilePath))
                return false;
            
            string targetPath = Path.GetFullPath(Path.Combine(LocalPath, relativeFilePath));
            return FileHelper.Exists(targetPath);
        }
    }
}