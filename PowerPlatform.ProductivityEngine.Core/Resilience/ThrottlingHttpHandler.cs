using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using PowerPlatform.ProductivityEngine.Core.Authentication;
using PowerPlatform.ProductivityEngine.Core.Connections;

namespace PowerPlatform.ProductivityEngine.Core.Resilience
{
    public class ThrottlingHttpHandler : DelegatingHandler
    {
        private readonly IAuthenticationProvider _authProvider;
        private readonly ConnectionProfile _profile;

        // Centralized, environment-specific semaphores to coordinate throttling cross-threads
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> EnvironmentSemaphores = 
            new ConcurrentDictionary<string, SemaphoreSlim>();

        public ThrottlingHttpHandler(IAuthenticationProvider authProvider, ConnectionProfile profile)
        {
            _authProvider = authProvider ?? throw new ArgumentNullException(nameof(authProvider));
            _profile = profile ?? throw new ArgumentNullException(nameof(profile));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string environmentKey = _profile.EnvironmentUrl.TrimEnd('/').ToLowerInvariant();
            var semaphore = EnvironmentSemaphores.GetOrAdd(environmentKey, _ => new SemaphoreSlim(1, 1));

            int maxRetries = 5;
            int attempt = 0;

            while (true)
            {
                // 1. Wait on the semaphore to check if we are currently throttled by another thread
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                semaphore.Release();

                // 2. Attach or renew OAuth Bearer token
                string token = await _authProvider.GetAccessTokenAsync(_profile).ConfigureAwait(false);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

                HttpResponseMessage response = null;
                Exception caughtException = null;

                try
                {
                    // Clone the request if we are doing retries, because HttpClient doesn't allow sending the same request twice
                    // (we skip cloning on the very first attempt to avoid overhead if it succeeds)
                    HttpRequestMessage requestToSend = request;
                    if (attempt > 0)
                    {
                        requestToSend = await CloneHttpRequestMessageAsync(request).ConfigureAwait(false);
                    }

                    response = await base.SendAsync(requestToSend, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (IsTransientException(ex) && attempt < maxRetries)
                {
                    caughtException = ex;
                }

                if (caughtException != null)
                {
                    attempt++;
                    int delayMs = (int)Math.Pow(2, attempt) * 1000;
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                // 3. Handle Throttling (HTTP 429)
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    attempt++;
                    if (attempt > maxRetries)
                    {
                        return response;
                    }

                    // Calculate delay from Retry-After header
                    int delayMs = 0;
                    if (response.Headers.RetryAfter != null)
                    {
                        if (response.Headers.RetryAfter.Delta.HasValue)
                        {
                            delayMs = (int)response.Headers.RetryAfter.Delta.Value.TotalMilliseconds;
                        }
                        else if (response.Headers.RetryAfter.Date.HasValue)
                        {
                            delayMs = (int)(response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow).TotalMilliseconds;
                        }
                    }

                    if (delayMs <= 0)
                    {
                        delayMs = (int)Math.Pow(2, attempt) * 1000;
                    }

                    // Pause all other parallel workers for that target environment using the Semaphore
                    await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                    try
                    {
                        await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        semaphore.Release();
                    }

                    continue; // Retry
                }

                // 4. Handle Transient Faults (502, 503, 504)
                if (IsTransientStatusCode(response.StatusCode) && attempt < maxRetries)
                {
                    attempt++;
                    int delayMs = (int)Math.Pow(2, attempt) * 1000;
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                    continue; // Retry
                }

                return response;
            }
        }

        private static bool IsTransientStatusCode(HttpStatusCode statusCode)
        {
            return statusCode == HttpStatusCode.BadGateway ||          // 502
                   statusCode == HttpStatusCode.ServiceUnavailable ||    // 503
                   statusCode == HttpStatusCode.GatewayTimeout;          // 504
        }

        private static bool IsTransientException(Exception ex)
        {
            return ex is System.IO.IOException || 
                   ex is System.Net.Sockets.SocketException || 
                   ex is TaskCanceledException || 
                   ex is TimeoutException;
        }

        // Helper to clone request messages for retries
        private static async Task<HttpRequestMessage> CloneHttpRequestMessageAsync(HttpRequestMessage req)
        {
            var clone = new HttpRequestMessage(req.Method, req.RequestUri)
            {
                Version = req.Version
            };

            // Copy headers
            foreach (var header in req.Headers)
            {
                clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            // Copy properties
            foreach (var prop in req.Options)
            {
                clone.Options.Set(new HttpRequestOptionsKey<object>(prop.Key), prop.Value);
            }

            // Copy content
            if (req.Content != null)
            {
                var contentBytes = await req.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                clone.Content = new ByteArrayContent(contentBytes);

                // Copy content headers
                foreach (var header in req.Content.Headers)
                {
                    clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return clone;
        }
    }
}
