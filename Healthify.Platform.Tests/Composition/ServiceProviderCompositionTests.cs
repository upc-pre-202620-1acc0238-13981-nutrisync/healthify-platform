using System.Diagnostics;
using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Healthify.Platform.Tests.Composition;

/// <summary>
///     The real container of Program.cs, built with ValidateOnBuild and ValidateScopes: every registration can be
///     constructed, no singleton captures a scoped service and no dependency is circular (between facades or
///     anywhere else). Then every controller, command and query service, facade, composer and event handler of the
///     platform is resolved for real.
/// </summary>
/// <remarks>
///     Equivalent to WebApplicationFactory without the package: Program's entry point runs with a fake connection
///     string and a test secret on the command line, and is stopped right after <c>builder.Build()</c>, so the
///     migrations, the seeder and the hosted services never run and nothing connects to a database.
/// </remarks>
public class ServiceProviderCompositionTests
{
    [Fact]
    public void The_container_of_Program_builds_with_full_validation_and_resolves_every_platform_service()
    {
        using var program = ProgramHost.Build();
        using var scope = program.Host.Services.CreateScope();
        var provider = scope.ServiceProvider;
        var platform = typeof(Program).Assembly;

        var registrations = program.Services
            .Where(d => !d.ServiceType.IsGenericTypeDefinition)
            .Where(d => d.ServiceType != typeof(IHostedService))
            .Where(d => d.ServiceType.Assembly == platform ||
                        d.ImplementationType?.Assembly == platform ||
                        d.ServiceType.GenericTypeArguments.Any(a => a.Assembly == platform))
            .ToList();
        var failures = new List<string>();
        foreach (var serviceType in registrations.Select(d => d.ServiceType).Distinct())
            try
            {
                Assert.All(provider.GetServices(serviceType), Assert.NotNull);
            }
            catch (Exception ex)
            {
                failures.Add($"{serviceType}: {ex.GetBaseException().Message}");
            }

        Assert.Empty(failures);
        // ValidateScopes is really on: a scoped service cannot be taken from the root provider.
        Assert.Throws<InvalidOperationException>(() =>
            program.Host.Services.GetRequiredService<Healthify.Platform.Shared.Domain.Repositories.IUnitOfWork>());
        // The categories the request names, so a silent drop in the scan does not pass unnoticed.
        Assert.Contains(registrations, d => d.ServiceType.Name.EndsWith("CommandService"));
        Assert.Contains(registrations, d => d.ServiceType.Name.EndsWith("QueryService"));
        Assert.Contains(registrations, d => d.ServiceType.Name.EndsWith("ContextFacade"));
        Assert.Contains(registrations, d => d.ServiceType.Name.EndsWith("Composer"));
        Assert.Contains(registrations, d => d.ImplementationType?.Name.EndsWith("Handler") == true);
    }

    /// <summary>IN-7: the multipart photo endpoint is documented (Swashbuckle and <c>IFormFile</c> must agree).</summary>
    [Fact]
    public void The_swagger_document_generates_and_documents_the_multipart_meal_photo_endpoint()
    {
        using var program = ProgramHost.Build();

        var document = program.Host.Services.GetRequiredService<Swashbuckle.AspNetCore.Swagger.ISwaggerProvider>()
            .GetSwagger("v1");

        var path = document.Paths["/api/v1/patients/{patientId}/meal-photo-analyses"];
        var operation = Assert.Single(path.Operations!.Values);
        Assert.True(operation.RequestBody!.Content!.ContainsKey("multipart/form-data"));
        Assert.Contains("201", operation.Responses!.Keys);
        Assert.Contains("413", operation.Responses!.Keys);
    }

