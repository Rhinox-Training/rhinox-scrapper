using System;

namespace Rhinox.Scrapper
{
    public static class ProgressHelper
    {
        public static Progress<float> Pipe(IProgress<float> source, float baseValue, float pipedAmount)
        {
            var subProgressHandler = new Progress<float>();
            subProgressHandler.ProgressChanged += (_, v) =>
            {
                source.Report(baseValue + v * pipedAmount);
            };
            return subProgressHandler;
        }
        
        public static Progress<ProgressBytes> PipeBytes(IProgress<float> source, float baseValue, float pipedAmount)
        {
            var subProgressHandler = new Progress<ProgressBytes>();
            subProgressHandler.ProgressChanged += (_, v) =>
            {
                source.Report(baseValue + v.Progress * pipedAmount);
            };
            return subProgressHandler;
        }
        
        public static Progress<float> Pipe(IProgress<ProgressBytes> source, long totalBytes, float baseValue, float pipedAmount)
        {
            var subProgressHandler = new Progress<float>();
            subProgressHandler.ProgressChanged += (_, v) =>
            {
                source.Report(new ProgressBytes(baseValue + v * pipedAmount, totalBytes));
            };
            return subProgressHandler;
        }
    }
}