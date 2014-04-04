using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNet.Abstractions;
using Microsoft.AspNet.DependencyInjection;
using Microsoft.AspNet.DependencyInjection.Fallback;
using Microsoft.AspNet.SignalR.Hosting;
using Microsoft.AspNet.SignalR.Http;
using Microsoft.AspNet.SignalR.Hubs;
using Microsoft.AspNet.SignalR.Infrastructure;
//using Microsoft.AspNet.SignalR.Tests.Common.Infrastructure;
using Moq;
using Newtonsoft.Json;
using Xunit;

namespace Microsoft.AspNet.SignalR.Tests.Server.Hubs
{
    public class HubDispatcherFacts
    {
        public static IEnumerable<string[]> JSProxyUrls
        {
            get
            {
                return new List<string[]>()
                {
                    new []{"http://something/signalr/hubs"},
                    new []{"http://something/signalr/js"}
                };
            }
        }

        [Theory]
        [InlineData("JSProxyUrls")]
        public void RequestingSignalrHubsUrlReturnsProxy(string proxyUrl)
        {
            // Arrange
            var dispatcher = new HubDispatcher(new HubConfiguration());
            var request = GetRequestForUrl(proxyUrl);
            var response = new Mock<IResponse>();
            string contentType = null;
            var buffer = new List<string>();
            response.SetupSet(m => m.ContentType = It.IsAny<string>()).Callback<string>(type => contentType = type);
            response.Setup(m => m.Write(It.IsAny<ArraySegment<byte>>())).Callback<ArraySegment<byte>>(data => buffer.Add(Encoding.UTF8.GetString(data.Array, data.Offset, data.Count)));

            // Act
            var context = new HostContext(request.Object, response.Object);

            var serviceProvider = new ServiceCollection()
                                    .Add(SignalRServices.GetServices())
                                    .BuildServiceProvider();

            dispatcher.Initialize(serviceProvider);
            dispatcher.ProcessRequest(context).Wait();

            // Assert
            Assert.Equal("application/javascript; charset=UTF-8", contentType);
            Assert.Equal(1, buffer.Count);
            Assert.NotNull(buffer[0]);
            Assert.False(buffer[0].StartsWith("throw new Error("));
        }

        [Theory]
        [InlineData("JSProxyUrls")]
        public void RequestingSignalrHubsUrlWithTrailingSlashReturnsProxy(string proxyUrl)
        {
            // Arrange
            var dispatcher = new HubDispatcher(new HubConfiguration());
            var request = GetRequestForUrl(proxyUrl);
            var response = new Mock<IResponse>();
            string contentType = null;
            var buffer = new List<string>();
            response.SetupSet(m => m.ContentType = It.IsAny<string>()).Callback<string>(type => contentType = type);
            response.Setup(m => m.Write(It.IsAny<ArraySegment<byte>>())).Callback<ArraySegment<byte>>(data => buffer.Add(Encoding.UTF8.GetString(data.Array, data.Offset, data.Count)));

            // Act
            var context = new HostContext(request.Object, response.Object);
            var serviceProvider = new ServiceCollection()
                        .Add(SignalRServices.GetServices())
                        .BuildServiceProvider();

            dispatcher.Initialize(serviceProvider);
            dispatcher.ProcessRequest(context).Wait();

            // Assert
            Assert.Equal("application/javascript; charset=UTF-8", contentType);
            Assert.Equal(1, buffer.Count);
            Assert.NotNull(buffer[0]);
            Assert.False(buffer[0].StartsWith("throw new Error("));
        }

