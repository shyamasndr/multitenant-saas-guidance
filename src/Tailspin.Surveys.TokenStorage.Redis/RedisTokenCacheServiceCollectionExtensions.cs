using Tailspin.Surveys.TokenStorage.Redis;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class TokenStorageServiceCollectionExtensions
    {
        public static void AddRedisTokenStorage(this IServiceCollection services)
        {
            // Add the default token service
            services.AddScoped<ITokenCacheService, RedisTokenCacheService>();
        }
    }
}
