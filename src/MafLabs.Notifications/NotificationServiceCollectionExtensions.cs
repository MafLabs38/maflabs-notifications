using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace MafLabs.Notifications;

public static class NotificationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the built-in providers and an <see cref="INotificationSender"/> that follows <c>Notifications:Provider</c>. The application binds
    /// <see cref="NotificationOptions"/> (or a class derived from it) to its own configuration section. Another channel is one more
    /// <c>AddSingleton&lt;INotificationProvider, ...&gt;()</c> before this call. It also registers the empty <see cref="NotificationCategories"/> registry
    /// and the options of the program-wide policy (<see cref="NotificationPolicyOptions"/>, <c>Configure</c> them like any options). No database is needed:
    /// the application keeps its own log and episodes.
    /// </summary>
    public static IServiceCollection AddNotifications(this IServiceCollection services)
    {
        foreach (var provider in NotificationProviders.BuiltIn)
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton(typeof(INotificationProvider), provider));
        }

        services.TryAddSingleton<IValidateOptions<NotificationOptions>, NotificationOptionsValidator>();
        services.AddOptions<NotificationPolicyOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<NotificationPolicyOptions>, NotificationPolicyOptionsValidator>());
        services.TryAddSingleton<NotificationCategories>();
        services.TryAddSingleton(serviceProvider =>
            NotificationProviders.Select(serviceProvider.GetServices<INotificationProvider>(), serviceProvider.GetRequiredService<IOptions<NotificationOptions>>()));
        return services;
    }

    /// <summary>
    /// Same as <see cref="AddNotifications(IServiceCollection)"/>, and declares the categories of the program at once:
    /// <c>services.AddNotifications(categories =&gt; categories.Add("data", "Data", "Backups and data assets"))</c>. A mistake in a declaration (a duplicate id, a policy out of range) throws here, at startup.
    /// </summary>
    public static IServiceCollection AddNotifications(this IServiceCollection services, Action<NotificationCategories> declareCategories)
    {
        ArgumentNullException.ThrowIfNull(declareCategories);
        var categories = new NotificationCategories();
        declareCategories(categories);
        services.AddNotifications();
        services.Replace(ServiceDescriptor.Singleton(categories));
        return services;
    }
}
