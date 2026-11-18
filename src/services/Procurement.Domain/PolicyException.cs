namespace Procurement.Domain;

public sealed class PolicyException(string code) : Exception(code);
