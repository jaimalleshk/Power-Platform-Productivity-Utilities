using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using PowerPlatform.ProductivityEngine.Core.Authentication;
using PowerPlatform.ProductivityEngine.Core.Connections;
using PowerPlatform.ProductivityEngine.Core.Resilience;
using Xunit;

namespace Core.Resilience.Tests
{
    public class ThrottlingHttpHandlerTests
    {
        private class MockAuthenticationProvider : IAuthenticationProvider
        {
            public Task<string> GetAccessTokenAsync(ConnectionProfile profile)
            {
                return Task.FromResult("dummy_token");
            }

            public void ClearTokenCache(string environmentUrl)
            {
            }
        }

        private class MockInnerHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handlerFunc;

            public int RequestCount { get; private set; }

            public MockInnerHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handlerFunc)
            {
                _handlerFunc = handlerFunc;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestCount++;
                return _handlerFunc(request, cancellationToken);
            }
        }

        [Fact]
        public async Task SendAsync_On429WithRetryAfter_RetriesAfterDelayAndSucceeds()
        {
            // Arrange
            var profile = new ConnectionProfile
            {
                EnvironmentUrl = "https://mockorg.crm.dynamics.com",
                TimeoutSeconds = 10
            };
            var authProvider = new MockAuthenticationProvider();

            int callCount = 0;
            var innerHandler = new MockInnerHandler((req, cancel) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    // Return 429 with Retry-After header of 1 second (1000 ms) for fast testing
                    var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                    response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
                    return Task.FromResult(response);
                }
                else
                {
                    // Return 200 OK
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
                }
            });

            var throttlingHandler = new ThrottlingHttpHandler(authProvider, profile)
            {
                InnerHandler = innerHandler
            };

            var client = new HttpClient(throttlingHandler);
            var stopwatch = Stopwatch.StartNew();

            // Act
            var res = await client.GetAsync("https://mockorg.crm.dynamics.com/api/data/v9.2/Accounts");

            // Assert
            stopwatch.Stop();
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Equal(2, innerHandler.RequestCount);
            // Verify that the delay was at least 1 second (950ms to allow small scheduler deviation)
            Assert.True(stopwatch.ElapsedMilliseconds >= 950, $"Expected delay of at least 1000ms, but was {stopwatch.ElapsedMilliseconds}ms");
        }

        [Fact]
        public async Task SendAsync_OnTransientError_RetriesExponentiallyAndSucceeds()
        {
            // Arrange
            var profile = new ConnectionProfile
            {
                EnvironmentUrl = "https://mockorg.crm.dynamics.com",
                TimeoutSeconds = 10
            };
            var authProvider = new MockAuthenticationProvider();

            int callCount = 0;
            var innerHandler = new MockInnerHandler((req, cancel) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    // Return 503 Service Unavailable
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
                }
                else
                {
                    // Return 200 OK
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
                }
            });

            var throttlingHandler = new ThrottlingHttpHandler(authProvider, profile)
            {
                InnerHandler = innerHandler
            };

            var client = new HttpClient(throttlingHandler);
            var stopwatch = Stopwatch.StartNew();

            // Act
            var res = await client.GetAsync("https://mockorg.crm.dynamics.com/api/data/v9.2/Accounts");

            // Assert
            stopwatch.Stop();
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            Assert.Equal(2, innerHandler.RequestCount);
            // Exponential backoff for attempt 1 is 2^1 * 1000 = 2000 ms
            Assert.True(stopwatch.ElapsedMilliseconds >= 1950, $"Expected delay of at least 2000ms, but was {stopwatch.ElapsedMilliseconds}ms");
        }

        [Fact]
        public async Task SendAsync_ParallelRequests_AreSequentializedDuringThrottling()
        {
            // Arrange
            var profile = new ConnectionProfile
            {
                EnvironmentUrl = "https://mockorg.crm.dynamics.com",
                TimeoutSeconds = 10
            };
            var authProvider = new MockAuthenticationProvider();

            int callCount = 0;
            var innerHandler = new MockInnerHandler((req, cancel) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    // First request hits 429, delays for 1 second (1000ms)
                    var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                    response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
                    return Task.FromResult(response);
                }
                // Subsequent requests return 200 OK
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

            var throttlingHandler1 = new ThrottlingHttpHandler(authProvider, profile) { InnerHandler = innerHandler };
            var throttlingHandler2 = new ThrottlingHttpHandler(authProvider, profile) { InnerHandler = innerHandler };

            var client1 = new HttpClient(throttlingHandler1);
            var client2 = new HttpClient(throttlingHandler2);

            var stopwatch = Stopwatch.StartNew();

            // Act: Start both requests concurrently
            var task1 = client1.GetAsync("https://mockorg.crm.dynamics.com/api/data/v9.2/Accounts");
            // Delay slightly before starting the second task to guarantee task1 hits 429 first
            await Task.Delay(100);
            var task2 = client2.GetAsync("https://mockorg.crm.dynamics.com/api/data/v9.2/Contacts");

            await Task.WhenAll(task1, task2);
            stopwatch.Stop();

            // Assert
            // The first request will run, fail with 429, lock the semaphore, delay 1s, retry, and succeed (2 calls total for task1).
            // The second request starts, waits for the semaphore lock to be released, then runs and succeeds (1 call total for task2).
            // Total calls should be 3.
            Assert.Equal(3, innerHandler.RequestCount);
            // Verify that the elapsed time is at least 1s (reflecting the 1s delay)
            Assert.True(stopwatch.ElapsedMilliseconds >= 1000, $"Expected elapsed time to incorporate the throttling delay, was {stopwatch.ElapsedMilliseconds}ms");
        }
    }
}
