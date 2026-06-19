using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace CASInterfaceService.Authorization
{
    public class ConditionalAuthorizationHandler : AuthorizationHandler<ConditionalAuthorizationRequirement>
    {
        private readonly IConfiguration _configuration;

        public ConditionalAuthorizationHandler(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            ConditionalAuthorizationRequirement requirement)
        {
            var authEnabled = _configuration.GetValue<bool>("auth:jwt:enabled");

            if (!authEnabled)
            {
                Log.Debug("Authentication is disabled via feature flag. Allowing unauthenticated access.");
                context.Succeed(requirement);
                return Task.CompletedTask;
            }

            if (context.User.Identity?.IsAuthenticated == true)
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}
