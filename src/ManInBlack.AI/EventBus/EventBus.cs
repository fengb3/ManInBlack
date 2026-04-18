// using ManInBlack.AI.Core.Attributes;
// using Microsoft.Extensions.DependencyInjection;
// using ModelContextProtocol.Client;
//
// namespace ManInBlack.AI.EventBus;
//
// [ServiceRegister.Scoped]
// public class ScopeId
// {
//     public string Id { get; } = Guid.NewGuid().ToString();
//
//     public override string ToString() => this.Id;
//
//     public override bool Equals(object? obj)
//     {
//         return obj is ScopeId other && this.Id == other.Id;
//     }
//
//     public override int GetHashCode() => this.Id.GetHashCode();
// }
//
// [ServiceRegister.Scoped]
// public class EventBus(ScopeId)
// {
//     public IDisposable Subscribe<TEvent>(EventHandlerDelegate<TEvent> handler)
//     {
//     }
// }
//
// public delegate Task EventHandlerDelegate<in TEvent>(TEvent evt);
//
// public static class EventBus<TEvent>
// {
//     private static Dictionary<string, List<EventHandlerDelegate<TEvent>>> _handlersByScope = [];
//
//     public static IDisposable Subscribe(EventHandlerDelegate<TEvent> handler, ScopeId scope)
//     {
//         if (!_handlersByScope.TryGetValue(scope.Id, out var handlers))
//         {
//             handlers = [];
//             _handlersByScope[scope.Id] = handlers;
//         }
//
//         return new Subscription(handler);
//     }
//
//     public static void Publish(TEvent evt, ScopeId scope)
//     {
//         if (_handlersByScope.TryGetValue(scope, out var handlers))
//         {
//             foreach (var handler in handlers)
//             {
//                 handler(evt);
//             }
//         }
//     }
//
//     private class Subscription : IDisposable
//     {
//         private EventHandlerDelegate<TEvent>? _handler;
//
//         public Subscription(EventHandlerDelegate<TEvent> handler, ScopeId scope)
//         {
//             _handler = handler;
//             if (!_handlersByScope.TryGetValue(scope.Id, out var handlers))
//             {
//                 handlers = [];
//                 _handlersByScope[scope.Id] = handlers;
//             }
//             handlers.Add(handler);
//         }
//
//         public void Dispose()
//         {
//             if (_handler != null)
//             {
//                 foreach (var handlers in _handlersByScope.Values)
//                 {
//                     handlers.Remove(_handler);
//                 }
//                 _handler = null;
//             }
//         }
//     }
// }