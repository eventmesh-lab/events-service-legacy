using System;
using System.Reflection;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using events_service.Application.Commands.CrearEvento;
using events_service.Application.Commands.EditarEvento;
using events_service.Application.Commands.FinalizarEvento;
using events_service.Application.Commands.IniciarEvento;
using events_service.Application.Commands.PagarPublicacion;
using events_service.Application.Commands.PublicarEvento;
using events_service.Domain.Ports;
using events_service.Infrastructure.Messaging;
using events_service.Infrastructure.Persistence;
using events_service.Infrastructure.Repositories;
using events_service.Infrastructure.Fallback;

var builder = WebApplication.CreateBuilder(args);

// Configuración de servicios
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Events Service API",
        Version = "v1",
        Description = "API para la gestión del ciclo de vida completo de eventos"
    });
});

// Configurar Entity Framework Core con PostgreSQL
builder.Services.AddDbContext<EventsDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("EventsDb"),
        npgsqlOptions => npgsqlOptions.MigrationsAssembly("events-service.Infrastructure")));

// Registrar MediatR
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly()));
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(CrearEventoCommand).Assembly));

// Registrar FluentValidation
builder.Services.AddValidatorsFromAssembly(typeof(CrearEventoCommandValidator).Assembly);
builder.Services.AddValidatorsFromAssembly(typeof(PublicarEventoCommandValidator).Assembly);

// Registrar repositorios
builder.Services.Configure<EventoFallbackOptions>(builder.Configuration.GetSection("FallbackStorage"));
builder.Services.AddSingleton<IEventoFallbackStore, JsonEventoFallbackStore>();
builder.Services.AddScoped<EventoRepository>();
builder.Services.AddScoped<IEventoRepository, HybridEventoRepository>();

// Registrar mensajería RabbitMQ
builder.Services.AddRabbitMqMessaging(builder.Configuration);

var app = builder.Build();

// Configurar pipeline HTTP
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "Events Service API v1");
    });
}

app.UseHttpsRedirection();
app.UseAuthorization();

// Mapear endpoints
app.MapControllers();
app.MapEventosEndpoints();

app.Run();

// Extensiones para endpoints
public static class EventosEndpointsExtensions
{
    public static void MapEventosEndpoints(this WebApplication app)
    {
        var eventos = app.MapGroup("/api/eventos").WithTags("Eventos");

        // POST /api/eventos
        eventos.MapPost("/", async (CrearEventoCommand command, IMediator mediator) =>
        {
            var result = await mediator.Send(command);
            return Results.Created($"/api/eventos/{result.Id}", result);
        })
        .WithName("CrearEvento")
        .WithSummary("Crea un nuevo evento en estado borrador")
        .Produces<CrearEventoCommandResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        // POST /api/eventos/{id}/publicar
        eventos.MapPost("/{id:guid}/publicar", async (Guid id, PublicarEventoCommand command, IMediator mediator) =>
        {
            var enrichedCommand = command with { EventoId = id };
            await mediator.Send(enrichedCommand);
            return Results.Ok();
        })
        .WithName("PublicarEvento")
        .WithSummary("Publica un evento existente")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        // PUT /api/eventos/{id}
        eventos.MapPut("/{id:guid}", async (Guid id, EditarEventoCommand command, IMediator mediator) =>
        {
            var enrichedCommand = command with { EventoId = id };
            await mediator.Send(enrichedCommand);
            return Results.NoContent();
        })
        .WithName("EditarEvento")
        .WithSummary("Edita un evento en estado borrador")
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        // POST /api/eventos/{id}/pagar-publicacion
        eventos.MapPost("/{id:guid}/pagar-publicacion", async (Guid id, PagarPublicacionCommand command, IMediator mediator) =>
        {
            var enrichedCommand = command with { EventoId = id };
            await mediator.Send(enrichedCommand);
            return Results.Accepted($"/api/eventos/{id}");
        })
        .WithName("PagarPublicacionEvento")
        .WithSummary("Inicia el pago de publicación del evento")
        .Produces(StatusCodes.Status202Accepted)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        // POST /api/eventos/{id}/iniciar
        eventos.MapPost("/{id:guid}/iniciar", async (Guid id, IMediator mediator) =>
        {
            var command = new IniciarEventoCommand { EventoId = id };
            await mediator.Send(command);
            return Results.Ok();
        })
        .WithName("IniciarEvento")
        .WithSummary("Marca un evento publicado como en curso")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        // POST /api/eventos/{id}/finalizar
        eventos.MapPost("/{id:guid}/finalizar", async (Guid id, IMediator mediator) =>
        {
            var command = new FinalizarEventoCommand { EventoId = id };
            await mediator.Send(command);
            return Results.Ok();
        })
        .WithName("FinalizarEvento")
        .WithSummary("Finaliza un evento en curso")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound);

        // GET /api/eventos/{id}
        eventos.MapGet("/{id:guid}", async (Guid id, IEventoRepository repository) =>
        {
            var evento = await repository.GetByIdAsync(id);
            if (evento == null)
                return Results.NotFound();
            return Results.Ok(evento);
        })
        .WithName("ObtenerEvento")
        .WithSummary("Obtiene los detalles de un evento")
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);
    }
}
