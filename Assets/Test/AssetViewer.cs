using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Rhinox.Perceptor;
using Rhinox.Scrapper;
using Rhinox.Utilities;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RMDY
{
    public class AssetViewer : MonoBehaviour
    {
        private class AddressableItem
        {
            public string ResourceKey;

            public string DisplayName => $"{ResourceKey}";
        }
        
        private List<AddressableItem> _options;
        private int _optionIndex;

        public GameObject Root;
        private GameObject _generatedObject;

        public TMP_Text Text;

        private bool _loadingNewPart;
        
        private void Awake()
        {
            PLog.CreateIfNotExists();
            Scrapper.PreloadCompleted += OnPreloadCompleted;
        }

        private void OnDestroy()
        {
            Scrapper.PreloadCompleted -= OnPreloadCompleted;
        }

        private void OnPreloadCompleted(object[] keys, long totalBytes)
        {
            if (enabled)
                Initialize();
        }

        [ContextMenu("Initialize")]
        private void Initialize()
        {
            _options = new List<AddressableItem>();
            foreach (var resourceKey in Scrapper.GetLoadedResourceKeys(".prefab"))
            {
                if (string.IsNullOrWhiteSpace(resourceKey))
                    continue;

                var entry = new AddressableItem()
                {
                    ResourceKey = resourceKey
                };
                _options.Add(entry);
            }
            _optionIndex = 0;
            UpdateRender();
        }

        public void Next()
        {
            if (_loadingNewPart)
                return;
            
            _optionIndex = (_optionIndex + 1) % _options.Count;
            UpdateRender();
        }

        public void Previous()
        {
            if (_loadingNewPart)
                return;
            
            _optionIndex = (_options.Count + _optionIndex - 1) % _options.Count;
            UpdateRender();
        }

        private void Update()
        {
            if (_options == null)
                return;
            
            if (Input.GetKeyDown(KeyCode.LeftArrow))
                Previous();
            else if (Input.GetKeyDown(KeyCode.RightArrow))
                Next();
        }

        private void UpdateRender()
        {
            if (_generatedObject != null)
                Destroy(_generatedObject);

            var option = _options[_optionIndex];
            _generatedObject = new GameObject(option.DisplayName);
            _generatedObject.transform.SetParent(Root.transform, false);

            _loadingNewPart = true;
            new ManagedCoroutine(Scrapper.LoadAssetAsync<GameObject>(option.ResourceKey, (x) =>
            {
                if (x == null)
                {
                    Debug.LogError($"Cannot visualize, {option.ResourceKey}");
                    _loadingNewPart = false;
                    return;
                }
                
                Debug.Log($"Loaded: '{option.ResourceKey}'");
                var asset = Instantiate(x, _generatedObject.transform, false) as GameObject;

                _loadingNewPart = false;
                
                if (Text != null)
                    Text.text = $"{Path.GetFileNameWithoutExtension(asset.name).Replace("(Clone)", "")} [{_optionIndex + 1} / {_options?.Count}]";
            }));
        }

    }
}