        [Theory]
        [InlineData("JSProxyUrls")]
        public void RequestingSignalrHubsUrlWithJavaScriptProxiesDesabledDoesNotReturnProxy(string proxyUrl)
        {
            // Arrange
            var dispatcher = new HubDispatcher(new HubConfiguration() { EnableJavaScriptProxies = false });
            var request = GetRequestForUrl(proxyUrl);
            var response = new Mock<IResponse>();
            string contentType = null;
            var buffer = new List<string>();
            response.SetupSet(m => m.ContentType = It.IsAny<string>()).Callback<string>(type => contentType = type);
            response.Setup(m => m.Write(It.IsAny<ArraySegment<byte>>())).Callback<ArraySegment<byte>>(data => buffer.Add(Encoding.UTF8.GetString(data.Array, data.Offset, data.Count)));

            // Act
            var context = new HostContext(request.Object, response.Object);
            var serviceProvider = new ServiceCollection()
                        .Add(SignalRServices.GetServices())
                        .BuildServiceProvider();

            dispatcher.Initialize(serviceProvider);
            dispatcher.ProcessRequest(context).Wait();

            // Assert
            Assert.Equal("application/javascript; charset=UTF-8", contentType);
            Assert.Equal(1, buffer.Count);
            Assert.True(buffer[0].StartsWith("throw new Error("));
        }

        [Fact]
        public void DetailedErrorsAreDisabledByDefault()
        {
            // Arrange
            var dispatcher = new HubDispatcher(new HubConfiguration());

            var request = GetRequestForUrl("http://something/signalr/send");
            var qs = new Mock<IReadableStringCollection>();
            request.Setup(m => m.QueryString).Returns(qs.Object);
            qs.Setup(m => m["transport"]).Returns("longPolling");
            qs.Setup(m => m["connectionToken"]).Returns("longPolling");
            qs.Setup(m => m["transport"]).Returns("longPolling");

            var form = new Mock<IReadableStringCollection>();
            request.Setup(m => m.ReadForm()).Returns(Task.FromResult<IReadableStringCollection>(form.Object));

            string contentType = null;
            var buffer = new List<string>();

            var response = new Mock<IResponse>();
            response.SetupGet(m => m.CancellationToken).Returns(CancellationToken.None);
            response.SetupSet(m => m.ContentType = It.IsAny<string>()).Callback<string>(type => contentType = type);
            response.Setup(m => m.Write(It.IsAny<ArraySegment<byte>>())).Callback<ArraySegment<byte>>(data => buffer.Add(Encoding.UTF8.GetString(data.Array, data.Offset, data.Count)));

            // Act
            var context = new HostContext(request.Object, response.Object);

            var serviceProvider = new ServiceCollection()
            .Add(SignalRServices.GetServices())
            .AddTransient<ErrorHub, ErrorHub>()
            .BuildServiceProvider();

            dispatcher.Initialize(serviceProvider);

            //TODO: register IProtectedData = how to get EmptyProtectedData
            // resolver.Register(typeof(IProtectedData), () => new EmptyProtectedData());

            dispatcher.ProcessRequest(context).Wait();

            var json = JsonSerializer.Create(new JsonSerializerSettings());

            // Assert
            Assert.Equal("application/json; charset=UTF-8", contentType);
            Assert.True(buffer.Count > 0);

            using (var reader = new StringReader(String.Join(String.Empty, buffer)))
            {
                var hubResponse = (HubResponse)json.Deserialize(reader, typeof(HubResponse));
                Assert.Contains("ErrorHub.Error", hubResponse.Error);
                Assert.DoesNotContain("Custom", hubResponse.Error);
            }
        }

