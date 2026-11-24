using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace MafLabs.Notifications;

public static class NotificationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the built-in providers and an <see cref="INotificationSender"/> that follows <c>Notifications:Provider</c>. The application binds
    /// <see cref="NotificationOptions"/> (or a class derived from it) to its own configuration section. Another channel is one more
    /// <c>AddSingleton&lt;INotificationProvider, ...&gt;()</c> before this call.
    /// </summary>
    public static IServiceCollection AddNotifications(this IServiceCollection services)
    {
        foreach (var provider in NotificationProviders.BuiltIn)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(INotificationProvider), provider));
        }

        services.TryAddSingleton<IValidateOptions<NotificationOptions>, NotificationOptionsValidator>();
        services.TryAddSingleton(serviceProvider =>
            NotificationProviders.Select(serviceProvider.GetServices<INotificationProvider>(), serviceProvider.GetRequiredService<IOptions<NotificationOptions>>()));
        return services;
    }
}
