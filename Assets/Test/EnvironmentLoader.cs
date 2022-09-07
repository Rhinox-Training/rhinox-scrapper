using System;
using System.IO;
using System.Linq;
using Rhinox.Lightspeed;
using Rhinox.Lightspeed.IO;
using Rhinox.Perceptor;
using UnityEngine;

namespace Modulab
{
    public static class EnvironmentLoader
    {
        public static string AddressableCatalogUrl;
        
        public static bool Load(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;
            if (!Path.IsPathRooted(path))
            {
            #if UNITY_EDITOR
                path = FileHelper.GetFullPath(path, FileHelper.GetProjectPath());        
            #else
                path = FileHelper.GetFullPath(path, Application.streamingAssetsPath);
            #endif
            }

            if (File.Exists(path))
            {
                var content = File.ReadAllLines(path);

                AddressableCatalogUrl = FindValue(content, "CATALOG_URL");
                PLog.Info($"AddressableCatalogUrl: {AddressableCatalogUrl}");
            }
            else
            {
                PLog.Warn($"ModulabConfig file not found: {path}");
            }
            
            return true;
        }

        private static string FindValue(string[] content, string key, string defaultVal = null)
        {
            key += "=";
            
            var line = content
                .Where(x => x != null)
                .FirstOrDefault(x => x.StartsWith(key, StringComparison.InvariantCultureIgnoreCase));
            if (line == null)
                return defaultVal;

            line = line.Replace(key, "").Trim();
            return line;
        }
    }
}