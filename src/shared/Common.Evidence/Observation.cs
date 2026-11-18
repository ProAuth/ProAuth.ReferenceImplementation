using System.Text.Json.Serialization;

namespace Common.Evidence;

// A closed schema: credentials, arbitrary claims, URLs, headers and exception text have no field here.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record Observation(Guid EventId, DateTimeOffset ObservedAt, Component Source, Operation Operation,
    int HttpStatus, string TraceId, string SpanId, string? ParentSpanId, Guid? BusinessId, BusinessState? State, int? Revision)
{
    public bool IsValid() => EventId != Guid.Empty && Enum.IsDefined(Source) && Enum.IsDefined(Operation) &&
        HttpStatus is >= 100 and <= 599 && Hex(TraceId, 32) && Hex(SpanId, 16) &&
        (ParentSpanId is null || Hex(ParentSpanId, 16)) && (State is null || Enum.IsDefined(State.Value)) &&
        (Revision is null or > 0) && BusinessId != Guid.Empty &&
        ObservedAt >= DateTimeOffset.UtcNow.AddHours(-24) && ObservedAt <= DateTimeOffset.UtcNow.AddMinutes(2);
    private static bool Hex(string? value, int length) => value?.Length == length && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}
[JsonConverter(typeof(JsonStringEnumConverter<Component>))]
public enum Component
{
    Procurement, Fulfilment, Treasury
}
[JsonConverter(typeof(JsonStringEnumConverter<Operation>))]
public enum Operation
{
    RequestPurchase, ApprovePurchase, ExchangeHandoff, ImportOrder, ListOrders, AcceptOrder, DispatchOrder, ReceiveOrder, ReadReceipts, ListPayments, PreparePayments, ReleasePayment, Reconcile
}
[JsonConverter(typeof(JsonStringEnumConverter<BusinessState>))]
public enum BusinessState
{
    Requested, Approved, Assigned, Accepted, Dispatched, Received, Pending, Released
}
public sealed record EvidenceOperation(Operation Operation);
public sealed record BusinessObservation(Guid Id, BusinessState State, int Revision);