    [Fact]
    public void Every_controller_is_activated_from_the_container()
    {
        using var program = ProgramHost.Build();
        using var scope = program.Host.Services.CreateScope();
        var feature = new ControllerFeature();
        program.Host.Services.GetRequiredService<ApplicationPartManager>().PopulateFeature(feature);
        var controllers = feature.Controllers.Where(c => c.Assembly == typeof(Program).Assembly).ToList();

        var failures = new List<string>();
        foreach (var controller in controllers)
            try
            {
                Assert.IsAssignableFrom<ControllerBase>(
                    ActivatorUtilities.CreateInstance(scope.ServiceProvider, controller.AsType()));
            }
            catch (Exception ex)
            {
                failures.Add($"{controller.Name}: {ex.GetBaseException().Message}");
            }

        Assert.Empty(failures);
        Assert.Contains(controllers, c => c.Name == "FollowUpCheckInController");
        Assert.Contains(controllers, c => c.Name == "PatientConsultationsOverviewController");
    }

    [Fact]
    public void The_harness_really_rejects_a_circular_dependency()
    {
        // Two facades that read each other, the shape this test exists to catch.
        var ex = Assert.ThrowsAny<Exception>(() => ProgramHost.Build(services =>
        {
            services.AddScoped<CycleA>();
            services.AddScoped<CycleB>();
        }));

        Assert.Contains("circular", ex.GetBaseException().Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CycleA(CycleB b)
    {
        public CycleB B { get; } = b;
    }

    private sealed class CycleB(CycleA a)
    {
        public CycleA A { get; } = a;
    }

    /// <summary>The host of Program.cs, stopped after Build, with the service collection it was built from.</summary>
    private sealed class ProgramHost : IDisposable
    {
        private static readonly object Gate = new();

        private ProgramHost(IHost host, IReadOnlyList<ServiceDescriptor> services)
        {
            Host = host;
            Services = services;
        }

        public IHost Host { get; }
        public IReadOnlyList<ServiceDescriptor> Services { get; }

        public void Dispose()
        {
            Host.Dispose();
        }

        public static ProgramHost Build(Action<IServiceCollection>? extraServices = null)
        {
            // The hosting diagnostic events are process wide: one Program at a time.
            lock (Gate)
            {
                var observer = new HostingObserver(extraServices);
                using var subscription = DiagnosticListener.AllListeners.Subscribe(observer);
                string[] args =
                [
                    "--environment=CompositionTest",
                    // Application parts (the controllers) are discovered from the application name, which would
                    // otherwise be the test host's.
                    "--applicationName=Healthify.Platform",
                    // Never reached: nothing opens a connection while the container is only built and resolved.
                    "--ConnectionStrings:DefaultConnection=server=127.0.0.1;port=1;database=not_a_database;user=none;password=none",
                    "--TokenSettings:Secret=composition-test-secret-of-at-least-32-characters"
                ];

                try
                {
                    typeof(Program).Assembly.EntryPoint!.Invoke(null, [args]);
                }
                catch (TargetInvocationException ex) when (ex.GetBaseException() is HostCaptured)
                {
                }

                return new ProgramHost(
                    observer.Host ?? throw new InvalidOperationException("Program did not build its host."),
                    observer.Services);
            }
        }
    }

    /// <summary>Thrown from the HostBuilt event to stop Program before migrations, seeding and app.Run.</summary>
    private sealed class HostCaptured : Exception;

    private sealed class HostingObserver(Action<IServiceCollection>? extraServices)
        : IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>
    {
        private readonly List<IDisposable> _subscriptions = [];
        public IHost? Host { get; private set; }
        public IReadOnlyList<ServiceDescriptor> Services { get; private set; } = [];

        public void OnNext(DiagnosticListener listener)
        {
            if (listener.Name == "Microsoft.Extensions.Hosting") _subscriptions.Add(listener.Subscribe(this));
        }

        public void OnNext(KeyValuePair<string, object?> value)
        {
            switch (value)
            {
                case { Key: "HostBuilding", Value: IHostBuilder builder }:
                    builder.UseServiceProviderFactory(new DefaultServiceProviderFactory(new ServiceProviderOptions
                    {
                        ValidateOnBuild = true,
                        ValidateScopes = true
                    }));
                    builder.ConfigureServices((_, services) =>
                    {
                        extraServices?.Invoke(services);
                        Services = services.ToList();
                    });
                    break;
                case { Key: "HostBuilt", Value: IHost host }:
                    Host = host;
                    _subscriptions.ForEach(s => s.Dispose());
                    throw new HostCaptured();
            }
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }
}
