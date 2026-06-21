using System;

namespace PowerPlatform.ProductivityEngine.Core.Reporting
{
    public enum ProgressStatus
    {
        Info,
        Warning,
        Success,
        Error
    }

    public class ProgressUpdate
    {
        public string Stage { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public double PercentComplete { get; set; }
        public ProgressStatus Status { get; set; } = ProgressStatus.Info;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
