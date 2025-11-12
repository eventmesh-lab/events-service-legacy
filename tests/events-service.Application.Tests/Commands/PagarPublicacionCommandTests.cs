#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using FluentValidation;
using Moq;
using Xunit;
using events_service.Application.Commands.PagarPublicacion;
using events_service.Domain.Entities;
using events_service.Domain.Events;
using events_service.Domain.Ports;
using events_service.Domain.ValueObjects;

namespace events_service.Application.Tests.Commands
{
    /// <summary>
    /// Pruebas para validar el comportamiento del comando PagarPublicacionCommand y su handler.
    /// </summary>
    public class PagarPublicacionCommandTests
    {
        [Fact]
        public void PagarPublicacionCommandValidator_ConMontoNoPositivo_FallaValidacion()
        {
            // Arrange
            var validator = new PagarPublicacionCommandValidator();
            var command = new PagarPublicacionCommand
            {
                EventoId = Guid.NewGuid(),
                TransaccionPagoId = Guid.NewGuid(),
                Monto = 0
            };

            // Act
            var result = validator.Validate(command);

            // Assert
            Assert.False(result.IsValid);
        }

        [Fact]
        public async Task PagarPublicacionCommandHandler_ConDatosValidos_CambiaEstadoAPendientePago()
        {
            // Arrange
            var evento = CrearEventoBorrador();
            var command = new PagarPublicacionCommand
            {
                EventoId = evento.Id,
                TransaccionPagoId = Guid.NewGuid(),
                Monto = evento.TarifaPublicacion
            };

            var repositoryMock = new Mock<IEventoRepository>();
            repositoryMock.Setup(r => r.GetByIdAsync(evento.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(evento);

            var publisherMock = new Mock<IDomainEventPublisher>();
            var validatorMock = new Mock<IValidator<PagarPublicacionCommand>>();
            validatorMock.Setup(v => v.ValidateAsync(It.IsAny<PagarPublicacionCommand>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FluentValidation.Results.ValidationResult());

            var handler = new PagarPublicacionCommandHandler(repositoryMock.Object, publisherMock.Object, validatorMock.Object);

            // Act
            await handler.Handle(command, CancellationToken.None);

            // Assert
            Assert.True(evento.Estado.EsPendientePago);
            Assert.Equal(command.TransaccionPagoId, evento.TransaccionPagoId);
            repositoryMock.Verify(r => r.UpdateAsync(evento, It.IsAny<CancellationToken>()), Times.Once);
            publisherMock.Verify(p => p.PublishAsync(It.IsAny<PagoPublicacionIniciado>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public async Task PagarPublicacionCommandHandler_CuandoEventoNoExiste_LanzaExcepcion()
        {
            // Arrange
            var command = new PagarPublicacionCommand
            {
                EventoId = Guid.NewGuid(),
                TransaccionPagoId = Guid.NewGuid(),
                Monto = 100m
            };

            var repositoryMock = new Mock<IEventoRepository>();
            repositoryMock.Setup(r => r.GetByIdAsync(command.EventoId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((Evento?)null);

            var publisherMock = new Mock<IDomainEventPublisher>();
            var validatorMock = new Mock<IValidator<PagarPublicacionCommand>>();
            validatorMock.Setup(v => v.ValidateAsync(It.IsAny<PagarPublicacionCommand>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new FluentValidation.Results.ValidationResult());

            var handler = new PagarPublicacionCommandHandler(repositoryMock.Object, publisherMock.Object, validatorMock.Object);

            // Act & Assert
            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(command, CancellationToken.None));
        }

        private static Evento CrearEventoBorrador()
        {
            var secciones = new[]
            {
                new Seccion("General", 100, new PrecioEntrada(50m))
            };

            return Evento.Crear(
                "Evento",
                "Descripción",
                new FechaEvento(DateTime.UtcNow.AddDays(5)),
                new DuracionEvento(2, 0),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "Música",
                100m,
                secciones);
        }
    }
}
