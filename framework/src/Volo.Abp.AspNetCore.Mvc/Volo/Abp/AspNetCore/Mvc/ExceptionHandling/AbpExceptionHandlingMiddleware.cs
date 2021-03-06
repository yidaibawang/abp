﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using Volo.Abp.AspNetCore.Mvc.Uow;
using Volo.Abp.Http;
using Volo.Abp.Json;

namespace Volo.Abp.AspNetCore.Mvc.ExceptionHandling
{
    public class AbpExceptionHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<AbpUnitOfWorkMiddleware> _logger;

        private readonly Func<object, Task> _clearCacheHeadersDelegate;

        public AbpExceptionHandlingMiddleware(RequestDelegate next, ILogger<AbpUnitOfWorkMiddleware> logger)
        {
            _next = next;
            _logger = logger;

            _clearCacheHeadersDelegate = ClearCacheHeaders;
        }

        public async Task Invoke(HttpContext httpContext)
        {
            try
            {
                await _next(httpContext);
            }
            catch (Exception ex)
            {
                // We can't do anything if the response has already started, just abort.
                if (httpContext.Response.HasStarted)
                {
                    _logger.LogWarning("An exception occured, but response has already started!");
                    throw;
                }

                if (httpContext.Items["_AbpActionInfo"] is AbpActionInfoInHttpContext actionInfo)
                {
                    if (actionInfo.IsObjectResult) //TODO: Align with AbpExceptionFilter.ShouldHandleException!
                    {
                        await HandleAndWrapException(httpContext, ex);
                        return;
                    }
                }

                throw;
            }
        }

        private async Task HandleAndWrapException(HttpContext httpContext, Exception exception)
        {
            _logger.LogException(exception);

            var errorInfoConverter = httpContext.RequestServices.GetRequiredService<IExceptionToErrorInfoConverter>();
            var statusCodeFinder = httpContext.RequestServices.GetRequiredService<IHttpExceptionStatusCodeFinder>();
            var jsonSerializer = httpContext.RequestServices.GetRequiredService<IJsonSerializer>();

            httpContext.Response.Clear();
            httpContext.Response.StatusCode = (int)statusCodeFinder.GetStatusCode(httpContext, exception);
            httpContext.Response.OnStarting(_clearCacheHeadersDelegate, httpContext.Response);
            httpContext.Response.Headers.Add(AbpHttpConsts.AbpErrorFormat, "true");

            await httpContext.Response.WriteAsync(
                jsonSerializer.Serialize(
                    new RemoteServiceErrorResponse(
                        errorInfoConverter.Convert(exception)
                    )
                )
            );
        }

        private Task ClearCacheHeaders(object state)
        {
            var response = (HttpResponse)state;

            response.Headers[HeaderNames.CacheControl] = "no-cache";
            response.Headers[HeaderNames.Pragma] = "no-cache";
            response.Headers[HeaderNames.Expires] = "-1";
            response.Headers.Remove(HeaderNames.ETag);

            return Task.CompletedTask;
        }
    }
}
