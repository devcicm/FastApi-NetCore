using System;
using System.Collections.Generic;

namespace FastApi_NetCore.Core.Interfaces
{
    /// <summary>
    /// Interface base para todos los eventos del sistema
    /// </summary>
    public interface IEvent
    {
        /// <summary>
        /// Identificador único del evento
        /// </summary>
        Guid EventId { get; }

        /// <summary>
        /// Timestamp de cuando ocurrió el evento
        /// </summary>
        DateTime Timestamp { get; }

        /// <summary>
        /// Nombre del evento
        /// </summary>
        string EventName { get; }

        /// <summary>
        /// Fuente que generó el evento
        /// </summary>
        string Source { get; }

        /// <summary>
        /// Versión del schema del evento
        /// </summary>
        string Version { get; }

        /// <summary>
        /// Metadatos adicionales del evento
        /// </summary>
        Dictionary<string, object> Metadata { get; }
    }

    /// <summary>
    /// Clase base abstracta para eventos
    /// </summary>
    public abstract class EventBase : IEvent
    {
        protected EventBase(string source = "FastApi.NetCore")
        {
            EventId = Guid.NewGuid();
            Timestamp = DateTime.UtcNow;
            EventName = GetType().Name;
            Source = source;
            Version = "1.0";
            Metadata = new Dictionary<string, object>();
        }

        public Guid EventId { get; }
        public DateTime Timestamp { get; }
        public string EventName { get; }
        public string Source { get; }
        public string Version { get; }
        public Dictionary<string, object> Metadata { get; }
    }

    /// <summary>
    /// Interface para manejo de eventos
    /// </summary>
    public interface IEventHandler<in T> where T : IEvent
    {
        Task HandleAsync(T @event);
    }

    /// <summary>
    /// Interface para el bus de eventos
    /// </summary>
    public interface IEventBus
    {
        /// <summary>
        /// Publica un evento de forma asíncrona
        /// </summary>
        Task PublishAsync<T>(T @event) where T : IEvent;

        /// <summary>
        /// Suscribe un handler a un tipo de evento específico
        /// </summary>
        void Subscribe<T>(IEventHandler<T> handler) where T : IEvent;

        /// <summary>
        /// Suscribe un handler usando una función lambda
        /// </summary>
        void Subscribe<T>(Func<T, Task> handler) where T : IEvent;

        /// <summary>
        /// Desuscribe un handler de un tipo de evento
        /// </summary>
        void Unsubscribe<T>(IEventHandler<T> handler) where T : IEvent;

        /// <summary>
        /// Obtiene estadísticas del event bus
        /// </summary>
        EventBusStatistics GetStatistics();
    }

    /// <summary>
    /// Estadísticas del event bus
    /// </summary>
    public class EventBusStatistics
    {
        public int TotalEventsPublished { get; set; }
        public int TotalSubscriptions { get; set; }
        public int EventsInQueue { get; set; }
        public Dictionary<string, int> EventsByType { get; set; } = new();
        public DateTime LastEventTimestamp { get; set; }
        public TimeSpan AverageProcessingTime { get; set; }
    }
}