        [Fact]
        public void DetailedErrorsFromFaultedTasksAreDisabledByDefault()
        {
            // Arrange
            var dispatcher = new HubDispatcher(new HubConfiguration());

            var request = GetRequestForUrl("http://something/signalr/send");

            var qs = new Mock<IReadableStringCollection>();
            qs.Setup(m => m["transport"]).Returns("longPolling");
            qs.Setup(m => m["connectionToken"]).Returns("0");
            qs.Setup(m => m["data"]).Returns("{\"H\":\"ErrorHub\",\"M\":\"ErrorTask\",\"A\":[],\"I\":0}");

            request.Setup(m => m.QueryString).Returns(qs.Object);

            var res = new Mock<IReadableStringCollection>();
            request.Setup(m => m.ReadForm()).Returns(Task.FromResult<IReadableStringCollection>(res.Object));

            string contentType = null;
            var buffer = new List<string>();

            var response = new Mock<IResponse>();
            response.SetupGet(m => m.CancellationToken).Returns(CancellationToken.None);
            response.SetupSet(m => m.ContentType = It.IsAny<string>()).Callback<string>(type => contentType = type);
            response.Setup(m => m.Write(It.IsAny<ArraySegment<byte>>())).Callback<ArraySegment<byte>>(data => buffer.Add(Encoding.UTF8.GetString(data.Array, data.Offset, data.Count)));

            // Act
            var context = new HostContext(request.Object, response.Object);

            var serviceProvider = new ServiceCollection()
            .Add(SignalRServices.GetServices())
            .AddTransient<ErrorHub, ErrorHub>()
            .BuildServiceProvider();

            dispatcher.Initialize(serviceProvider);

            // TODO
            // resolver.Register(typeof(IProtectedData), () => new EmptyProtectedData());

            dispatcher.ProcessRequest(context).Wait();

            var json = JsonSerializer.Create(new JsonSerializerSettings());

            // Assert
            Assert.Equal("application/json; charset=UTF-8", contentType);
            Assert.True(buffer.Count > 0);

            using (var reader = new StringReader(String.Join(String.Empty, buffer)))
            {
                var hubResponse = (HubResponse)json.Deserialize(reader, typeof(HubResponse));
                Assert.Contains("ErrorHub.ErrorTask", hubResponse.Error);
                Assert.DoesNotContain("Custom", hubResponse.Error);
            }
        }

        [Fact]
        public void DetailedErrorsCanBeEnabled()
        {
            // Arrange
            var dispatcher = new HubDispatcher(new HubConfiguration() { EnableDetailedErrors = true });

            var request = GetRequestForUrl("http://something/signalr/send");

            var qs = new Mock<IReadableStringCollection>();
            qs.Setup(m => m["transport"]).Returns("longPolling");
            qs.Setup(m => m["connectionToken"]).Returns("0");
            qs.Setup(m => m["data"]).Returns("{\"H\":\"ErrorHub\",\"M\":\"ErrorTask\",\"A\":[],\"I\":0}");

            request.Setup(m => m.QueryString).Returns(qs.Object);

            var form = new Mock<IReadableStringCollection>();
            request.Setup(m => m.ReadForm()).Returns(Task.FromResult<IReadableStringCollection>(form.Object));

            string contentType = null;
            var buffer = new List<string>();

            var response = new Mock<IResponse>();
            response.SetupGet(m => m.CancellationToken).Returns(CancellationToken.None);
            response.SetupSet(m => m.ContentType = It.IsAny<string>()).Callback<string>(type => contentType = type);
            response.Setup(m => m.Write(It.IsAny<ArraySegment<byte>>())).Callback<ArraySegment<byte>>(data => buffer.Add(Encoding.UTF8.GetString(data.Array, data.Offset, data.Count)));

            // Act
            var context = new HostContext(request.Object, response.Object);

            var serviceProvider = new ServiceCollection()
            .Add(SignalRServices.GetServices())
            .AddTransient<ErrorHub, ErrorHub>()
            .BuildServiceProvider();

            dispatcher.Initialize(serviceProvider);

            // TODO
            // resolver.Register(typeof(ErrorHub), () => new ErrorHub());

            dispatcher.ProcessRequest(context).Wait();

            var json = JsonSerializer.Create(new JsonSerializerSettings());


            // Assert
            Assert.Equal("application/json; charset=UTF-8", contentType);
            Assert.True(buffer.Count > 0);

            using (var reader = new StringReader(String.Join(String.Empty, buffer)))
            {
                var hubResponse = (HubResponse)json.Deserialize(reader, typeof(HubResponse));
                Assert.Equal("Custom Error.", hubResponse.Error);
            }
        }

