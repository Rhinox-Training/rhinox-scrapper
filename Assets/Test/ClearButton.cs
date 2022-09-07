using Rhinox.Scrapper;
using UnityEngine;
using UnityEngine.UI;

namespace Test
{
    [RequireComponent(typeof(Button))]
    public class ClearButton : MonoBehaviour
    {
        private Button _button;

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
            Scrapper.ClearCache();
        }
    }
}