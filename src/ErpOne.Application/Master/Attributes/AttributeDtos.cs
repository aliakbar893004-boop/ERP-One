namespace ErpOne.Application.Attributes;

public record AttributeValueDto(int Id, string Code, string Value);
public record AttributeDto(int Id, string Code, string Name, IReadOnlyList<AttributeValueDto> Values, DateTime CreatedAt, string? CreatedBy);

public record AttributeValueInput(string Code, string Value);
public record CreateAttributeRequest(string Code, string Name, IReadOnlyList<AttributeValueInput> Values);
public record UpdateAttributeRequest(string Code, string Name, IReadOnlyList<AttributeValueInput> Values);
