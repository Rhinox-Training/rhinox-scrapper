namespace Rhinox.Scrapper
{
    public struct ProgressBytes
    {
        public float Progress;
        public long TotalBytes;

        public ProgressBytes(float progress, long total)
        {
            Progress = progress;
            TotalBytes = total;
        }
    }
}