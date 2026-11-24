using System.Net;
using Client;
using Xunit;

namespace Client.Tests
{
    public class ClientApplicationTests
    {
        [Fact]
        public async Task SuccessfulScenario_CreatesEventAndPrintsServerResponse()
        {
            TestHandler handler = new TestHandler(request =>
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("created")
                };
            });
            using (HttpClient httpClient = new HttpClient(handler))
            {
                httpClient.BaseAddress = new Uri("http://localhost:5000");
                using (StringReader input = new StringReader("Конференция\n1\n1"))
                {
                    using (StringWriter output = new StringWriter())
                    {
                        await new ClientApplication(httpClient, input, output).ExecuteAsync("3");
                        Assert.Equal(HttpMethod.Get, handler.Request!.Method);
                        Assert.Equal("/api/events", handler.Request.RequestUri!.AbsolutePath);
                        Assert.Contains("Статус: OK", output.ToString());
                        Assert.Contains("created", output.ToString());
                    }
                }
            }
        }

        [Fact]
        public async Task JsonResponse_PrintsIndentedJson()
        {
            TestHandler handler = new TestHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"primary\":true,\"subscribers\":[{\"name\":\"Logger\"}]}")
            });
            using HttpClient httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
            using StringWriter output = new StringWriter();

            await new ClientApplication(httpClient, new StringReader(""), output).ExecuteAsync("8");

            Assert.Contains("  \"primary\": true", output.ToString());
            Assert.Contains("    {", output.ToString());
            Assert.DoesNotContain("{\"primary\":true", output.ToString());
        }

        [Fact]
        public async Task UnknownOperation_PrintsErrorAndDoesNotCallServer()
        {
            TestHandler handler = new TestHandler(request =>
            {
                throw new InvalidOperationException("HTTP не должен вызываться");
            });
            using (HttpClient httpClient = new HttpClient(handler))
            {
                using (StringWriter output = new StringWriter())
                {
                    await new ClientApplication(httpClient, new StringReader(string.Empty), output).ExecuteAsync("99");
                    Assert.Contains("Неверная операция.", output.ToString());
                    Assert.Null(handler.Request);
                }
            }
        }

        [Fact]
        public async Task InvalidData_PrintsInputError()
        {
            TestHandler handler = new TestHandler(request =>
            {
                throw new InvalidOperationException("HTTP не должен вызываться");
            });
            using (HttpClient httpClient = new HttpClient(handler))
            {
                using (StringWriter output = new StringWriter())
                {
                    await new ClientApplication(httpClient, new StringReader("не число"), output).ExecuteAsync("5");
                    Assert.Contains("Ошибка:", output.ToString());
                    Assert.Null(handler.Request);
                }
            }
        }

        [Fact]
        public async Task UnavailableServer_PrintsConnectionError()
        {
            TestHandler handler = new TestHandler(request =>
            {
                throw new HttpRequestException("Сервер недоступен");
            });
            using (HttpClient httpClient = new HttpClient(handler))
            {
                httpClient.BaseAddress = new Uri("http://localhost:5000");
                using (StringWriter output = new StringWriter())
                {
                    await new ClientApplication(httpClient, new StringReader("Конференция\n1\n1"), output).ExecuteAsync("3");
                    Assert.Contains("Ошибка: Сервер недоступен", output.ToString());
                }
            }
        }

        [Fact]
        public async Task Registration_ShowsPublicationResult()
        {
            TestHandler handler = new TestHandler(request =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("SUCCESS") };
                response.Headers.Add("X-Event-Publication", "PUBLISHED");
                return response;
            });
            using (HttpClient httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") })
            using (StringWriter output = new StringWriter())
            {
                await new ClientApplication(httpClient, new StringReader("1\n2"), output, "Client2").ExecuteAsync("5");
                Assert.Equal("/api/registrations", handler.Request!.RequestUri!.AbsolutePath);
                Assert.Equal("POST", handler.Request.Method.Method);
                Assert.Equal("Client2", handler.Request.Headers.GetValues("X-Client-Id").Single());
                Assert.Contains("Событие опубликовано через Pub/Sub", output.ToString());
            }
        }

        [Theory]
        [InlineData("8", "/api/consensus", "GET")]
        [InlineData("10", "/api/consensus/check", "POST")]
        public async Task ConsensusCommands_UseExpectedEndpoint(string choice, string path, string method)
        {
            TestHandler handler = new TestHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}")
            });
            using HttpClient httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
            using StringWriter output = new StringWriter();
            await new ClientApplication(httpClient, new StringReader(""), output).ExecuteAsync(choice);
            Assert.Equal(path, handler.Request!.RequestUri!.AbsolutePath);
            Assert.Equal(method, handler.Request.Method.Method);
        }

        [Theory]
        [InlineData("9", "3\n0", "/api/consensus/3/availability")]
        [InlineData("11", "4\n1", "/api/consensus/4/vote")]
        public async Task NodeCommands_UseNodeId(string choice, string input, string path)
        {
            TestHandler handler = new TestHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
            using HttpClient http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
            using StringWriter output = new StringWriter();
            await new ClientApplication(http, new StringReader(input), output).ExecuteAsync(choice);
            Assert.Equal(path, handler.Request!.RequestUri!.AbsolutePath);
            Assert.Equal(HttpMethod.Post, handler.Request.Method);
        }

        [Theory]
        [InlineData("9", "6\n1")]
        [InlineData("11", "1\nmaybe")]
        public async Task NodeCommands_RejectInvalidInput(string choice, string input)
        {
            TestHandler handler = new TestHandler(_ => throw new Exception("Unexpected request"));
            using HttpClient http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
            using StringWriter output = new StringWriter();
            await new ClientApplication(http, new StringReader(input), output).ExecuteAsync(choice);
            Assert.Null(handler.Request);
            Assert.Contains("Ошибка", output.ToString());
        }

        [Fact]
        public async Task Registration_PrintsConsensusFailure()
        {
            TestHandler handler = new TestHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{\"achieved\":false,\"yes\":2}") });
            using HttpClient http = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") };
            using StringWriter output = new StringWriter();
            await new ClientApplication(http, new StringReader("1\n2"), output).ExecuteAsync("5");
            Assert.Contains("ServiceUnavailable", output.ToString());
            Assert.Contains("\"achieved\": false", output.ToString());
        }

        [Fact]
        public async Task FailoverResponse_PrintsReplicaAndSynchronizationNotice()
        {
            TestHandler handler = new TestHandler(request =>
            {
                HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") };
                response.Headers.Add("X-Data-Instance", "Replica");
                response.Headers.Add("X-Replication", "PENDING");
                response.Headers.Add("X-Synchronization-Required", "true");
                return response;
            });
            using (HttpClient httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5000") })
            using (StringWriter output = new StringWriter())
            {
                await new ClientApplication(httpClient, new StringReader(""), output).ExecuteAsync("4");
                Assert.Contains("Использованный экземпляр: Replica", output.ToString());
                Assert.Contains("Требуется синхронизация", output.ToString());
            }
        }

        private sealed class TestHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> responseFactory;
            public TestHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
            {
                this.responseFactory = responseFactory;
            }

            public HttpRequestMessage? Request { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Request = request;
                return Task.FromResult(responseFactory(request));
            }
        }
    }
}
