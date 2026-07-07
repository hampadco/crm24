using ElementorBuilder.Abstractions;
using ElementorBuilder.Options;
using ElementorBuilder.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ElementorBuilder.Extensions;

public static class ElementorBuilderServiceExtensions
{
    public static IMvcBuilder AddElementorBuilder(
        this IMvcBuilder mvcBuilder,
        Action<ElementorBuilderOptions>? configure = null)
    {
        mvcBuilder.AddApplicationPart(typeof(ElementorBuilderServiceExtensions).Assembly);
        RegisterCoreServices(mvcBuilder.Services, configure);
        return mvcBuilder;
    }

    public static IServiceCollection AddElementorBuilder(
        this IServiceCollection services,
        Action<ElementorBuilderOptions>? configure = null)
    {
        RegisterCoreServices(services, configure);

        services.AddControllersWithViews()
            .AddApplicationPart(typeof(ElementorBuilderServiceExtensions).Assembly);

        return services;
    }

    private static void RegisterCoreServices(
        IServiceCollection services,
        Action<ElementorBuilderOptions>? configure)
    {
        if (configure != null)
        {
            services.Configure(configure);
        }
        else
        {
            services.Configure<ElementorBuilderOptions>(_ => { });
        }

        services.AddScoped<ElementorMediaFileService>();
        services.TryAddScoped<IElementorMediaUsageChecker, NullElementorMediaUsageChecker>();
    }
}
