using System;
using Rhinox.Scrapper;
using UnityEngine;

namespace Test
{
    public class DisableOnDownloadComplete : MonoBehaviour
    {
        private void Awake()
        {
            Scrapper.PreloadCompleted += OnDownloadComplete;
        }
        
        private void OnDestroy()
        {
            Scrapper.PreloadCompleted -= OnDownloadComplete;
        }

        private void OnDownloadComplete(object[] keys, long totalBytes)
        {
            gameObject.SetActive(false);
        }
    }
}