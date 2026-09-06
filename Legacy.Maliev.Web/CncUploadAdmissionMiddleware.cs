// <copyright file="CncUploadAdmissionMiddleware.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web
{
    using Microsoft.AspNetCore.Http;
    using Microsoft.AspNetCore.Http.Features;
    using Microsoft.Extensions.Primitives;
    using System;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Defines the shared CNC upload resource limits used before and after multipart binding.
    /// </summary>
    internal static class CncUploadAdmissionPolicy
    {
        private static readonly object ValidatedRoleItemKey = new object();

        internal const int MaximumModelFileSizeBytes = 25 * 1024 * 1024;

        internal const int MaximumDrawingFileSizeBytes = 10 * 1024 * 1024;

        internal const int MultipartOverheadAllowanceBytes = 256 * 1024;

        internal const int MaximumConcurrentAdmissions = 2;

        internal static void SetValidatedRole(HttpContext context, string? role) => context.Items[ValidatedRoleItemKey] = role;

        internal static bool TryGetValidatedRole(HttpContext context, out string? role)
        {
            role = context.Items.TryGetValue(ValidatedRoleItemKey, out object? value) ? value as string : null;
            return string.Equals(role, "model", StringComparison.Ordinal)
                || string.Equals(role, "drawing", StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Applies CNC upload size and concurrency admission before Razor Pages binds multipart data.
    /// </summary>
    internal sealed class CncUploadAdmissionMiddleware
    {
        private static readonly SemaphoreSlim AdmissionGate = new SemaphoreSlim(
            CncUploadAdmissionPolicy.MaximumConcurrentAdmissions,
            CncUploadAdmissionPolicy.MaximumConcurrentAdmissions);

        private readonly RequestDelegate next;

        /// <summary>
        /// Initializes a new instance of the <see cref="CncUploadAdmissionMiddleware"/> class.
        /// </summary>
        /// <param name="next">The next request delegate.</param>
        public CncUploadAdmissionMiddleware(RequestDelegate next)
        {
            this.next = next;
        }

        /// <summary>
        /// Enforces the role-specific request limit and bounded admission lease.
        /// </summary>
        /// <param name="context">The current HTTP context.</param>
        /// <returns>A task representing the request.</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            CncRequestClassification classification = ClassifyRequest(context.Request);
            if (classification == CncRequestClassification.NotCnc)
            {
                await this.next(context);
                return;
            }

            if (classification == CncRequestClassification.Invalid
                || !TryGetAdmissionRole(context.Request.Query, out string? role))
            {
                await WriteErrorAsync(
                    context,
                    StatusCodes.Status400BadRequest,
                    "The CNC upload role or quotation process is missing or invalid.");
                return;
            }

            long maximumRequestSize = GetMaximumFileSize(role) + CncUploadAdmissionPolicy.MultipartOverheadAllowanceBytes;
            IHttpMaxRequestBodySizeFeature? sizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (sizeFeature == null
                || (sizeFeature.IsReadOnly && (!sizeFeature.MaxRequestBodySize.HasValue || sizeFeature.MaxRequestBodySize.Value > maximumRequestSize)))
            {
                await WriteErrorAsync(
                    context,
                    StatusCodes.Status413PayloadTooLarge,
                    "This CNC upload cannot be admitted within the required request-size limit.");
                return;
            }

            if (!sizeFeature.IsReadOnly)
            {
                sizeFeature.MaxRequestBodySize = maximumRequestSize;
            }

            if (context.Request.ContentLength.HasValue && context.Request.ContentLength.Value > maximumRequestSize)
            {
                await WriteErrorAsync(
                    context,
                    StatusCodes.Status413PayloadTooLarge,
                    $"This {role} upload exceeds the CNC request-size limit.");
                return;
            }

            bool acquired = await AdmissionGate.WaitAsync(0);
            if (!acquired)
            {
                context.Response.Headers.RetryAfter = "1";
                await WriteErrorAsync(
                    context,
                    StatusCodes.Status429TooManyRequests,
                    "CNC file admission is busy. Please wait briefly and retry this upload.");
                return;
            }

            try
            {
                CncUploadAdmissionPolicy.SetValidatedRole(context, role);
                await this.next(context);
            }
            finally
            {
                AdmissionGate.Release();
            }
        }

        private static CncRequestClassification ClassifyRequest(HttpRequest request)
        {
            string? path = request.Path.Value?.TrimEnd('/');
            if (!HttpMethods.IsPost(request.Method))
            {
                return CncRequestClassification.NotCnc;
            }

            string? normalizedPath = path?.TrimEnd('/');
            if (!string.Equals(normalizedPath, "/instantquotation/cnc-machining", StringComparison.OrdinalIgnoreCase))
            {
                return CncRequestClassification.NotCnc;
            }

            StringValues handlers = request.Query["Handler"];
            bool anyHandlerClaimsUpload = handlers.Any(value => IsNormalizedValue(value, "UploadFile"));
            if (!anyHandlerClaimsUpload)
            {
                return CncRequestClassification.NotCnc;
            }

            if (handlers.Count != 1)
            {
                return CncRequestClassification.Invalid;
            }

            return request.Query.ContainsKey("quotationProcess")
                ? CncRequestClassification.Invalid
                : CncRequestClassification.Cnc;
        }

        private static bool IsNormalizedValue(string? value, string expected) =>
            string.Equals(value?.Trim(), expected, StringComparison.OrdinalIgnoreCase);

        private static bool TryGetAdmissionRole(IQueryCollection query, out string? role)
        {
            return TryGetSingleQueryValue(query, "uploadRole", out role)
                && (string.Equals(role, "model", StringComparison.Ordinal)
                    || string.Equals(role, "drawing", StringComparison.Ordinal));
        }

        private static bool TryGetSingleQueryValue(IQueryCollection query, string key, out string? value)
        {
            StringValues values = query[key];
            value = values.Count == 1 ? values[0] : null;
            return values.Count == 1;
        }

        private static long GetMaximumFileSize(string? role)
        {
            return string.Equals(role, "drawing", StringComparison.Ordinal)
                ? CncUploadAdmissionPolicy.MaximumDrawingFileSizeBytes
                : CncUploadAdmissionPolicy.MaximumModelFileSizeBytes;
        }

        private static async Task WriteErrorAsync(HttpContext context, int statusCode, string message)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync($"{{\"success\":false,\"message\":{System.Text.Json.JsonSerializer.Serialize(message)}}}");
        }

        private enum CncRequestClassification
        {
            NotCnc,
            Cnc,
            Invalid,
        }
    }
}
