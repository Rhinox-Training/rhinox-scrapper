using System;
using System.IO;
using Modulab;
using Rhinox.Lightspeed.IO;
using Rhinox.Perceptor;
using Rhinox.Utilities;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;

namespace Rhinox.Scrapper.Test
{
    public class TestProgressLoader : MonoBehaviour
    {
        public string RemoteCatalogURL;
        public string Key = "ALL";

        public TMP_Text Text;
        public Slider Slider;

        private void OnEnable()
        {
            Scrapper.CustomErrorHandling += OnError;
        }

        private void OnDisable()
        {
            Scrapper.CustomErrorHandling -= OnError;
        }

        private void OnError(string errormessage, Exception e)
        {
            Debug.LogError(errormessage);
        }

        private void Start()
        {
            if (string.IsNullOrWhiteSpace(RemoteCatalogURL))
            {
                EnvironmentLoader.Load(Path.Combine(FileHelper.GetProjectPath(), ".env"));
                RemoteCatalogURL = EnvironmentLoader.AddressableCatalogUrl;
            }

            PLog.Debug("Start event on GameObject called, InitAddressableStart");
            var managedCoroutine = new ManagedCoroutine(Scrapper.InitializeAsync());
            managedCoroutine.OnFinished += OnFinished;
            //StartCoroutine(InitAddressables());
        }

        private void OnFinished(bool manual)
        {
            new ManagedCoroutine(Scrapper.LoadCatalogAsync(RemoteCatalogURL));
        }
        
        [ContextMenu("DownloadAsset")]
        public void DownloadAssets()
        {
            StartCoroutine(Scrapper.PreloadAsync((progress) =>
            {
                if (Text != null)
                    Text.text = $"{progress*100.0f:##0.0#}%";
                if (Slider != null)
                    Slider.value = progress;
            }, Key));
        }
    }
}