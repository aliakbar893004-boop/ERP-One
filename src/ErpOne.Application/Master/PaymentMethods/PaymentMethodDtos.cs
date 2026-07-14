using ErpOne.Domain.Entities;

namespace ErpOne.Application.PaymentMethods;

public record PaymentMethodDto(int Id, string Code, string Name, PaymentType Type, bool IsActive, DateTime CreatedAt, string? CreatedBy);
public record CreatePaymentMethodRequest(string Code, string Name, PaymentType Type, bool IsActive);
public record UpdatePaymentMethodRequest(string Code, string Name, PaymentType Type, bool IsActive);
