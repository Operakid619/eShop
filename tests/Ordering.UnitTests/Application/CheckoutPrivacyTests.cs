using eShop.Ordering.API.Application.Behaviors;
using eShop.Ordering.API.Application.Queries;
using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;

namespace eShop.Ordering.UnitTests.Application;

[TestClass]
public class CheckoutPrivacyTests
{
    private const string Card = "4111111111111111";
    private const string SecurityCode = "987";

    private static CreateOrderRequest Request(string card) => new(
        "buyer-id", "Example Buyer", "Abuja", "Example Street", "FCT", "NG", "900001",
        card, "Example Buyer", new DateTime(2030, 1, 1), SecurityCode, 1, "buyer-id", []);

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("123")]
    public async Task Invalid_card_is_rejected_before_dispatch(string card)
    {
        var mediator = Substitute.For<IMediator>();
        var logger = new RecordingLogger<OrderServices>();
        var services = new OrderServices(mediator, Substitute.For<IOrderQueries>(),
            Substitute.For<IIdentityService>(), logger);

        var result = await OrdersApi.CreateOrderAsync(Guid.NewGuid(), Request(card), services);

        Assert.IsInstanceOfType<BadRequest<string>>(result.Result);
        await mediator.DidNotReceive().Send(Arg.Any<IdentifiedCommand<CreateOrderCommand, bool>>(), default);
        AssertNoPaymentData(logger);
    }

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Checkout_logs_metadata_without_payment_payloads(bool validRequestId)
    {
        var mediator = Substitute.For<IMediator>();
        mediator.Send(Arg.Any<IdentifiedCommand<CreateOrderCommand, bool>>(), default).Returns(true);
        var logger = new RecordingLogger<OrderServices>();
        var services = new OrderServices(mediator, Substitute.For<IOrderQueries>(),
            Substitute.For<IIdentityService>(), logger);

        var result = await OrdersApi.CreateOrderAsync(
            validRequestId ? Guid.NewGuid() : Guid.Empty, Request(Card), services);

        AssertNoPaymentData(logger);
        if (validRequestId)
        {
            Assert.IsInstanceOfType<Ok>(result.Result);
            await mediator.Received(1).Send(Arg.Is<IdentifiedCommand<CreateOrderCommand, bool>>(
                c => c.Command.CardNumber == "XXXXXXXXXXXX1111"), default);
        }
        else
        {
            Assert.IsInstanceOfType<BadRequest<string>>(result.Result);
        }
    }

    [TestMethod]
    public async Task Pipeline_does_not_log_nested_commands()
    {
        var logger = new RecordingLogger<LoggingBehavior<IdentifiedCommand<CreateOrderCommand, bool>, bool>>();
        var behavior = new LoggingBehavior<IdentifiedCommand<CreateOrderCommand, bool>, bool>(logger);
        var command = new IdentifiedCommand<CreateOrderCommand, bool>(Command(), Guid.NewGuid());

        var result = await behavior.Handle(command, _ => Task.FromResult(true), CancellationToken.None);

        Assert.IsTrue(result);
        AssertNoPaymentData(logger);
    }

    [TestMethod]
    public async Task Validation_logs_field_names_without_attempted_values()
    {
        var validator = new InlineValidator<CreateOrderCommand>();
        validator.RuleFor(c => c.CardSecurityNumber).Must(_ => false)
            .WithMessage("Rejected value: {PropertyValue}");
        var logger = new RecordingLogger<ValidatorBehavior<CreateOrderCommand, bool>>();
        var behavior = new ValidatorBehavior<CreateOrderCommand, bool>([validator], logger);

        await Assert.ThrowsExactlyAsync<OrderingDomainException>(() => behavior.Handle(
            Command(), _ => Task.FromResult(true), CancellationToken.None));

        AssertNoPaymentData(logger);
        Assert.IsTrue(logger.Values.OfType<string>().Any(v => v.Contains("CardSecurityNumber")));
    }

    private static CreateOrderCommand Command() => new([], "buyer", "Example Buyer", "Abuja",
        "Example Street", "FCT", "NG", "900001", Card, "Example Buyer", new DateTime(2030, 1, 1),
        SecurityCode, 1);

    private static void AssertNoPaymentData<T>(RecordingLogger<T> logger)
    {
        Assert.IsNotEmpty(logger.Values);
        foreach (var value in logger.Values)
        {
            // Inspect structured values, not just rendered messages.
            Assert.IsFalse(value is CreateOrderRequest or CreateOrderCommand
                or IdentifiedCommand<CreateOrderCommand, bool>);
            var serialized = System.Text.Json.JsonSerializer.Serialize(value);
            Assert.DoesNotContain(Card, serialized);
            Assert.DoesNotContain("\"" + SecurityCode + "\"", serialized);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<object> Values { get; } = [];
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception exception, Func<TState, Exception, string> formatter)
        {
            if (state is IEnumerable<KeyValuePair<string, object>> properties)
            {
                Values.AddRange(properties.Select(p => p.Value));
            }
        }
    }
}
