using Cortex.Mediator;
using Healthify.Platform.FoodCatalog.Interfaces.Acl;
using Healthify.Platform.Shared.Application.Internal.EventHandlers;
using Healthify.Platform.Shared.Domain.Model.Events;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Healthify.Platform.Tests.TestSupport;

/// <summary>Small helpers over NSubstitute shared by the tests of several contexts.</summary>
public static class Fakes
{
    /// <summary>Every notification published through the mediator, in publication order.</summary>
    public static List<object> Published(IMediator mediator)
    {
        return mediator.ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == nameof(IMediator.PublishAsync))
            .Select(c => c.GetArguments()[0]!)
            .ToList();
    }

    /// <summary>A Food Catalog facade that resolves exactly the given foods, by identifier.</summary>
    public static IFoodCatalogContextFacade Catalog(params ReferenceFoodItem[] foods)
    {
        var catalog = Substitute.For<IFoodCatalogContextFacade>();
        catalog.GetReferenceFoodById(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => foods.FirstOrDefault(f => f.ReferenceFoodId == call.ArgAt<int>(0)));
        return catalog;
    }

    /// <summary>
    ///     A scope factory whose scopes resolve <typeparamref name="TService" /> to the given instance, for
    ///     event handlers that open their own DI scope.
    /// </summary>
    public static IServiceScopeFactory ScopeFactoryWith<TService>(TService service) where TService : class
    {
        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(TService)).Returns(service);
        var scope = Substitute.For<IServiceScope, IAsyncDisposable>();
        scope.ServiceProvider.Returns(provider);
        var factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);
        return factory;
    }

    /// <summary>
    ///     A scope factory whose scopes resolve each given service type to its instance, for event
    ///     handlers that need more than one service from their DI scope.
    /// </summary>
    public static IServiceScopeFactory ScopeFactoryWith(params (Type Type, object Instance)[] services)
    {
        var provider = Substitute.For<IServiceProvider>();
        foreach (var (type, instance) in services) provider.GetService(type).Returns(instance);
        var scope = Substitute.For<IServiceScope, IAsyncDisposable>();
        scope.ServiceProvider.Returns(provider);
        var factory = Substitute.For<IServiceScopeFactory>();
        factory.CreateScope().Returns(scope);
        return factory;
    }

    /// <summary>
    ///     Makes the substituted mediator deliver <typeparamref name="TEvent" /> to a real handler, the
    ///     way Cortex.Mediator does in production: the publish awaits the handler.
    /// </summary>
    public static void Route<TEvent>(IMediator mediator, IEventHandler<TEvent> handler) where TEvent : IEvent
    {
        mediator.PublishAsync(Arg.Any<TEvent>(), Arg.Any<CancellationToken>())
            .Returns(call => handler.Handle(call.ArgAt<TEvent>(0), call.ArgAt<CancellationToken>(1)));
    }

    public static ReferenceFoodItem Food(int id, string name, decimal energyKcalPer100g)
    {
        return new ReferenceFoodItem(id, name, energyKcalPer100g, 10m, 20m, 5m, false);
    }
}
