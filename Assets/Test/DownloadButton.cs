using System;
using Rhinox.Scrapper;
using Rhinox.Scrapper.Test;
using UnityEngine;
using UnityEngine.UI;

namespace Test
{
    [RequireComponent(typeof(Button))]
    public class DownloadButton : MonoBehaviour
    {
        private Button _button;
        public TestProgressLoader Loader;

        private void Awake()
        {
            _button = GetComponent<Button>();
            _button.onClick.AddListener(OnButtonClick);
        }

        private void OnDestroy()
        {
            if (_button != null)
                _button.onClick.RemoveListener(OnButtonClick);  
        }

        private void OnButtonClick()
        {
            if (Loader != null)
                Loader.DownloadAssets();
        }
    }
}