using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.IO;
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
        
        public IEnumerator<float> DownloadAssetAsync(string relativeFilePath, int timeout, int retryCount = 3, bool overwrite = false)
        {
            if (_downloadHandler != null)
            {
                PLog.Error<ScrapperLogger>($"Download still running, skipping execute of {relativeFilePath}...");
                yield break;
            }
            
            if (string.IsNullOrWhiteSpace(relativeFilePath))
            {
                PLog.Trace<ScrapperLogger>($"{nameof(relativeFilePath)} was empty, skipping...");
                yield break;
            }

            string path = Path.Combine(RemoteAddress, relativeFilePath);
            string targetPath = Path.GetFullPath(Path.Combine(LocalPath, relativeFilePath));

            if (FileHelper.Exists(targetPath) && !overwrite)
            {
                PLog.Trace<ScrapperLogger>($"File at '{targetPath}' already exists, but overwrite set to false...");
                yield break;
            }

            int tryCount = 0;
            IEnumerator<float> enumerator = null;
            UnityWebRequest www = null;
            do
            {
                www = UnityWebRequest.Get(path);
                enumerator = ExecuteWebRequest(www, timeout, (x) => { _downloadHandler = x; },
                    () => _downloadHandler = null);
                yield return enumerator.Current;
                while (enumerator.MoveNext())
                    yield return enumerator.Current;

                if (_downloadHandler == null)
                    tryCount++;
                
            } while (tryCount < retryCount && _downloadHandler == null);

            if (_downloadHandler == null)
            {
                PLog.Error<ScrapperLogger>($"Download still failed after {retryCount} retries, can't download resource '{relativeFilePath}'...");
                yield break;
            }

            var bytes = _downloadHandler.data;
            _downloadHandler = null;
            if (bytes.Length == 0)
            {
                PLog.Trace<ScrapperLogger>($"No file at '{path}', cannot download...");
                yield break;
            }
            
            try
            {
                FileHelper.CreateDirectoryIfNotExists(Path.GetDirectoryName(targetPath));
                File.WriteAllBytes(targetPath, bytes);
            }
            catch (Exception e)
            {
                PLog.Error<ScrapperLogger>($"File.WriteAllBytes failed '{targetPath}', reason: {e.ToString()}");
                yield break;
            }
            yield return 1.0f;
        }
        
        private static IEnumerator<float> ExecuteWebRequest(UnityWebRequest www, int timeOut, Action<DownloadHandler> handler, Action onFailed = null)
        {
            // path = path.Replace("http://", "file:///");
            // path = path.Replace("https://", "file:///");
            www.timeout = timeOut;
            www.SendWebRequest();

            while (!www.isDone)
            {
                yield return www.downloadProgress;
            }
            
            if (www.result == UnityWebRequest.Result.ConnectionError || 
                www.result == UnityWebRequest.Result.ProtocolError ||
                www.result == UnityWebRequest.Result.DataProcessingError)
            {
                PLog.Error<ScrapperLogger>($"Network error: {www.url} - {www.error}");
                onFailed?.Invoke();
                yield break;
            }

            handler?.Invoke(www.downloadHandler);
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