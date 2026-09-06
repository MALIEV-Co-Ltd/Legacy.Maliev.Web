// <copyright file="CncUploadAdmissionMiddlewareTests.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Tests
{
    using Legacy.Maliev.Web;
    using Microsoft.AspNetCore.Builder;
    using Microsoft.AspNetCore.Hosting;
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Http.Features;
    using Microsoft.Extensions.Primitives;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Net;
    using System.Net.Http;
    using System.Net.Sockets;
    using System.Text;
    using System.Threading.Tasks;
    using Xunit;

    /// <summary>
    /// Guards CNC upload resource admission before Razor multipart model binding.
    /// </summary>
    public class CncUploadAdmissionMiddlewareTests
    {
        private const long Megabyte = 1024L * 1024L;

        private const long MultipartAllowance = 256L * 1024L;

        /// <summary>
        /// The middleware sets the role-specific transport ceiling before invoking the component
        /// that may bind and buffer the multipart request.
        /// </summary>
        /// <param name="role">The query-selected resource role.</param>
        /// <param name="fileLimitMegabytes">The corresponding exact handler file limit.</param>
        [Theory]
        [InlineData("model", 25)]
        [InlineData("drawing", 10)]
        public async Task CncUpload_SetsRoleSpecificRequestLimitBeforeNext(string role, int fileLimitMegabytes)
        {
            var feature = new TestMaxRequestBodySizeFeature();
            DefaultHttpContext context = CreateCncContext(role, (fileLimitMegabytes * Megabyte) + MultipartAllowance);
            context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);
            bool nextCalled = false;
            var middleware = new CncUploadAdmissionMiddleware(next =>
            {
                nextCalled = true;
                Assert.Equal((fileLimitMegabytes * Megabyte) + MultipartAllowance, feature.MaxRequestBodySize);
                Assert.True(CncUploadAdmissionPolicy.TryGetValidatedRole(next, out string? validatedRole));
                Assert.Equal(role, validatedRole);
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context);

            Assert.True(nextCalled);
            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        }

        /// <summary>
        /// Declared bodies beyond the role ceiling are rejected without invoking or reading the
        /// multipart binding boundary.
        /// </summary>
        /// <param name="role">The query-selected resource role.</param>
        /// <param name="fileLimitMegabytes">The corresponding exact handler file limit.</param>
        [Theory]
        [InlineData("model", 25)]
        [InlineData("drawing", 10)]
        public async Task CncUpload_RejectsOversizedContentLengthBeforeNextOrBodyRead(string role, int fileLimitMegabytes)
        {
            var body = new ThrowOnReadStream();
            DefaultHttpContext context = CreateCncContext(role, (fileLimitMegabytes * Megabyte) + MultipartAllowance + 1);
            context.Request.Body = body;
            bool nextCalled = false;
            var middleware = new CncUploadAdmissionMiddleware(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context);

            Assert.False(nextCalled);
            Assert.False(body.WasRead);
            Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
            Assert.Equal("application/json", context.Response.ContentType);
        }

        /// <summary>
        /// CNC upload routes cannot bypass the pre-binding boundary by omitting or inventing the
        /// query role/process used solely to select the stricter resource ceiling.
        /// </summary>
        /// <param name="query">The malformed CNC upload query.</param>
        [Theory]
        [InlineData("?Handler=UploadFile")]
        [InlineData("?Handler=UploadFile&uploadRole=fixture")]
        public async Task CncUpload_RejectsMissingOrInvalidAdmissionQuery(string query)
        {
            DefaultHttpContext context = CreateContext("/instantquotation/cnc-machining", query, contentLength: 1);
            bool nextCalled = false;
            var middleware = new CncUploadAdmissionMiddleware(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context);

            Assert.False(nextCalled);
            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }

        /// <summary>
        /// The dedicated CNC route is admitted before binding, including case and trailing-slash variants.
        /// </summary>
        /// <param name="path">The InstantQuotation route form.</param>
        /// <param name="query">The handler and upload role.</param>
        [Theory]
        [InlineData("/instantquotation/cnc-machining", "?Handler=UploadFile&uploadRole=model")]
        [InlineData("/InstantQuotation/CNC-MACHINING/", "?handler=uploadfile&uploadRole=model")]
        [InlineData("/instantquotation/cnc-machining", "?Handler=%20uPlOaDfIlE%20&uploadRole=model")]
        public async Task CncUpload_AllCncRouteSignalsPassThroughPrebindingAdmission(string path, string query)
        {
            DefaultHttpContext context = CreateContext(path, query, contentLength: 1);
            var feature = new TestMaxRequestBodySizeFeature();
            context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);
            bool nextCalled = false;
            var middleware = new CncUploadAdmissionMiddleware(next =>
            {
                nextCalled = true;
                Assert.True(CncUploadAdmissionPolicy.TryGetValidatedRole(next, out string? role));
                Assert.Equal("model", role);
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context);

            Assert.True(nextCalled);
            Assert.Equal((25 * Megabyte) + MultipartAllowance, feature.MaxRequestBodySize);
        }

        /// <summary>
        /// Ambiguous handler selectors that include UploadFile cannot collapse to a normal bypass
        /// before Razor chooses the upload handler.
        /// </summary>
        /// <param name="query">The duplicate handler selector.</param>
        [Theory]
        [InlineData("?Handler=UploadFile&Handler=UploadFile&uploadRole=model")]
        [InlineData("?Handler=Other&Handler=%20UPLOADFILE%20&uploadRole=model")]
        public async Task CncUpload_RejectsAmbiguousHandlerSelectorsThatClaimUploadFile(string query)
        {
            DefaultHttpContext context = CreateContext("/instantquotation/cnc-machining", query, contentLength: 1);
            bool nextCalled = false;
            var middleware = new CncUploadAdmissionMiddleware(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context);

            Assert.False(nextCalled);
            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }

        /// <summary>
        /// A single handler selector that does not claim UploadFile remains outside CNC upload
        /// admission and reaches the ordinary Razor handler pipeline.
        /// </summary>
        [Fact]
        public async Task NonUploadHandler_BypassesCncUploadAdmission()
        {
            DefaultHttpContext context = CreateContext(
                "/instantquotation/cnc-machining",
                "?Handler=Calculate&uploadRole=model",
                contentLength: 1);
            bool nextCalled = false;
            var middleware = new CncUploadAdmissionMiddleware(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context);

            Assert.True(nextCalled);
            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        }

        /// <summary>
        /// Kestrel's decoded path and parsed multi-value query collection preserve the same
        /// normalization and ambiguity decisions exercised at the middleware boundary.
        /// </summary>
        [Fact]
        public async Task CncUpload_LiveHttpNormalizesEncodedPathAndRejectsAmbiguousHandlers()
        {
            int port = ReserveFreePort();
            string origin = $"http://127.0.0.1:{port}";
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseKestrel().UseUrls(origin);
            await using var host = builder.Build();
            host.Run(context =>
                    new CncUploadAdmissionMiddleware(current =>
                    {
                        current.Response.StatusCode = StatusCodes.Status204NoContent;
                        return Task.CompletedTask;
                    }).InvokeAsync(context));
            await host.StartAsync();

            try
            {
                using var client = new HttpClient();
                HttpResponseMessage encodedPath = await client.PostAsync(
                    origin + "/instantquotation/%20cnc-machining%20?Handler=UploadFile&uploadRole=model",
                    new ByteArrayContent(Array.Empty<byte>()));
                HttpResponseMessage normalizedHandler = await client.PostAsync(
                    origin + "/instantquotation/cnc-machining?Handler=%20uPlOaDfIlE%20&uploadRole=model",
                    new ByteArrayContent(Array.Empty<byte>()));
                HttpResponseMessage duplicateHandler = await client.PostAsync(
                    origin + "/instantquotation/cnc-machining?Handler=UploadFile&Handler=UploadFile&uploadRole=model",
                    new ByteArrayContent(Array.Empty<byte>()));
                HttpResponseMessage mixedHandler = await client.PostAsync(
                    origin + "/instantquotation/cnc-machining?Handler=Other&Handler=UploadFile&uploadRole=model",
                    new ByteArrayContent(Array.Empty<byte>()));
                HttpResponseMessage nonUpload = await client.PostAsync(
                    origin + "/instantquotation/cnc-machining?Handler=Calculate&uploadRole=model",
                    new ByteArrayContent(Array.Empty<byte>()));
                HttpResponseMessage additive = await client.PostAsync(
                    origin + "/instantquotation/3d-printing?Handler=UploadFile",
                    new ByteArrayContent(Array.Empty<byte>()));

                Assert.Equal(HttpStatusCode.NoContent, encodedPath.StatusCode);
                Assert.Equal(HttpStatusCode.NoContent, normalizedHandler.StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, duplicateHandler.StatusCode);
                Assert.Equal(HttpStatusCode.BadRequest, mixedHandler.StatusCode);
                Assert.Equal(HttpStatusCode.NoContent, nonUpload.StatusCode);
                Assert.Equal(HttpStatusCode.NoContent, additive.StatusCode);
            }
            finally
            {
                await host.StopAsync();
            }
        }

        /// <summary>
        /// The dedicated CNC route rejects legacy process selectors instead of restoring a mode switch.
        /// </summary>
        [Theory]
        [InlineData("/instantquotation/cnc-machining", "3d-printing")]
        [InlineData("/instantquotation/cnc-machining", "cnc-machining")]
        public async Task CncUpload_RejectsLegacyProcessSelectors(string path, string queryProcess)
        {
            DefaultHttpContext context = CreateContext(
                path,
                "?Handler=UploadFile&" + $"quotationProcess={queryProcess}&uploadRole=model",
                contentLength: 1);
            bool nextCalled = false;
            var middleware = new CncUploadAdmissionMiddleware(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context);

            Assert.False(nextCalled);
            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }

        /// <summary>
        /// Duplicate process selectors involving CNC are ambiguous even when every value is the
        /// same normalized process.
        /// </summary>
        /// <param name="query">The duplicated process query.</param>
        [Theory]
        [InlineData("?Handler=UploadFile&" + "quotationProcess=cnc-machining&" + "quotationProcess=cnc-machining&uploadRole=model")]
        [InlineData("?Handler=UploadFile&" + "quotationProcess=3d-printing&" + "quotationProcess=%20CNC-MACHINING%20&uploadRole=model")]
        public async Task CncUpload_RejectsDuplicateProcessSelectorsThatClaimCnc(string query)
        {
            DefaultHttpContext context = CreateContext("/instantquotation/cnc-machining", query, contentLength: 1);
            bool nextCalled = false;
            var middleware = new CncUploadAdmissionMiddleware(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context);

            Assert.False(nextCalled);
            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }

        /// <summary>
        /// A canonical CNC path cannot reach binding without the query role needed to select and
        /// mark the exact resource boundary.
        /// </summary>
        [Fact]
        public async Task CncUpload_CanonicalPathWithoutRoleFailsClosed()
        {
            DefaultHttpContext context = CreateContext(
                "/instantquotation/cnc-machining",
                "?Handler=UploadFile",
                contentLength: 1);
            bool nextCalled = false;
            var middleware = new CncUploadAdmissionMiddleware(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context);

            Assert.False(nextCalled);
            Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
        }

        /// <summary>
        /// Two in-flight pre-binding admissions saturate the process gate; the third receives a
        /// retryable 429 and a later request succeeds after both leases are released.
        /// </summary>
        [Fact]
        public async Task CncUpload_SaturationRejectsBeforeNextAndRecoversAfterRelease()
        {
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var entered = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            int enteredCount = 0;
            var middleware = new CncUploadAdmissionMiddleware(async _ =>
            {
                int count = System.Threading.Interlocked.Increment(ref enteredCount);
                if (count == 2)
                {
                    entered.TrySetResult(count);
                }

                await release.Task;
            });
            Task first = middleware.InvokeAsync(CreateCncContext("model", 1));
            Task second = middleware.InvokeAsync(CreateCncContext("drawing", 1));

            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                DefaultHttpContext saturated = CreateCncContext("model", 1);
                await middleware.InvokeAsync(saturated);

                Assert.Equal(StatusCodes.Status429TooManyRequests, saturated.Response.StatusCode);
                Assert.Equal("1", saturated.Response.Headers.RetryAfter.ToString());
                Assert.Equal(2, enteredCount);
            }
            finally
            {
                release.TrySetResult(true);
            }

            await Task.WhenAll(first, second);
            DefaultHttpContext retry = CreateCncContext("model", 1);
            await middleware.InvokeAsync(retry);

            Assert.Equal(StatusCodes.Status200OK, retry.Response.StatusCode);
            Assert.Equal(3, enteredCount);
        }

        /// <summary>
        /// Additive uploads retain the existing global 100 MB form boundary and bypass the CNC
        /// gate even when their declared request is larger than the CNC model ceiling.
        /// </summary>
        [Fact]
        public async Task AdditiveUpload_BypassesCncAdmissionBoundary()
        {
            DefaultHttpContext context = CreateContext(
                "/instantquotation/3d-printing",
                "?Handler=UploadFile",
                contentLength: 100 * Megabyte);
            var feature = new TestMaxRequestBodySizeFeature { MaxRequestBodySize = 100 * Megabyte };
            context.Features.Set<IHttpMaxRequestBodySizeFeature>(feature);
            bool nextCalled = false;
            var middleware = new CncUploadAdmissionMiddleware(_ =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            });

            await middleware.InvokeAsync(context);

            Assert.True(nextCalled);
            Assert.Equal(100 * Megabyte, feature.MaxRequestBodySize);
        }

        /// <summary>
        /// A representative multipart body remains readable by downstream form binding after the
        /// middleware applies the pre-binding lease and request-size feature.
        /// </summary>
        [Fact]
        public async Task CncUpload_AdmittedMultipartBodyReachesRealFormReader()
        {
            const string boundary = "----maliev-cnc-boundary";
            byte[] bytes = Encoding.UTF8.GetBytes(
                $"--{boundary}\r\nContent-Disposition: form-data; name=\"uploadRole\"\r\n\r\nmodel\r\n--{boundary}--\r\n");
            DefaultHttpContext context = CreateCncContext("model", bytes.Length);
            context.Request.ContentType = $"multipart/form-data; boundary={boundary}";
            context.Request.Body = new MemoryStream(bytes);
            IFormCollection? boundForm = null;
            var middleware = new CncUploadAdmissionMiddleware(async current =>
            {
                boundForm = await current.Request.ReadFormAsync();
            });

            await middleware.InvokeAsync(context);

            Assert.NotNull(boundForm);
            Assert.Equal("model", boundForm["uploadRole"].ToString());
        }

        private static DefaultHttpContext CreateCncContext(string role, long contentLength)
        {
            DefaultHttpContext context = CreateContext(
                "/instantquotation/cnc-machining",
                "?Handler=UploadFile&" + $"uploadRole={role}",
                contentLength);
            context.Features.Set<IHttpMaxRequestBodySizeFeature>(new TestMaxRequestBodySizeFeature());
            return context;
        }

        private static int ReserveFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static DefaultHttpContext CreateContext(string path, string query, long contentLength)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = HttpMethods.Post;
            context.Request.Path = path;
            context.Request.QueryString = new QueryString(query);
            context.Request.ContentLength = contentLength;
            context.Response.Body = new MemoryStream();
            return context;
        }

        private sealed class TestMaxRequestBodySizeFeature : IHttpMaxRequestBodySizeFeature
        {
            public bool IsReadOnly { get; set; }

            public long? MaxRequestBodySize { get; set; }
        }

        private sealed class ThrowOnReadStream : Stream
        {
            internal bool WasRead { get; private set; }

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => 0;

            public override long Position { get => 0; set => throw new NotSupportedException(); }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                this.WasRead = true;
                throw new InvalidOperationException("The rejected body must not be read.");
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
