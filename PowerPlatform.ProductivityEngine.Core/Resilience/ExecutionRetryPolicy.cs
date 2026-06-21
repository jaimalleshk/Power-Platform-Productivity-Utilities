using System;
using System.Threading.Tasks;

namespace PowerPlatform.ProductivityEngine.Core.Resilience
{
    public static class ExecutionRetryPolicy
    {
        public static async Task<T> ExecuteWithRetryAsync<T>(
            Func<Task<T>> action, 
            int maxRetries = 5, 
            Action<Exception, int, int> onRetry = null)
        {
            int attempt = 0;
            while (true)
            {
                try
                {
                    return await action().ConfigureAwait(false);
                }
                catch (Exception ex) when (IsTransient(ex) && attempt < maxRetries)
                {
                    attempt++;
                    int delayMs = (int)Math.Pow(2, attempt) * 1000;
                    onRetry?.Invoke(ex, attempt, delayMs);
                    await Task.Delay(delayMs).ConfigureAwait(false);
                }
            }
        }

        public static async Task ExecuteWithRetryAsync(
            Func<Task> action, 
            int maxRetries = 5, 
            Action<Exception, int, int> onRetry = null)
        {
            await ExecuteWithRetryAsync<bool>(async () =>
            {
                await action().ConfigureAwait(false);
                return true;
            }, maxRetries, onRetry).ConfigureAwait(false);
        }

        private static bool IsTransient(Exception ex)
        {
            if (ex is TimeoutException || ex is System.IO.IOException || ex is System.Net.Sockets.SocketException)
                return true;

            // Handle Dataverse Client service exceptions if they are transient
            string message = ex.Message;
            if (message.Contains("TooManyRequests") || message.Contains("429") || 
                message.Contains("Bad Gateway") || message.Contains("502") || 
                message.Contains("Service Unavailable") || message.Contains("503") || 
                message.Contains("Gateway Timeout") || message.Contains("504"))
            {
                return true;
            }

            if (ex.InnerException != null)
            {
                return IsTransient(ex.InnerException);
            }

            return false;
        }
    }
}