        [Fact]
        public void DetailedErrorsFromFaultedTasksCanBeEnabled()
        {
            // Arrange
            var dispatcher = new HubDispatcher(new HubConfiguration() { EnableDetailedErrors = true });

            var request = GetRequestForUrl("http://something/signalr/send");

            var qs = new Mock<IReadableStringCollection>();
            qs.Setup(m => m["transport"]).Returns("longPolling");
            qs.Setup(m => m["connectionToken"]).Returns("0");
            qs.Setup(m => m["data"]).Returns("{\"H\":\"ErrorHub\",\"M\":\"ErrorTask\",\"A\":[],\"I\":0}");

            request.Setup(m => m.QueryString).Returns(qs.Object);

            var form = new Mock<IReadableStringCollection>();
            request.Setup(m => m.ReadForm()).Returns(Task.FromResult<IReadableStringCollection>(form.Object));

            string contentType = null;
            var buffer = new List<string>();

            var response = new Mock<IResponse>();
            response.SetupGet(m => m.CancellationToken).Returns(CancellationToken.None);
            response.SetupSet(m => m.ContentType = It.IsAny<string>()).Callback<string>(type => contentType = type);
            response.Setup(m => m.Write(It.IsAny<ArraySegment<byte>>())).Callback<ArraySegment<byte>>(data => buffer.Add(Encoding.UTF8.GetString(data.Array, data.Offset, data.Count)));

            // Act
            var context = new HostContext(request.Object, response.Object);

            var serviceProvider = new ServiceCollection()
            .Add(SignalRServices.GetServices())
            .AddTransient<ErrorHub, ErrorHub>()
            .BuildServiceProvider();

            dispatcher.Initialize(serviceProvider);

            // TODO
            // resolver.Register(typeof(ErrorHub), () => new ErrorHub());

            dispatcher.ProcessRequest(context).Wait();

            var json = JsonSerializer.Create(new JsonSerializerSettings());


            // Assert
            Assert.Equal("application/json; charset=UTF-8", contentType);
            Assert.True(buffer.Count > 0);

            using (var reader = new StringReader(String.Join(String.Empty, buffer)))
            {
                var hubResponse = (HubResponse)json.Deserialize(reader, typeof(HubResponse));
                Assert.Equal("Custom Error from task.", hubResponse.Error);
            }
        }

        [Fact]
        public void DuplicateHubNamesThrows()
        {
            // Arrange
            var dispatcher = new HubDispatcher(new HubConfiguration());
            var request = new Mock<IRequest>();

            var qs = new Mock<IReadableStringCollection>();
            qs.Setup(m => m["connectionData"]).Returns(@"[{name: ""foo""}, {name: ""Foo""}]");
            request.Setup(m => m.QueryString).Returns(qs.Object);

            var mockHub = new Mock<IHub>();
            var mockHubManager = new Mock<IHubManager>();
            mockHubManager.Setup(m => m.GetHub("foo")).Returns(new HubDescriptor { Name = "foo", HubType = mockHub.Object.GetType() });

            var serviceProvider = new ServiceCollection()
            .Add(SignalRServices.GetServices())
            .AddInstance<IHubManager>(mockHubManager.Object)
            .BuildServiceProvider();

            dispatcher.Initialize(serviceProvider);
            Assert.Throws<InvalidOperationException>(() => dispatcher.Authorize(request.Object));
        }

        private static Mock<IRequest> GetRequestForUrl(string url)
        {
            var request = new Mock<IRequest>();
            var qs = new Mock<IReadableStringCollection>();

            var uri = new Uri(url);
            //request.Setup(m => m).Returns(uri);
            request.Setup(m => m.LocalPath).Returns(uri.LocalPath);
            request.Setup(m => m.QueryString).Returns(qs.Object);
            return request;
        }

        private class ErrorHub : Hub
        {
            public void Error()
            {
                throw new Exception("Custom Error.");
            }

            public async Task ErrorTask()
            {
                await TaskAsyncHelper.Delay(TimeSpan.FromMilliseconds(1));
                throw new Exception("Custom Error from task.");
            }
        }
    }
}
