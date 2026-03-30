using Microsoft.AspNetCore.Mvc.Controllers;
using System.Security.Cryptography;
using System.Text;

namespace TobacoServer.Services
{
    public class InternalApiKeyGuardMiddleware
    {
        public const string HeaderName = "X-Internal-Api-Key";
        private readonly RequestDelegate _next;
        private readonly string _configuredKey;

        public InternalApiKeyGuardMiddleware(RequestDelegate next, IConfiguration configuration)
        {
            _next = next;
            _configuredKey = configuration["Security:InternalApiKey"]
                ?? throw new InvalidOperationException("Security:InternalApiKey is not configured.");
        }

        public async Task Invoke(HttpContext context)
        {
            var actionDescriptor = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
            if (actionDescriptor is null || string.Equals(actionDescriptor.ControllerName, "Home", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            var providedKey = context.Request.Headers[HeaderName].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(providedKey))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsync("Unauthorized");
                return;
            }

            var configuredBytes = Encoding.UTF8.GetBytes(_configuredKey);
            var providedBytes = Encoding.UTF8.GetBytes(providedKey);

            if (configuredBytes.Length != providedBytes.Length ||
                !CryptographicOperations.FixedTimeEquals(configuredBytes, providedBytes))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsync("Forbidden");
                return;
            }

            await _next(context);
        }
    }
}
