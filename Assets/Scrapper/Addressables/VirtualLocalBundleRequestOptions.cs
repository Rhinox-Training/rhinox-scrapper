using System;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.ResourceManagement.Util;

namespace Rhinox.Scrapper
{
    /// <summary>
    /// Custom version of AssetBundleRequestOptions used to compute needed download size while bypassing the cache.  In the future a virtual cache may be implemented.
    /// </summary>
    [Serializable]
    public class VirtualLocalBundleRequestOptions : AssetBundleRequestOptions
    {
        /// <inheritdoc/>
        public override long ComputeSize(IResourceLocation location, ResourceManager resourceManager)
        {
            return BundleSize;
        }
    }
}